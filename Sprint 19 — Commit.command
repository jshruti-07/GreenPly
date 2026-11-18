#!/bin/bash
#
# Sprint 19 — commit the French catalogue, R-18 and the container test.
#
# RUN THIS ONLY AFTER 'Sprint 17 — Gate + Smoke + Perf.command' IS GREEN.
# Two smoke assertions were fixed after the last run and have not been
# re-executed; committing before that would record a result nobody has seen.
#
set -euo pipefail
cd "$(dirname "$0")" || exit 1
rm -f .git/index.lock

if ! grep -q "API SMOKE: GREEN" smoke-log.txt 2>/dev/null; then
  echo
  echo "STOP: smoke-log.txt does not say GREEN."
  echo "Run 'Sprint 17 — Gate + Smoke + Perf.command' first."
  echo
  exit 1
fi
if ! grep -q "PERF: GREEN" perf-log.txt 2>/dev/null; then
  echo
  echo "STOP: perf-log.txt does not say GREEN."
  exit 1
fi
echo "smoke and perf both report GREEN — proceeding"

echo
echo ">> commit 1 of 2 — the French catalogue"
git add services/catalog/db/init/03_dev_seed_i18n.sql

git commit -F - <<'MSG'
feat(seed): a French catalogue, so three languages can actually be tested

fr-FR had been configured in catalog.locale since the schema was written
and had zero rows in every _tr table. French was selectable and
completely empty, which meant the language work could only ever be
demonstrated in the two languages the client export happens to carry --
and a bug that assumes exactly two would never have surfaced.

The client's export has no French: every translated column in it is [DE]
or [EN]. So this had to come from us, which runs against D-2026-08-16-a
(the seed is derived, never invented). It therefore ships as a SEPARATE
file. Anything in 02 came from the client, anything in 03 did not, and
deleting one file removes all of it.

Two mechanisms, because two kinds of text:

  - Reference vocabulary -- 8 groups, 32 attribute labels, 18
    applications, 23 categories, 18 options -- is translated term by
    term. It is a closed set of standard ceramic and construction
    vocabulary.

  - Product names and colours are DERIVED at apply time from the English
    rows. That is the part worth keeping: because the French is computed
    rather than pasted in, a refreshed client export regenerates
    02_dev_seed.sql and this file still produces correct French for it,
    with no second regeneration and no stale rows pointing at products
    that no longer exist.

One product in five is left untranslated on purpose. A catalogue
mid-translation is the normal state of a PIM, and it is the only way to
see the fallback marker work -- the grid in French shows real French
names beside German ones flagged as fallbacks.

The colour lookup maps WHOLE LABELS, not words. The first version
substituted word by word and produced French that was wrong in a way no
row count would catch: "stone-grey" became "pierre-gris" and "chalk
white" became "chalk blanc", because French puts the adjective after the
noun. Every one of those would have satisfied a "labels differ between
locales" assertion while being unreadable. A colour now either has a
correct French name or keeps its English one, which is what real
catalogues do with marketing coinages anyway.

The file gates itself. It fails the load if the pack is empty, if every
product got translated (the fallback path would be untestable), if fewer
than half did, if a French name still contains English technical
vocabulary, or if the colour lookup has been gutted. That check caught
three real ordering bugs in the phrase table while it was being written
-- 110 leaked names, then 75, then 24 -- each one a short phrase
consuming text a longer one needed.

Measured on the seeded catalogue: 940 of 1,171 product names translated,
231 left as fallbacks, 43 of the 60 colours in use given French names.
MSG
echo "   commit 1 done"

echo
echo ">> commit 2 of 2 — R-18, the container test, and the docs"
git add -A

git commit -F - <<'MSG'
feat(catalog): per-locale asset titles (R-18) and a container-validation test

R-18 CLOSED. Sprint 18 refused to migrate asset titles and said why:
asset_tr was written to the base locale only, so making the read follow
the selector would have let a steward edit a title in English, silently
write it to German, and watch the edit vanish on a product that already
had an English row.

Both halves move together now. The write targets the locale the steward
is looking at, the read renders that locale with a base-locale fallback,
and the Planning tab's geometry titles follow the same rule so the same
asset cannot read differently on two tabs. Five asset endpoints bind
?locale=, and on the writes it is not presentation -- it is the row key,
so an unconfigured locale is rejected before anything is persisted.

Verified directly against PostgreSQL: writing a French title creates only
the fr-FR row, reading in French returns it, and the German and English
rows come back byte-identical. That last one is the invariant R-18
existed for.

The resolution differs from what the risk register proposed. Per-locale
asset text is edited on Media & Documents, where the title already lives,
rather than being moved to the Localization tab -- that kept the change
to signatures and one SQL statement instead of adding N assets x M
locales to a tab designed around one product's copy.

CONTAINER VALIDATION. Two tests, a few milliseconds, no database. They
build the composition root the API builds and fail if anything cannot be
constructed -- precisely the Sprint 17 defect that 333 unit tests missed
and that cost a full smoke run to find. Unit tests cannot see it and it
is not their fault: they construct handlers with new, which is the right
way to test a handler and exactly why a missing registration is invisible
to them. The composition root is a separate thing and now has its own
test. Both passed first time, which answers a fair question about the
last two sprints -- after ~30 signature changes the dependency graph is
still sound.

Two smoke assertions written this sprint were themselves defective and
are fixed here: one pair requested pageSize=200 against a documented
maximum of 100, and one pair shadowed jsonq's argument array with a find
callback parameter. The second matters more than it looks -- one of that
pair passed while comparing undefined against undefined, so it was being
counted as evidence for R-18 while measuring nothing at all. R-10, again.
MSG
echo "   commit 2 done"

echo
git --no-pager log --oneline -5
echo
git status --short --untracked-files=no
echo
echo "=============================================="
echo " Committed. Nothing has been pushed."
echo "=============================================="
