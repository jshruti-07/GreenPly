#!/bin/bash
# Double-clickable Sprint 0 verification launcher.
# Runs scripts/verify-local.sh and writes everything to verify-log.txt
# so Claude can read the results through the project folder and fix issues.
cd "$(dirname "$0")" || exit 1

LOG="verify-log.txt"
{
  echo "=== PIM verification run: $(date) ==="
  echo "--- toolchain ---"
  echo -n "dotnet: ";  command -v dotnet  >/dev/null && dotnet --version || echo "NOT INSTALLED"
  echo -n "node:   ";  command -v node    >/dev/null && node --version   || echo "NOT INSTALLED"
  echo -n "npm:    ";  command -v npm     >/dev/null && npm --version    || echo "NOT INSTALLED"
  echo -n "docker: ";  command -v docker  >/dev/null && docker --version || echo "NOT INSTALLED"
  if command -v docker >/dev/null; then
    docker info >/dev/null 2>&1 && echo "docker daemon: RUNNING" || echo "docker daemon: NOT RUNNING (start Docker Desktop)"
  fi
  echo
} > "$LOG" 2>&1

./scripts/verify-local.sh >> "$LOG" 2>&1
STATUS=$?

{
  echo
  echo "=== exit code: $STATUS ==="
  echo "=== run complete: $(date) ==="
} >> "$LOG" 2>&1

echo
echo "Done. Full output written to verify-log.txt (exit code $STATUS)."
echo "You can close this window."
exit $STATUS
