#!/bin/bash
# =============================================================================
# Verify Running Stack.command  —  READ-ONLY
#
# 'Smoke Test API.command' exercises the API directly on :5100. This checks the
# seam that nothing else covers: that the Vite dev server on :5174 is serving
# the app AND correctly proxying /api through to the catalog API.
#
# Nothing is started, stopped or modified. Output: verify-stack-log.txt
# =============================================================================

set -uo pipefail
cd "$(dirname "$0")" || { echo "FATAL: cannot cd to script directory"; exit 1; }
exec > >(tee "verify-stack-log.txt") 2>&1

BOLD=$'\033[1m'; RESET=$'\033[0m'
RED=$'\033[31m'; GREEN=$'\033[32m'
PASS=0; FAIL=0

ok()   { printf '  %sPASS%s  %s\n' "$GREEN" "$RESET" "$1"; PASS=$((PASS+1)); }
bad()  { printf '  %sFAIL%s  %s\n' "$RED" "$RESET" "$1"; FAIL=$((FAIL+1)); }
sec()  { printf '\n%s== %s ==%s\n' "$BOLD" "$1" "$RESET"; }

# check_status <label> <url> <expected-code>
check_status() {
  local label="$1" url="$2" want="$3" got
  got=$(curl -s -o /dev/null -w '%{http_code}' --max-time 15 "$url" 2>/dev/null)
  if [ "$got" = "$want" ]; then ok "$label (= $got)"; else bad "$label (= $got, want $want) $url"; fi
}

# check_body <label> <url> <grep-pattern>
#
# NB: 'grep -q' exits on first match, which SIGPIPEs the writer; under
# 'set -o pipefail' that turns a successful match into a failed pipeline.
# It bit this script on the large swagger document and produced a false
# FAIL. grep must therefore consume all input — redirect, never -q.
check_body() {
  local label="$1" url="$2" pattern="$3" body
  body=$(curl -s --max-time 15 "$url" 2>/dev/null)
  if printf '%s' "$body" | grep -E "$pattern" >/dev/null 2>&1; then
    ok "$label"
  else
    bad "$label — pattern '$pattern' not found in response from $url"
    printf '        first 200 bytes: %s\n' "$(printf '%s' "$body" | head -c 200)"
  fi
}

echo "Stack verification — $(date)"

sec "Infrastructure containers"
if docker info >/dev/null 2>&1; then
  docker compose -f infra/docker-compose.yml ps --format '  {{.Name}}\t{{.State}}\t{{.Status}}' 2>/dev/null \
    || docker ps --filter "name=infra-" --format '  {{.Names}}\t{{.State}}\t{{.Status}}'
  for c in infra-postgres-1 infra-rabbitmq-1 infra-redis-1; do
    state=$(docker inspect -f '{{.State.Status}}' "$c" 2>/dev/null || echo "absent")
    [ "$state" = "running" ] && ok "$c running" || bad "$c is '$state'"
  done
else
  bad "docker engine not responding — cannot inspect containers"
fi

sec "Catalog API direct (:5100)"
check_status "healthz"                 "http://localhost:5100/healthz"      200
check_status "swagger.json"            "http://localhost:5100/swagger/v1/swagger.json" 200
check_body   "swagger describes paths" "http://localhost:5100/swagger/v1/swagger.json" '"paths"'

sec "Frontend dev server (:5174)"
check_status "index.html served"       "http://localhost:5174/"             200
check_body   "index has module entry"  "http://localhost:5174/"             '<script[^>]+type="module"'
check_body   "vite client injected"    "http://localhost:5174/"             '/@vite/client'

sec "Vite proxy seam  /api -> :5100"
# This is the integration point that neither the API smoke test nor the
# frontend build exercises: a request to the dev server must reach the API.
#
# The route is discovered from the live swagger document rather than guessed —
# guessing '/api/products' produced a 404 that looked like a proxy fault when
# the API actually mounts /api/v1/*. Take the first parameterless GET path.
ROUTE=$(curl -s --max-time 15 "http://localhost:5100/swagger/v1/swagger.json" 2>/dev/null \
        | grep -oE '"/api/[^"{}]+"' | tr -d '"' | head -1)

if [ -z "$ROUTE" ]; then
  bad "could not discover any /api/* route from swagger — skipping the proxy check"
else
  echo "  route under test: $ROUTE"
  DIRECT_CODE=$(curl -s -o /dev/null -w '%{http_code}' --max-time 15 "http://localhost:5100$ROUTE")
  PROXY_CODE=$(curl -s -o /dev/null -w '%{http_code}' --max-time 15 "http://localhost:5174$ROUTE")
  echo "  direct :5100 -> $DIRECT_CODE     via proxy :5174 -> $PROXY_CODE"

  case "$PROXY_CODE" in
    000)         bad "no response from :5174 — is the dev server running?" ;;
    502|503|504) bad "proxy returned $PROXY_CODE — dev server is up but cannot reach :5100" ;;
    *)
      # The proxy's job is to forward faithfully; matching the direct response
      # is the assertion, not any particular status code.
      if [ "$PROXY_CODE" = "$DIRECT_CODE" ]; then
        ok "proxy forwards $ROUTE faithfully (both = $PROXY_CODE)"
      else
        bad "proxy status $PROXY_CODE != direct status $DIRECT_CODE for $ROUTE"
      fi
      ;;
  esac

  if [ "$PROXY_CODE" = "200" ] && [ "$DIRECT_CODE" = "200" ]; then
    DIRECT_BODY=$(curl -s --max-time 15 "http://localhost:5100$ROUTE")
    PROXY_BODY=$(curl -s --max-time 15 "http://localhost:5174$ROUTE")
    if [ "$DIRECT_BODY" = "$PROXY_BODY" ]; then
      ok "proxied body is byte-identical to the direct response"
    else
      bad "proxied body differs from the direct response"
    fi
  fi
fi

sec "Outbox transport (ADR-007)"
# The README claims nothing ever leaves the service. Development config now
# sets Outbox:RabbitMq:Uri, which registers RabbitMqOutboxTransport instead of
# the log-only one. OutboxHealthCheck returns Degraded whenever the transport
# reports IsDeliveringExternally = false, so the aggregate health body answers
# the question directly — no guessing from config files.
HEALTH_BODY=$(curl -s --max-time 15 "http://localhost:5100/healthz" 2>/dev/null)
echo "  /healthz body: ${HEALTH_BODY:-<empty>}"
case "$HEALTH_BODY" in
  Healthy)
    ok "aggregate health Healthy — outbox is delivering externally (log-only transport would force Degraded)" ;;
  Degraded)
    bad "aggregate health Degraded — at least one check is unhappy; the outbox may still be log-only" ;;
  Unhealthy)
    bad "aggregate health Unhealthy" ;;
  *)
    bad "unexpected /healthz body: '${HEALTH_BODY}'" ;;
esac

# Independent confirmation from the broker itself, via docker so no credentials
# are needed here.
if docker info >/dev/null 2>&1; then
  EXCHANGES=$(docker exec infra-rabbitmq-1 rabbitmqctl list_exchanges name 2>/dev/null)
  if printf '%s' "$EXCHANGES" | grep -E '^catalog\.events$' >/dev/null 2>&1; then
    ok "RabbitMQ exchange 'catalog.events' exists — the service really connected"
  else
    bad "RabbitMQ exchange 'catalog.events' not found. Exchanges present:"
    printf '%s\n' "$EXCHANGES" | sed 's/^/        /' | head -15
  fi
fi

sec "Summary"
printf '  passed: %s   failed: %s\n' "$PASS" "$FAIL"
if [ "$FAIL" -eq 0 ]; then
  printf '%sSTACK: GREEN%s\n' "$GREEN$BOLD" "$RESET"
  exit 0
else
  printf '%sSTACK: %s CHECK(S) FAILED%s\n' "$RED$BOLD" "$FAIL" "$RESET"
  exit 1
fi
