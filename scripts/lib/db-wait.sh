#!/usr/bin/env bash
# Shared PostgreSQL readiness helper for the local verification scripts.
#
# WHY THIS EXISTS
# ---------------
# Sprint 21's gate failed on "dev seed present (>=16 products)" with a count of
# 0, while "catalog schema exists" and "catalog_svc role exists" both passed on
# the same freshly reset volume. Those three facts are only consistent with one
# thing: the checks ran WHILE db/init was still executing. 00_roles.sql and
# 01_catalog_schema.sql had been applied; 02_dev_seed.sql (~2.4 MB of INSERTs)
# and 03_dev_seed_i18n.sql had not.
#
# The old wait was:
#
#     docker compose exec -T postgres pg_isready -U pim_admin -d pim
#
# pg_isready with no -h connects over the Unix socket. The official postgres
# image runs its init scripts against a temporary server started with
# listen_addresses='' — reachable on the socket, NOT on TCP. So the old check
# answered "ready" within a second or two of `up -d`, in the middle of init.
# It was not measuring what it was named after.
#
# Verified, not assumed (2026-08-18, postgres 17 binaries, isolated cluster on
# port 5441):
#
#     listen_addresses=''          socket: accepting connections   TCP: no response
#     listen_addresses='127.0.0.1' socket: accepting connections   TCP: accepting connections
#
# So TCP acceptance is false for the whole init phase and true afterwards. It is
# also the readiness the API actually needs, since the API connects over TCP.
#
# One caveat found while proving this, worth keeping in mind before anyone
# "simplifies" the check to run on the host: pg_isready -h 127.0.0.1 can be
# answered by a DIFFERENT PostgreSQL that happens to own that port. The probe
# below runs INSIDE the container, where 127.0.0.1:5432 can only be this
# cluster. Do not move it to the host, where the port is 15432 precisely
# because two other projects already hold the usual ones.
#
# STAGE 2 exists because stage 1 depends on an implementation detail of the
# postgres image's entrypoint. If that detail ever changes, stage 2 still
# catches the race: it polls for the seed to actually be present. It is a
# bounded poll, so it still FAILS when the seed genuinely did not load — it
# waits for a slow truth, it does not manufacture one.

# pim_wait_for_db <compose-file> [tcp_timeout_s] [seed_timeout_s] [min_products]
#
# Exit codes:
#   0  TCP is accepting connections and the dev seed is present
#   1  timed out waiting for TCP  (infra problem: container never came up)
#   2  TCP came up but the seed never appeared (init failed, or volume not reset)
#
# Prints an elapsed-time line for each stage. Those two numbers are the early
# warning if this race ever returns: on a healthy reset run stage 1 takes tens
# of seconds (init is doing the work) and stage 2 is immediate.
pim_wait_for_db() {
  local compose="$1"
  local tcp_timeout="${2:-180}"
  local seed_timeout="${3:-120}"
  local min_products="${4:-16}"

  local started elapsed
  started=$(date +%s)

  # --- Stage 1: TCP acceptance inside the container -------------------------
  local tcp_ok=1
  while :; do
    if docker compose -f "$compose" exec -T postgres \
         pg_isready -h 127.0.0.1 -p 5432 -U pim_admin -d pim >/dev/null 2>&1; then
      tcp_ok=0
      break
    fi
    elapsed=$(( $(date +%s) - started ))
    [ "$elapsed" -ge "$tcp_timeout" ] && break
    sleep 2
  done

  elapsed=$(( $(date +%s) - started ))
  if [ $tcp_ok -ne 0 ]; then
    echo "postgres: NOT accepting TCP after ${elapsed}s (limit ${tcp_timeout}s)"
    echo "last 40 lines of the postgres container log:"
    docker compose -f "$compose" logs --tail 40 postgres 2>&1 || true
    return 1
  fi
  echo "postgres: accepting TCP after ${elapsed}s (db/init has finished)"

  # --- Stage 2: the seed is actually there ----------------------------------
  local seed_started count seed_ok=1
  seed_started=$(date +%s)
  count=""
  while :; do
    count=$(docker compose -f "$compose" exec -T postgres \
      psql -U pim_admin -d pim -tAc "SELECT count(*) FROM catalog.product" 2>/dev/null \
      | tr -d '[:space:]')
    if [ -n "$count" ] && [ "$count" -ge "$min_products" ] 2>/dev/null; then
      seed_ok=0
      break
    fi
    elapsed=$(( $(date +%s) - seed_started ))
    [ "$elapsed" -ge "$seed_timeout" ] && break
    sleep 2
  done

  elapsed=$(( $(date +%s) - seed_started ))
  if [ $seed_ok -ne 0 ]; then
    echo "postgres: seed still absent after ${elapsed}s (count=${count:-unreadable}, need >=${min_products})"
    echo "init script output from the container log:"
    local initlines
    initlines=$(docker compose -f "$compose" logs postgres 2>&1 \
      | grep -E "Applying|Applied|db/init complete|ERROR|FATAL" | tail -30)
    if [ -n "$initlines" ]; then
      echo "$initlines"
    else
      echo "  (none — db/init never ran, which means the volume was not empty."
      echo "   Re-run with --reset-db, or: cd infra && docker compose down -v)"
    fi
    return 2
  fi
  echo "postgres: dev seed present after ${elapsed}s (${count} products)"
  return 0
}
