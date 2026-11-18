#!/bin/bash
# Records the 2026-08-19 planning decisions. Docs only — no code changed.
set -u
cd "$(dirname "$0")" || exit 1
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"
exec > >(tee "sprint29-commit-log.txt") 2>&1

rm -f .git/index.lock
find .git/objects -name 'tmp_obj_*' -delete 2>/dev/null
rm -f sprint28-commit-log.txt 2>/dev/null

git add docs/DELIVERY_PLAN.md "Sprint 29 — Commit Decisions.command"
git commit -q -m "docs: record the 2026-08-19 planning decisions and re-sequence what is left

Four decisions, and a correction the plan needed anyway.

D-2026-08-19-c defers SYSTEMS & ASSEMBLIES to Phase 2. Worth stating why, because
the plan has carried it as 'no blocker, not started' for fourteen sprints and
that was only half true. The catalogue does contain six real pool-edge systems --
Bamberg, Finnland, Finnland II, Wiesbaden D/E/S, 113 products between them --
but they are modelled as taxonomy series, and the client export carries no bill
of components: no roles, no quantities, no certified combinations. Building the
screen the prototype draws would mean inventing that data, which D-2026-08-16-a
forbids. Whether a pool-edge series IS a system in the client's sense is
undecided, so the honest move is to stop calling it unblocked.

D-2026-08-19-d names Classifications & Standards as the next screen and leaves
its SCOPE open. Exactly one product in this catalogue is classified -- the ETIM,
BMEcat, UNSPSC, IFC and OmniClass mappings in the seed are hand-written
reference data for the lead product, and the client's export has no
classification columns at all. A read-only screen would therefore report about
0.2% against a prototype that draws 94%. Whether the sprint should also close
that gap is under discussion; no code has been written.

The blocked-tabs table also stops claiming Workflow is blocked on a service
somebody else has to build. It was five tables and a state machine, and it
shipped in Sprints 27-28.

Ahead of every sprint in the new sequence, and not a sprint: push to GitHub and
install CI. Outstanding since Sprint 17." || { echo "commit failed"; exit 1; }

echo "committed: $(git log --oneline -1)"
echo
git status --short
echo
git log --oneline -3
echo
echo "Nothing was pushed. You can close this window."
