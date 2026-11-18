#!/bin/bash
#
# Sprint 17 fix loop — re-run the gate only. No commits, no repo surgery.
# Writes gate-log.txt. Safe to run as many times as needed.
#
cd "$(dirname "$0")" || exit 1
LOG="gate-log.txt"

if ! command -v dotnet >/dev/null; then
  for d in /usr/local/share/dotnet "$HOME/.dotnet" /opt/homebrew/opt/dotnet/libexec; do
    [ -x "$d/dotnet" ] && export PATH="$d:$PATH" && break
  done
fi
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

{
  echo "=== gate re-run: $(date) ==="
  echo "HEAD: $(git log -1 --oneline)"
  echo "dirty paths: $(git status --porcelain | wc -l | tr -d ' ')"
  echo
} > "$LOG" 2>&1

echo ">> running the gate — several minutes"
./scripts/verify-local.sh --reset-db >> "$LOG" 2>&1
GATE=$?
{ echo; echo "=== verify-local.sh exit code: $GATE ==="; } >> "$LOG" 2>&1

echo
echo "=============================================="
echo " Gate exit code: $GATE"
echo " Detail: gate-log.txt"
echo " You can close this window."
echo "=============================================="
exit "$GATE"
