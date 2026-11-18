#!/bin/bash
#
# Sprint 21 — the whole verification stack in one run.
#
#   0. Evidence   (what the postgres image does during db/init)
#   1. Build gate (verify-local.sh --reset-db)  -> gate-log.txt
#   2. API smoke  (smoke-api.sh)                -> smoke-log.txt
#   3. Perf check (perf-check.sh)               -> perf-log.txt
#
# Stops at the first red stage: a smoke run against a build that did not
# compile, or a perf number from a broken read model, is noise rather than
# evidence.
#
cd "$(dirname "$0")" || exit 1

if ! command -v dotnet >/dev/null; then
  for d in /usr/local/share/dotnet "$HOME/.dotnet" /opt/homebrew/opt/dotnet/libexec; do
    [ -x "$d/dotnet" ] && export PATH="$d:$PATH" && break
  done
fi
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

# --- 0. Evidence for the readiness fix ---------------------------------------
# The Sprint 21 gate failed with 0 products on a freshly reset volume while the
# schema and role checks passed. That is only possible if the checks ran during
# db/init. The claim behind the fix is that the postgres image runs init scripts
# against a temporary server with listen_addresses='' — socket reachable, TCP
# not. This prints the image's own code for that, so the claim is checkable
# here rather than taken on trust.
echo ">> 0/3  Evidence: how the postgres image runs db/init"
docker run --rm --entrypoint sh postgres:17-alpine -c \
  "grep -n \"listen_addresses\" /usr/local/bin/docker-entrypoint.sh" 2>&1 \
  | sed 's/^/     /' || echo "     (could not read the entrypoint — not fatal)"
echo

echo ">> 1/3  Build gate — restore, build, backend tests, frontend, DB reset"
./scripts/verify-local.sh --reset-db > gate-log.txt 2>&1
GATE=$?
echo "   gate exit code: $GATE"
grep -E "^postgres: " gate-log.txt | sed 's/^/     /'
if [ "$GATE" -ne 0 ]; then
  tail -25 gate-log.txt
  echo
  echo "STOPPED: the gate is red, so smoke and perf would tell you nothing."
  echo "Detail: gate-log.txt"
  exit 1
fi

echo
echo ">> 2/3  API smoke — resetting to the generated seed first"
(cd infra && docker compose down -v >/dev/null 2>&1)
./scripts/smoke-api.sh > smoke-log.txt 2>&1
SMOKE=$?
echo "   smoke exit code: $SMOKE"
grep -E "^postgres: " smoke-log.txt | sed 's/^/     /'
tail -8 smoke-log.txt
if [ "$SMOKE" -ne 0 ]; then
  echo
  echo "STOPPED: smoke is red. Perf inflates the catalogue and would destroy"
  echo "the seed this failure needs to be reproduced against."
  echo "Detail: smoke-log.txt"
  exit 1
fi

echo
echo ">> 3/3  Perf check — inflates the catalogue, so it runs last"
./scripts/perf-check.sh > perf-log.txt 2>&1
PERF=$?
echo "   perf exit code: $PERF"
tail -8 perf-log.txt

echo
echo "=============================================="
echo " gate: $GATE   smoke: $SMOKE   perf: $PERF   (0 = pass)"
echo " Detail: gate-log.txt, smoke-log.txt, perf-log.txt"
echo " You can close this window."
echo "=============================================="

[ "$PERF" -eq 0 ] && exit 0
exit 1
