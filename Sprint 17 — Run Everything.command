#!/bin/bash
#
# Sprint 17 — verify & publish, run end to end.
#
# Double-click this. It writes everything to sprint17-log.txt and prints a
# short progress line per phase. It does NOT push to GitHub and it does NOT
# move the repo out of iCloud — both of those are separate, deliberate steps.
#
# Phases
#   A  preflight — what toolchain is actually on this machine
#   B  repo hygiene — clear the stale git lock and the iCloud debris
#   C  commit Sprint 15 and Sprint 16 as two separate commits
#   D  full gate — restore, build, test, DB reset, API smoke
#
cd "$(dirname "$0")" || exit 1
LOG="sprint17-log.txt"
: > "$LOG"

phase()  { printf '\n\n########## %s ##########\n' "$*" >> "$LOG"; printf '\n>> %s\n' "$*"; }
note()   { printf '%s\n' "$*" >> "$LOG"; printf '   %s\n' "$*"; }
run()    { printf '\n$ %s\n' "$*" >> "$LOG"; "$@" >> "$LOG" 2>&1; }

# .command shells start from a login-less environment and often lack the PATH
# that an interactive Terminal has. Probe the known dotnet homes explicitly.
if ! command -v dotnet >/dev/null; then
  for d in /usr/local/share/dotnet "$HOME/.dotnet" /opt/homebrew/opt/dotnet/libexec /usr/local/bin; do
    [ -x "$d/dotnet" ] && export PATH="$d:$PATH" && break
  done
fi
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

############################################################ A — preflight ####
phase "PHASE A — preflight"
{
  echo "date:      $(date)"
  echo "pwd:       $(pwd)"
  echo "user:      $(whoami)"
  echo "macOS:     $(sw_vers -productVersion 2>/dev/null) $(uname -m)"
  echo
  echo -n "dotnet:    "; command -v dotnet >/dev/null && dotnet --version || echo "NOT FOUND"
  echo -n "node:      "; command -v node   >/dev/null && node --version   || echo "NOT FOUND"
  echo -n "npm:       "; command -v npm    >/dev/null && npm --version    || echo "NOT FOUND"
  echo -n "docker:    "; command -v docker >/dev/null && docker --version || echo "NOT FOUND"
  echo -n "git:       "; command -v git    >/dev/null && git --version    || echo "NOT FOUND"
  echo
  echo "dotnet SDKs:"; dotnet --list-sdks 2>/dev/null || echo "  (none)"
  echo "nuget cache entries: $(ls ~/.nuget/packages 2>/dev/null | wc -l | tr -d ' ')"
  echo
  echo -n "docker daemon: "
  if docker info >/dev/null 2>&1; then echo "RUNNING"; else echo "NOT RUNNING"; fi
  echo
  echo "--- network reachability (the gate needs NuGet) ---"
  curl -s -m 10 -o /dev/null -w "  api.nuget.org      %{http_code}\n" https://api.nuget.org/v3/index.json
  curl -s -m 10 -o /dev/null -w "  registry.npmjs.org %{http_code}\n" https://registry.npmjs.org/
  curl -s -m 10 -o /dev/null -w "  github.com         %{http_code}\n" https://github.com
} >> "$LOG" 2>&1
note "toolchain probed"

if ! command -v dotnet >/dev/null; then
  note "STOP: no dotnet SDK on PATH. Nothing further can run."
  echo "Done (stopped in Phase A) — see $LOG"
  exit 1
fi

# Docker is needed by the integration tests (Testcontainers) and by the DB
# reset. Start Docker Desktop and wait, rather than failing 20 minutes in.
if ! docker info >/dev/null 2>&1; then
  note "Docker is not running — starting Docker Desktop and waiting up to 3 minutes"
  open -a Docker 2>/dev/null
  for _ in $(seq 1 90); do
    docker info >/dev/null 2>&1 && break
    sleep 2
  done
  if docker info >/dev/null 2>&1; then note "Docker is up"; else note "WARNING: Docker still not up — the gate will likely fail"; fi
fi

######################################################## B — repo hygiene ####
phase "PHASE B — repo hygiene"
rm -f .git/index.lock
find .git -maxdepth 1 -name 'index [0-9]*' -print -delete >> "$LOG" 2>&1
rm -f .git/ta6ICX4 .gitattr_probe
rm -rf "frontend/node_modules 2"
if [ -d _to_delete ]; then
  du -sh _to_delete >> "$LOG" 2>&1
  rm -rf _to_delete
  note "removed the quarantined debris"
fi
run git fsck --full --no-progress
note "git fsck done"
run git status --short

######################################################### C — the commits ####
# Split so a red gate is attributable (risk R-16). Only CatalogModule.cs spans
# both sprints; it goes with Sprint 16 so the Sprint 15 commit still compiles.
phase "PHASE C — commit Sprint 15, then Sprint 16"

if git diff --quiet && git diff --cached --quiet && [ -z "$(git status --porcelain --untracked-files=normal)" ]; then
  note "working tree already clean — commits appear to be done, skipping"
else
  run git add \
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

  git commit -F - >> "$LOG" 2>&1 <<'MSG'
feat(catalog): transactional outbox (ADR-007) — unverified at commit time

outbox_message table, enqueue inside SaveChangesAsync's transaction,
versioned wire contracts, and a relay with lease-based claiming,
per-aggregate ordering, exponential backoff and dead-lettering. Health
check and metrics included.

Deliberately delivers nothing: IOutboxTransport has only a logging
implementation, so the health check reports Degraded until a real
transport is attached. See ADR-007 §Deferred.

Two package pins are inferred rather than verified:
Microsoft.Extensions.Hosting.Abstractions and
Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions, both 10.0.0.

DDL and SQL semantics verified 2026-08-18 against a real PostgreSQL 16:
schema applies clean, four partial indexes and three check constraints
created as designed, and the claim / mark-published / mark-failed /
reclaim / depth statements all execute with the intended semantics,
including the per-aggregate ordering guard. The C# around them was not
compiled at the time of this commit.

Committed separately from Sprint 16 so a red gate is attributable (R-16).
MSG
  note "commit 1 (Sprint 15) done"

  run git add -A
  git commit -F - >> "$LOG" 2>&1 <<'MSG'
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

Also carries CatalogModule.cs, the one file spanning both sprints, so
that the Sprint 15 commit compiles standalone.

Frontend verified 2026-08-17: tsc clean, 260/260, eslint clean, vite
build succeeds. SQL verified 2026-08-18 on PostgreSQL 16: the product_tr
and taxonomy_node_tr laterals resolve to en-GB where a translation
exists and fall back to de-DE where it does not, reporting the locale
actually used; grid page of 50 runs in 7ms at 500 products and 21ms at
6,000, index-backed, no sequential scan on product_tr.
MSG
  note "commit 2 (Sprint 16) done"
fi

run git --no-pager log --oneline -4

############################################################ D — the gate ####
phase "PHASE D — full gate (restore, build, test, DB reset, API smoke)"
note "this is the long one — expect several minutes"
./scripts/verify-local.sh --reset-db >> "$LOG" 2>&1
GATE=$?
printf '\n=== verify-local.sh exit code: %s ===\n' "$GATE" >> "$LOG"

if [ "$GATE" -eq 0 ]; then
  note "GATE GREEN"
else
  note "GATE RED (exit $GATE) — the log has the detail"
fi

phase "SUMMARY"
{
  echo "gate exit code: $GATE"
  echo "HEAD:           $(git log -1 --oneline)"
  echo "working tree:   $(git status --porcelain | wc -l | tr -d ' ') changed paths"
} >> "$LOG" 2>&1

echo
echo "=============================================="
echo " Finished. Gate exit code: $GATE"
echo " Full detail: sprint17-log.txt"
echo " You can close this window."
echo "=============================================="
exit "$GATE"
