# Apply Plywood Categories Migration Script
param()

$ErrorActionPreference = 'Stop'

Write-Host "Executing Plywood Categories Migration..." -ForegroundColor Cyan

# 1. Execute SQL Migration in Postgres container
docker exec -i infra-postgres-1 psql -U pim_admin -d pim -f /pim-init/catalog/07_cleanup_tiles_and_rebuild_plywood_taxonomy.sql

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nSUCCESS: Taxonomy database updated!" -ForegroundColor Green
    Write-Host "Verifying product assignments..." -ForegroundColor Cyan
    docker exec -i infra-postgres-1 psql -U pim_admin -d pim -c "SELECT n.code, count(p.id) AS products FROM catalog.taxonomy_node n LEFT JOIN catalog.product p ON p.primary_node_id = n.id WHERE n.path <@ 'greenply.plywood_blockboard' GROUP BY n.code ORDER BY n.code;"
} else {
    Write-Host "`nFAILED to execute SQL migration." -ForegroundColor Red
}
