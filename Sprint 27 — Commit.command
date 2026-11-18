#!/bin/bash
#
# Commits Sprint 27 — workflow management. Local only, nothing pushed.
#
set -u
cd "$(dirname "$0")" || exit 1
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

exec > >(tee "sprint27-commit-log.txt") 2>&1

# The Linux-side bridge cannot delete files, so a git command run from there
# leaves its lock and temp objects behind. Clear them first: a stale index.lock
# makes every git write below fail with "File exists".
rm -f .git/index.lock
find .git/objects -name 'tmp_obj_*' -delete 2>/dev/null

rm -rf .s27 _to_delete docs/_to_delete scripts/_to_delete frontend/_to_delete 2>/dev/null
rm -f sprint26-commit-log.txt sprint27-run-log.txt 2>/dev/null

git add \
  services/catalog/db/init/01_catalog_schema.sql \
  services/catalog/db/init/02_dev_seed.sql \
  services/catalog/src/Pim.Catalog.Domain/Workflow \
  services/catalog/src/Pim.Catalog.Domain/Products/IWorkflowPolicy.cs \
  services/catalog/src/Pim.Catalog.Domain/Products/Product.cs \
  services/catalog/src/Pim.Catalog.Application/Workflow \
  services/catalog/src/Pim.Catalog.Application/Products/ChangeWorkflowState/ChangeWorkflowStateCommand.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/Workflow \
  services/catalog/src/Pim.Catalog.Infrastructure/Repositories/ApprovalTaskRepository.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/ReadModels/WorkflowReadStore.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/Persistence/CatalogDbContext.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/Persistence/Configurations/ApprovalTaskConfiguration.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/CatalogModule.cs \
  services/catalog/src/Pim.Catalog.Api/Endpoints/WorkflowEndpoints.cs \
  services/catalog/src/Pim.Catalog.Api/Program.cs \
  services/catalog/tests/Pim.Catalog.Domain.Tests/Workflow \
  services/catalog/tests/Pim.Catalog.Domain.Tests/Products/ProductEditTests.cs \
  services/catalog/tests/Pim.Catalog.Domain.Tests/ErrorTaxonomyTests.cs \
  services/catalog/tests/Pim.Catalog.Integration.Tests/WorkflowPipelineTests.cs \
  frontend/src/features/workflow \
  frontend/src/features/products/WorkflowTab.tsx \
  frontend/src/features/products/WorkflowTab.test.tsx \
  frontend/src/features/products/ProductDetailPage.tsx \
  frontend/src/features/products/ProductDetailPage.test.tsx \
  frontend/src/features/products/ProductDetailPage.a11y.test.tsx \
  frontend/src/features/products/productsApi.ts \
  frontend/src/features/products/types.ts \
  frontend/src/features/products/testApi.ts \
  scripts/smoke-api.sh \
  scripts/perf-check.sh \
  docs/samples \
  "Sprint 27 — Gate + Smoke + Perf.command" \
  "Sprint 27 — Open Workflow Tab.command"

git commit -q -m "feat(workflow): the editorial pipeline stops being code

Five tables and a state machine, per Database Design v2.0 section 12: a
configurable process, a live approval task per product, and an append-only
trail of every transition.

CUSTOM, AND THAT IS WHAT THE MATRIX SAYS. The technology stack matrix puts
'Workflow Provider Interface' in the platform-neutral column of the Workflow
Layer row. Elsa and Temporal appear only under On-Premises, Logic Apps and
Durable Functions only under Azure, Step Functions only under AWS -- providers
BEHIND the neutral interface rather than the interface itself. The client's
instruction not to take a third-party workflow dependency and the matrix
therefore agree. Nothing was added to Directory.Packages.props.

WHERE THE RULE LIVES. The transition table existed three times: a Dictionary in
the Product aggregate, a Record<string,string[]> in the React app, and a CHECK
constraint. Two are now deleted. What did NOT move is the invariant -- the
aggregate still refuses to put a product in an arbitrary state, it just asks an
injected IWorkflowPolicy instead of a private dictionary. There is nothing about
a product that makes Draft to Published impossible; there is something about how
this client chooses to work, and that is configuration.

Two tests state that rather than assume it. One loads the real definition from a
real PostgreSQL container and asserts the reachable states are exactly what the
aggregate used to hardcode. The other runs a pipeline that allows Draft straight
to Published and proves the same aggregate now permits it -- without which the
rule would still be a constant with extra steps.

ONE TRANSACTION, AND AN ASSERTION THAT IT HELD. product.workflow_state is a
denormalised projection of the open task's step, because the grid, the cockpit
and the board all filter on it across the whole catalogue. approval_task and
approval_history are therefore mapped with EF Core and not Dapper, so the task
move, the history row and the projection commit together or not at all. Smoke
then counts, in SQL: rows where the two disagree, tasks completed short of a
terminal step, and history rows describing a transition the pipeline does not
declare. All zero. A denormalisation nobody checks is a bug with a design
rationale attached.

SIX TRANSITIONS, NOT FIVE. Prototype v4.2 draws three buttons on Approval, so
REJECT and REQUEST_CHANGES both land on Draft and stay two actions. That is also
why the tab posts ACTIONS rather than destinations: only the caller knows whether
they are rejecting something or asking for more work, and that difference is what
an approval trail is for. The state-oriented endpoint the header has used since
Sprint 4 still exists and now resolves a destination back to a verb through the
same service.

THE AUDIT VERB IS RESOLVED AFTER THE FACT. AuditBehavior runs the handler first
so it can skip failures, and an action-oriented command cannot know its own
destination -- APPROVE publishes from Approval and merely passes review from
Review. The command therefore carries two internal fields the handler fills in.
A mutable field on a command deserves a defence, and it is this: without it every
move would be logged under one flat verb and 'when was this published' would have
no answer in the activity log. Smoke asserts exactly one published entry.

NO AUTHENTICATION (R-3). Anyone who can reach the API can approve and publish,
and the recorded actor is a string the caller typed about themselves. The API
returns that statement in its own payload and the tab renders it under the
history; a frontend test asserts the sentence is present, so deleting the
disclosure breaks a build. Recorded as R-26.

Three of my own smoke assertions were vacuous and said so: 'PASS wf: a stale row
version -> 409 (= 500)' -- the helper prints its EXPECTED value, so a pass whose
stated expectation is not the one in the assertion's name is not a pass. All
three used a multi-line command substitution with escaped quotes; rewritten with
the status on its own line they read 409, 409 and 400. The API had been correct
throughout. The evidence had not been.

Gate 389 backend / 322 frontend. Smoke 631/631. Perf 39/39. Workflow tab checked
in the browser at both Draft and Approval." || { echo "feature commit failed"; exit 1; }

echo "committed: $(git log --oneline -1)"

git add docs/DELIVERY_STATUS.md docs/RELEASE_NOTES.md "Sprint 27 — Commit.command"

git commit -q -m "docs: record Sprint 27, and a risk about what an approval trail is worth

R-26: the trail records who somebody SAID they were. The sequence of transitions
is real -- written in the same transaction as the move and cross-checked in SQL
-- but actor_user_id is always NULL and actor_display is free text the caller
supplies about themselves, on an unauthenticated endpoint. That makes the trail
operationally useful and legally worthless, and a client would have no way to
tell from a screen that looks exactly like a real approval trail. So the
disclosure ships in the payload and on the screen rather than only here.

Also corrects the standing claim that Workflow was blocked. It was waiting on an
approval engine, and an approval engine turned out to be five tables and a state
machine rather than a service somebody else had to build. Channels genuinely is
blocked: there are no channel tables, so there is nowhere to publish to.

Authentication is now the next thing worth building. Four interim compromises
are queued behind it -- owner_display_name, assignee_display_name, an
unauthenticated upload endpoint and an unverifiable approval trail -- and every
one has a nullable *_user_id column already waiting, so the schema does not move
when it lands.

Adds docs/samples/BuildCore-FD-90-GS-1000.ifc: a valid IFC4 fire door for
exercising the BIM upload path by hand. Deliberately not wired into smoke, which
generates its own fixtures so it stays runnable on a clean checkout." || { echo "docs commit failed"; exit 1; }

echo "committed: $(git log --oneline -1)"
echo
git status --short
echo
git log --oneline -4
echo
echo "Nothing was pushed. You can close this window."
