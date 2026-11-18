#!/bin/bash
#
# Commits the documentation updates for Sprints 21-23. Local only, nothing
# pushed. Separate from the code commits on purpose: a documentation change that
# turns up inside a feature commit is invisible in review, and a feature change
# that turns up inside a documentation commit is worse.
#
set -u
cd "$(dirname "$0")" || exit 1
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

rm -rf docs/_to_delete frontend/_to_delete scripts/_to_delete frontend/src/design/_to_delete 2>/dev/null

git add docs/DELIVERY_STATUS.md docs/RELEASE_NOTES.md
git add "Sprint 23 — Commit Docs.command" 2>/dev/null

git commit -q -m "docs: record Sprints 21-23, and correct two stale release-note headers

DELIVERY_STATUS gains the Categories and Attribute Library entries, the scope
burn-down moves from 2 of 15 top-level screens to 4, and the remaining eleven
are split into the ones that are thin over data that already exists
(Classifications, Translation, Data Quality) and the ones that need a service
that does not (Channels, Workflows, anything under Administration, which needs
authentication first).

New risk R-21: coverage figures are meaningless without their denominator, and
the denominator is easy to widen by accident. Three screens now compute one
against three different populations, all rendered as the same bar. Mitigated by
carrying the denominator in every payload and rendering it in the accessible
label; open as a habit, because nothing stops a fourth screen inventing a
fourth denominator.

RELEASE_NOTES gains entries for 21-23 and a summary table for 17-20, which
shipped without their own. The Sprint 15 and 16 headers said NOT VERIFIED; they
were verified in Sprint 17 and have said otherwise for six sprints. Corrected
with a dated note rather than a silent edit — the original text stays as
written, because a note recording what was believed at the time is worth more
than one edited to look prescient." || { echo "nothing to commit, or commit failed"; exit 1; }

echo "committed: $(git log --oneline -1)"
echo
git status --short
echo
echo "Nothing was pushed. You can close this window."
