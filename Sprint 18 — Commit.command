#!/bin/bash
#
# Sprint 18 — commit the locale work and the CI workflow.
#
# Two commits: the feature, then the docs and CI. Same reason as Sprint 17 —
# a red gate should be attributable to code, not to a paragraph.
#
set -euo pipefail
cd "$(dirname "$0")" || exit 1
rm -f .git/index.lock

echo ">> before"
git --no-pager log --oneline -3
echo
git status --short --untracked-files=no

echo
echo ">> commit 1 of 2 — the locale work"
git add \
  services/catalog/src/Pim.Catalog.Api/Endpoints/ProductEndpoints.cs \
  services/catalog/src/Pim.Catalog.Application/Products/Planning/PlanningContracts.cs \
  services/catalog/src/Pim.Catalog.Application/Products/Planning/PlanningHandlers.cs \
  services/catalog/src/Pim.Catalog.Application/Products/Logistics/LogisticsContracts.cs \
  services/catalog/src/Pim.Catalog.Application/Products/Logistics/LogisticsHandlers.cs \
  services/catalog/src/Pim.Catalog.Application/Products/Localization/LocalizationContracts.cs \
  services/catalog/src/Pim.Catalog.Application/Products/Localization/LocalizationHandlers.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/ReadModels/ProductPlanningStore.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/ReadModels/ProductRelationshipStore.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/ReadModels/ProductApplicationStore.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/ReadModels/ProductLocalizationStore.cs \
  services/catalog/tests/Pim.Catalog.Application.Tests/Products/DetailTabLocaleTests.cs \
  scripts/smoke-api.sh

git commit -F - <<'MSG'
feat(catalog): display locale through the detail tabs (R-15 Phase 2)

Pick English, open a product, and Planning, Relationships, Applications
and Localization answer in English. Before this the grid obeyed the
selector and everything behind it stayed German — the half-migration
R-15 was opened for.

The rule the sprint turned on, because it decided every case:

    Labels follow the reader. Data does not.

A label is the name of a thing — an attribute's caption, an
application's name, a linked product's title. Which language it appears
in is a property of who is looking. A value is the content itself — a
measurement, an assignment, a translation. Moving those with a view
setting would mean one product reporting different facts to different
readers.

Moved:
  - Planning parameter labels (attribute captions, read-only tab)
  - Relationship target names, resolved against the TARGET's base
    locale, not this product's: two products in one catalogue can carry
    different masters, and links crossing that boundary are exactly
    where resolving against the wrong side shows up
  - Application option labels
  - Localization field labels

Deliberately not moved:
  - Planning parameter VALUES — a measurement is not a translation
  - The relationship link graph and the application assignment set —
    switching language must not appear to add or drop data
  - Localization's per-locale content, keyed by each row's own locale
  - Asset titles. asset_tr is written to the base locale only and no
    screen can edit a per-locale asset title, so migrating the read
    alone would let a steward edit in English, write to German, and on
    a product with an existing English row watch the edit vanish. Read
    and write move together or not at all. Tracked as R-18; it needs a
    decision, not a patch.

Eight endpoints bind ?locale= — four reads and four writes. The writes
carry it because a save answering in a different language from the form
it was sent from flips the tab under the user; the write itself has no
language. One exception: PUT /{code}/localization/{locale} already
spends 'locale' on the route segment naming the language being EDITED,
so the display locale travels as ?displayLocale= there, and the command
spells the two apart for the same reason.

Verification. Every new smoke assertion fetches the SAME endpoint twice
in two locales and requires the labels to differ, rather than checking a
written-down string: no literal means a regenerated catalogue cannot rot
it, and each one fails against the previous code, which returned
base-locale text whatever was asked for. The mirror assertions matter
as much — values, assignments, link counts and per-locale content are
required to be identical across the two locales, which is what would
catch an over-eager future migration.

One stale assertion found: 'app: German label present' hard-coded
"Gewerbe" while requesting no locale, so it had been asserting the
default rather than the data. Named its locale rather than deleting it.

Verified on the dev Mac, 2026-08-18:
  gate  GREEN — 156 domain + 124 application + 62 integration + 260 frontend
  smoke GREEN — 399/399
  perf  GREEN — 24/24 at 1,503 products
MSG
echo "   commit 1 done"

echo
echo ">> commit 2 of 2 — docs and the CI workflow"
git add docs/DELIVERY_STATUS.md pipelines/github-actions-ci.yml
git add -u
git commit -F - <<'MSG'
docs(sprint-18): close R-15 for labels, open R-18; author CI

R-15 is closed for labels across all four remaining detail tabs. It
carries forward only as R-18, the asset-title exception, which is a
decision about where per-locale asset editing belongs rather than work
that was skipped.

CI is authored at pipelines/github-actions-ci.yml and NOT installed.
.github/workflows/ is write-protected against remote tooling — correctly,
since a workflow file executes code — so installing it is deliberately a
human step:

    mkdir -p .github/workflows
    cp pipelines/github-actions-ci.yml .github/workflows/ci.yml

The workflow has never executed. Nothing about it is verified, and the
first run should be treated as the test of the workflow rather than of
the code.

Its shape follows from Sprint 17 rather than from the Azure pipeline it
replaces. Of the six defects that reached a commit there, four would
have been caught by build, one by test against a real Postgres, and one
— the API refusing to start over an unregistered DI dependency — only by
starting the process. So the workflow runs the smoke suite, and runs it
by calling scripts/smoke-api.sh rather than reproducing its setup, so
that CI and the dev Mac cannot quietly diverge.
MSG
echo "   commit 2 done"

echo
echo ">> after"
git --no-pager log --oneline -5
echo
git status --short --untracked-files=no
echo
echo "=============================================="
echo " Committed. Nothing has been pushed."
echo " You can close this window."
echo "=============================================="
