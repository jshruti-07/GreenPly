#!/bin/bash
#
# Commits Sprint 26 — asset ingest and renditions. Local only, nothing pushed.
#
set -u
cd "$(dirname "$0")" || exit 1
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

# Mirror everything to a log so the result can be read back without
# screen-scraping Terminal.
exec > >(tee "sprint26-commit-log.txt") 2>&1

# The Linux-side bridge cannot delete files, so a git command run from there
# leaves its lock and temp objects behind. Clear them before anything else --
# a stale index.lock makes every git write below fail with "File exists".
rm -f .git/index.lock
find .git/objects -name 'tmp_obj_*' -delete 2>/dev/null

rm -rf _to_delete docs/_to_delete scripts/_to_delete frontend/_to_delete frontend/src/design/_to_delete 2>/dev/null

git add \
  services/catalog/Directory.Packages.props \
  services/catalog/src/Pim.Catalog.Infrastructure/Pim.Catalog.Infrastructure.csproj \
  services/catalog/src/Pim.Catalog.Domain/Assets \
  services/catalog/src/Pim.Catalog.Application/AssetLibrary \
  services/catalog/src/Pim.Catalog.Infrastructure/Storage \
  services/catalog/src/Pim.Catalog.Infrastructure/Assets \
  services/catalog/src/Pim.Catalog.Infrastructure/ReadModels/AssetIngestStore.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/ReadModels/AssetLibraryStore.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/CatalogModule.cs \
  services/catalog/src/Pim.Catalog.Api/Endpoints/AssetIngestEndpoints.cs \
  services/catalog/src/Pim.Catalog.Api/Program.cs \
  services/catalog/tests/Pim.Catalog.Application.Tests/Auditing/AuditCoverageTests.cs \
  frontend/src/features/assets \
  scripts/smoke-api.sh \
  scripts/perf-check.sh \
  "Sprint 26 — Add Image Package.command"

git commit -q -m "feat(assets): ingest, checksum de-duplication and real renditions

The PIM now holds its own binaries. Upload, SHA-256 de-duplication, and
thumbnail/web derivatives that are generated rather than declared -- which is
what Sprint 25 found the seed had been faking.

SkiaSharp 4.151.1, chosen because it is MIT licensed. SixLabors.ImageSharp moved
to the Six Labors Split License at v3: free for open source and small companies,
commercial licence above a revenue threshold. That is a cost decision for the
client and not one to take quietly inside a sprint. The version was resolved by
'dotnet add package' rather than inferred -- Sprint 15 guessed two pins blind
and got lucky, which is not a habit worth repeating.

What the pipeline does, and why each part is the way it is:

  * Streams to a temp file while hashing. The size is MEASURED, not read from
    Content-Length, and the cap is enforced during the read -- reading a hostile
    40 GB body to completion in order to reject it is itself the attack.
  * Looks up the checksum before writing anything, so a re-upload returns the
    existing asset with 200 rather than a second copy of the same bytes.
  * Content-addressed keys, so no caller input becomes part of a path. The
    traversal guard compares the RESOLVED path against the root rather than
    looking for '..' in a string.
  * Writes to a .partial file and moves it into place: the key is a checksum, so
    a truncated file would masquerade as verified content for ever.
  * Generates thumb (320px) and web (1600px) and never upscales. Re-encoding
    drops EXIF, which removes camera serials and GPS from storefront images.
  * Refuses to decode above 120 megapixels -- a 60 KB PNG can declare
    40,000x40,000 and cost 6.4 GB of RAM.

BIM and CAD are stored and served as opaque binaries. Asking for a thumbnail of
an IFC returns the file rather than 404: there is no thumbnail of a Revit family
and there never will be.

The project's own guardrails caught three defects I did not: an error factory
that dereferenced a parameter the taxonomy suite passes as null, an error typed
Failure (which maps to 500, and was unused, so deleted), and AuditCoverageTests
refusing UploadAssetCommand until it was exempted WITH A REASON -- the audit
trail is product-keyed and an uploaded asset belongs to no product yet (R-25).

The 'Not shown yet' panel also had to stop declaring Upload and Renditions
missing now that they exist. A smoke assertion now fails if a shipped capability
is ever listed as absent again.

NO AUTHENTICATION on the upload endpoint (R-3 is still open). Accepted for local
development only and recorded as R-24 -- now the most serious item on the risk
register.

Gate 353 backend / 312 frontend. Smoke 590/590. Perf 36/36." || { echo "feature commit failed"; exit 1; }

echo "committed: $(git log --oneline -1)"

git add docs/DELIVERY_STATUS.md docs/RELEASE_NOTES.md "Sprint 26 — Commit.command"

git commit -q -m "docs: record Sprint 26, and a new risk about asset provenance

R-25: asset history has nowhere to live. The audit trail is keyed by product,
and an uploaded asset belongs to no product until it is linked, so
UploadAssetCommand is explicitly exempt. With R-3 also open there is no actor
either -- so nobody can answer 'who uploaded this file, and when'. The fix is an
audit surface keyed by entity type and id, which audit_log already has columns
for, and it is worth doing alongside authentication rather than before it.

R-24 moves from anticipated to LIVE: the unauthenticated upload endpoint now
exists. R-23 half closes -- the PIM stores its own binaries, but 2,129 imported
assets still resolve through Magento URLs this project does not control.

Also records two of my own assertions that were measuring nothing: a rendition
check that passed against an absent derivative, and a perf case that skipped
every run because uploads sort after the imported catalogue by file name." || { echo "docs commit failed"; exit 1; }

echo "committed: $(git log --oneline -1)"
echo
git status --short
echo
git log --oneline -4
echo
echo "Nothing was pushed. You can close this window."
