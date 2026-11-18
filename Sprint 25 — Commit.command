#!/bin/bash
#
# Commits Sprint 25 — the Digital Assets library. Local only, nothing pushed.
#
set -u
cd "$(dirname "$0")" || exit 1
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

rm -rf _to_delete docs/_to_delete scripts/_to_delete frontend/_to_delete frontend/src/design/_to_delete 2>/dev/null

git add \
  services/catalog/src/Pim.Catalog.Application/AssetLibrary \
  services/catalog/src/Pim.Catalog.Infrastructure/ReadModels/AssetLibraryStore.cs \
  services/catalog/src/Pim.Catalog.Api/Endpoints/AssetLibraryEndpoints.cs \
  services/catalog/src/Pim.Catalog.Api/Program.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/CatalogModule.cs \
  services/catalog/db/init/02_dev_seed.sql \
  frontend/src/features/assets \
  frontend/src/design/components.css \
  frontend/src/App.tsx \
  scripts/smoke-api.sh \
  scripts/perf-check.sh

git commit -q -m "feat(assets): the Digital Assets library, and a rendition that stopped lying

Sixth of fifteen top-level screens. Every image, drawing, BIM model and document
the catalogue references, with the two questions no per-product screen can
answer: which assets are linked to nothing, and which products have no main
image.

THE RENDITION FIX is the part worth reading. asset_rendition holds derivatives
-- thumb, web, print -- and the seed inserted one row per image claiming
role='web' whose URI was the ORIGINAL file and whose format was hard-coded
'jpg'. ProductAssetStore and ProductPlanningStore both prefer that row over the
raw storage key, so for nine sprints the API has reported a web rendition for
1,969 images and served full-size originals. Nothing distinguished them, and a
storefront consuming it would put a 2.4 MB file behind a thumbnail.

The row is now labelled 'original', which is true, and its format is read off
the file name rather than assumed -- the export contains .png and .tif too.
Every asset response carries servedRendition naming which derivative the URL
points at, and the detail panel says in words that no web or thumbnail rendition
exists. Existing behaviour is unchanged: both read models already fell back to
storage_key, and the fake row's URI WAS storage_key.

Same defect class as Sprint 21's structurally-zero KPI, one layer down: a value
that could not be wrong because it was not really a value.

Verification is arithmetic rather than presence. Per-type facet counts must sum
to the library total; the sum of each type's own filtered query must equal the
unfiltered total; orphan + linked must equal all. Any filter drifting from the
figure that sent the user to it fails one of them.

BIM and CAD were already modelled and seeded (80 + 40) and reachable only
through one product's Planning tab. They are now browsable and filterable like
any other type. Upload is Sprint 26; previews are deliberately not planned,
because IFC/RVT/DWG rendering needs a CAD toolchain with its own licence.

Gate 353 backend / 309 frontend. Smoke 558/558. Perf 35/35." || { echo "feature commit failed"; exit 1; }

echo "committed: $(git log --oneline -1)"

git add docs/DELIVERY_STATUS.md docs/RELEASE_NOTES.md "Sprint 25 — Commit.command"

git commit -q -m "docs: record Sprint 25, and two risks the asset work makes concrete

R-23: the PIM owns no asset binaries. Every storage_key points into the client's
Magento MAM, so nothing here can produce a thumbnail, a web-sized image or a
channel-ready derivative -- the three things a DAM exists for. Sprint 26's
ingest addresses the first half; the 2,129 assets that still resolve through
someone else's URL need a migration decision with a cost attached.

R-24: upload will exist before authentication does. Accepted for local
development only, and recorded as a release blocker rather than a footnote --
the ingest endpoint must not be reachable from outside the dev machine until
R-3 closes. It also means the audit trail cannot say who uploaded what.

Scope burn-down moves to 6 of 15 top-level screens." || { echo "docs commit failed"; exit 1; }

echo "committed: $(git log --oneline -1)"
echo
git status --short
echo
git log --oneline -4
echo
echo "Nothing was pushed. You can close this window."
