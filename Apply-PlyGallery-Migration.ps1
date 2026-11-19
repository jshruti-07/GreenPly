# Apply Ply Gallery Branding Migration Script
# Safely updates running PostgreSQL database container with zero data loss.
param(
    [string]$ContainerName = "infra-postgres-1",
    [string]$DbUser = "pim_admin",
    [string]$DbName = "pim"
)

$ErrorActionPreference = 'Stop'

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Applying Ply Gallery Rebranding Migration to Database   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$SqlFile = Join-Path $PSScriptRoot "Apply-PlyGallery-Migration.sql"
if (-not (Test-Path $SqlFile)) {
    Write-Error "Migration file not found: $SqlFile"
    exit 1
}

# 1. Pipe SQL Migration into Postgres container
Write-Host "`n[1/2] Executing migration inside container '$ContainerName'..." -ForegroundColor Yellow
Get-Content $SqlFile -Raw | docker exec -i $ContainerName psql -U $DbUser -d $DbName

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n[2/2] SUCCESS: Database successfully rebranded to Ply Gallery!" -ForegroundColor Green
    Write-Host "Verifying product and taxonomy paths..." -ForegroundColor Cyan
    docker exec -i $ContainerName psql -U $DbUser -d $DbName -c "SELECT n.code, count(p.id) AS products FROM catalog.taxonomy_node n LEFT JOIN catalog.product p ON p.primary_node_id = n.id WHERE n.path <@ 'plygallery' GROUP BY n.code ORDER BY n.code;"
} else {
    Write-Host "`nFAILED: Migration execution encountered an error." -ForegroundColor Red
    exit $LASTEXITCODE
}
