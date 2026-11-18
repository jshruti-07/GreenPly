<#
.SYNOPSIS
  Starts the PIM platform on Windows: infra containers, catalog API, frontend.

.DESCRIPTION
  The Windows equivalent of 'Run PIM.command'. Same ports, same behaviour:
    infra      docker compose up -d   (Postgres 15432, RabbitMQ, Redis 6380)
    API        http://localhost:5100  (Swagger at /swagger)
    frontend   http://localhost:5174

  Run 'Setup-Windows.ps1' first on a machine that has never built this repo.

  Close this window or press Ctrl+C to stop both the API and the dev server.

.PARAMETER Lan
  Also expose the dev server on this machine's LAN address so other machines
  can reach the UI. Off by default: the dev server has NO authentication.

.EXAMPLE
  .\Run-PIM.ps1
.EXAMPLE
  .\Run-PIM.ps1 -Lan
#>
[CmdletBinding()]
param(
    [switch]$Lan
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepoRoot

$ApiPort      = 5100
$FrontendPort = 5174

function Write-Step { param([string]$Text) Write-Host "`n== $Text ==" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Text) Write-Host "  PASS  $Text" -ForegroundColor Green }
function Write-Warn { param([string]$Text) Write-Host "  WARN  $Text" -ForegroundColor Yellow }
function Stop-WithError {
    param([string]$Text)
    Write-Host "`nFATAL: $Text" -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------- toolchain --
# Setup-Windows.ps1 installs into %USERPROFILE%; a machine-wide install is on
# PATH already. Probe both rather than assuming either.
function Find-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($candidate in @(
        (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'),
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe')
    )) {
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

function Find-Node {
    $cmd = Get-Command node -ErrorAction SilentlyContinue
    if ($cmd) { return (Split-Path -Parent $cmd.Source) }
    $candidate = Join-Path $env:USERPROFILE '.local\node22'
    if (Test-Path (Join-Path $candidate 'node.exe')) { return $candidate }
    return $null
}

$dotnetPath = Find-Dotnet
if (-not $dotnetPath) {
    Stop-WithError "dotnet not found. Run Setup-Windows.ps1 first."
}
$env:DOTNET_ROOT = Split-Path -Parent $dotnetPath
$env:PATH = "$($env:DOTNET_ROOT);$($env:PATH)"

$nodeDir = Find-Node
if (-not $nodeDir) {
    Stop-WithError "node not found. Run Setup-Windows.ps1 first."
}
$env:PATH = "$nodeDir;$($env:PATH)"

# ------------------------------------------------------------ port hygiene --
# A previous run's API keeps serving on its port and the new one fails to bind
# quietly, leaving the UI talking to a stale build. Reclaim the port, but only
# from our own processes -- never from something unrelated.
function Clear-StalePort {
    param(
        [int]$Port,
        [string[]]$OwnerNames,
        [string]$Label
    )

    $connections = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    if (-not $connections) { return }

    # NB: not $pid -- that is a PowerShell automatic variable holding THIS
    # process's id, and shadowing it here would be a trap for the next reader.
    foreach ($ownerPid in ($connections.OwningProcess | Select-Object -Unique)) {
        $proc = Get-Process -Id $ownerPid -ErrorAction SilentlyContinue
        if (-not $proc) { continue }

        if ($OwnerNames -contains $proc.ProcessName) {
            Write-Host "  stopping stale $Label (pid $ownerPid) so this run serves the current build"
            Stop-Process -Id $ownerPid -Force -ErrorAction SilentlyContinue
        }
        else {
            Stop-WithError @"
port $Port is held by an unrelated process (pid $ownerPid): $($proc.ProcessName)
       Close it yourself, or change the port, rather than having this script
       kill something it does not recognise.
"@
        }
    }

    for ($i = 0; $i -lt 15; $i++) {
        if (-not (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)) { return }
        Start-Sleep -Seconds 1
    }
    Stop-WithError "port $Port is still occupied."
}

Clear-StalePort -Port $ApiPort      -OwnerNames @('Pim.Catalog.Api', 'dotnet') -Label 'catalog API'
Clear-StalePort -Port $FrontendPort -OwnerNames @('node')                      -Label 'frontend dev server'

# ---------------------------------------------------------------- infra -----
Write-Step "Infra (postgres 15432, rabbitmq, redis 6380)"
docker info *> $null
if ($LASTEXITCODE -ne 0) {
    Stop-WithError "Docker engine is not responding. Start Docker Desktop, wait for 'Engine running', then re-run."
}
Push-Location (Join-Path $RepoRoot 'infra')
try {
    docker compose up -d
    if ($LASTEXITCODE -ne 0) { Stop-WithError "docker compose up failed." }
}
finally { Pop-Location }
Write-Ok "containers up"

# ----------------------------------------------------------------- API ------
# Development env is required: there is no launchSettings.json in the repo.
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS        = "http://localhost:$ApiPort"

Write-Step "Starting catalog API on :$ApiPort (log: api.log)"
$apiLog    = Join-Path $RepoRoot 'api.log'
$apiErrLog = Join-Path $RepoRoot 'api.err.log'
$apiProc = Start-Process -FilePath $dotnetPath `
    -ArgumentList 'run', '--project', 'src/Pim.Catalog.Api' `
    -WorkingDirectory (Join-Path $RepoRoot 'services/catalog') `
    -RedirectStandardOutput $apiLog `
    -RedirectStandardError  $apiErrLog `
    -NoNewWindow -PassThru

Write-Host -NoNewline "  waiting for API health"
$apiUp = $false
for ($i = 0; $i -lt 45; $i++) {
    try {
        $resp = Invoke-WebRequest -Uri "http://localhost:$ApiPort/healthz" -UseBasicParsing -TimeoutSec 3
        if ($resp.StatusCode -eq 200) { $apiUp = $true; break }
    }
    catch {
        # Not up yet. Connection refused during startup is expected, so this
        # loop deliberately swallows it and retries rather than failing fast.
    }
    Write-Host -NoNewline "."
    Start-Sleep -Seconds 2
}
Write-Host ""

if (-not $apiUp) {
    Write-Host "API failed to start - last log lines:" -ForegroundColor Red
    if (Test-Path $apiLog)    { Get-Content $apiLog    -Tail 20 }
    if (Test-Path $apiErrLog) { Get-Content $apiErrLog -Tail 20 }
    Stop-WithError "the UI would have had nothing to talk to."
}
Write-Ok "API up (Swagger: http://localhost:$ApiPort/swagger)"

# -------------------------------------------------------------- frontend ----
Write-Step "Starting frontend on :$FrontendPort"
Set-Location (Join-Path $RepoRoot 'frontend')

# A fresh clone has no node_modules; going straight to 'npm run dev' would die
# here with vite not found, after the API is already up. 'npm ci' (not install)
# reproduces exactly the tree the gate verified.
if (-not (Test-Path 'node_modules\.bin\vite.cmd')) {
    if (-not (Test-Path 'package-lock.json')) {
        Stop-WithError "node_modules is missing and there is no package-lock.json to install from."
    }
    Write-Host "  -- node_modules missing: npm ci (first run takes a minute) --"
    npm ci
    if ($LASTEXITCODE -ne 0) { Stop-WithError "npm ci failed." }
}

$viteArgs = @('run', 'dev', '--', '--port', "$FrontendPort", '--strictPort')
if ($Lan) {
    $viteArgs += '--host'
    Write-Warn "exposing the dev server on the LAN. It has NO authentication -"
    Write-Host "        anyone who can reach this port has full read/write on the catalog."
}

# Opened after any install, so the browser does not race a cold npm ci.
Start-Job -ScriptBlock {
    Start-Sleep -Seconds 4
    Start-Process "http://localhost:$using:FrontendPort"
} | Out-Null

# try/finally rather than an engine-exiting handler: that handler runs in its
# own scope and cannot see $apiProc, so it silently did nothing and left the
# API holding :5100 after Ctrl+C. This stops it on every exit path.
try {
    npm @viteArgs
}
finally {
    if ($apiProc -and -not $apiProc.HasExited) {
        Write-Host "`n  stopping catalog API (pid $($apiProc.Id))"
        Stop-Process -Id $apiProc.Id -Force -ErrorAction SilentlyContinue
    }
}
