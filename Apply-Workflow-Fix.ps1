# Apply Workflow State Normalization & Pipeline Fix
param()

$ErrorActionPreference = 'Stop'

Write-Host "Applying Workflow Normalization & Pipeline Fix..." -ForegroundColor Cyan

$sqlFile = Join-Path $PSScriptRoot "services\catalog\db\init\10_fix_workflow_states_and_tasks.sql"

if (Test-Path $sqlFile) {
    Get-Content $sqlFile -Raw | docker exec -i infra-postgres-1 psql -U pim_admin -d pim
} else {
    Write-Error "Migration file not found: $sqlFile"
}

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nSUCCESS: Workflow states normalized and pipeline ensured!" -ForegroundColor Green
    Write-Host "Verifying product workflow states..." -ForegroundColor Cyan
    docker exec -i infra-postgres-1 psql -U pim_admin -d pim -c "SELECT workflow_state, count(*) AS products FROM catalog.product GROUP BY workflow_state ORDER BY workflow_state;"
    Write-Host "`nVerifying active workflow definition..." -ForegroundColor Cyan
    docker exec -i infra-postgres-1 psql -U pim_admin -d pim -c "SELECT id, code, name, entity_type, is_active FROM catalog.workflow_definition WHERE is_active;"
} else {
    Write-Host "`nFAILED to execute workflow migration." -ForegroundColor Red
}
