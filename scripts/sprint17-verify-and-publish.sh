#!/usr/bin/env bash
#
# Sprint 17 — Verify & Publish
#
# RUN THIS ON YOUR MAC, in Terminal. It cannot be run from the Claude session:
# the mounted filesystem forbids unlink(), so every git command that takes the
# index lock leaves a stale .git/index.lock behind and the next one fails.
#
# What it does:
#   1. Deletes the debris Claude already quarantined into _to_delete/
#   2. Moves the repo out of iCloud            (optional — MOVE_OUT_OF_ICLOUD)
#   3. Commits Sprint 15 and Sprint 16 as two separate commits (risk R-16)
#   4. Compacts the object store
#   5. Prints the push commands (it does NOT push — that is your call)
#
# It does NOT run the .NET gate. Do that separately with:
#   "Verify PIM (reset db).command"
#
set -euo pipefail

MOVE_OUT_OF_ICLOUD="${MOVE_OUT_OF_ICLOUD:-1}"   # set to 0 to keep the repo where it is
NEW_HOME="${NEW_HOME:-$HOME/dev/pim-platform}"
REPO="${REPO:-$HOME/Documents/Claude/Projects/PIM project/pim-platform}"

say() { printf '\n\033[1m== %s\033[0m\n' "$*"; }
die() { printf '\033[31mFAIL: %s\033[0m\n' "$*" >&2; exit 1; }

[ -d "$REPO/.git" ] || die "no git repo at $REPO"
cd "$REPO"

# ---------------------------------------------------------------- 1. debris ---
say "1. Removing quarantined debris"
if [ -d _to_delete ]; then
  du -sh _to_delete
  rm -rf _to_delete
  echo "removed _to_delete/"
else
  echo "_to_delete/ already gone — nothing to do"
fi
rm -f .git/index.lock
# Belt and braces: any iCloud conflict copy that reappeared while you were away.
find .git -maxdepth 1 -name 'index [0-9]*' -delete 2>/dev/null || true
rm -rf "frontend/node_modules 2" 2>/dev/null || true

say "1b. Integrity check BEFORE anything destructive"
git fsck --full --no-progress || die "git fsck reported a problem — stop and investigate"
git status --short | head -50

# ------------------------------------------------------------------ 2. move ---
if [ "$MOVE_OUT_OF_ICLOUD" = "1" ]; then
  say "2. Moving the repo out of iCloud (risk R-1)"
  if [ -e "$NEW_HOME" ]; then
    die "$NEW_HOME already exists — move or rename it first"
  fi
  mkdir -p "$(dirname "$NEW_HOME")"
  # Copy first, verify, then remove the original. A straight mv across an
  # iCloud boundary can leave a half-materialised tree if it is interrupted.
  echo "copying… (this reads every file out of iCloud, so it will take a while)"
  cp -a "$REPO" "$NEW_HOME"
  cd "$NEW_HOME"
  git fsck --full --no-progress || die "the copy is not a valid repo — original left untouched at $REPO"
  echo "copy verified at $NEW_HOME"
  echo
  echo "The original is STILL at:"
  echo "  $REPO"
  echo "Delete it yourself once you are satisfied — this script will not."
  REPO="$NEW_HOME"
else
  say "2. Skipping the iCloud move (MOVE_OUT_OF_ICLOUD=0)"
fi

cd "$REPO"

# --------------------------------------------------------------- 3. commits ---
# Split rationale (risk R-16): Sprints 15 and 16 are both unverified. Committing
# them together means a red gate cannot be attributed to either. The file split
# below is clean — only CatalogModule.cs touches both sprints, and it is placed
# in the Sprint 16 commit so that the Sprint 15 commit still compiles on its own
# (the relay is present but not yet registered; the extension method is simply
# unused, which is not a compile error).

say "3a. Commit 1 of 2 — Sprint 15, transactional outbox (ADR-007)"
git add \
  docs/adr/ADR-007-transactional-outbox.md \
  services/catalog/Directory.Packages.props \
  services/catalog/db/init/01_catalog_schema.sql \
  services/catalog/src/Pim.Catalog.Api/Program.cs \
  services/catalog/src/Pim.Catalog.Api/appsettings.json \
  services/catalog/src/Pim.Catalog.Api/appsettings.Development.json \
  services/catalog/src/Pim.Catalog.Infrastructure/Outbox \
  services/catalog/src/Pim.Catalog.Infrastructure/Persistence/CatalogDbContext.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/Pim.Catalog.Infrastructure.csproj \
  services/catalog/tests/Pim.Catalog.Integration.Tests/Outbox

git commit -F - <<'MSG'
feat(catalog): transactional outbox (ADR-007) — UNVERIFIED

outbox_message table, enqueue inside SaveChangesAsync's transaction,
versioned wire contracts, and a relay with lease-based claiming,
per-aggregate ordering, exponential backoff and dead-lettering.
Health check and metrics included.

Deliberately does not deliver anything: IOutboxTransport has only a
logging implementation, so the health check reports Degraded until a
real transport is attached. See ADR-007 §Deferred.

NOT COMPILED OR TESTED. Authored without a .NET SDK, Docker or NuGet.
Two package pins are inferred: Microsoft.Extensions.Hosting.Abstractions
and Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions, both at
10.0.0 to match the other Microsoft.Extensions.* pins. If either version
does not exist, restore fails immediately and visibly.

DDL verified 2026-08-18 against PostgreSQL 16 (not 17): schema applies
clean, the four partial indexes and three check constraints are created
as designed, and the claim / mark-published / mark-failed / reclaim /
depth statements all execute with the intended semantics — including the
per-aggregate ordering guard. The C# around them remains unexecuted.

Committed separately from Sprint 16 so that a red gate is attributable
(risk R-16).
MSG

say "3b. Commit 2 of 2 — Sprint 16, display locale (ADR-009)"
git add -A
git commit -F - <<'MSG'
feat(catalog): display locale and language selector (ADR-009)

The grid, its total count and the CSV export render in a chosen
language, defaulting to English rather than the German base locale. A
"Data language" selector in the shell header, persisted per workstation.
Rows report which locale their text actually came from, and the UI marks
fallbacks.

Completeness deliberately stays on the base locale: it answers "is this
product finished?", which must not move when the reader changes
language. Per-locale completeness is ADR-008, scheduled separately.

Does NOT cover the product detail tabs — they still read base-locale
text, so a detail screen opened from an English grid shows German
attribute labels. Tracked as R-15.

Frontend verified 2026-08-17: tsc clean, 260/260 tests, eslint clean,
vite build succeeds, against a clean npm ci in a scratch copy.
Backend NOT compiled.

Also carries CatalogModule.cs, which registers both the shared
NpgsqlDataSource and the Sprint 15 relay — the one file that spans both
sprints. Placed here so the Sprint 15 commit compiles standalone.

SQL verified 2026-08-18 against PostgreSQL 16 on the 500-product
catalogue and a 6,000-row inflation: the product_tr and taxonomy_node_tr
laterals resolve to en-GB where a translation exists and fall back to
the de-DE base locale where it does not, reporting the locale actually
used in both cases. Grid page of 50 executes in 7ms at 500 products and
21ms at 6,000, index-backed throughout, no sequential scan on
product_tr.
MSG

# --------------------------------------------------------------- 4. compact ---
say "4. Compacting the object store"
git gc --aggressive --prune=now
git count-objects -vH | grep -E 'count|size-pack'

say "5. Result"
git --no-pager log --oneline -5
echo
echo "Largest tracked files (GitHub warns at 50MB, rejects at 100MB):"
git ls-files | xargs -I{} stat -f '%z {}' {} 2>/dev/null | sort -rn | head -5

cat <<EOF

──────────────────────────────────────────────────────────────────────
NEXT — publish. Run these yourself, from $REPO

  gh repo create <your-org>/pim-platform --private --source=. --remote=origin
  git push -u origin main

--private is deliberate: appsettings.Development.json carries the local
dev DB password, and 02_dev_seed.sql contains the client's product
catalogue.

THEN — the backend gate, which is the whole point of Sprint 17:

  open "Verify PIM (reset db).command"

Run it on commit 1 first, then commit 2, so a failure is attributable:

  git checkout HEAD~1 && ./scripts/verify-local.sh   # Sprint 15 alone
  git checkout main   && ./scripts/verify-local.sh   # Sprint 16 on top
──────────────────────────────────────────────────────────────────────
EOF
