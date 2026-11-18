#!/bin/bash
# =============================================================================
# Diagnose Docker.command   —  READ-ONLY
#
# The Docker engine will not answer `docker info`, and Docker Desktop's window
# cannot be inspected remotely. This collects the state that explains why,
# without changing anything: no containers are started or stopped, no settings
# are written, no files are removed.
#
# Output: docker-diag-log.txt
# =============================================================================

set -uo pipefail
cd "$(dirname "$0")" || { echo "FATAL: cannot cd to script directory"; exit 1; }
exec > >(tee "docker-diag-log.txt") 2>&1

BOLD=$'\033[1m'; RESET=$'\033[0m'
sec() { printf '\n%s----- %s -----%s\n' "$BOLD" "$1" "$RESET"; }
run() { printf '$ %s\n' "$*"; "$@" 2>&1 | head -40; printf '[exit %s]\n' "${PIPESTATUS[0]}"; }

echo "Docker diagnostics — $(date)"
echo "macOS $(sw_vers -productVersion 2>/dev/null)  $(uname -m)"

sec "CLI and daemon"
run docker --version
run docker context show
run docker context ls
printf '$ docker info (10s cap)\n'
if command -v timeout >/dev/null 2>&1; then
  timeout 10 docker info 2>&1 | head -40
else
  # macOS has no coreutils timeout by default; bound it with a watchdog.
  docker info > /tmp/pim-docker-info.$$ 2>&1 &
  info_pid=$!
  ( sleep 10; kill "$info_pid" 2>/dev/null ) & watchdog=$!
  wait "$info_pid" 2>/dev/null
  kill "$watchdog" 2>/dev/null
  head -40 "/tmp/pim-docker-info.$$"
  rm -f "/tmp/pim-docker-info.$$" 2>/dev/null
fi

sec "Docker processes"
printf '$ pgrep -afl -i docker\n'
pgrep -fl -i docker 2>/dev/null | head -25 || echo "(none running)"

sec "Sockets"
for s in "/var/run/docker.sock" "$HOME/.docker/run/docker.sock" \
         "$HOME/.docker/desktop/docker.sock" "$HOME/.docker/desktop/docker-cli.sock"; do
  if [ -S "$s" ]; then echo "  socket present : $s"
  elif [ -e "$s" ]; then echo "  exists, NOT a socket: $s"
  else echo "  absent         : $s"; fi
done

sec "Disk space (Docker's VM disk lives on the data volume)"
run df -h /
run df -h /System/Volumes/Data

sec "Docker Desktop application state"
DATA="$HOME/Library/Containers/com.docker.docker/Data"
echo "  data dir: $DATA"
[ -d "$DATA" ] && du -sh "$DATA" 2>/dev/null
for f in "$HOME/Library/Group Containers/group.com.docker/settings-store.json" \
         "$HOME/Library/Group Containers/group.com.docker/settings.json"; do
  [ -f "$f" ] && { echo "  settings file: $f"; }
done
VMDISK="$DATA/vms/0/data/Docker.raw"
[ -f "$VMDISK" ] && echo "  VM disk: $(du -h "$VMDISK" 2>/dev/null | cut -f1) at $VMDISK"

sec "Docker Desktop host log (last 60 lines)"
LOGDIR="$DATA/log/host"
if [ -d "$LOGDIR" ]; then
  # Newest log file first; this is where licence/sign-in/engine-start errors land.
  NEWEST=$(ls -t "$LOGDIR"/*.log 2>/dev/null | head -1)
  if [ -n "$NEWEST" ]; then
    echo "  file: $NEWEST"
    tail -60 "$NEWEST"
  else
    echo "  no *.log files in $LOGDIR"
    ls -t "$LOGDIR" 2>/dev/null | head -10
  fi
else
  echo "  no host log directory at $LOGDIR"
fi

sec "Docker Desktop backend log (last 40 lines)"
BLOG="$DATA/log/host/com.docker.backend.log"
[ -f "$BLOG" ] && tail -40 "$BLOG" || echo "  not present (may be rolled into the host log above)"

sec "Recent Docker crash reports"
ls -t "$HOME/Library/Logs/DiagnosticReports"/*ocker* 2>/dev/null | head -5 \
  || echo "  none"

echo
printf '%sDiagnostics complete — nothing was changed. Output: docker-diag-log.txt%s\n' "$BOLD" "$RESET"
