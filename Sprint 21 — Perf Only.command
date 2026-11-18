#!/bin/bash
#
# Sprint 21 — perf check only.
#
# The catalogue is already inflated past 1,500 products by the previous run, and
# perf-check.sh tops it up itself if it is not. Nothing else needs re-running:
# the gate and smoke both passed on this build.
#
cd "$(dirname "$0")" || exit 1

if ! command -v dotnet >/dev/null; then
  for d in /usr/local/share/dotnet "$HOME/.dotnet" /opt/homebrew/opt/dotnet/libexec; do
    [ -x "$d/dotnet" ] && export PATH="$d:$PATH" && break
  done
fi
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

echo ">> Perf check (now including the product cockpit)"
./scripts/perf-check.sh > perf-log.txt 2>&1
PERF=$?
echo "   perf exit code: $PERF"
grep -E "dashboard:|^  passed:|^PERF:" perf-log.txt
echo
echo "Detail: perf-log.txt. You can close this window."
exit $PERF
