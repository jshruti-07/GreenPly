#!/bin/bash
# =============================================================================
# Start Docker and Run PIM.command
#
# Docker Desktop is installed on this machine (the `docker` CLI resolves) but
# its engine is not running, so nothing that needs Postgres/RabbitMQ/Redis can
# start. This script:
#
#   1. launches Docker Desktop (whatever it is called in /Applications)
#   2. waits for the engine to answer `docker info`
#   3. hands off to 'Setup Mac.command --yes', which finishes prerequisite
#      checks, builds, runs the full test suite and then starts the app
#
# It never installs Docker and never accepts a licence on your behalf — if the
# app needs interaction on first launch, this script waits and then tells you.
#
# Log: docker-start-log.txt (the handoff continues into setup-log.txt)
# =============================================================================

set -uo pipefail

cd "$(dirname "$0")" || { echo "FATAL: cannot cd to script directory"; exit 1; }
exec > >(tee "docker-start-log.txt") 2>&1

BOLD=$'\033[1m'; RESET=$'\033[0m'
RED=$'\033[31m'; GREEN=$'\033[32m'; YELLOW=$'\033[33m'
ok()   { printf '  %sPASS%s  %s\n' "$GREEN" "$RESET" "$1"; }
warn() { printf '  %sWARN%s  %s\n' "$YELLOW" "$RESET" "$1"; }
fatal() { printf '\n%sFATAL:%s %s\n' "$RED" "$RESET" "$1"; exit 1; }

ENGINE_WAIT_SECONDS=180

printf '%s== Docker engine ==%s\n' "$BOLD" "$RESET"

command -v docker >/dev/null 2>&1 \
  || fatal "docker CLI not found. Docker Desktop does not appear to be installed.
       Install it from https://www.docker.com/products/docker-desktop/ then re-run."

if docker info >/dev/null 2>&1; then
  ok "engine already running ($(docker --version))"
else
  # The bundle has been named both Docker.app and Docker Desktop.app across
  # releases; resolve it rather than assuming either.
  APP_PATH=""
  for candidate in "/Applications/Docker.app" "/Applications/Docker Desktop.app" \
                   "$HOME/Applications/Docker.app" "$HOME/Applications/Docker Desktop.app"; do
    [ -d "$candidate" ] && { APP_PATH="$candidate"; break; }
  done

  if [ -z "$APP_PATH" ]; then
    warn "no Docker*.app found in /Applications — falling back to 'open -a Docker'"
    open -a Docker 2>/dev/null \
      || fatal "Could not launch Docker Desktop. The docker CLI exists but the
       application bundle could not be located. Start Docker Desktop manually,
       wait for 'Engine running', then run 'Setup and Run PIM.command'."
  else
    echo "  launching $APP_PATH"
    open -a "$APP_PATH" \
      || fatal "Could not launch $APP_PATH. Start Docker Desktop manually, wait for
       'Engine running', then run 'Setup and Run PIM.command'."
  fi

  printf '  waiting for the engine (up to %ss)' "$ENGINE_WAIT_SECONDS"
  ENGINE_UP=0
  for _ in $(seq 1 $((ENGINE_WAIT_SECONDS / 3))); do
    if docker info >/dev/null 2>&1; then ENGINE_UP=1; break; fi
    printf '.'
    sleep 3
  done
  echo

  [ "$ENGINE_UP" -eq 1 ] || fatal "Docker engine did not come up within ${ENGINE_WAIT_SECONDS}s.
       Docker Desktop may be waiting on you — a licence/terms dialog, a sign-in
       prompt, an update, or an admin password. Bring it to the front, clear
       whatever it is asking for, then run 'Setup and Run PIM.command'."
  ok "engine running ($(docker --version))"
fi

echo
printf '%s== Handing off to Setup Mac.command ==%s\n' "$BOLD" "$RESET"
[ -x "./Setup Mac.command" ] \
  || fatal "'Setup Mac.command' is missing or not executable in $PWD"

exec "./Setup Mac.command" --yes
