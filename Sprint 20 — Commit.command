#!/bin/bash
#
# Sprint 20 — commit the RabbitMQ transport.
#
set -euo pipefail
cd "$(dirname "$0")" || exit 1
rm -f .git/index.lock

for f in gate-log:GREEN smoke-log:"API SMOKE: GREEN" perf-log:"PERF: GREEN"; do
  log="${f%%:*}.txt"; want="${f#*:}"
  grep -q "$want" "$log" 2>/dev/null || { echo "STOP: $log does not report green."; exit 1; }
done
echo "gate, smoke and perf all report green — proceeding"

echo
echo ">> commit 1 of 2 — the transport"
git add \
  services/catalog/Directory.Packages.props \
  services/catalog/src/Pim.Catalog.Infrastructure/Pim.Catalog.Infrastructure.csproj \
  services/catalog/src/Pim.Catalog.Infrastructure/Outbox/RabbitMqOptions.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/Outbox/RabbitMqOptionsValidator.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/Outbox/RabbitMqOutboxTransport.cs \
  services/catalog/src/Pim.Catalog.Infrastructure/Outbox/OutboxServiceCollectionExtensions.cs \
  services/catalog/src/Pim.Catalog.Api/appsettings.json \
  services/catalog/src/Pim.Catalog.Api/appsettings.Development.json \
  services/catalog/tests/Pim.Catalog.Integration.Tests/Outbox/RabbitMqOptionsValidatorTests.cs \
  scripts/smoke-api.sh

git commit -F - <<'MSG'
feat(outbox): publish to RabbitMQ — messages finally leave the service

Sprint 15 built the outbox and pointed it at a log, because the authoring
environment had no NuGet and inventing a RabbitMQ.Client version would
have produced code that could not restore. ADR-007 recorded that as a
deliberate deferral and predicted what closing it would cost: pin the
package, add one IOutboxTransport, register it.

That is exactly what this is. NOTHING in the outbox changed -- not the
schema, not the relay, not the leasing or backoff or dead-lettering, not
one of its thirty existing tests. The seam was worth building.

What the design turns on:

PUBLISHER CONFIRMS, and specifically two flags. IOutboxTransport says
returning normally means "the broker has accepted responsibility".
RabbitMQ v7 needs publisherConfirmationsEnabled AND
publisherConfirmationTrackingEnabled; with only the first,
BasicPublishAsync returns as soon as the frame is written. That version
satisfies the interface's signature and violates its contract -- the
relay marks the row published and the message is gone. It would pass
every test anyone would think to write, which is why the interface
spelled the requirement out in prose.

THE PRODUCER DECLARES THE EXCHANGE AND NOTHING ELSE. Queues and bindings
belong to the consumers that read them; a producer that declares its
consumers' queues has taken ownership of their retention and lifetime and
will break them on its next deploy. The smoke suite is a consumer, so it
declares its own queue -- before creating anything, because a topic
exchange drops what it cannot route and a queue bound afterwards would
sit empty and read as a broken transport.

ABSENCE OF CONFIGURATION IS THE SWITCH. No Outbox:RabbitMq:Uri means the
log-only transport stays registered and the health check keeps saying so.
No enabled flag: a second way to express the same thing is a second way
to get it wrong. The API still starts without a broker, because a catalog
service that cannot publish can still serve the grid, and failing startup
on a missing broker turns a messaging outage into a total one.

Routing key is the event type on a durable topic exchange, so consumers
bind catalog.product.created, catalog.product.* or catalog.# and choose
their own granularity without this service tracking who wants what.

Verified on the dev Mac. Nine live smoke assertions read messages back
off a real queue through the management API: that something arrived, that
it was routed by event type, that it carries the product THIS run created
rather than one left by an earlier one, that message_id is set for
consumer deduplication, that schema_version rides in a header, that
delivery mode is persistent, and that the payload matches the published
wire contract. Two more assert the outbox fully drained and that nothing
dead-lettered -- the difference between "the transport works" and "the
transport worked once". An unreachable broker fails the suite rather than
skipping it.

  gate  GREEN — 156 domain + 124 application + 73 integration + 260 frontend
  smoke GREEN — 422/422
  perf  GREEN — 24/24 at 1,503 products
MSG
echo "   commit 1 done"

echo
echo ">> commit 2 of 2 — the R-9 finding and the docs"
git add -A

git commit -F - <<'MSG'
fix(tooling): the frontend repair script never did what it claimed

The gate went red on the frontend during a sprint that changed no
frontend code: npm ci died with ENOTEMPTY on iCloud conflict copies
inside node_modules/combined-stream, nineteen of them across the tree.

Investigating turned up something worse than the symptom. The recorded
R-9 mitigation is "dependencies live in node_modules.nosync, with
node_modules as a symlink" -- and it was not in place. node_modules was a
real 215-entry directory and node_modules.nosync was empty, so iCloud had
been syncing the tree the whole time.

The cause is in the repair script itself. It created the symlink and THEN
ran npm ci, which deletes node_modules before installing -- destroying
the symlink every single time the script was run. The mitigation was
defeated by the command it existed to protect, and had been since it was
written. Nothing detected it because the only symptom is iCloud
eventually corrupting a tree that was never supposed to be synced.

Now: install first, relocate after. The script also states plainly that
the next npm ci -- including the gate's own -- will undo it again, which
is true and was not being said. It cannot be made durable while the repo
lives in iCloud; that is R-1, and this is the strongest evidence for it
so far.

Also closes ADR-007's Deferred section, keeping the original text for the
reasoning it records.
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
