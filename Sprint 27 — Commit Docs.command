#!/bin/bash
# Follow-up docs commit for Sprint 27: the scope burn-down sections, edited
# after the main docs commit had already run.
set -u
cd "$(dirname "$0")" || exit 1
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"
exec > >(tee "sprint27-commit-log.txt") 2>&1

rm -f .git/index.lock
find .git/objects -name 'tmp_obj_*' -delete 2>/dev/null

git add docs/DELIVERY_STATUS.md "Sprint 27 — Commit Docs.command"
git commit -q -m "docs: Workflow leaves the blocked list in the scope burn-down

Thirteen of fifteen detail tabs, not twelve. The Workflows & Tasks SCREEN is
still a placeholder -- the board is Sprint 28 -- but it is no longer waiting on
a service that does not exist, and the burn-down said it was." || { echo "commit failed"; exit 1; }

echo "committed: $(git log --oneline -1)"
echo
git status --short
echo
git log --oneline -3
echo
echo "Nothing was pushed. You can close this window."
