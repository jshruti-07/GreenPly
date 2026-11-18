# Apply Plywood Digital Assets Migration Script
param()

$ErrorActionPreference = 'Stop'

Write-Host "Executing Digital Assets Migration (Ceramics to Plywood)..." -ForegroundColor Cyan

# Execute SQL Migration in Postgres container
docker exec -i infra-postgres-1 psql -U pim_admin -d pim -f /pim-init/catalog/08_replace_tile_assets_with_plywood_assets.sql

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nSUCCESS: Digital assets updated in database!" -ForegroundColor Green
    Write-Host "Checking assets by type and link counts..." -ForegroundColor Cyan
    docker exec -i infra-postgres-1 psql -U pim_admin -d pim -c "SELECT a.asset_type, count(DISTINCT a.id) AS total_assets, count(l.id) AS total_links FROM catalog.asset a LEFT JOIN catalog.asset_link l ON l.asset_id = a.id GROUP BY a.asset_type ORDER BY a.asset_type;"
} else {
    Write-Host "`nFAILED to execute assets migration." -ForegroundColor Red
}
