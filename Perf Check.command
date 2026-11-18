#!/bin/bash
# Seeds >1000 products and times the list endpoint (Sprint 4 hardening).
# Leaves the perf rows behind — run "Verify PIM (reset db).command" to restore
# the clean 16-product seed.
cd "$(dirname "$0")" || exit 1
./scripts/perf-check.sh > perf-log.txt 2>&1
STATUS=$?
tail -30 perf-log.txt
echo
echo "Full output: perf-log.txt (exit code $STATUS). You can close this window."
exit $STATUS
