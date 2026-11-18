# PIM Platform

Product Information Management platform — microservices on Kubernetes.
Monorepo: .NET 10 services, React + TypeScript frontend.

Reference documents: *PIM Solution & Technology Architecture v1.3* (being
revised per ADR-004), *PIM - Recommended Technology Stack Matrix*,
`pim_database_v2.0.sql` (data design source of truth).

## Architecture (ADR-004, ADR-005)

Coarse services, one PostgreSQL cluster with schema-per-service, REST via
NGINX Ingress gateway, async events via RabbitMQ. No shared tables, no
cross-schema FKs; cross-service references are logical ids resolved via events.

```
services/
  catalog/    ACTIVE   product, sku, taxonomy, brand, attributes (schema: catalog)
  asset/      planned  DAM (schema: asset)
  search/     planned  Elasticsearch projections
  gateway/    contract NGINX Ingress routing (no code)
shared/dotnet/Pim.SharedKernel    domain primitives, Result pattern
frontend/                          React + TS + Vite (verified: lint/build/tests)
infra/                             docker-compose (Postgres+RabbitMQ+Redis), initdb
pipelines/azure-pipelines.yml      path-filtered CI per service
docs/adr/                          decision records (ADR-004 = microservices)
```

Each service is Clean Architecture (Domain / Application / Infrastructure /
Api) with its own solution, tests, and db/init DDL.

## Prerequisites

.NET SDK 10.0.x · Node 22+ · Docker Desktop

## First run

On a machine that has never built this repo, double-click `Setup Mac.command`
first: it installs the .NET SDK into `~/.dotnet` and Node 22 (Homebrew, or an
official tarball into `~/.local` when Homebrew is absent), checks Docker, then
restores and builds. It changes nothing that is already present and takes
`--check-only` to report without installing.

Then double-click `Run PIM.command` (starts infra + API :5100 + frontend :5174),
or manually:

```bash
cd infra && docker compose up -d        # Postgres :15432 (catalog schema + role), RabbitMQ, Redis :6380

cd ../services/catalog
dotnet restore && dotnet build && dotnet test

ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5100 \
  dotnet run --project src/Pim.Catalog.Api   # Swagger at localhost:5100/swagger

cd ../../frontend
npm ci && npm run dev -- --port 5174        # proxies /api → localhost:5100
```

`npm ci`, not `install`: it reproduces exactly the tree the gate verified and
fails loudly if `package.json` and the lockfile have drifted.

API routes are mounted at `/api/v1/*` (e.g. `/api/v1/attributes`); the dev
server proxies the whole `/api` prefix.

Non-default ports on purpose: 5000 (AirPlay), 5432/5433/6379/5173 are taken
by other local projects on this machine.

## Verification status

Last full verification **2026-08-21** on macOS 26.5.2 / arm64, SDK 10.0.400,
Node 22.23.2, Docker Desktop 4.77.0 — built and run from a clean clone:

| Suite | Result |
| --- | --- |
| `Pim.Catalog.Domain.Tests` | 187 passed |
| `Pim.Catalog.Application.Tests` | 124 passed |
| `Pim.Catalog.Integration.Tests` | 86 passed (Testcontainers, ~39 s) |
| frontend `lint` + `test` + `build` | 332 passed across 36 files, lint clean |
| `Smoke Test API.command` | 676 assertions, 0 failed |
| `Verify Running Stack.command` | 13 checks, 0 failed |

Re-run the backend gate via `Verify PIM.command` or `./scripts/verify-local.sh`;
`Test Frontend Only.command` is the fast frontend loop. `Verify Running Stack.command`
checks a *running* stack — containers, API, dev server, the Vite `/api` proxy
seam, and whether the outbox is actually reaching the broker.

Earlier history: the scaffold was authored without NuGet access and accepted at
the Sprint 0 gate on 2026-08-12 (47 backend tests at the time).

### Known environment traps

- `Pim.Catalog.Integration.Tests` drives Testcontainers and needs a **live
  Docker daemon**. Without one it emits ~49 connection failures that read like
  product defects and are not. `Setup Mac.command` narrows the run to the unit
  projects when the daemon is unreachable.
- If the Docker engine refuses to start with `ensuring disk: cannot resize
  .../Docker.raw ... permission denied`, the VM image has become root-owned
  while Docker Desktop runs as you. `Diagnose Docker.command` and
  `Docker Disk Check.command` (both read-only) identify it; the fix is
  `sudo chown "$(id -un)":staff` on that file. Seen 2026-08-21.

## Hard rules

- A service never touches another service's schema (enforced by DB role grants).
- Expected business failures return `Result` + coded `Error` → RFC 7807.
- Business rules live in Domain; FluentValidation covers input shape only.
- Transactional outbox is REQUIRED before any cross-service event flow ships
  (ADR-004) — in-process dispatch is a local-dev placeholder only.

## Known gaps (tracked)

- No authentication (identity provider TBD — Auth0 candidate). Dev only.
- The transactional outbox (ADR-007, Sprint 15) now **does** publish.
  `RabbitMqOutboxTransport` is registered whenever `Outbox:RabbitMq:Uri` is set,
  and `appsettings.Development.json` sets it. With no Uri configured the
  registration falls through to `LoggingOutboxTransport`, and `OutboxHealthCheck`
  reports **Degraded** — deliberately, so "draining to a log" can never be
  mistaken for a healthy system. Any non-Development environment must supply a
  Uri or accept that nothing leaves the service.
  Verified running 2026-08-21: `/healthz` = Healthy and the `catalog.events`
  exchange is present on the broker.
- Sprint 15 has now been compiled and run. `Outbox/` is covered by integration
  tests for the store, retry policy, event mapper and both options validators.
  `RabbitMqOutboxTransport` itself has **no direct test** — its behaviour is
  currently only evidenced by the health check and the live exchange above.
- asset-service, search-service: README contracts only.
- Observability: Serilog console only; OpenTelemetry + Grafana stack pending.
