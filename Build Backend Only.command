#!/bin/bash
# Backend compile + unit tests only — no docker, no frontend. Fast inner loop
# while writing C#; the full gate still runs before a sprint is accepted.
cd "$(dirname "$0")" || exit 1
LOG="$PWD/build-log.txt"

{
  echo "=== backend build: $(date) ==="
  if ! command -v dotnet >/dev/null; then
    for d in /usr/local/share/dotnet "$HOME/.dotnet" /opt/homebrew/opt/dotnet/libexec; do
      if [ -x "$d/dotnet" ]; then export PATH="$d:$PATH"; break; fi
    done
  fi
  command -v dotnet >/dev/null || { echo "FATAL: dotnet not found"; exit 1; }

  cd services/catalog || exit 1
  echo "--- build ---"
  dotnet build --nologo -v q || exit 1

  echo "--- domain + application tests (integration needs docker; excluded here) ---"
  dotnet test tests/Pim.Catalog.Domain.Tests --nologo -v q --no-build || exit 1
  dotnet test tests/Pim.Catalog.Application.Tests --nologo -v q --no-build || exit 1

  echo "BACKEND: OK"
} > "$LOG" 2>&1
STATUS=$?

{ echo; echo "=== exit code: $STATUS ==="; } >> "$LOG" 2>&1
echo "Done — see build-log.txt (exit code $STATUS). You can close this window."
exit $STATUS
