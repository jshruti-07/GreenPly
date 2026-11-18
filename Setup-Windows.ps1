<#
.SYNOPSIS
  One-time bootstrap of a Windows machine for the PIM platform.

.DESCRIPTION
  The Windows equivalent of 'Setup Mac.command'. Installs ONLY what is missing,
  into your user profile, with no administrator rights:

    .NET SDK 10.x   -> %USERPROFILE%\.dotnet      (official dotnet-install.ps1)
    Node 22.x       -> %USERPROFILE%\.local\node22 (official nodejs.org zip,
                                                    SHA-256 verified)

  Verifies but NEVER installs:
    Docker Desktop  -> must be installed and RUNNING. It is a GUI application
                       whose first launch needs a licence accepted and a
                       privileged helper installed; scripting that would be
                       unreliable and a poor security posture.

  Then prepares the repo: npm ci, dotnet restore / build / test.

  Idempotent - safe to re-run. Transcript written to setup-log.txt.

.PARAMETER CheckOnly
  Report what is missing and change nothing.

.EXAMPLE
  .\Setup-Windows.ps1 -CheckOnly
.EXAMPLE
  .\Setup-Windows.ps1
#>
[CmdletBinding()]
param(
    [switch]$CheckOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepoRoot
Start-Transcript -Path (Join-Path $RepoRoot 'setup-log.txt') -Force | Out-Null

$DotnetChannel = '10.0'
$DotnetMinBand = 100      # global.json pins 10.0.100, rollForward latestFeature
$NodeMajor     = 22

$script:NeedDotnet = $true
$script:NeedNode   = $true
$script:DockerOk   = $false
$script:DotnetExe  = $null
$script:NodeDir    = $null

function Write-Step { param([string]$T) Write-Host "`n== $T ==" -ForegroundColor Cyan }
function Write-Ok   { param([string]$T) Write-Host "  PASS  $T" -ForegroundColor Green }
function Write-Bad  { param([string]$T) Write-Host "  FAIL  $T" -ForegroundColor Red }
function Write-Warn { param([string]$T) Write-Host "  WARN  $T" -ForegroundColor Yellow }
function Stop-WithError {
    param([string]$T)
    Write-Host "`nFATAL: $T" -ForegroundColor Red
    Stop-Transcript | Out-Null
    exit 1
}

Write-Step 'Machine'
Write-Host "  OS    : $([System.Environment]::OSVersion.VersionString)"
Write-Host "  Arch  : $env:PROCESSOR_ARCHITECTURE"
Write-Host "  Repo  : $RepoRoot"
Write-Host "  Started: $(Get-Date)"

if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
    $NodeArch = 'win-arm64'
} else {
    $NodeArch = 'win-x64'
}

if (-not (Test-Path (Join-Path $RepoRoot 'services\catalog\Pim.Catalog.sln'))) {
    Stop-WithError @"
This does not look like the pim-platform repo root
       (services\catalog\Pim.Catalog.sln not found). Put this script beside
       'Run-PIM.ps1' and try again.
"@
}

# ---------------------------------------------------------------- .NET SDK --
Write-Step ".NET SDK $DotnetChannel"

function Find-DotnetExe {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($c in @(
        (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'),
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe')
    )) { if (Test-Path $c) { return $c } }
    return $null
}

# global.json wants 10.0.100+ with rollForward latestFeature, so any 10.0
# feature band at or above 100 satisfies the build.
function Test-DotnetBand {
    param([string]$Exe)
    $sdks = & $Exe --list-sdks 2>$null
    if (-not $sdks) { return $false }
    foreach ($line in $sdks) {
        $version = ($line -split '\s+')[0]
        $parts = $version -split '\.'
        if ($parts.Count -ge 3 -and $parts[0] -eq '10' -and $parts[1] -eq '0') {
            $band = 0
            if ([int]::TryParse($parts[2], [ref]$band) -and $band -ge $DotnetMinBand) { return $true }
        }
    }
    return $false
}

$script:DotnetExe = Find-DotnetExe
if ($script:DotnetExe -and (Test-DotnetBand -Exe $script:DotnetExe)) {
    Write-Ok "found $(& $script:DotnetExe --version) at $($script:DotnetExe)"
    $script:NeedDotnet = $false
}
elseif ($script:DotnetExe) {
    Write-Warn "dotnet present at $($script:DotnetExe) but no SDK satisfies global.json (10.0.$DotnetMinBand+)"
    & $script:DotnetExe --list-sdks | ForEach-Object { Write-Host "          $_" }
}
else {
    Write-Bad 'dotnet not found'
}

if ($script:NeedDotnet -and -not $CheckOnly) {
    $installer = Join-Path $env:TEMP "dotnet-install-$([guid]::NewGuid()).ps1"
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
    $installDir = Join-Path $env:USERPROFILE '.dotnet'
    & $installer -Channel $DotnetChannel -InstallDir $installDir
    Remove-Item $installer -ErrorAction SilentlyContinue

    $script:DotnetExe = Join-Path $installDir 'dotnet.exe'
    if (-not (Test-Path $script:DotnetExe)) { Stop-WithError "dotnet-install.ps1 finished but $($script:DotnetExe) is missing." }
    if (-not (Test-DotnetBand -Exe $script:DotnetExe)) {
        Stop-WithError "Installed SDK does not satisfy global.json (needs 10.0.$DotnetMinBand+)."
    }
    Write-Ok "installed $(& $script:DotnetExe --version)"
    $script:NeedDotnet = $false
}

if ($script:DotnetExe) {
    $env:DOTNET_ROOT = Split-Path -Parent $script:DotnetExe
    $env:PATH = "$($env:DOTNET_ROOT);$($env:PATH)"
}

# -------------------------------------------------------------------- Node --
Write-Step "Node $NodeMajor+"

function Find-NodeDir {
    $cmd = Get-Command node -ErrorAction SilentlyContinue
    if ($cmd) { return (Split-Path -Parent $cmd.Source) }
    $c = Join-Path $env:USERPROFILE '.local\node22'
    if (Test-Path (Join-Path $c 'node.exe')) { return $c }
    return $null
}

$script:NodeDir = Find-NodeDir
if ($script:NodeDir) {
    $nodeVersion = & (Join-Path $script:NodeDir 'node.exe') -v 2>$null
    $major = 0
    if ($nodeVersion -match '^v(\d+)') { $major = [int]$Matches[1] }
    if ($major -ge $NodeMajor) {
        Write-Ok "found $nodeVersion at $($script:NodeDir)"
        $script:NeedNode = $false
    }
    else {
        Write-Bad "node $nodeVersion is older than v$NodeMajor"
    }
}
else {
    Write-Bad 'node not found'
}

if ($script:NeedNode -and -not $CheckOnly) {
    $base  = "https://nodejs.org/dist/latest-v$NodeMajor.x"
    $sums  = (Invoke-WebRequest -Uri "$base/SHASUMS256.txt" -UseBasicParsing).Content
    $entry = $sums -split "`n" | Where-Object { $_ -match "node-v$NodeMajor\.[\d.]+-$NodeArch\.zip\s*$" } | Select-Object -First 1
    if (-not $entry) { Stop-WithError "No $NodeArch zip listed at $base - cannot install Node unattended." }

    $fields   = $entry -split '\s+'
    $expected = $fields[0]
    $fileName = $fields[1]

    $tmpZip = Join-Path $env:TEMP $fileName
    Write-Host "  downloading $fileName"
    Invoke-WebRequest -Uri "$base/$fileName" -OutFile $tmpZip -UseBasicParsing

    $actual = (Get-FileHash -Path $tmpZip -Algorithm SHA256).Hash
    if ($actual -ne $expected.ToUpperInvariant()) {
        Remove-Item $tmpZip -ErrorAction SilentlyContinue
        Stop-WithError "SHA-256 mismatch on $fileName - refusing to install."
    }
    Write-Ok 'checksum verified'

    $localRoot = Join-Path $env:USERPROFILE '.local'
    New-Item -ItemType Directory -Path $localRoot -Force | Out-Null
    $staging = Join-Path $env:TEMP "node-stage-$([guid]::NewGuid())"
    Expand-Archive -Path $tmpZip -DestinationPath $staging -Force
    Remove-Item $tmpZip -ErrorAction SilentlyContinue

    $unpacked = Get-ChildItem -Path $staging -Directory | Select-Object -First 1
    if (-not $unpacked -or -not (Test-Path (Join-Path $unpacked.FullName 'node.exe'))) {
        Stop-WithError "Unexpected zip layout: no node.exe under $staging"
    }

    $dest = Join-Path $localRoot 'node22'
    # Move the old one aside rather than deleting, so a bad extract never
    # destroys a working install.
    if (Test-Path $dest) {
        Move-Item -Path $dest -Destination "$dest.old.$(Get-Date -Format yyyyMMdd-HHmmss)"
    }
    Move-Item -Path $unpacked.FullName -Destination $dest
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue

    $script:NodeDir = $dest
    $env:PATH = "$dest;$($env:PATH)"
    Write-Ok "installed $(& (Join-Path $dest 'node.exe') -v) at $dest"
    $script:NeedNode = $false
}
elseif (-not $script:NeedNode) {
    $env:PATH = "$($script:NodeDir);$($env:PATH)"
}

# ------------------------------------------------------------------ Docker --
Write-Step 'Docker'
$dockerCmd = Get-Command docker -ErrorAction SilentlyContinue
if (-not $dockerCmd) {
    Write-Bad 'docker CLI not found - Docker Desktop is not installed'
    Write-Host '        Install it from https://www.docker.com/products/docker-desktop/'
    Write-Host '        launch it once, accept the terms, then re-run this script.'
}
else {
    docker info *> $null
    if ($LASTEXITCODE -eq 0) {
        Write-Ok "docker engine responding ($(docker --version))"
        $script:DockerOk = $true
    }
    else {
        Write-Bad 'docker CLI found but the daemon is not responding'
        Write-Host "        Start Docker Desktop and wait for 'Engine running', then re-run."
    }
}

if ($CheckOnly) {
    Write-Step 'Check-only mode - nothing was installed'
    Stop-Transcript | Out-Null
    exit 0
}

# ---------------------------------------------------- frontend dependencies --
Write-Step 'Frontend dependencies'
if ($script:NeedNode) {
    Write-Warn 'skipped: Node is not available'
}
else {
    Push-Location (Join-Path $RepoRoot 'frontend')
    try {
        if (-not (Test-Path 'package-lock.json')) {
            Stop-WithError 'frontend\package-lock.json missing - refusing to run npm install against an unpinned tree.'
        }
        # npm ci, not install: reproduces exactly the tree the gate verified and
        # fails loudly if package.json and the lockfile have drifted.
        npm ci
        if ($LASTEXITCODE -ne 0) { Stop-WithError 'npm ci failed.' }
    }
    finally { Pop-Location }
    Write-Ok 'node_modules installed from lockfile'
}

# ---------------------------------------------------- backend build / test --
Write-Step 'Backend restore / build / test'
if ($script:NeedDotnet) {
    Write-Warn 'skipped: .NET SDK 10 is not available'
}
else {
    Push-Location (Join-Path $RepoRoot 'services\catalog')
    try {
        & $script:DotnetExe restore
        if ($LASTEXITCODE -ne 0) { Stop-WithError 'dotnet restore failed.' }
        & $script:DotnetExe build --no-restore
        if ($LASTEXITCODE -ne 0) { Stop-WithError 'dotnet build failed.' }
        Write-Ok 'catalog service builds'

        # Pim.Catalog.Integration.Tests drives Testcontainers, which needs a live
        # Docker daemon. Running it without one produces ~49 connection failures
        # that look like product defects but are pure environment noise.
        if ($script:DockerOk) {
            & $script:DotnetExe test --no-build
            if ($LASTEXITCODE -ne 0) { Stop-WithError 'backend tests failed.' }
            Write-Ok 'catalog tests green (unit + integration)'
        }
        else {
            Write-Warn 'Docker is down: running unit test projects only, skipping Testcontainers integration tests'
            & $script:DotnetExe test --no-build 'tests\Pim.Catalog.Domain.Tests'
            if ($LASTEXITCODE -ne 0) { Stop-WithError 'domain unit tests failed.' }
            & $script:DotnetExe test --no-build 'tests\Pim.Catalog.Application.Tests'
            if ($LASTEXITCODE -ne 0) { Stop-WithError 'application unit tests failed.' }
            Write-Ok 'catalog unit tests green (integration tests NOT run - Docker required)'
        }
    }
    finally { Pop-Location }
}

# ----------------------------------------------------------------- summary --
Write-Step 'Summary'
$ready = $true
if ($script:NeedDotnet) { Write-Bad '.NET SDK 10';   $ready = $false } else { Write-Ok '.NET SDK 10' }
if ($script:NeedNode)   { Write-Bad "Node $NodeMajor+"; $ready = $false } else { Write-Ok "Node $NodeMajor+" }
if (-not $script:DockerOk) { Write-Bad 'Docker engine'; $ready = $false } else { Write-Ok 'Docker engine' }

if ($ready) {
    # Persist both on the user PATH so a fresh shell finds them.
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (-not $userPath) { $userPath = '' }
    $toAdd = @(@($env:DOTNET_ROOT, $script:NodeDir) | Where-Object { $_ -and ($userPath -notlike "*$_*") })
    if ($toAdd.Count -gt 0) {
        $newPath = (@($toAdd) + $userPath) -join ';'
        [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
        [Environment]::SetEnvironmentVariable('DOTNET_ROOT', $env:DOTNET_ROOT, 'User')
        Write-Ok 'PATH and DOTNET_ROOT recorded for your user account (new shells pick them up)'
    }

    Write-Host "`nSetup complete." -ForegroundColor Green
    Write-Host "Next:  .\Run-PIM.ps1     (infra + API :5100 + frontend :5174)"
    Write-Host "First run also pulls the postgres/rabbitmq/redis images - allow a few minutes."
}
else {
    Write-Host "`nSetup incomplete - resolve the FAIL lines above and re-run." -ForegroundColor Red
    Stop-Transcript | Out-Null
    exit 1
}

Stop-Transcript | Out-Null
