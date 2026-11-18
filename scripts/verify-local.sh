#!/usr/bin/env bash
# PIM platform — Sprint 0 backend verification loop (run on the dev Mac).
# The AI sandbox has no .NET SDK / NuGet access, so THIS script is the
# authoritative backend check until Azure DevOps CI is live.
#
# Usage: ./scripts/verify-local.sh [--skip-infra] [--skip-frontend]
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SKIP_INFRA=false
SKIP_FRONTEND=false
RESET_DB=false
for arg in "$@"; do
  case "$arg" in
    --skip-infra) SKIP_INFRA=true ;;
    --skip-frontend) SKIP_FRONTEND=true ;;
    # Postgres runs db/init/*.sql ONLY on an empty data dir, so changes to the
    # schema or the dev seed require dropping the volume. Destroys local data.
    --reset-db) RESET_DB=true ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

# shellcheck source=lib/db-wait.sh
. "$ROOT/scripts/lib/db-wait.sh"

PASS=()
FAIL=()
step() { printf '\n\033[1m== %s ==\033[0m\n' "$1"; }
record() { if [ "$2" -eq 0 ]; then PASS+=("$1"); else FAIL+=("$1"); fi; }

# --- 0. Toolchain ---------------------------------------------------------
step "Toolchain"
# .command launchers may run without the login PATH — probe known dotnet homes.
if ! command -v dotnet >/dev/null; then
  for d in /usr/local/share/dotnet "$HOME/.dotnet" /opt/homebrew/opt/dotnet/libexec; do
    if [ -x "$d/dotnet" ]; then export PATH="$d:$PATH"; echo "dotnet found at $d (added to PATH)"; break; fi
  done
fi
command -v dotnet >/dev/null || { echo "FATAL: dotnet not found. Install .NET SDK 10.0.x"; exit 1; }
dotnet --version
DOTNET_MAJOR=$(dotnet --version | cut -d. -f1)
[ "$DOTNET_MAJOR" -ge 10 ] || echo "WARN: repo pins SDK 10.0.x (global.json); found $(dotnet --version)"

# --- 1. Infra: Postgres + RabbitMQ + Redis, catalog DDL -------------------
if [ "$SKIP_INFRA" = false ]; then
  step "Infra (docker compose)"
  command -v docker >/dev/null || { echo "FATAL: docker not found. Start Docker Desktop."; exit 1; }
  # Self-heal: a postgres container that exists but is not running means a
  # crashed/aborted first init — its volume may hold a half-initialised data
  # dir (init scripts only run on an EMPTY dir). Reset cleanly.
  if [ "$RESET_DB" = true ]; then
    echo "--reset-db: dropping volumes so db/init scripts (schema + dev seed) re-run"
    (cd "$ROOT/infra" && docker compose down -v)
  fi
  PG_STATE=$(docker compose -f "$ROOT/infra/docker-compose.yml" ps -a --format '{{.Service}} {{.State}}' 2>/dev/null | awk '$1=="postgres"{print $2}' || true)
  if [ -n "$PG_STATE" ] && [ "$PG_STATE" != "running" ]; then
    echo "postgres container state '$PG_STATE' — resetting infra (down -v) before retry"
    (cd "$ROOT/infra" && docker compose down -v)
  fi
  (cd "$ROOT/infra" && docker compose up -d)
  record "docker compose up" $?

  # Waits for TCP acceptance, which the postgres image only offers AFTER
  # db/init has finished — the socket answers throughout init, which is what
  # the previous version of this check was measuring. See scripts/lib/db-wait.sh.
  step "Wait for PostgreSQL ready and db/init complete (max 180s)"
  pim_wait_for_db "$ROOT/infra/docker-compose.yml"
  DB_WAIT=$?
  # 1 = the container never accepted connections, which is a readiness failure.
  # 2 = it did, but the seed never arrived. That is reported by the seed
  #     assertion below, not here — collapsing the two would hide which broke.
  if [ $DB_WAIT -eq 1 ]; then record "postgres ready" 1; else record "postgres ready" 0; fi

  step "Catalog DDL applied (schema + role exist)"
  docker compose -f "$ROOT/infra/docker-compose.yml" exec -T postgres \
    psql -U pim_admin -d pim -tAc \
    "SELECT 1 FROM information_schema.schemata WHERE schema_name='catalog'" | grep -q 1
  record "catalog schema exists" $?
  docker compose -f "$ROOT/infra/docker-compose.yml" exec -T postgres \
    psql -U pim_admin -d pim -tAc "SELECT 1 FROM pg_roles WHERE rolname='catalog_svc'" | grep -q 1
  record "catalog_svc role exists" $?

  step "Dev seed applied (16 demo products from prototype v4.2)"
  SEEDED=$(docker compose -f "$ROOT/infra/docker-compose.yml" exec -T postgres \
    psql -U pim_admin -d pim -tAc "SELECT count(*) FROM catalog.product" 2>/dev/null | tr -d '[:space:]')
  echo "products in catalog.product: ${SEEDED:-unknown}"
  [ "${SEEDED:-0}" -ge 16 ] 2>/dev/null
  record "dev seed present (>=16 products)" $?
  [ "${SEEDED:-0}" -ge 16 ] 2>/dev/null || {
    echo "The wait above already gave db/init up to 120s to produce this, so this"
    echo "is not the old timing race: either init failed, or the volume was never"
    echo "empty and the scripts therefore never ran."
    echo "HINT: re-run with --reset-db — init scripts only execute on an empty volume."
  }
fi

# --- 2. Backend: restore / build / test -----------------------------------
step "dotnet restore (first run WILL surface package-version issues — expected)"
(cd "$ROOT/services/catalog" && dotnet restore Pim.Catalog.sln)
RESTORE=$?
record "dotnet restore" $RESTORE

if [ $RESTORE -eq 0 ]; then
  step "dotnet build (warnings are errors)"
  (cd "$ROOT/services/catalog" && dotnet build Pim.Catalog.sln -c Release --no-restore)
  BUILD=$?
  record "dotnet build" $BUILD

  if [ $BUILD -eq 0 ]; then
    step "dotnet test"
    (cd "$ROOT/services/catalog" && dotnet test Pim.Catalog.sln -c Release --no-build)
    record "dotnet test" $?
  else
    echo "Skipping tests — build failed."
  fi
else
  echo "Skipping build/test — restore failed. Paste the version errors back to Claude;"
  echo "fixing Directory.Packages.props from restore feedback is planned Sprint 0 work."
fi

# --- 3. Frontend -----------------------------------------------------------
if [ "$SKIP_FRONTEND" = false ]; then
  step "Frontend lint + test + build"
  (cd "$ROOT/frontend" && npm ci && npm run lint && npm run test -- --run && npm run build)
  record "frontend lint/test/build" $?
fi

# --- Summary ---------------------------------------------------------------
step "Summary"
for p in "${PASS[@]:-}"; do [ -n "$p" ] && echo "  PASS  $p"; done
for f in "${FAIL[@]:-}"; do [ -n "$f" ] && echo "  FAIL  $f"; done
if [ ${#FAIL[@]} -eq 0 ]; then
  echo
  echo "Sprint 0 backend gate: GREEN — scaffold accepted (per README)."
  exit 0
else
  echo
  echo "Sprint 0 backend gate: RED — paste failing output back to Claude for the fix loop."
  exit 1
fi
