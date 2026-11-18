#!/bin/bash
# =============================================================================
# Fix Docker Disk Ownership.command
#
# DIAGNOSIS (from docker-disk-log.txt, 2026-08-21):
#   ~/Library/Containers/com.docker.docker/Data/vms/0/data/Docker.raw
#   is owned by  root:staff  mode -rw-r--r--
#   Docker Desktop runs as   apple (uid 502)
#   => truncate() on that file returns EPERM, and the engine aborts with
#      "ensuring disk: cannot resize ... permission denied"
#   No BSD flags, no ACLs, volume is writable APFS. Ownership is the whole cause.
#
# FIX APPLIED HERE (reversible, no password required):
#   The containing directory is owned by 'apple', so this script RENAMES the
#   root-owned image aside. Docker Desktop then creates a fresh one it owns.
#
#   NOTHING IS DELETED. The original is kept as
#     Docker.raw.root-owned.<timestamp>.bak
#   in the same directory (315 MB of actual blocks; the 386 GB figure is the
#   sparse logical size). To undo:
#     sudo mv <backup> Docker.raw
#
#   Consequence: Docker starts with an empty VM disk. Any images, containers
#   and volumes inside the old image will not be visible until it is restored.
#   The stack pulls its images fresh anyway.
#
# The alternative fix, which needs your admin password and which this script
# deliberately does NOT attempt, is:
#   sudo chown apple:staff <the Docker.raw path above>
# That one keeps the existing image contents. Prefer it if you have containers
# or volumes in there you care about — Ctrl-C now and run it instead.
#
# Log: docker-fix-log.txt
# =============================================================================

set -uo pipefail
cd "$(dirname "$0")" || { echo "FATAL: cannot cd to script directory"; exit 1; }
exec > >(tee "docker-fix-log.txt") 2>&1

BOLD=$'\033[1m'; RESET=$'\033[0m'
RED=$'\033[31m'; GREEN=$'\033[32m'; YELLOW=$'\033[33m'
ok()   { printf '  %sPASS%s  %s\n' "$GREEN" "$RESET" "$1"; }
warn() { printf '  %sWARN%s  %s\n' "$YELLOW" "$RESET" "$1"; }
fatal() { printf '\n%sFATAL:%s %s\n' "$RED" "$RESET" "$1"; exit 1; }

RAW="$HOME/Library/Containers/com.docker.docker/Data/vms/0/data/Docker.raw"
RAWDIR=$(dirname "$RAW")

printf '%s== Docker VM disk ownership ==%s\n' "$BOLD" "$RESET"
echo "  target: $RAW"

# --- guard 1: Docker must not be running, or it will fight us over the file ---
# com.docker.vmnetd is a root-launched privileged helper and is expected to be
# present whether or not Docker Desktop itself is running — ignore it.
if pgrep -f "com.docker.backend" >/dev/null 2>&1 \
   || pgrep -f "Docker Desktop.app" >/dev/null 2>&1 \
   || pgrep -f "/Applications/Docker.app/Contents/MacOS/Docker Desktop" >/dev/null 2>&1; then
  fatal "Docker Desktop is running. Quit it (menu-bar whale -> Quit Docker Desktop),
       wait for it to disappear from the menu bar, then run this again."
fi
ok "Docker Desktop is not running"

# --- guard 2: only act when the diagnosis actually holds --------------------
[ -e "$RAW" ] || fatal "No Docker.raw at that path. Nothing to fix here — if the engine
       still fails, re-run 'Diagnose Docker.command' and share the output."

OWNER=$(stat -f '%Su' "$RAW")
FLAGS=$(stat -f '%Sf' "$RAW")
echo "  owner: $OWNER   flags: ${FLAGS:--}"

if [ "$OWNER" = "$(id -un)" ]; then
  ok "already owned by $(id -un) — ownership is not the problem any more"
  echo
  echo "  Nothing changed. If the engine still will not start, the cause has moved;"
  echo "  re-run 'Diagnose Docker.command'."
  exit 0
fi

[ "$OWNER" = "root" ] || warn "owner is '$OWNER', not root — proceeding anyway, since it is not you"

if [ -n "${FLAGS}" ] && [ "${FLAGS}" != "-" ]; then
  fatal "The image carries BSD flags ('$FLAGS'). Renaming will not help and could
       mislead. Clear them first (sudo chflags nouchg ...) and re-run."
fi

# --- guard 3: the directory must be ours, else the rename cannot work -------
DIROWNER=$(stat -f '%Su' "$RAWDIR")
[ "$DIROWNER" = "$(id -un)" ] \
  || fatal "The containing directory is owned by '$DIROWNER', not you, so this
       password-free fix is not possible. Use instead:
         sudo chown $(id -un):staff \"$RAW\""

BACKUP="$RAW.root-owned.$(date +%Y%m%d-%H%M%S).bak"
echo
echo "  renaming aside (NOT deleting):"
echo "    from : $RAW"
echo "    to   : $BACKUP"
mv "$RAW" "$BACKUP" \
  || fatal "Rename failed. Fall back to: sudo chown $(id -un):staff \"$RAW\""
ok "original preserved at $BACKUP"
echo "  actual space it occupies: $(du -h "$BACKUP" 2>/dev/null | cut -f1)"

echo
printf '%s== Handing off to Start Docker and Run PIM.command ==%s\n' "$BOLD" "$RESET"
[ -x "./Start Docker and Run PIM.command" ] \
  || fatal "'Start Docker and Run PIM.command' is missing or not executable in $PWD"

exec "./Start Docker and Run PIM.command"
