#!/bin/bash
# Full Sprint gate WITH a database reset, so db/init changes (schema + dev
# seed) are re-applied. Destroys local PIM database contents.
cd "$(dirname "$0")" || exit 1
LOG="verify-log.txt"
{
  echo "=== PIM verification run (--reset-db): $(date) ==="
  echo "--- toolchain ---"
  echo -n "dotnet: ";  command -v dotnet  >/dev/null && dotnet --version || echo "NOT INSTALLED"
  echo -n "node:   ";  command -v node    >/dev/null && node --version   || echo "NOT INSTALLED"
  echo -n "docker: ";  command -v docker  >/dev/null && docker --version || echo "NOT INSTALLED"
  echo
} > "$LOG" 2>&1
./scripts/verify-local.sh --reset-db >> "$LOG" 2>&1
STATUS=$?
{ echo; echo "=== exit code: $STATUS ==="; } >> "$LOG" 2>&1
echo "Done — see verify-log.txt (exit code $STATUS). You can close this window."
exit $STATUS
