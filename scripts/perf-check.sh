#!/usr/bin/env bash
# Sprint 4 perf check: seed the catalogue past 1,000 products and time the
# list endpoint under realistic filter/sort combinations.
#
# Budgets are deliberately generous — this is a regression tripwire for the
# read model (missing index, accidental N+1, unbounded scan), not a benchmark.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Seed-derived constants, generated with the seed. Same reason the smoke suite
# sources them: a product code written out here silently rots the moment the
# catalogue is refreshed from a newer client export.
FACTS="$ROOT/scripts/seed-facts.env"
[ -f "$FACTS" ] || { echo "FATAL: $FACTS missing. Run scripts/generate-seed.py."; exit 1; }
# shellcheck disable=SC1090
. "$FACTS"

API_PORT=5100
BASE="http://localhost:$API_PORT"
TARGET_PRODUCTS=${TARGET_PRODUCTS:-1000}
BUDGET_MS=${BUDGET_MS:-1500}

COMPOSE="$ROOT/infra/docker-compose.yml"
# shellcheck source=lib/db-wait.sh
. "$ROOT/scripts/lib/db-wait.sh"

PASS=0
FAIL=0

if ! command -v dotnet >/dev/null; then
  for d in /usr/local/share/dotnet "$HOME/.dotnet" /opt/homebrew/opt/dotnet/libexec; do
    [ -x "$d/dotnet" ] && export PATH="$d:$PATH" && break
  done
fi

echo "== Ensuring infra is up =="
(cd "$ROOT/infra" && docker compose up -d) >/dev/null || { echo "FATAL: infra failed"; exit 1; }
# Socket readiness is true throughout db/init; this waits for TCP plus a
# populated catalogue, because the very next thing this script does is clone
# the seeded products and an empty catalogue would clone nothing while
# reporting success. See scripts/lib/db-wait.sh.
if ! pim_wait_for_db "$COMPOSE"; then
  echo "FATAL: PostgreSQL is not ready or the dev seed is missing — perf numbers"
  echo "measured against an empty or half-loaded catalogue would be meaningless."
  exit 1
fi

echo "== Seeding to >= $TARGET_PRODUCTS products =="
# Clone the existing seeded products with new codes. Reuses their attribute
# values via the same attribute_set so completeness still computes per row.
docker compose -f "$COMPOSE" exec -T postgres psql -U pim_admin -d pim -v ON_ERROR_STOP=1 <<SQL
SET search_path TO catalog;

-- Clones are prefixed 'PERF-<n>-'. A prefix (not a substring like '-P') is
-- required: real codes such as INS-PIR-80 contain '-P' and were previously
-- excluded from cloning, leaving the seed short of the target.
INSERT INTO product (product_code, primary_node_id, brand_id, attribute_set_id,
                     base_locale, lifecycle_state, workflow_state, owner_display_name, updated_at)
SELECT 'PERF-' || g || '-' || p.product_code,
       p.primary_node_id, p.brand_id, p.attribute_set_id,
       p.base_locale, p.lifecycle_state, p.workflow_state, p.owner_display_name,
       p.updated_at - (g || ' minutes')::interval
FROM product p
CROSS JOIN generate_series(
  1,
  GREATEST(1, ($TARGET_PRODUCTS / GREATEST(1, (SELECT count(*) FROM product WHERE product_code NOT LIKE 'PERF-%'))) + 1)
) AS g
WHERE p.product_code NOT LIKE 'PERF-%'
ON CONFLICT (product_code) DO NOTHING;

INSERT INTO product_tr (product_id, locale_code, name, translation_status)
SELECT c.id, tr.locale_code, tr.name || ' (perf ' || c.id || ')', tr.translation_status
FROM product c
JOIN product orig ON orig.product_code = regexp_replace(c.product_code, '^PERF-[0-9]+-', '')
JOIN product_tr tr ON tr.product_id = orig.id
WHERE c.product_code LIKE 'PERF-%'
ON CONFLICT (product_id, locale_code) DO NOTHING;

INSERT INTO attribute_value (product_id, attribute_id, value_text, data_state)
SELECT c.id, av.attribute_id, av.value_text, av.data_state
FROM product c
JOIN product orig ON orig.product_code = regexp_replace(c.product_code, '^PERF-[0-9]+-', '')
JOIN attribute_value av ON av.product_id = orig.id
WHERE c.product_code LIKE 'PERF-%'
ON CONFLICT DO NOTHING;

ANALYZE product;
ANALYZE product_tr;
ANALYZE attribute_value;
SQL
[ $? -ne 0 ] && { echo "FATAL: perf seed failed"; exit 1; }

COUNT=$(docker compose -f "$COMPOSE" exec -T postgres psql -U pim_admin -d pim -tAc \
  "SELECT count(*) FROM catalog.product" | tr -d '[:space:]')
echo "products now: $COUNT"
if [ "${COUNT:-0}" -lt "$TARGET_PRODUCTS" ]; then
  echo "FAIL: seeding produced only $COUNT products (< $TARGET_PRODUCTS)"
  exit 1
fi

# Start the API if it is not already listening.
STARTED_API=false
if ! curl -sf "$BASE/healthz" >/dev/null 2>&1; then
  echo "== Starting API =="
  export ASPNETCORE_ENVIRONMENT=Development
  export ASPNETCORE_URLS="$BASE"
  (cd "$ROOT/services/catalog" && dotnet run --project src/Pim.Catalog.Api --no-launch-profile > "$ROOT/api.log" 2>&1) &
  API_PID=$!
  STARTED_API=true
  trap 'kill $API_PID 2>/dev/null' EXIT
  for _ in $(seq 1 45); do
    curl -sf "$BASE/healthz" >/dev/null 2>&1 && break
    sleep 2
  done
fi
curl -sf "$BASE/healthz" >/dev/null 2>&1 || { echo "FATAL: API not healthy"; tail -20 "$ROOT/api.log"; exit 1; }

time_request() { # label, url
  # Warm once (JIT + plan cache), then take the best of three.
  #
  # The status code is checked FIRST. A 404 is the fastest response the API can
  # produce, so timing one and calling it a pass is worse than not measuring at
  # all: every detail-tab budget reported 1ms while the hard-coded product code
  # had not existed since the seed was regenerated (2026-08-16).
  local status
  status=$(curl -s -o /dev/null -w '%{http_code}' "$2")
  if [ "$status" != "200" ]; then
    echo "  FAIL  $1 — HTTP $status, nothing was measured ($2)"
    FAIL=$((FAIL + 1))
    return
  fi

  local best=999999
  for _ in 1 2 3; do
    local ms
    ms=$(curl -s -o /dev/null -w '%{time_total}' "$2" | awk '{printf "%d", $1 * 1000}')
    [ "$ms" -lt "$best" ] && best=$ms
  done

  if [ "$best" -le "$BUDGET_MS" ]; then
    echo "  PASS  $1 — ${best}ms (budget ${BUDGET_MS}ms)"
    PASS=$((PASS + 1))
  else
    echo "  FAIL  $1 — ${best}ms exceeds ${BUDGET_MS}ms"
    FAIL=$((FAIL + 1))
  fi
}

echo
echo "== Timings at $COUNT products =="
time_request "first page, default sort"      "$BASE/api/v1/products"
time_request "sort by completeness"          "$BASE/api/v1/products?sortBy=quality&sortDir=desc"
time_request "sort by name"                  "$BASE/api/v1/products?sortBy=name"
# 'AB-' prefixes every seeded code and survives the PERF- clone prefix, so this
# is the widest possible ILIKE — the worst case for the search path. 'fire' was
# left over from the previous seed and had matched nothing since it changed,
# which made this the cheapest query in the file rather than the dearest.
time_request "free-text search (broad)"      "$BASE/api/v1/products?search=AB-"
time_request "free-text search (narrow)"     "$BASE/api/v1/products?search=$SEED_CATEGORY_CODE"
time_request "status filter"                 "$BASE/api/v1/products?status=Draft"

# Resolve a real category rather than guessing an id: an id that matches no
# node still returns 200, so the subtree join would never actually be exercised.
CATEGORY_ID=$(curl -s "$BASE/api/v1/taxonomy-nodes?nodeType=CATEGORY" \
  | node -e 'let r="";process.stdin.on("data",d=>r+=d).on("end",()=>{
      const n=JSON.parse(r).find(x=>x.code===process.argv[1]);
      process.stdout.write(String(n ? n.id : ""));})' "$SEED_CATEGORY_CODE")
if [ -z "$CATEGORY_ID" ]; then
  echo "  FAIL  category subtree filter — could not resolve category '$SEED_CATEGORY_CODE'"
  FAIL=$((FAIL + 1))
else
  time_request "category subtree filter"     "$BASE/api/v1/products?categoryId=$CATEGORY_ID"
fi
time_request "deep page (page 20)"           "$BASE/api/v1/products?page=20"
time_request "max page size (100)"           "$BASE/api/v1/products?pageSize=100"
time_request "CSV export, all matching"      "$BASE/api/v1/products/export.csv"

# ---------------------------------------------------------------------------
# Detail tabs (added Sprint 11).
#
# These are per-product reads, so most do not scale with catalogue size. They
# are timed anyway because two of them read reference data unfiltered by
# product — the classification feature and class catalogues — and would degrade
# as those grow rather than as the catalogue does. The grid was the only thing
# measured until now, and the grid was also the only thing that had regressed.
# ---------------------------------------------------------------------------
echo
echo "== Detail tab timings =="
DETAIL="$BASE/api/v1/products/$SEED_LEAD_PRODUCT"
time_request "detail: product"               "$DETAIL"
time_request "detail: attributes"            "$DETAIL/attributes"
time_request "detail: variants"              "$DETAIL/variants"
time_request "detail: classifications"       "$DETAIL/classifications"
time_request "detail: compliance"            "$DETAIL/compliance"
time_request "detail: packaging"             "$DETAIL/packaging"
time_request "detail: relationships"         "$DETAIL/relationships"
time_request "detail: applications"          "$DETAIL/applications"
# Planning composes three modules in one request and Localization fans out
# across every configured locale, so both do more work per call than the tabs
# that read a single table.
time_request "detail: planning"              "$DETAIL/planning"
time_request "detail: localization"          "$DETAIL/localization"

# The audit log is the only table in the system that grows without bound, and
# the Activity tab both pages it and COUNTs it on every request. Inflate it well
# past what a real product would accumulate and measure there, not at 20 rows.
AUDIT_ROWS=${AUDIT_ROWS:-25000}
echo
echo "== Inflating audit_log to >= $AUDIT_ROWS rows for the demo product =="
docker compose -f "$COMPOSE" exec -T postgres psql -U pim_admin -d pim -v ON_ERROR_STOP=1 <<SQL >/dev/null
SET search_path TO catalog;
INSERT INTO audit_log (occurred_at, action, entity_type, entity_id, new_value, source)
SELECT now() - (g || ' seconds')::interval,
       (ARRAY['created','updated','deleted','published','linked'])[1 + (g % 5)],
       'product',
       (SELECT id FROM product WHERE product_code = '$SEED_LEAD_PRODUCT'),
       jsonb_build_object('detail', 'perf row ' || g),
       'perf'
FROM generate_series(1, $AUDIT_ROWS) AS g;
SQL

AUDIT_COUNT=$(docker compose -f "$COMPOSE" exec -T postgres psql -U pim_admin -d pim -tAc   "SELECT count(*) FROM catalog.audit_log" | tr -d '[:space:]')
echo "  audit_log rows: $AUDIT_COUNT"

# Refuse to "measure" an empty table. The INSERT above resolves the product id
# with a subselect, so a stale product code makes entity_id NULL, the INSERT
# fails its NOT NULL constraint, and all four Activity timings run against 0
# rows and report 1ms. That happened on 2026-08-16 and still printed GREEN.
if [ "${AUDIT_COUNT:-0}" -lt "$AUDIT_ROWS" ]; then
  echo "FATAL: audit_log holds $AUDIT_COUNT rows, expected >= $AUDIT_ROWS."
  echo "       The Activity budgets below would be meaningless. Check that"
  echo "       '$SEED_LEAD_PRODUCT' is still in the catalogue."
  exit 1
fi

time_request "activity: first page"          "$DETAIL/activity?page=1&pageSize=25"
time_request "activity: action filter"       "$DETAIL/activity?page=1&pageSize=25&action=updated"
time_request "activity: search"              "$DETAIL/activity?page=1&pageSize=25&search=perf"
# Deep paging is where OFFSET-based paging degrades first.
time_request "activity: deep page (400)"     "$DETAIL/activity?page=400&pageSize=25"


# --- Product cockpit (Sprint 21) ---------------------------------------------
# The dashboard is the landing screen and the most expensive read in the
# service: six aggregates in one request, one of which computes completeness for
# every product in the catalogue. It is also the query most likely to be fine at
# 500 rows and unusable at 50,000, because nothing about it is paginated — there
# is no page size to hide behind.
#
# Measured at both locales. The completeness aggregate ignores the display
# locale by design (a product's completeness is a property of the product), but
# the label lookups do not, so a locale that forced a different plan would show
# up as a gap between these two numbers rather than as a wrong answer.
# --- Categories (Sprint 22) --------------------------------------------------
# The tree computes a subtree product count for EVERY node in one query, over an
# inflated catalogue. It is the query most likely to degrade quietly as the
# taxonomy grows, because nothing about it is paged and the cost is per node
# times per product.
time_request "categories: tree"        "$BASE/api/v1/categories/tree"
time_request "categories: tree (fr-FR)" "$BASE/api/v1/categories/tree?locale=fr-FR"

# --- Attribute Library (Sprint 23) -------------------------------------------
# Unpaged by design, and every row carries two catalogue-wide aggregates
# (applicable products, products with a value). That is the combination most
# likely to look fine at 36 attributes and stop being fine at 3,600.
time_request "attributes: library"     "$BASE/api/v1/attributes"

# --- Translation Workbench (Sprint 24) ---------------------------------------
# The summary counts every product against every locale in one pass; the queue
# is the only paged read added since the grid. Both are timed because the
# summary's cost grows with products x languages, which is the term nobody
# notices until a fourth language is configured.
# The target comes from seed-facts.env, which is generated with the seed — not
# scraped out of a JSON response with sed, which would depend on key order.
TR_TARGET="${SEED_TARGET_LOCALE:-en-GB}"
time_request "translations: workbench"  "$BASE/api/v1/translations"
time_request "translations: queue"      "$BASE/api/v1/translations/queue?target=$TR_TARGET&state=attention&pageSize=25"
time_request "translations: deep page"  "$BASE/api/v1/translations/queue?target=$TR_TARGET&state=all&page=40&pageSize=25"

# --- Digital Assets (Sprint 25) ----------------------------------------------
# The summary runs five catalogue-wide aggregates in one statement, including a
# NOT EXISTS over every product. The orphan filter is a NOT EXISTS over every
# asset. Both are the shape that is fine at 2,000 rows and not at 200,000.
time_request "assets: summary"         "$BASE/api/v1/assets/summary"
time_request "assets: library"         "$BASE/api/v1/assets?pageSize=24"
time_request "assets: orphan filter"   "$BASE/api/v1/assets?usage=orphan&pageSize=24"

# Serving a rendition. Cheap by construction — it is a file read — but it is the
# request a storefront makes most often, so a regression here is the one that
# would be felt first. Uses whichever local asset the smoke run left behind;
# skipped rather than faked if there is none.
# usage=orphan, not the unfiltered first page: uploads sort after the imported
# catalogue by file name, so the plain list never contained one and this case
# silently skipped — a perf check that measures nothing, which is R-10's whole
# lesson. Freshly uploaded assets are unlinked by definition, so the orphan
# filter finds them precisely.
DAM_LOCAL=$(curl -s "$BASE/api/v1/assets?usage=orphan&pageSize=100" | node -e '
let raw="";process.stdin.on("data",d=>raw+=d);process.stdin.on("end",()=>{
  try{const d=JSON.parse(raw);const a=(d.items||[]).find(i=>i.storageProvider==="local");
  process.stdout.write(a?a.publicId:"")}catch{process.stdout.write("")}});')
if [ -n "$DAM_LOCAL" ]; then
  time_request "assets: thumb bytes"   "$BASE/api/v1/assets/$DAM_LOCAL/file?rendition=thumb"
else
  echo "  SKIP  assets: thumb bytes — no locally stored asset present"
fi

# --- Workflow (Sprint 27) ----------------------------------------------------
# The definition is cached for five minutes, so the first of these three calls
# pays for the three round trips that load it and the rest do not. That is the
# point: this measures the CACHED path, which is the one every workflow request
# after the first actually takes.
time_request "workflow: pipeline"      "$BASE/api/v1/workflow/definition"

# One product's position and trail. The task lookup is a LATERAL over
# approval_task ordered by open-first, which is index-supported today and is
# exactly the query that degrades once a product accumulates years of closed
# tasks. Measured against a product the smoke run drove all the way to
# published, so the history join has rows rather than being an empty scan
# reporting 1ms (R-10).
PERF_WF=$(curl -s "$BASE/api/v1/products?status=Published&pageSize=100&sortBy=code&sortDir=asc" | node -e '
let raw="";process.stdin.on("data",d=>raw+=d);process.stdin.on("end",()=>{
  try{const d=JSON.parse(raw);const p=(d.items||[])[0];process.stdout.write(p?p.code:"")}
  catch{process.stdout.write("")}});')

if [ -n "$PERF_WF" ]; then
  time_request "workflow: product position" "$BASE/api/v1/products/$PERF_WF/workflow"
else
  echo "  SKIP  workflow: product position — no published product found"
fi

# A product with an OPEN task and a real trail: the smoke suite leaves
# $SEED_DRAFT_PRODUCT with two history rows, which is the case the tab hits
# most often.
time_request "workflow: open task + trail" "$BASE/api/v1/products/$SEED_DRAFT_PRODUCT/workflow"

# --- Workflows board and queue (Sprint 28) -----------------------------------
# The board is a catalogue-wide GROUP BY plus a window function over every
# product, which is the shape that is fine at 500 rows and is not at 200,000.
# It is served by ix_product_workflow_board; this is the tripwire for that
# index disappearing or stopping being used.
time_request "workflow: board"        "$BASE/api/v1/workflow/board"
time_request "workflow: board (fr-FR)" "$BASE/api/v1/workflow/board?locale=fr-FR"

# The list view: a paged read with the same laterals the grid uses, plus a
# lateral over approval_history for the last action.
time_request "workflow: queue"        "$BASE/api/v1/workflow/queue?pageSize=25"
time_request "workflow: queue filtered" "$BASE/api/v1/workflow/queue?pageSize=25&step=draft"
# Deep paging is where OFFSET degrades first, and the queue offers it.
time_request "workflow: queue deep page" "$BASE/api/v1/workflow/queue?pageSize=25&page=20"

time_request "dashboard: cockpit (base locale)" "$BASE/api/v1/dashboard"
time_request "dashboard: cockpit (fr-FR)"       "$BASE/api/v1/dashboard?locale=fr-FR"

echo
echo "== Summary =="
echo "  passed: $PASS   failed: $FAIL   products: $COUNT"
if [ $FAIL -eq 0 ]; then
  echo "PERF: GREEN"
  exit 0
else
  echo "PERF: RED — the read model needs attention before more rows land"
  exit 1
fi
