# PIM Platform — getting it running

Everything you need to go from this folder to a working stack. Pick your OS,
follow one section, and you should be at a running UI in about 15 minutes
(most of it waiting on downloads).

Verified working on macOS 26.5 / arm64 on 2026-10-12. The Windows path is
written but has **not been run on a Windows machine yet** — see
[Honest status](#honest-status) before you start.

---

## What you are running

A Product Information Management platform: .NET 10 microservices, React +
TypeScript frontend, PostgreSQL / RabbitMQ / Redis in Docker.

| Piece | Where it ends up |
| --- | --- |
| Catalog API | http://localhost:5100 — Swagger at `/swagger` |
| Frontend | http://localhost:5174 |
| PostgreSQL | localhost:**15432** |
| RabbitMQ | localhost:5672 — management UI on 15672 |
| Redis | localhost:**6380** |

Those ports are deliberately non-default. 5000 is taken by AirPlay on macOS,
and 5432 / 5433 / 6379 / 5173 were already in use on the machine this was
built on. Don't "fix" them to the defaults without checking what else you'd
collide with.

---

## Prerequisites

| Tool | Version | Notes |
| --- | --- | --- |
| .NET SDK | **10.0.100 or newer** | `services/catalog/global.json` pins the 10.0 feature band |
| Node.js | **22 or newer** | tested on 22.23.2 |
| Docker Desktop | any current | must be **running**, not just installed |

The setup scripts below install .NET and Node for you, into your home
directory, without administrator rights. **Docker Desktop you install
yourself** — it is a GUI app that needs a licence accepted and a privileged
helper installed on first launch, and no script should be doing that quietly
on your behalf.

Get it from <https://www.docker.com/products/docker-desktop/>, launch it once,
accept the terms, and wait for **Engine running** before you continue.

---

## Where to get the code

**Preferred — clone from Azure DevOps.** This repo already has a remote:

```
https://altzor@dev.azure.com/altzor/PIM/_git/pim-platform
```

```bash
git clone https://altzor@dev.azure.com/altzor/PIM/_git/pim-platform
cd pim-platform
```

Branches: `main` and `frontend`.

Clone if you possibly can. A zip is a snapshot — you can't pull the next
change, you can't open a pull request, and two people editing two zips is how
work gets lost.

**Otherwise — this folder.** It is a complete working copy with `.git`
included, so `git log`, `git status` and branching all work, and you can add
the remote later with:

```bash
git remote -v     # should already show origin
git fetch origin
```

---

## macOS

Double-click, in this order. Both open in Terminal and print what they're doing.

1. **`Setup Mac.command`** — installs .NET 10 into `~/.dotnet` and Node 22
   (via Homebrew if you have it, otherwise an official nodejs.org tarball into
   `~/.local`), checks Docker, then restores and builds.
   Run `./"Setup Mac.command" --check-only` first if you want a report without
   anything being installed.

2. **`Start Docker and Run PIM.command`** — launches Docker Desktop, waits for
   the engine, then brings up infra, the API and the dev server. Use this one
   the first time; `Run PIM.command` on its own assumes Docker is already up.

A browser opens on http://localhost:5174 once the API is healthy.

If macOS blocks a script with *"cannot be opened because it is from an
unidentified developer"*, right-click it → **Open** → **Open**. That is the
Gatekeeper prompt for downloaded files, and it only appears once per file.

---

## Windows

Open **PowerShell** in this folder and run, in order:

```powershell
# One-off: allow local scripts for this session only. Nothing is changed
# permanently, and it does not weaken the policy for other PowerShell windows.
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

.\Setup-Windows.ps1 -CheckOnly    # report only, installs nothing
.\Setup-Windows.ps1               # install what is missing, build, test
.\Run-PIM.ps1                     # start infra + API + frontend
```

`Setup-Windows.ps1` installs .NET 10 into `%USERPROFILE%\.dotnet` and Node 22
into `%USERPROFILE%\.local\node22` (official zip, SHA-256 verified), then adds
both to your user PATH. No administrator rights needed.

Start Docker Desktop yourself before `Run-PIM.ps1`, and wait for **Engine
running**.

---

## Any OS — the manual path

If you'd rather not run a script, or you're on Linux:

```bash
# 1. infra
cd infra && docker compose up -d
cd ..

# 2. backend
cd services/catalog
dotnet restore && dotnet build && dotnet test
cd ../..

# 3. API — leave this running in its own terminal
cd services/catalog
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5100 \
  dotnet run --project src/Pim.Catalog.Api

# 4. frontend — another terminal
cd frontend
npm ci
npm run dev -- --port 5174 --strictPort
```

`ASPNETCORE_ENVIRONMENT=Development` is **required** — there is no
`launchSettings.json` in the repo, and without it the API has no connection
string and will not start.

Use `npm ci`, not `npm install`. It reproduces exactly the dependency tree
this project was verified against and fails loudly if `package.json` and the
lockfile have drifted apart.

---

## Checking it actually works

| What | Expected |
| --- | --- |
| http://localhost:5100/healthz | `Healthy` |
| http://localhost:5100/swagger | Swagger UI listing `/api/v1/*` routes |
| http://localhost:5174 | the PIM UI |

On macOS there are two scripts that check this for you, and neither changes
anything:

- **`Smoke Test API.command`** — 676 assertions against the running API.
- **`Verify Running Stack.command`** — 13 checks: containers, API, dev server,
  the Vite `/api` proxy seam, and whether the outbox is reaching RabbitMQ.

API routes live under **`/api/v1/*`** (e.g. `/api/v1/attributes`), not `/api/*`.
The dev server proxies the whole `/api` prefix through to port 5100.

---

## Sharing the UI with someone on your network

The dev server binds to all interfaces by default (`frontend/vite.config.ts`,
`server.host`). On startup Vite prints a **Network:** line with the address to
hand out — something like `http://192.168.1.177:5174/`.

Only the dev server is exposed. The API stays bound to localhost and is
reached through the proxy running on the host machine, so port 5100 is never
open to the network.

Set `PIM_LAN=0` in your environment to keep it loopback-only.

> **This dev server has no authentication.** Anyone who can reach port 5174 has
> full read and write access to the catalog — create, edit, delete, workflow
> actions. Fine on your own office network. Not fine on café, hotel or guest
> Wi-Fi.

On Windows, `Run-PIM.ps1` is loopback-only unless you pass `-Lan`.

---

## When it goes wrong

**`npm run dev` dies with "vite: not found"**
`node_modules` is missing. Run `npm ci` in `frontend/`. The run scripts do
this for you; the bare `npm run dev` does not.

**49 integration tests fail with Docker connection errors**
`Pim.Catalog.Integration.Tests` uses Testcontainers and needs a **running**
Docker daemon. The failures look like product defects and are not. Start
Docker and re-run; or run only the unit projects:

```bash
dotnet test tests/Pim.Catalog.Domain.Tests
dotnet test tests/Pim.Catalog.Application.Tests
```

**"port 5100 is held by an unrelated process"**
The run scripts refuse to kill processes they don't recognise. Find out what
has the port (`lsof -nP -iTCP:5100 -sTCP:LISTEN` on macOS,
`Get-NetTCPConnection -LocalPort 5100` on Windows) and close it yourself.

**Docker engine won't start — `cannot resize .../Docker.raw ... permission denied`**
The Docker VM disk image has become owned by `root` while Docker Desktop runs
as you, so it can't resize it. Hit this on macOS on 2026-08-21. Fix:

```bash
sudo chown "$(id -un)":staff ~/Library/Containers/com.docker.docker/Data/vms/0/data/Docker.raw
```

`Diagnose Docker.command` and `Docker Disk Check.command` (both read-only)
confirm the diagnosis before you touch anything.

**The UI loads but every API call 404s**
Check you're calling `/api/v1/...`, not `/api/...`.

**Postgres won't start / port 15432 in use**
Another Postgres is probably on that port. `docker compose down` in `infra/`,
check with `lsof`, then bring it back up.

---

## Known gaps

These are real and tracked — not things you've misconfigured:

- **No authentication.** Development only. Identity provider is still TBD.
- **`RabbitMqOutboxTransport` has no direct test.** It works (verified live:
  health reports `Healthy` and the `catalog.events` exchange appears on the
  broker), but nothing pins that behaviour down in CI. Without
  `Outbox:RabbitMq:Uri` configured, the outbox silently falls back to a
  log-only transport and health reports **Degraded** by design — any
  non-Development environment must supply a Uri or accept that no message
  leaves the service.
- **asset-service and search-service are README contracts only** — no code.
- **Observability is Serilog to console.** OpenTelemetry and Grafana pending.
- **The `docs/adr/` directory is missing from the repo.** ADR-004, 005, 007,
  008 and 009 are cited throughout the README and source comments, but the
  decision records themselves are not checked in. Ask before assuming you know
  why an architectural rule exists.

---

## Honest status

What has actually been run, and what hasn't:

| | Status |
| --- | --- |
| macOS setup + run | **Verified** 2026-10-12 on macOS 26.5.2 arm64, from a clean start |
| Backend tests | **397 passing** — Domain 187, Application 124, Integration 86 |
| Frontend tests | **332 passing** across 36 files, lint clean |
| API smoke | **676 assertions, 0 failed** |
| LAN exposure | **Verified** — bound to `*:5174`, API confirmed loopback-only |
| **Windows scripts** | **Never executed on Windows.** Syntax-checked with the PowerShell parser only. |

If you're the first person to run `Setup-Windows.ps1`, expect to hit something
and please report it rather than working around it silently — the fix belongs
in the script.
