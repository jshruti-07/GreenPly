#!/bin/bash
#
# Sprint 23 — finish the run: repair, verify, commit.
#
# WHY THIS EXISTS
# ---------------
# The Sprint 23 gate went red on `npm ci`, not on the code:
#
#   npm error ENOTEMPTY: directory not empty, rmdir '.../node_modules/dunder-proto'
#
# That is R-9. iCloud had written 107 conflict copies ("LICENSE 2",
# "package 3.json") inside node_modules, and npm cannot remove a directory that
# keeps growing files underneath it. The backend was green on the same run
# (156 + 124 + 73 tests), and the API smoke (472) and perf (28) suites both
# passed afterwards — only the frontend stage never got to run its tests.
#
# The conflicted tree has already been moved to frontend/_to_delete/ (the file
# bridge can move but not delete). Everything below runs on your Mac, which can.
#
# Steps:
#   1. Delete the conflicted tree that was moved aside
#   2. Reinstall dependencies into node_modules.nosync, with node_modules as a
#      symlink to it — the R-9 mitigation, applied AFTER install because npm ci
#      deletes node_modules and would destroy the symlink
#   3. Full gate + smoke + perf
#   4. Commit Sprint 23 locally if all three are green. Nothing is pushed.
#
set -u
cd "$(dirname "$0")" || exit 1

if ! command -v dotnet >/dev/null; then
  for d in /usr/local/share/dotnet "$HOME/.dotnet" /opt/homebrew/opt/dotnet/libexec; do
    [ -x "$d/dotnet" ] && export PATH="$d:$PATH" && break
  done
fi
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

echo ">> 1/4  Removing the conflicted dependency tree"
if [ -d frontend/_to_delete ]; then
  # Can take a minute: iCloud re-materialises files while rm walks the tree.
  rm -rf frontend/_to_delete && echo "   removed" || echo "   WARNING: could not remove frontend/_to_delete"
fi
rm -rf scripts/_to_delete frontend/src/design/_to_delete 2>/dev/null

echo
echo ">> 2/4  Reinstalling frontend dependencies"
cd frontend || exit 1
rm -rf node_modules node_modules.nosync
npm ci || { echo "npm ci failed — stopping here."; exit 1; }

# Relocate AFTER installing. The previous version of this did it before, and
# every npm ci wiped the symlink it had just created — the mitigation was
# defeated by the command it was protecting (found in Sprint 20).
if [ -d node_modules ] && [ ! -L node_modules ]; then
  mv node_modules node_modules.nosync && ln -s node_modules.nosync node_modules \
    && echo "   node_modules -> node_modules.nosync (kept out of iCloud until the next npm ci)"
fi
cd ..

echo
echo ">> 3/4  Gate, smoke and perf"
./scripts/verify-local.sh --reset-db > gate-log.txt 2>&1
GATE=$?
echo "   gate: $GATE"
grep -E "^postgres: " gate-log.txt
tail -14 gate-log.txt | grep -E "PASS|FAIL|GREEN|RED"
if [ "$GATE" -ne 0 ]; then
  echo "STOPPED: gate red. Detail in gate-log.txt — send it to Claude."
  exit 1
fi

(cd infra && docker compose down -v >/dev/null 2>&1)
./scripts/smoke-api.sh > smoke-log.txt 2>&1
SMOKE=$?
echo "   smoke: $SMOKE"
grep -cE "^  PASS" smoke-log.txt | sed 's/^/   assertions passed: /'
grep -E "  FAIL " smoke-log.txt | head -20
if [ "$SMOKE" -ne 0 ]; then
  echo "STOPPED: smoke red. Detail in smoke-log.txt — send it to Claude."
  exit 1
fi

./scripts/perf-check.sh > perf-log.txt 2>&1
PERF=$?
echo "   perf: $PERF"
grep -E "attributes:|categories:|^  passed:" perf-log.txt
if [ "$PERF" -ne 0 ]; then
  echo "STOPPED: perf red. Detail in perf-log.txt — send it to Claude."
  exit 1
fi

echo
echo ">> 4/4  Committing Sprint 23 (local only, nothing pushed)"
git add \
  services/catalog/src/Pim.Catalog.Application/AttributeLibrary \
  services/catalog/src/Pim.Catalog.Infrastructure/ReadModels/AttributeLibraryStore.cs \
  services/catalog/src/Pim.Catalog.Api/Endpoints/AttributeLibraryEndpoints.cs \
  services/catalog/src/Pim.Catalog.Api/Program.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/CatalogModule.cs \
  frontend/src/features/attributes \
  frontend/src/App.tsx \
  scripts/smoke-api.sh \
  scripts/perf-check.sh \
  docs/DELIVERY_STATUS.md \
  "Sprint 22 — Commit.command" \
  "Sprint 23 — Finish.command"

git commit -q -m "feat(attributes): the Attribute Library, read and navigable

Third of fifteen top-level screens. Every attribute the catalogue defines, with
its group, type, unit, options, set membership and how far it is filled in;
one attribute in detail shows its text in every configured locale including the
ones with none, its options with real usage counts, and the sets that include it.

The decision that matters is the coverage DENOMINATOR. An attribute is only
expected on products whose attribute set includes it, so coverage is measured
against those products — not against the catalogue. Measured against the
catalogue, a correctly scoped attribute used by one product family reads as 3%,
and the number would punish exactly the modelling it exists to encourage. An
attribute in no set at all reports 'not in any product's set' rather than 0%,
because 0% accuses somebody of not filling in a field nobody has ever seen.

Same rule for 'filled in' as the grid, the cockpit and the category screen:
approved values, base locale or language-neutral. Four screens, one definition.

Groups come from attribute_group rather than from the attributes on screen, so
a group nobody has put anything in is visible instead of silently absent.

A shadowing bug was caught in the smoke assertions before it shipped: a callback
parameter named 'a' hides the argument array jsonq passes under the same name.
That exact mistake shipped in Sprint 19 and made an assertion compare undefined
with undefined and pass." || { echo "commit failed"; exit 1; }

echo "   $(git log --oneline -1)"
echo
echo "=============================================="
echo " gate: $GATE   smoke: $SMOKE   perf: $PERF   (0 = pass)"
echo " Sprint 23 committed locally. Nothing was pushed."
echo " You can close this window."
echo "=============================================="
