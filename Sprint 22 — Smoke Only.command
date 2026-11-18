#!/bin/bash
#
# Sprint 22 — API smoke only, against a freshly reset seed.
#
# The volume is dropped first because the perf run inflates the catalogue past
# 1,500 products, and every smoke assertion is written against the generated
# seed. Running smoke on an inflated catalogue produces failures that describe
# the previous run rather than this build.
#
cd "$(dirname "$0")" || exit 1

if ! command -v dotnet >/dev/null; then
  for d in /usr/local/share/dotnet "$HOME/.dotnet" /opt/homebrew/opt/dotnet/libexec; do
    [ -x "$d/dotnet" ] && export PATH="$d:$PATH" && break
  done
fi
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

echo ">> Resetting the database to the generated seed"
(cd infra && docker compose down -v >/dev/null 2>&1)

echo ">> API smoke"
./scripts/smoke-api.sh > smoke-log.txt 2>&1
SMOKE=$?
echo "   smoke exit code: $SMOKE"
grep -E "^postgres: " smoke-log.txt
grep -E "  (PASS|FAIL)  cat: " smoke-log.txt
tail -4 smoke-log.txt
echo
echo "Detail: smoke-log.txt. You can close this window."
exit $SMOKE
