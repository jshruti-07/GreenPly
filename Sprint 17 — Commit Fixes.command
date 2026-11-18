#!/bin/bash
#
# Sprint 17 — commit the six fixes the verification run found.
#
# One commit, not six: they are a single unit of work (making Sprints 15 and 16
# actually pass), and splitting them would imply each was independently
# verified, which it was not — only the final green run covers them.
#
set -euo pipefail
cd "$(dirname "$0")" || exit 1

rm -f .git/index.lock

echo ">> current state"
git --no-pager log --oneline -3
echo
git status --short

echo
echo ">> staging the source fixes"
git add \
  services/catalog/Directory.Packages.props \
  services/catalog/src/Pim.Catalog.Infrastructure/Pim.Catalog.Infrastructure.csproj \
  services/catalog/src/Pim.Catalog.Infrastructure/CatalogModule.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/ReadModels/LocaleCatalog.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/ReadModels/ProductListReader.cs \
  services/catalog/src/Pim.Catalog.Api/Endpoints/ProductEndpoints.cs \
  services/catalog/tests/Pim.Catalog.Application.Tests/Products/ExportProductsQueryTests.cs \
  services/catalog/tests/Pim.Catalog.Integration.Tests/Outbox/OutboxStoreTests.cs \
  scripts/smoke-api.sh

git commit -F - <<'MSG'
fix(catalog): make Sprints 15 and 16 pass their first real gate

Six defects, every one found by running the code rather than reading it.
Sprints 15 and 16 had never been compiled; this is what that cost.

Would not compile:

1. AddCatalogOutbox bound its options section with
   Configure<OutboxOptions>(IConfiguration), an overload that lives in
   Microsoft.Extensions.Options.ConfigurationExtensions — a package the
   Infrastructure project did not reference. Added, explicitly, for the
   same reason the other Microsoft.Extensions.* references are explicit.

2. ExportProductsQueryHandler gained an ILocaleCatalog and
   ProductListItemDto gained NameLocale/CategoryLocale in Sprint 16; the
   handler's own test file was never updated. Fixed, and three cases
   added for the behaviour Sprint 16 shipped without any handler-level
   coverage: the English default, the pass-through of a requested
   locale in its canonical spelling, and the rejection of an
   unconfigured one without touching the reader.

3. LocaleCatalog owned a SemaphoreSlim and was not disposable (CA1001).
   Now IDisposable. It does not dispose the NpgsqlDataSource it is
   handed — that is borrowed, and closing another component's pool is a
   worse bug than the one being fixed.

4. An OutboxStore test asserted ShouldContain on the nullable
   last_error column. Asserted non-null first.

Compiled, then failed:

5. ALL 20 ProductListReader integration tests failed at once. Dapper
   materialises positional records by matching constructor parameters to
   columns BY POSITION; Sprint 16 inserted name_locale/category_locale
   into the middle of the SELECT and appended them to the end of
   ProductRow. The two are one coupled unit and are now documented as
   such. This is R-12's shape again: a Dapper convention that fails
   every row rather than some.

Started, then behaved wrongly:

6. The API would not start at all. DisplayLocaleResolver is a
   constructor dependency of both attribute handlers and was never
   registered; ValidateOnBuild refused the container. Invisible to every
   unit test, because those construct handlers directly — only starting
   the process finds it.

7. Substring search silently stopped working across locales. The ILIKE
   branch was scoped to the display lateral's name, so a whole German
   word still matched (search_vector aggregates across locales) while a
   German fragment did not, as soon as the grid defaulted to English.
   Nobody decided that. ILIKE now searches every product_tr row:
   matching is locale-agnostic, rendering is not.

Also: the attributes endpoints never bound `locale`, though the query and
command have carried the parameter since ADR-009 and the handlers already
resolve it — so the tab was pinned to one language whatever the shell was
set to. Bound on both GET and PUT.

Smoke assertions corrected rather than deleted: three were written for a
German-default grid and were passing on a coincidence. They now name the
locale they mean, and assert both directions plus the rejection of an
unknown locale — the half that was never covered.

Verified on the dev Mac, 2026-08-18:
  gate  GREEN — 156 domain + 115 application + 62 integration + 260 frontend
  smoke GREEN — 385/385
  perf  GREEN — 24/24 at 1,503 products
MSG

echo
echo ">> untracking the run log my earlier 'git add -A' swept into Sprint 16"
git rm --cached --quiet sprint17-log.txt 2>/dev/null || true

echo
echo ">> staging the documentation (explicit list, no 'add -A' this time)"
git add \
  .gitignore \
  docs/DELIVERY_STATUS.md \
  "Sprint 17 — Gate + Smoke + Perf.command"

git commit -F - <<'MSG'
docs(sprint-17): record the verification run and close R-14 and R-16

Sprints 15 and 16 are Done rather than code complete: both have now
passed the gate, the smoke suite and the perf check on the dev Mac.

R-14 (Sprint 15 authored blind) and R-16 (two unverified sprints
stacked) are closed by that run. Both inferred package pins resolved on
the first restore, so the failure mode they were flagged for did not
happen — the failures were elsewhere, and worse, because they were the
kind no amount of review finds.

R-15 corrected: it claimed the attributes path still joined on
base_locale. It does not — Sprint 16 migrated it, and the gap was that
the endpoint never bound the parameter. The remaining detail-tab stores
are unchanged and still Phase 2.

R-1 upgraded from "costs time" to "threatens the repository": iCloud had
written nine conflict copies of the git index inside .git/, and a stale
index.lock had been blocking every write since 2026-08-17.

R-17 added: every read model couples its SELECT's column order to a
positional record's parameter order, ~20 of them, and nothing pins the
coupling except the integration suite — which does not cover every store
equally. Defect 5 is what that looks like when it fires.

Adds 'Sprint 17 — Gate + Smoke + Perf.command', which runs the three
suites in the only order that yields evidence: smoke against a build that
did not compile, or perf against a broken read model, is noise. The
one-shot scripts from this sprint are gitignored, as are the run logs —
one of which this commit untracks after an over-broad 'git add -A'.
MSG

echo
echo ">> result"
git --no-pager log --oneline -5
echo
git status --short
echo
echo "=============================================="
echo " Committed. Nothing has been pushed."
echo " You can close this window."
echo "=============================================="
