#!/bin/bash
#
# Commits Sprints 21 and 22 as two separate commits. Local only — nothing is
# pushed, no remote is contacted.
#
# Two commits rather than one, for the reason Sprint 17 learned the hard way
# (R-16): when a gate goes red against a single commit holding two features,
# every failure needs bisecting by hand. Split, and each one is attributable on
# sight.
#
# Runs on the Mac rather than through the file bridge because git writes leave a
# .git/index.lock that the bridge cannot remove.
#
set -u
cd "$(dirname "$0")" || exit 1
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

echo "== Working tree before =="
git status --short
echo

# Scratch directories created while working on the user's machine: the bridge
# cannot delete files, so removals are staged into _to_delete/ instead. They are
# not part of the source tree and must not enter history.
for d in frontend/src/design/_to_delete scripts/_to_delete; do
  [ -d "$d" ] && echo "note: $d contains files the bridge could not delete — not committed"
done
cat > .git/info/exclude.tmp <<'EOF'
_to_delete/
EOF
grep -q "_to_delete/" .git/info/exclude 2>/dev/null || cat .git/info/exclude.tmp >> .git/info/exclude
rm -f .git/info/exclude.tmp

# ---------------------------------------------------------------- Sprint 21 --
git add \
  services/catalog/src/Pim.Catalog.Application/Dashboard \
  services/catalog/src/Pim.Catalog.Infrastructure/ReadModels/DashboardStore.cs \
  services/catalog/src/Pim.Catalog.Api/Endpoints/DashboardEndpoints.cs \
  frontend/src/features/dashboard \
  scripts/lib \
  scripts/verify-local.sh \
  "Sprint 21 — Gate + Smoke + Perf.command" \
  "Sprint 21 — Perf Only.command"

git commit -q -m "feat(dashboard): the Product Cockpit, and a gate that was measuring the wrong thing

v4.2's first screen and the first item in the client's Phase 1 estimate. Every
figure comes from one request, so the panels cannot disagree about when they
were computed. Panels the design specifies but this build cannot honestly
populate — channel errors, workflow tasks — are returned in omittedPanels with
the reason and rendered, because an absent chart reads as zero.

Two defects found by cross-checking the cockpit against the products grid,
neither visible on screen nor to any of the 353 backend tests:

  * 'Ready to publish' filtered workflow_state = 'approved', a value the domain
    never writes (draft/review/approval/published). The KPI was structurally
    zero — a number that could not be wrong because it could not be anything.
  * The workflow pipeline was three hand-written stages against a four-state
    domain: 'published' missing, 'approved' matching nothing. It showed 251 of
    501 products and looked complete. Stages now derive from the enum, and an
    unrecognised state is surfaced rather than dropped.

Also fixes a readiness check that had been racing since Sprint 0.
pg_isready with no -h uses the Unix socket, which the postgres image's
init-time server accepts while db/init is still running — so the gate asserted
the seed at ~1s into init and read 0 products. Verified against an isolated
cluster (listen_addresses='' → socket yes, TCP no) and against the image's own
entrypoint. scripts/lib/db-wait.sh now waits for TCP, then for the seed.

Gate green: 353 backend / 267 frontend. Smoke 446/446. Perf 26/26." || {
  echo "Sprint 21 commit failed"; exit 1; }

echo "committed: $(git log --oneline -1)"
echo

# ---------------------------------------------------------------- Sprint 22 --
git add \
  services/catalog/src/Pim.Catalog.Application/Categories \
  services/catalog/src/Pim.Catalog.Infrastructure/ReadModels/CategoryStore.cs \
  services/catalog/src/Pim.Catalog.Api/Endpoints/CategoryEndpoints.cs \
  services/catalog/src/Pim.Catalog.Api/Program.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/CatalogModule.cs \
  services/catalog/db/init/01_catalog_schema.sql \
  frontend/src/features/categories \
  frontend/src/App.tsx \
  frontend/src/design/components.css \
  scripts/smoke-api.sh \
  scripts/perf-check.sh \
  docs/DELIVERY_STATUS.md \
  "Sprint 22 — Smoke Only.command"

git commit -q -m "feat(categories): the merchandising tree, read and navigable

Second of fifteen top-level screens. The taxonomy with per-node product counts
(direct and subtree), each node's label in every configured locale including
the ones with none, and the attribute template with real coverage over the
products beneath it.

Three decisions worth keeping:

  * The tree's shape does not depend on the reading language. Labels follow the
    reader (ADR-009); dropping untranslated nodes would make their products
    unreachable with nothing on screen to say why.
  * taxonomy_node.attribute_set_id — the DECLARED template — is not populated on
    a single node in the client's catalogue, so a panel reading only that column
    would be empty forever, and an empty template panel reads as 'this branch
    demands nothing'. It now falls back to the set the products underneath
    actually use and states which of the two it is showing. It refuses to merge
    several sets: that needs a rule for an attribute required in one and
    optional in another, and none has been agreed.
  * Adds a GiST index on taxonomy_node.path. Subtree containment drives every
    count on the screen; without it each node costs a sequential scan.

The smoke suite's first version of these assertions passed while measuring
nothing — it picked a one-product node with no template, so four checks ran over
an empty array and two more compared one jsonq error with an identical one. The
node is now chosen by probing for one that has a template, and a guard turns an
empty response into a single honest failure instead of twelve misleading ones.

Gate green: 353 backend / 276 frontend. Smoke 472/472. Perf 28/28." || {
  echo "Sprint 22 commit failed"; exit 1; }

echo "committed: $(git log --oneline -1)"
echo
echo "== Working tree after =="
git status --short
echo
echo "== Last five commits =="
git log --oneline -5
echo
echo "Nothing was pushed. You can close this window."
