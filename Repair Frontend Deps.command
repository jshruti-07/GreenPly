#!/bin/bash
# Repairs node_modules corrupted by iCloud, WITHOUT waiting on a delete.
#
# Why not rm -rf first: this repo is in iCloud-synced ~/Documents. iCloud
# re-materialises files while rm removes them, so deleting a corrupt
# node_modules can run for tens of minutes and never finish.
#
# Instead: rename it aside (a rename is instant), install into a directory
# iCloud refuses to sync (.nosync), then delete the old tree in the background.
set -u
cd "$(dirname "$0")/frontend" || exit 1

STAMP=$(date +%s)
TRASH="node_modules.trash-$STAMP.nosync"

if [ -e node_modules ] || [ -L node_modules ]; then
  echo "== Moving the corrupt tree aside (instant) =="
  mv node_modules "$TRASH" 2>/dev/null || rm -f node_modules
fi

# ORDER MATTERS, and the obvious order is wrong.
#
# This used to create the symlink FIRST and let npm install through it. That
# does not work and never did: `npm ci` deletes node_modules before installing,
# so it removes the symlink and writes a real directory in its place. The
# arrangement this script exists to create was therefore destroyed by the very
# command it ran next -- which is why R-9 kept recurring after being marked
# mitigated, and why the delivery status has been describing a symlink that was
# not there. Verified 2026-08-18: after a "successful" repair, node_modules was
# a real 215-entry directory and node_modules.nosync was empty.
#
# So: install first, relocate after. npm gets the plain directory it insists on,
# and the tree ends up somewhere iCloud will not touch.
echo "== Installing =="
rm -rf node_modules.nosync 2>/dev/null
npm ci
STATUS=$?

if [ $STATUS -eq 0 ]; then
  echo "== Moving the tree out of iCloud's reach (.nosync) =="
  mv node_modules node_modules.nosync && ln -sfn node_modules.nosync node_modules
  if [ -L node_modules ] && [ -d node_modules.nosync ]; then
    echo "   node_modules -> node_modules.nosync ($(ls node_modules.nosync | wc -l | tr -d ' ') packages)"
  else
    echo "   WARNING: relocation failed; the tree is still syncing to iCloud."
    STATUS=1
  fi
fi

if [ -d "$TRASH" ]; then
  echo "== Deleting the old tree in the background =="
  ( rm -rf "$TRASH" >/dev/null 2>&1 & ) 
fi

echo
if [ $STATUS -eq 0 ]; then
  echo "Repair complete. node_modules -> node_modules.nosync"
  echo
  echo "NOTE: the next 'npm ci' -- including the one inside the gate -- will"
  echo "delete this symlink and install a real directory again, because that is"
  echo "what npm ci does. This script has to be re-run whenever iCloud corrupts"
  echo "the tree. The only durable fix is moving the repo out of iCloud (R-1)."
  echo
  echo "Next: run 'Verify PIM.command'."
else
  echo "npm ci failed (exit $STATUS) — see output above."
fi
echo "You can close this window."
exit $STATUS
