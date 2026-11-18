#!/bin/bash
# Drops the Postgres volume and re-applies db/init/*.sql (schema + dev seed),
# WITHOUT building or testing. Use while iterating on DDL — the full gate takes
# minutes and most schema mistakes surface here in seconds.
#
# Destroys local PIM database contents. Nothing else.
cd "$(dirname "$0")" || exit 1
LOG="$PWD/db-reset-log.txt"

{
  echo "=== DB reset: $(date) ==="
  command -v docker >/dev/null || { echo "FATAL: docker not found. Start Docker Desktop."; exit 1; }

  cd infra || exit 1
  docker compose down -v || exit 1
  docker compose up -d postgres || exit 1

  echo "--- waiting for postgres ---"
  READY=false
  for _ in $(seq 1 420); do
    if docker compose exec -T postgres pg_isready -U pim_admin -d pim >/dev/null 2>&1; then READY=true; break; fi
    sleep 1
  done
  [ "$READY" = true ] || { echo "FATAL: postgres did not become ready in 7 minutes"; docker compose logs postgres | tail -30; exit 1; }

  # Postgres runs the init scripts asynchronously after the port opens, so
  # pg_isready alone is not proof the DDL finished. Wait for the last object
  # the seed creates, then surface any init error from the container log.
  echo "--- waiting for db/init to finish ---"
  SEEDED=false
  for _ in $(seq 1 420); do
    N=$(docker compose exec -T postgres psql -U pim_admin -d pim -tAc \
      "SELECT count(*) FROM catalog.product_relationship" 2>/dev/null | tr -d '[:space:]')
    if [ -n "$N" ] && [ "$N" != "0" ]; then SEEDED=true; break; fi
    sleep 1
  done

  if [ "$SEEDED" != true ]; then
    echo "FATAL: init scripts did not complete. Container log follows:"
    docker compose logs postgres | grep -iE "error|fatal|invalid|syntax" | tail -30
    exit 1
  fi

  echo "--- row counts ---"
  docker compose exec -T postgres psql -U pim_admin -d pim -c "
    SELECT 'product' t, count(*) FROM catalog.product
    UNION ALL SELECT 'sku', count(*) FROM catalog.sku
    UNION ALL SELECT 'classification_scheme', count(*) FROM catalog.classification_scheme
    UNION ALL SELECT 'classification_class', count(*) FROM catalog.classification_class
    UNION ALL SELECT 'classification_feature', count(*) FROM catalog.classification_feature
    UNION ALL SELECT 'product_classification', count(*) FROM catalog.product_classification
    UNION ALL SELECT 'product_classification_value', count(*) FROM catalog.product_classification_value
    UNION ALL SELECT 'compliance_document', count(*) FROM catalog.compliance_document
    UNION ALL SELECT 'sustainability_indicator', count(*) FROM catalog.sustainability_indicator
    UNION ALL SELECT 'material_composition', count(*) FROM catalog.material_composition
    UNION ALL SELECT 'substance_compliance', count(*) FROM catalog.substance_compliance
    UNION ALL SELECT 'green_building_contribution', count(*) FROM catalog.green_building_contribution
    UNION ALL SELECT 'packaging_unit', count(*) FROM catalog.packaging_unit
    UNION ALL SELECT 'application', count(*) FROM catalog.application
    UNION ALL SELECT 'application_tr', count(*) FROM catalog.application_tr
    UNION ALL SELECT 'product_application', count(*) FROM catalog.product_application
    UNION ALL SELECT 'product_relationship', count(*) FROM catalog.product_relationship
    UNION ALL SELECT 'audit_log', count(*) FROM catalog.audit_log
    UNION ALL SELECT 'asset', count(*) FROM catalog.asset
    UNION ALL SELECT 'asset_link', count(*) FROM catalog.asset_link
    UNION ALL SELECT 'asset_tr', count(*) FROM catalog.asset_tr
    UNION ALL SELECT 'asset_rendition', count(*) FROM catalog.asset_rendition
    UNION ALL SELECT 'taxonomy_node', count(*) FROM catalog.taxonomy_node
    UNION ALL SELECT 'attribute_option', count(*) FROM catalog.attribute_option
    ORDER BY 1;"

  echo "DB RESET: OK"
} > "$LOG" 2>&1
STATUS=$?

{ echo; echo "=== exit code: $STATUS ==="; } >> "$LOG" 2>&1
echo "Done — see db-reset-log.txt (exit code $STATUS). You can close this window."
exit $STATUS
