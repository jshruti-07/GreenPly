#!/bin/bash
#
# Commits Sprint 28 — the Workflows & Tasks board and queue. Local only.
#
set -u
cd "$(dirname "$0")" || exit 1
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"
exec > >(tee "sprint28-commit-log.txt") 2>&1

rm -f .git/index.lock
find .git/objects -name 'tmp_obj_*' -delete 2>/dev/null
rm -rf .s27 2>/dev/null
rm -f sprint27-commit-log.txt sprint27-run-log.txt sprint28-run-log.txt 2>/dev/null

git add \
  services/catalog/db/init/01_catalog_schema.sql \
  services/catalog/src/Pim.Catalog.Application/Workflow \
  services/catalog/src/Pim.Catalog.Infrastructure/ReadModels/WorkflowBoardStore.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/CatalogModule.cs \
  services/catalog/src/Pim.Catalog.Api/Endpoints/WorkflowEndpoints.cs \
  services/catalog/tests/Pim.Catalog.Integration.Tests/WorkflowBoardStoreTests.cs \
  frontend/src/App.tsx \
  frontend/src/design/components.css \
  frontend/src/features/workflow \
  scripts/smoke-api.sh \
  scripts/perf-check.sh \
  "Sprint 28 — Gate + Smoke + Perf.command" \
  "Sprint 28 — Open Workflows Board.command"

git commit -q -m "feat(workflow): the Workflows and Tasks board, and a queue over it

Seventh of fifteen top-level screens, and the visible half of the module Sprint
27 built the engine for.

THE BOARD IS OVER PRODUCTS, NOT OVER TASKS. Most of the catalogue has never been
through the pipeline, so a board built from approval_task would show the handful
of products somebody has touched while the subtitle claims to cover the
catalogue. It reads product.workflow_state -- the projection Sprint 27 declared
and cross-checks in SQL -- and the task adds an assignee, a priority and a due
date WHERE ONE EXISTS. A card with no task therefore shows its OWNER, labelled
as such: owning a record and having accepted work on it are different claims,
and back-filling one from the other would put a name against work nobody agreed
to.

A COLUMN'S COUNT IS NOT ITS CARDS. Four columns cannot hold 1,503 products, so
each returns its most recently changed and states the gap on screen -- 'Showing
the 12 most recently changed, 363 more in this step'. A column headed 375 with
twelve cards and nothing to say the rest exist is a figure that means something
other than it appears to, which is R-19. Three assertions pin it, one of which
requires that at least one column is GENUINELY TRUNCATED so the cap is exercised
rather than trivially satisfied. Every column total is recomputed from the
products grid's own status filter: four independent cross-checks that must also
sum to the catalogue.

NO DRAG-AND-DROP, deliberately. A drag from Approval to Draft cannot say whether
it means REJECT or REQUEST_CHANGES and carries no reviewer note; implementing it
would flatten the distinction Sprint 27 exists to preserve. Cards are links to
the product's Workflow tab, where the note is typed -- and being links rather
than divs with click handlers, they are keyboard-reachable and announce
themselves.

An unrecognised workflow_state gets its own column, marked. Dropping it would
make every other column a larger share of a catalogue that had quietly shrunk,
which is exactly what Sprint 21 shipped on the cockpit.

An unknown step, priority or sort key is REJECTED with the allowed values rather
than answered with an empty list: an empty queue reads as 'nothing to do', the
most misleading thing this screen could say.

The sortable columns are declared by the query and implemented by the SQL store
-- two lists in two assemblies. An integration test runs every advertised column
in both directions, because a column the validator accepts and the store does
not know is a 500 on a value the API's own error message recommended. Priority
is deliberately NOT sortable: high, low, normal, urgent is not the order anybody
means.

One index, ix_product_workflow_board (workflow_state, updated_at DESC), serves
both halves of the board query. Board 9ms; the queue is 88ms and is now the
second most expensive read in the service.

Gate 397 backend / 332 frontend. Smoke 676/676. Perf 44/44. Board and list both
checked in the browser." || { echo "feature commit failed"; exit 1; }

echo "committed: $(git log --oneline -1)"

git add docs/DELIVERY_STATUS.md docs/RELEASE_NOTES.md "Sprint 28 — Commit.command"

git commit -q -m "docs: record Sprint 28 — workflow management is complete

Seven of fifteen top-level screens. Workflows & Tasks moves from placeholder to
done across two sprints: a configurable pipeline in five tables, approval tasks,
an append-only trail, the board and a filterable queue.

Authentication is now the next thing worth building, and the queue behind it has
not got shorter: owner_display_name, assignee_display_name, an unauthenticated
upload endpoint and an approval trail with no verifiable actor. Every one has a
nullable *_user_id column already waiting." || { echo "docs commit failed"; exit 1; }

echo "committed: $(git log --oneline -1)"
echo
git status --short
echo
git log --oneline -5
echo
echo "Nothing was pushed. You can close this window."
