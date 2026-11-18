#!/bin/bash
# Frontend lint + unit tests + production build only. Fast inner loop while
# writing React; the full gate still runs before a sprint is accepted.
ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT/frontend" || exit 1
LOG="$ROOT/frontend-log.txt"

{
  echo "=== frontend check: $(date) ==="
  command -v npm >/dev/null || { echo "FATAL: npm not found"; exit 1; }

  echo "--- lint ---"
  npm run lint || exit 1

  echo "--- test ---"
  npm run test -- --run || exit 1

  echo "--- build ---"
  npm run build || exit 1

  echo "FRONTEND: OK"
} > "$LOG" 2>&1
STATUS=$?

{ echo; echo "=== exit code: $STATUS ==="; } >> "$LOG" 2>&1
echo "Done — see frontend-log.txt (exit code $STATUS). You can close this window."
exit $STATUS
