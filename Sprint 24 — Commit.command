#!/bin/bash
#
# Commits Sprint 24 — the Translation Workbench. Local only, nothing pushed.
#
# Two commits: the feature, then the documentation. A docs change buried in a
# feature commit is invisible in review, and the reverse is worse.
#
set -u
cd "$(dirname "$0")" || exit 1
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

rm -rf docs/_to_delete scripts/_to_delete frontend/_to_delete frontend/src/design/_to_delete 2>/dev/null

git add \
  services/catalog/src/Pim.Catalog.Application/Translations \
  services/catalog/src/Pim.Catalog.Infrastructure/ReadModels/TranslationWorkbenchStore.cs \
  services/catalog/src/Pim.Catalog.Api/Endpoints/TranslationEndpoints.cs \
  services/catalog/src/Pim.Catalog.Api/Program.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/CatalogModule.cs \
  frontend/src/features/translation \
  frontend/src/features/products/ProductDetailPage.tsx \
  frontend/src/App.tsx \
  scripts/smoke-api.sh \
  scripts/perf-check.sh

git commit -q -m "feat(translations): the Translation Workbench, and a tab you can link to

Fifth of fifteen top-level screens, and the one that makes Sprint 19's
trilingual catalogue actionable: how far each language has got, broken down by
the KIND of work outstanding, and a filtered queue of the products holding it up.

'Outdated' is the only figure here that is not simply counted: the base-locale
text was edited after the translation of it (target.updated_at < base.updated_at
on product_tr). The domain already stamps that column on every write, so editing
a German master immediately marks its English and French copies stale with no
extra bookkeeping.

It is also the figure most at risk of being structurally zero — a fresh seed
writes every locale in one transaction, so nothing is outdated and a broken
query would look identical to a correct one (R-19). The smoke suite therefore
does not assert the number, it moves it: rename a product's base text, then
require that the count rose, that it rose because of THAT product, and that the
summary and the queue agree afterwards. Measured 0 -> 1.

Three decisions worth keeping:

  * The queue is a filter over products, not a separate list. state=all returns
    the whole catalogue and the smoke suite asserts exactly that against the
    products API.
  * 'attention' is asserted as a union, not a sum — a row can be outdated AND
    unapproved, and an equality would have passed on this seed by luck.
  * The base locale is rejected as a target rather than answered with an empty
    queue, because an empty queue reads as 'nothing left to translate'.

Product detail now keeps its open tab in the URL (?tab=localization) so the
queue can send someone straight to where the fix is made. An absent or
unrecognised tab falls back to General, so every existing test passes unchanged.

Gate 353 backend / 297 frontend. Smoke 527/527. Perf 32/32." || { echo "feature commit failed"; exit 1; }

echo "committed: $(git log --oneline -1)"

git add docs/DELIVERY_STATUS.md docs/RELEASE_NOTES.md "Sprint 24 — Commit.command"

git commit -q -m "docs: record Sprint 24, and a new risk about the translation vocabulary

Scope burn-down moves to 5 of 15 top-level screens. Data Quality is re-described
in the remaining work: its inputs all exist, what it lacks is a rules concept,
so it would ship smaller than v4.2 draws it — worth knowing before it is
scheduled rather than after.

New risk R-22: product_tr.translation_status has two vocabularies. The schema
comment says missing/machine/human/approved; the client's seed contains
approved/pending/draft. The workbench passes the stored value through untouched
and derives only 'missing' and 'outdated' on top, because reclassifying rows
this service does not own would make the screen disagree with the data. Closing
it is a decision about the editorial process plus a CHECK constraint, not a code
change." || { echo "docs commit failed"; exit 1; }

echo "committed: $(git log --oneline -1)"
echo
git status --short
echo
git log --oneline -4
echo
echo "Nothing was pushed. You can close this window."
