#!/bin/bash
#
# Sprint 27 — the whole verification stack in one run.
#
#   1. Build gate (verify-local.sh --reset-db)  -> gate-log.txt
#   2. API smoke  (smoke-api.sh)                -> smoke-log.txt
#   3. Perf check (perf-check.sh)               -> perf-log.txt
#
# --reset-db matters this sprint: MODULE 09 adds five tables and the default
# pipeline as reference data, so a run against the existing volume would test
# the previous schema and pass.
#
# Stops at the first red stage. A smoke run against a build that did not compile
# is noise, not evidence.
#
cd "$(dirname "$0")" || exit 1

if ! command -v dotnet >/dev/null; then
  for d in /usr/local/share/dotnet "$HOME/.dotnet" /opt/homebrew/opt/dotnet/libexec; do
    [ -x "$d/dotnet" ] && export PATH="$d:$PATH" && break
  done
fi
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

exec > >(tee "sprint27-run-log.txt") 2>&1

echo ">> 1/3  Build gate — restore, build, backend tests, frontend, DB reset"
./scripts/verify-local.sh --reset-db > gate-log.txt 2>&1
GATE=$?
echo "   gate exit code: $GATE"
grep -E "^postgres: |Passed!|Failed!|Test Files" gate-log.txt | sed 's/^/     /'
if [ "$GATE" -ne 0 ]; then
  tail -40 gate-log.txt
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
grep -E "  FAIL " smoke-log.txt | head -25 | sed 's/^/     /'
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
grep -E "  FAIL |  SKIP " perf-log.txt | sed 's/^/     /'
tail -8 perf-log.txt

echo
echo "=============================================="
echo " gate: $GATE   smoke: $SMOKE   perf: $PERF   (0 = pass)"
echo " Detail: gate-log.txt, smoke-log.txt, perf-log.txt"
echo " You can close this window."
echo "=============================================="

[ "$PERF" -eq 0 ] && exit 0
exit 1
