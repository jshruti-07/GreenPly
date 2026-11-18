#!/bin/bash
# Reports what the database currently contains and surfaces any init error,
# without touching anything. Safe to run at any time.
cd "$(dirname "$0")" || exit 1
LOG="$PWD/db-status-log.txt"

{
  echo "=== DB status: $(date) ==="
  cd infra || exit 1

  echo "--- containers ---"
  docker compose ps --format '{{.Service}}\t{{.State}}\t{{.Status}}'

  echo
  echo "--- can we connect? ---"
  if docker compose exec -T postgres pg_isready -U pim_admin -d pim; then
    echo
    echo "--- row counts ---"
    docker compose exec -T postgres psql -U pim_admin -d pim -c "
      SELECT 'product' t, count(*) FROM catalog.product
      UNION ALL SELECT 'product_tr', count(*) FROM catalog.product_tr
      UNION ALL SELECT 'sku', count(*) FROM catalog.sku
      UNION ALL SELECT 'taxonomy_node', count(*) FROM catalog.taxonomy_node
      UNION ALL SELECT 'attribute', count(*) FROM catalog.attribute
      UNION ALL SELECT 'attribute_option', count(*) FROM catalog.attribute_option
      UNION ALL SELECT 'attribute_value', count(*) FROM catalog.attribute_value
      UNION ALL SELECT 'asset', count(*) FROM catalog.asset
      UNION ALL SELECT 'asset_link', count(*) FROM catalog.asset_link
      UNION ALL SELECT 'product_relationship', count(*) FROM catalog.product_relationship
      ORDER BY 1;" 2>&1 | head -30

    echo
    echo "--- full-text search health (Sprint 14) ---"
    docker compose exec -T postgres psql -U pim_admin -d pim -c "
      SELECT count(*) FILTER (WHERE search_vector IS NULL)     AS null_vectors,
             count(*) FILTER (WHERE search_vector = ''::tsvector) AS empty_vectors,
             count(*)                                          AS products
      FROM catalog.product;" 2>&1 | head -10
    docker compose exec -T postgres psql -U pim_admin -d pim -c "
      SELECT p.product_code, length(p.search_vector::text) AS vector_len
      FROM catalog.product p ORDER BY p.product_code LIMIT 3;" 2>&1 | head -10
    echo "  a German stem must match an inflected form:"
    docker compose exec -T postgres psql -U pim_admin -d pim -c "
      SELECT count(*) AS hits_for_abdeckplatte
      FROM catalog.product
      WHERE search_vector @@ websearch_to_tsquery('german', 'Abdeckplatte');" 2>&1 | head -8

    echo
    echo "--- sample products ---"
    docker compose exec -T postgres psql -U pim_admin -d pim -c "
      SELECT p.product_code, tr.name
      FROM catalog.product p
      JOIN catalog.product_tr tr ON tr.product_id = p.id AND tr.locale_code = 'de-DE'
      ORDER BY p.product_code LIMIT 8;" 2>&1 | head -20
  else
    echo "postgres is not accepting connections yet"
  fi

  echo
  echo "--- any init errors in the container log? ---"
  docker compose logs postgres 2>&1 | grep -iE "ERROR:|FATAL:|violates|does not exist|syntax error" | head -20 \
    || echo "  none found"

  echo
  echo "--- last lines of the container log ---"
  docker compose logs --tail 8 postgres 2>&1
} > "$LOG" 2>&1

echo "Done — see db-status-log.txt. You can close this window."
