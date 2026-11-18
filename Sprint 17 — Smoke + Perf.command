#!/bin/bash
#
# Sprint 17 — the two runtime suites, back to back.
#
#   1. API smoke   — live HTTP against the seeded catalogue (smoke-log.txt)
#   2. Perf check  — read-model tripwire at volume        (perf-log.txt)
#
# Order matters: several smoke assertions are absolute counts against the
# generated seed, and perf-check.sh inflates the catalogue to 2,000 products
# and 25,000 audit rows. Perf must therefore run second, never before.
#
cd "$(dirname "$0")" || exit 1

if ! command -v dotnet >/dev/null; then
  for d in /usr/local/share/dotnet "$HOME/.dotnet" /opt/homebrew/opt/dotnet/libexec; do
    [ -x "$d/dotnet" ] && export PATH="$d:$PATH" && break
  done
fi
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

echo ">> 1/2  API smoke — resetting to the generated seed first"
(cd infra && docker compose down -v >/dev/null 2>&1)
./scripts/smoke-api.sh > smoke-log.txt 2>&1
SMOKE=$?
echo "   smoke exit code: $SMOKE"
tail -12 smoke-log.txt

echo
echo ">> 2/2  Perf check — inflates the catalogue, so this runs last"
./scripts/perf-check.sh > perf-log.txt 2>&1
PERF=$?
echo "   perf exit code: $PERF"
tail -12 perf-log.txt

echo
echo "=============================================="
echo " smoke: $SMOKE   perf: $PERF   (0 = pass)"
echo " Detail: smoke-log.txt, perf-log.txt"
echo " You can close this window."
echo "=============================================="

[ "$SMOKE" -eq 0 ] && [ "$PERF" -eq 0 ] && exit 0
exit 1
