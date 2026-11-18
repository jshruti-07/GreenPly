#!/bin/bash
# =============================================================================
# Docker Disk Check.command  —  READ-ONLY
#
# Docker Desktop's engine fails with:
#   ensuring disk: cannot resize ".../vms/0/data/Docker.raw" to 395700MiB:
#   truncate ...: permission denied
#
# EPERM on truncate() has a small set of causes. This narrows them down:
#   - a BSD file flag (uchg / schg / restricted) on the image or its parents
#   - wrong ownership or mode
#   - an ACL denying write/append
#   - a configured virtual-disk size larger than the volume can offer
#
# Nothing is modified. Output: docker-disk-log.txt
# =============================================================================

set -uo pipefail
cd "$(dirname "$0")" || { echo "FATAL: cannot cd to script directory"; exit 1; }
exec > >(tee "docker-disk-log.txt") 2>&1

BOLD=$'\033[1m'; RESET=$'\033[0m'
sec() { printf '\n%s----- %s -----%s\n' "$BOLD" "$1" "$RESET"; }

DATA="$HOME/Library/Containers/com.docker.docker/Data"
RAW="$DATA/vms/0/data/Docker.raw"

echo "Docker disk check — $(date)"

sec "Who am I"
id

sec "The disk image"
if [ -e "$RAW" ]; then
  # -O prints BSD flags; uchg/schg/restricted all produce EPERM on truncate.
  ls -lO "$RAW"
  echo
  stat -f 'owner=%Su group=%Sg mode=%Sp logical_size=%z bytes flags=%Sf' "$RAW"
  echo
  echo "actual blocks used:"
  du -h "$RAW"
else
  echo "  MISSING: $RAW"
fi

sec "ACLs on the image"
ls -le "$RAW" 2>/dev/null || echo "  (none / unreadable)"

sec "Parent directory chain (flags + mode)"
p="$RAW"
while [ "$p" != "$HOME" ] && [ "$p" != "/" ]; do
  p=$(dirname "$p")
  ls -ldO "$p" 2>/dev/null
done
ls -ldO "$HOME" 2>/dev/null

sec "Configured virtual disk size"
SETTINGS="$HOME/Library/Group Containers/group.com.docker/settings-store.json"
if [ -f "$SETTINGS" ]; then
  echo "  file: $SETTINGS"
  # Field name has varied across releases; show anything disk-related.
  grep -iE '"[^"]*(disk|Disk)[^"]*"[[:space:]]*:' "$SETTINGS" | head -20
else
  echo "  settings-store.json not found"
fi

sec "Free space vs requested 395700 MiB (~386 GiB)"
df -h /System/Volumes/Data
echo
echo "  requested : 395700 MiB = $((395700 / 1024)) GiB"
AVAIL_MIB=$(df -m /System/Volumes/Data | awk 'NR==2 {print $4}')
echo "  available : ${AVAIL_MIB} MiB = $((AVAIL_MIB / 1024)) GiB"
if [ "${AVAIL_MIB:-0}" -lt 395700 ]; then
  echo "  VERDICT   : requested size EXCEEDS free space"
else
  echo "  VERDICT   : free space is sufficient for the requested size"
fi

sec "Volume capabilities (sparse files need APFS)"
diskutil info /System/Volumes/Data 2>/dev/null \
  | grep -iE 'File System Personality|Type \(Bundle\)|Read-Only|Sparse' || echo "  (diskutil unavailable)"

echo
printf '%sCheck complete — nothing was changed. Output: docker-disk-log.txt%s\n' "$BOLD" "$RESET"
