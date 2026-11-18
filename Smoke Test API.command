#!/bin/bash
# End-to-end API smoke test against the seeded local database.
cd "$(dirname "$0")" || exit 1

# Reset to the generated seed first. A few assertions are absolute counts, so
# the SMOKE-* product left behind by the previous run would fail them.
(cd infra && docker compose down -v >/dev/null 2>&1)
./scripts/smoke-api.sh > smoke-log.txt 2>&1
STATUS=$?
tail -40 smoke-log.txt
echo
echo "Full output: smoke-log.txt (exit code $STATUS). You can close this window."
exit $STATUS
