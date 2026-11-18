#!/usr/bin/env bash
# End-to-end API smoke test: starts the catalog API against the seeded local
# database and asserts real HTTP responses across every shipped endpoint.
#
# Expectations come from scripts/seed-facts.env, which is generated next to
# db/init/02_dev_seed.sql by scripts/generate-seed.py. Nothing about the
# catalogue is written out here, so refreshing the seed from a newer client
# export means re-running the generator, not editing assertions.
#
# The suite assumes a freshly reset database (Reset DB Only.command). It
# restores what it can, but a few counts are absolute.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Seed-derived constants, generated alongside the seed itself. The suite reads
# these instead of hard-coding product codes and counts, so refreshing the
# catalogue from a newer client export does not mean editing 250 assertions.
FACTS="$ROOT/scripts/seed-facts.env"
[ -f "$FACTS" ] || { echo "FATAL: $FACTS missing. Run scripts/generate-seed.py."; exit 1; }
# shellcheck disable=SC1090
. "$FACTS"

# shellcheck source=lib/db-wait.sh
. "$ROOT/scripts/lib/db-wait.sh"

LEAD="$SEED_LEAD_PRODUCT"
SPARE="$SEED_SPARE_PRODUCT"
API_PORT=5100
BASE="http://localhost:$API_PORT"

if ! command -v dotnet >/dev/null; then
  for d in /usr/local/share/dotnet "$HOME/.dotnet" /opt/homebrew/opt/dotnet/libexec; do
    [ -x "$d/dotnet" ] && export PATH="$d:$PATH" && break
  done
fi
command -v dotnet >/dev/null || { echo "FATAL: dotnet not found"; exit 1; }
command -v node   >/dev/null || { echo "FATAL: node not found (used to parse JSON)"; exit 1; }

PASS=0
FAIL=0
check() { # name, actual, expected
  if [ "$2" = "$3" ]; then
    echo "  PASS  $1 (= $3)"
    PASS=$((PASS + 1))
  else
    echo "  FAIL  $1 — expected '$3', got '$2'"
    FAIL=$((FAIL + 1))
  fi
}

# jq is not guaranteed on macOS; node is (verified in this repo's toolchain).
#
# Usage: jsonq 'EXPR' [arg ...]
#   d  — the parsed response body
#   a  — the extra arguments, as strings
#
# The `a` channel exists because seed-derived values now come from
# seed-facts.env. Interpolating them into the expression string would mean
# every product code passed through two levels of quoting, and the single-quoted
# heredocs in this file silently do NOT interpolate — which is how three
# assertions ended up comparing against the literal text "$SPARE-XL".
jsonq() { local expr="$1"; shift; node -e '
  let raw = "";
  process.stdin.on("data", d => raw += d);
  process.stdin.on("end", () => {
    try {
      const a = process.argv.slice(2);
      process.stdout.write(String(eval("((d, a) => " + process.argv[1] + ")")(JSON.parse(raw), a)));
    }
    catch (e) { process.stdout.write("ERR:" + e.message); }
  });
' "$expr" "$@"; }

# A stale API from an earlier 'Run PIM.command' would answer on this port with
# an older build, producing misleading results. Reclaim the port, but ONLY if
# the listener is our own catalog API — never kill an unrelated process.
STALE_PIDS=$(lsof -nP -tiTCP:$API_PORT -sTCP:LISTEN 2>/dev/null | sort -u)
if [ -n "$STALE_PIDS" ]; then
  echo "== Port $API_PORT already in use =="
  for pid in $STALE_PIDS; do
    OWNER=$(ps -p "$pid" -o command= 2>/dev/null)
    case "$OWNER" in
      *Pim.Catalog.Api*)
        echo "  stale catalog API (pid $pid) — terminating so the smoke test runs against this build"
        kill "$pid" 2>/dev/null
        ;;
      *)
        echo "FATAL: port $API_PORT is held by an unrelated process (pid $pid):"
        echo "  $OWNER"
        echo "Free the port and re-run; this script will not kill processes it does not own."
        exit 1
        ;;
    esac
  done
  for _ in $(seq 1 15); do
    lsof -nP -iTCP:$API_PORT -sTCP:LISTEN >/dev/null 2>&1 || break
    sleep 1
  done
  if lsof -nP -iTCP:$API_PORT -sTCP:LISTEN >/dev/null 2>&1; then
    echo "FATAL: port $API_PORT still occupied after terminating the stale API."
    exit 1
  fi
  echo "  port released"
fi

echo "== Infra =="
(cd "$ROOT/infra" && docker compose up -d) >/dev/null || { echo "FATAL: infra failed"; exit 1; }

# The runner drops the volume before calling this script, so db/init runs from
# scratch here — roughly a minute for the 2.4 MB seed plus the French pack.
# Starting the API into that window is what makes the first assertions flaky:
# the API comes up, connects, and reads a catalogue that is still filling. Wait
# for TCP acceptance (false for the whole init phase) and a populated table.
echo "== Waiting for PostgreSQL and db/init =="
if ! pim_wait_for_db "$ROOT/infra/docker-compose.yml"; then
  echo "FATAL: PostgreSQL is not ready or the dev seed is missing. Every"
  echo "assertion below is written against that seed, so running now would"
  echo "produce failures that describe the database, not the API."
  exit 1
fi

echo "== Starting API on :$API_PORT =="
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="$BASE"
(cd "$ROOT/services/catalog" && dotnet run --project src/Pim.Catalog.Api --no-launch-profile > "$ROOT/api.log" 2>&1) &
API_PID=$!
trap 'kill $API_PID 2>/dev/null; wait $API_PID 2>/dev/null' EXIT

printf "waiting for health"
UP=1
for _ in $(seq 1 45); do
  if curl -sf "$BASE/healthz" >/dev/null 2>&1; then UP=0; break; fi
  printf "."
  sleep 2
done
echo
if [ $UP -ne 0 ]; then
  echo "FATAL: API never became healthy. Last log lines:"
  tail -30 "$ROOT/api.log"
  exit 1
fi

echo
# --- RabbitMQ observability queue (Sprint 20) --------------------------------
# Declared HERE, before any product is created, and that ordering is the whole
# trick: a topic exchange drops anything it cannot route, so a queue bound after
# the event was published would sit empty and the assertions further down would
# report "no delivery" for a transport that was working perfectly.
#
# The queue is the SMOKE SUITE's, not the service's. RabbitMqOutboxTransport
# declares the exchange and nothing else, because a producer that declares its
# consumers' queues has taken ownership of their retention and lifetime. This
# file is a consumer, so it owns this one.
RABBIT_MGMT="http://localhost:15672/api"
RABBIT_AUTH="pim:pim_local_dev"
SMOKE_QUEUE="smoke.outbox"
RABBIT_UP=0

if curl -s -o /dev/null -u "$RABBIT_AUTH" --max-time 5 "$RABBIT_MGMT/overview"; then
  RABBIT_UP=1
  # Same type and durability the transport declares, so whichever runs first the
  # other's declare is a no-op rather than a PRECONDITION_FAILED.
  curl -s -o /dev/null -u "$RABBIT_AUTH" -X PUT "$RABBIT_MGMT/exchanges/%2F/catalog.events" \
    -H 'content-type: application/json' -d '{"type":"topic","durable":true}'
  curl -s -o /dev/null -u "$RABBIT_AUTH" -X PUT "$RABBIT_MGMT/queues/%2F/$SMOKE_QUEUE" \
    -H 'content-type: application/json' -d '{"durable":true,"auto_delete":false}'
  curl -s -o /dev/null -u "$RABBIT_AUTH" -X POST "$RABBIT_MGMT/bindings/%2F/e/catalog.events/q/$SMOKE_QUEUE" \
    -H 'content-type: application/json' -d '{"routing_key":"catalog.#"}'
  # Left over from a previous run; absolute counts below would be meaningless.
  curl -s -o /dev/null -u "$RABBIT_AUTH" -X DELETE "$RABBIT_MGMT/queues/%2F/$SMOKE_QUEUE/contents"
fi

echo "== GET /api/v1/products =="

BODY=$(curl -s "$BASE/api/v1/products")
check "default: totalCount"        "$(printf '%s' "$BODY" | jsonq 'd.totalCount')" "$SEED_PRODUCT_COUNT"
check "default: page size honoured" "$(printf '%s' "$BODY" | jsonq 'd.items.length')" "25"
check "default: page"              "$(printf '%s' "$BODY" | jsonq 'd.page')" "1"
check "default: pageSize"          "$(printf '%s' "$BODY" | jsonq 'd.pageSize')" "25"
# The seed staggers updated_at, so newest-first is deterministic.
check "default sort = newest first" \
  "$(printf '%s' "$BODY" | jsonq 'String(new Date(d.items[0].updatedAtUtc) >= new Date(d.items[1].updatedAtUtc))')" "true"

BODY=$(curl -s "$BASE/api/v1/products?page=1&pageSize=5&sortBy=code&sortDir=asc")
check "sort by code asc: first"    "$(printf '%s' "$BODY" | jsonq 'd.items[0].code')" "$SEED_FIRST_BY_CODE"
check "sort by code asc: ordered"  "$(printf '%s' "$BODY" | jsonq 'String(d.items.every((x,i,a)=>i===0||a[i-1].code<=x.code))')" "true"

BODY=$(curl -s "$BASE/api/v1/products?page=2&pageSize=5&sortBy=code&sortDir=asc")
check "paging: items on page 2"    "$(printf '%s' "$BODY" | jsonq 'd.items.length')" "5"
check "paging: totalCount stable"  "$(printf '%s' "$BODY" | jsonq 'd.totalCount')" "$SEED_PRODUCT_COUNT"
check "paging: page echoed"        "$(printf '%s' "$BODY" | jsonq 'd.page')" "2"

BODY=$(curl -s "$BASE/api/v1/products?page=999")
check "past-end page: empty items" "$(printf '%s' "$BODY" | jsonq 'd.items.length')" "0"
check "past-end page: total kept"  "$(printf '%s' "$BODY" | jsonq 'd.totalCount')" "$SEED_PRODUCT_COUNT"

# Completeness comes from real gaps in the client export, so the extreme values
# are data-dependent. The invariant worth pinning is the ORDERING.
BODY=$(curl -s "$BASE/api/v1/products?sortBy=quality&sortDir=desc")
check "sort quality desc: ordered" "$(printf '%s' "$BODY" | jsonq 'String(d.items.every((x,i,a)=>i===0||a[i-1].completenessPct>=x.completenessPct))')" "true"
check "sort quality desc: top 100" "$(printf '%s' "$BODY" | jsonq 'd.items[0].completenessPct')" "100"

BODY=$(curl -s "$BASE/api/v1/products?sortBy=quality&sortDir=asc")
check "sort quality asc: ordered"  "$(printf '%s' "$BODY" | jsonq 'String(d.items.every((x,i,a)=>i===0||a[i-1].completenessPct<=x.completenessPct))')" "true"
# A spread proves the quality bar means something; all-100% would make it dead.
check "completeness varies"        "$(printf '%s' "$BODY" | jsonq 'String(d.items[0].completenessPct < 100)')" "true"

BODY=$(curl -s "$BASE/api/v1/products?status=draft")
check "filter status=draft"        "$(printf '%s' "$BODY" | jsonq 'd.totalCount')" "$SEED_DRAFT_COUNT"
BODY=$(curl -s "$BASE/api/v1/products?status=Approval")
check "filter status=Approval"     "$(printf '%s' "$BODY" | jsonq 'd.totalCount')" "$SEED_APPROVAL_COUNT"
check "filter returns only that status" "$(printf '%s' "$BODY" | jsonq 'String(d.items.every(x=>x.workflowState==="Approval"))')" "true"

BODY=$(curl -s "$BASE/api/v1/products?search=Mosaik")
check "search hits German names"   "$(printf '%s' "$BODY" | jsonq 'String(d.totalCount > 0)')" "true"
BODY=$(curl -s "$BASE/api/v1/products?search=AB-ALCINA")
check "search by code prefix"      "$(printf '%s' "$BODY" | jsonq 'String(d.totalCount > 0)')" "true"
BODY=$(curl -s "$BASE/api/v1/products?search=%25")
check "search '%' escaped"         "$(printf '%s' "$BODY" | jsonq 'd.totalCount')" "0"
BODY=$(curl -s "$BASE/api/v1/products?search=zzzznomatch")
check "search: no false positives" "$(printf '%s' "$BODY" | jsonq 'd.totalCount')" "0"

# ---- Full-text search (Sprint 14) -------------------------------------------
# Each of these fails under the ILIKE-only predicate that preceded it, which is
# the only reason to have added FTS. Asserting the capability, not the plan.

# 1. Stemming. 'Fliesen' and 'Fliese' are the same word to a German steward and
#    two unrelated strings to ILIKE.
FTS_PLURAL=$(curl -s "$BASE/api/v1/products?search=Fliesen&pageSize=1" | jsonq 'd.totalCount')
FTS_SINGULAR=$(curl -s "$BASE/api/v1/products?search=Fliese&pageSize=1" | jsonq 'd.totalCount')
check "fts: German stemming"       "$([ "$FTS_PLURAL" -gt 0 ] && [ "$FTS_SINGULAR" -gt 0 ] && echo yes || echo no)" "yes"

# 2. Multi-word across fields, in either order. ILIKE needs one contiguous
#    substring, so both of these returned nothing before.
FTS_AB=$(curl -s "$BASE/api/v1/products?search=Craft+Abdeckplatte&pageSize=1" | jsonq 'd.totalCount')
FTS_BA=$(curl -s "$BASE/api/v1/products?search=Abdeckplatte+Craft&pageSize=1" | jsonq 'd.totalCount')
check "fts: multi-word, any order" "$([ "$FTS_AB" -gt 0 ] && [ "$FTS_AB" = "$FTS_BA" ] && echo yes || echo no)" "yes"

# 3. Both locales reach the same product: the vector aggregates across
#    product_tr rows rather than indexing only the base locale.
check "fts: finds by English name"  "$(curl -s "$BASE/api/v1/products?search=cover+plate&pageSize=1" | jsonq 'String(d.totalCount > 0)')" "true"

# 4. The ILIKE branch survives. Partial words and code fragments are how this
#    catalogue is actually searched, and FTS matches whole lexemes only.
check "fts: partial word still works" "$(curl -s "$BASE/api/v1/products?search=Abdeck&pageSize=1" | jsonq 'String(d.totalCount > 0)')" "true"
check "fts: code fragment still works" "$(curl -s "$BASE/api/v1/products?search=CRAFT-EXTENDED&pageSize=1" | jsonq 'String(d.totalCount > 0)')" "true"

# 5. Malformed input must return nothing, not a 500. websearch_to_tsquery is
#    used precisely because to_tsquery raises on this.
check "fts: operator input -> 200"  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products?search=%26%20%7C%20%21")" "200"
check "fts: unbalanced quote -> 200" "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products?search=%22unclosed")" "200"

# 6. Relevance ranking is opt-in and refuses to be meaningless.
BODY=$(curl -s "$BASE/api/v1/products?search=Fliese&sortBy=relevance&sortDir=desc&pageSize=25")
check "fts: relevance sort -> 200"  "$(printf '%s' "$BODY" | jsonq 'String(d.totalCount > 0)')" "true"
check "fts: relevance is a page"    "$(printf '%s' "$BODY" | jsonq 'String(d.items.length > 0)')" "true"
check "fts: relevance needs a term" "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products?sortBy=relevance")" "400"
check "fts: export inherits rule"   "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products/export.csv?sortBy=relevance")" "400"

# 7. The trigger keeps the vector current, and — the part that would be easy to
#    get wrong — does NOT bump row_version. If it did, every translation save
#    would invalidate a concurrency token the client still held.
RV_BEFORE=$(curl -s "$BASE/api/v1/products/$SEED_SPARE_PRODUCT" | jsonq 'd.rowVersion')
curl -s -o /dev/null -X PUT "$BASE/api/v1/products/$SEED_SPARE_PRODUCT/localization/$SEED_TARGET_LOCALE" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[{\"field\":\"name\",\"value\":\"Searchable Beacon Zzqx\"}],\"translationStatus\":null,\"rowVersion\":$RV_BEFORE}"
check "fts: reindexed on write"    "$(curl -s "$BASE/api/v1/products?search=Zzqx&pageSize=1" | jsonq 'd.totalCount')" "1"
RV_AFTER=$(curl -s "$BASE/api/v1/products/$SEED_SPARE_PRODUCT" | jsonq 'd.rowVersion')
# Exactly one bump — from the write itself, not a second one from the trigger.
check "fts: trigger left rowVersion alone" "$((RV_AFTER - RV_BEFORE))" "1"

BODY=$(curl -s "$BASE/api/v1/products?search=$LEAD")
check "row: both locales present"  "$(printf '%s' "$BODY" | jsonq 'd.items[0].languages.join("|")')" "de-DE|en-GB"
check "row: variant count real"    "$(printf '%s' "$BODY" | jsonq 'd.items[0].variantCount')" "$SEED_LEAD_SKU_COUNT"
check "row: category from series"  "$(printf '%s' "$BODY" | jsonq 'String(d.items[0].category.length > 0)')" "true"

# --- display locale (Sprint 16, ADR-009) -------------------------------------
# This block used to be one assertion: "row: German name shown", against the
# default response. That assertion was correct until the default stopped being
# the base locale, and it is kept below with an explicit locale rather than
# deleted — the German text is still the right answer to the German question.

# Default is English, asserted on the reported locale rather than on the text,
# so this does not need a generated SEED_LEAD_NAME_EN to compare against.
check "locale: defaults to English"   "$(printf '%s' "$BODY" | jsonq 'd.items[0].nameLocale')" "en-GB"

DE_BODY=$(curl -s "$BASE/api/v1/products?search=$LEAD&locale=de-DE")
check "locale: German on request"     "$(printf '%s' "$DE_BODY" | jsonq 'd.items[0].name')" "$SEED_LEAD_NAME_DE"
check "locale: reports what it gave"  "$(printf '%s' "$DE_BODY" | jsonq 'd.items[0].nameLocale')" "de-DE"

# The lead product is translated into both, so switching language must change
# the text. Without this, every assertion above would still pass if the locale
# parameter did nothing at all.
DEFAULT_NAME=$(printf '%s' "$BODY" | jsonq 'd.items[0].name')
GERMAN_NAME=$(printf '%s' "$DE_BODY" | jsonq 'd.items[0].name')
check "locale: English differs from German" \
  "$([ "$DEFAULT_NAME" != "$GERMAN_NAME" ] && echo true || echo false)" "true"

check "locale: row count unaffected" \
  "$(printf '%s' "$DE_BODY" | jsonq 'd.totalCount')" \
  "$(printf '%s' "$BODY" | jsonq 'd.totalCount')"

# Rejected, not ignored. A silent fallback here is the bug this endpoint's
# contract exists to prevent.
check "locale: unknown is rejected" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products?locale=xx-XX")" "400"

check "locale: catalogue lists its languages" \
  "$(curl -s "$BASE/api/v1/locales" | jsonq 'String(d.length >= 2)')" "true"
check "locale: base locale flagged" \
  "$(curl -s "$BASE/api/v1/locales" | jsonq 'd.filter(l => l.isBase).map(l => l.code).join(",")')" "de-DE"
check "row: lifecycle enum form"   "$(printf '%s' "$BODY" | jsonq 'd.items[0].lifecycleState')" "Active"
check "row: workflow enum form"    "$(printf '%s' "$BODY" | jsonq 'String(["Draft","Review","Approval","Published"].includes(d.items[0].workflowState))')" "true"

echo
echo "== Validation errors (RFC 7807) =="

STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products?sortBy=updated_at")
check "unwhitelisted sortBy -> 400" "$STATUS" "400"
CTYPE=$(curl -s -o /dev/null -w '%{content_type}' "$BASE/api/v1/products?sortBy=updated_at")
case "$CTYPE" in
  application/problem+json*) check "error content-type" "problem+json" "problem+json" ;;
  *)                         check "error content-type" "$CTYPE" "problem+json" ;;
esac

STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products?pageSize=101")
check "pageSize>100 -> 400"        "$STATUS" "400"
STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products?page=0")
check "page=0 -> 400"              "$STATUS" "400"
STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products?status=Nonsense")
check "unknown status -> 400"      "$STATUS" "400"
STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products?sortDir=sideways")
check "bad sortDir -> 400"         "$STATUS" "400"

# SQL-injection probe: must be rejected by the whitelist, never executed.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products?sortBy=p.id;DROP%20TABLE%20catalog.product")
check "injection in sortBy -> 400" "$STATUS" "400"
STILL=$(curl -s "$BASE/api/v1/products" | jsonq 'd.totalCount')
check "table intact after probe"   "$STILL" "$SEED_PRODUCT_COUNT"

echo
echo "== Lookups (create wizard reference data) =="

BODY=$(curl -s "$BASE/api/v1/brands")
check "brands: count"              "$(printf '%s' "$BODY" | jsonq 'd.length')" "$SEED_BRAND_COUNT"
check "brands: localized name"     "$(printf '%s' "$BODY" | jsonq 'd.map(b=>b.name).sort().join(",")')" "$SEED_BRAND_NAMES"

BODY=$(curl -s "$BASE/api/v1/taxonomy-nodes?nodeType=CATEGORY")
check "categories: count"          "$(printf '%s' "$BODY" | jsonq 'd.length')" "$SEED_CATEGORY_COUNT"
# Every node must resolve a translated name. Asserting a specific label would
# only pin whichever category happened to be first in the client's export.
check "categories: all localized"  "$(printf '%s' "$BODY" | jsonq 'String(d.every(n=>typeof n.name === "string" && n.name.length > 0 && n.name !== n.code))')" "true"
check "categories: seed code present" "$(printf '%s' "$BODY" | jsonq 'String(d.some(n=>n.code===a[0]))' "$SEED_CATEGORY_CODE")" "true"
BODY=$(curl -s "$BASE/api/v1/taxonomy-nodes?nodeType=FAMILY")
check "families: count"            "$(printf '%s' "$BODY" | jsonq 'd.length')" "$SEED_FAMILY_COUNT"
BODY=$(curl -s "$BASE/api/v1/taxonomy-nodes?nodeType=SERIES")
check "series: count"              "$(printf '%s' "$BODY" | jsonq 'd.length')" "$SEED_SERIES_COUNT"
STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/taxonomy-nodes?nodeType=BOGUS")
check "bad nodeType -> 400"        "$STATUS" "400"

echo
echo "== Create product (v4.2 wizard) end-to-end =="

NEW_CODE="SMOKE-$(date +%s)"
CATEGORY_JSON=$(curl -s "$BASE/api/v1/taxonomy-nodes?nodeType=CATEGORY")
CATEGORY_ID=$(printf '%s' "$CATEGORY_JSON" | jsonq 'd.find(n=>n.code===a[0]).id' "$SEED_CATEGORY_CODE")
# The grid must resolve the same label the picker offered. Reading it from the
# lookup response rather than hard-coding it keeps this a real cross-check
# between two independent read models instead of a restatement of the seed.
CATEGORY_NAME=$(printf '%s' "$CATEGORY_JSON" | jsonq 'd.find(n=>n.code===a[0]).name' "$SEED_CATEGORY_CODE")
CREATE_PAYLOAD=$(cat <<JSON
{"code":"$NEW_CODE","primaryNodeId":$CATEGORY_ID,"baseLocale":"de-DE",
 "name":"Smoke Test Wandfliese","brandId":null,"attributeSetId":null,
 "gtin":null,"sapMaterialNo":"10000099999","ownerDisplayName":"Anna Becker"}
JSON
)

TOTAL_BEFORE_CREATE=$(curl -s "$BASE/api/v1/products" | jsonq 'd.totalCount')

STATUS=$(curl -s -o /tmp/pim-create.json -w '%{http_code}' -X POST "$BASE/api/v1/products" \
  -H 'Content-Type: application/json' -d "$CREATE_PAYLOAD")
check "create -> 201"              "$STATUS" "201"
check "create: code echoed"        "$(jsonq 'd.code' < /tmp/pim-create.json)" "$NEW_CODE"
check "create: name persisted"     "$(jsonq 'd.name' < /tmp/pim-create.json)" "Smoke Test Wandfliese"
check "create: owner persisted"    "$(jsonq 'd.ownerDisplayName' < /tmp/pim-create.json)" "Anna Becker"
check "create: starts as Draft"    "$(jsonq 'd.workflowState' < /tmp/pim-create.json)" "Draft"

# The whole point of Sprint 3: a created product must appear in the grid.
#
# Pinned to de-DE since ADR-009. The grid now DEFAULTS to English, so an
# assertion with no locale was silently testing the default rather than the
# data — and read 'Wall tiles' where the seed says 'Wandfliesen'. Naming the
# locale makes the expectation and the request agree instead of coinciding.
BODY=$(curl -s "$BASE/api/v1/products?search=$NEW_CODE&locale=de-DE")
check "new product is listed"      "$(printf '%s' "$BODY" | jsonq 'd.totalCount')" "1"
check "listed with its name"       "$(printf '%s' "$BODY" | jsonq 'd.items[0].name')" "Smoke Test Wandfliese"
check "listed with its owner"      "$(printf '%s' "$BODY" | jsonq 'd.items[0].owner')" "Anna Becker"
check "listed category resolved"   "$(printf '%s' "$BODY" | jsonq 'd.items[0].category')" "$CATEGORY_NAME"
TOTAL_AFTER_CREATE=$((TOTAL_BEFORE_CREATE + 1))
check "catalogue total grew by 1"  "$(curl -s "$BASE/api/v1/products" | jsonq 'd.totalCount')" "$TOTAL_AFTER_CREATE"

BODY=$(curl -s "$BASE/api/v1/products?sortBy=owner&sortDir=asc")
check "sort by owner works"        "$(printf '%s' "$BODY" | jsonq 'd.items[0].owner')" "Anna Becker"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products" \
  -H 'Content-Type: application/json' -d "$CREATE_PAYLOAD")
check "duplicate code -> 409"      "$STATUS" "409"

# Blank the name AND change the code: with the same code the request would be
# rejected as a duplicate (409) before validation ever looked at the name, and
# the check would pass for the wrong reason. It silently did exactly that once,
# when the product name in CREATE_PAYLOAD was renamed and this sed stopped
# matching.
NO_NAME=$(printf '%s' "$CREATE_PAYLOAD" \
  | sed -e 's/"name":"[^"]*"/"name":""/' -e "s/\"code\":\"$NEW_CODE\"/\"code\":\"$NEW_CODE-B\"/")
check "missing name: payload built" "$(printf '%s' "$NO_NAME" | jsonq 'String(d.name === "" && d.code !== a[0])' "$NEW_CODE")" "true"
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products" \
  -H 'Content-Type: application/json' -d "$NO_NAME")
check "missing name -> 400"        "$STATUS" "400"

echo
echo "== CSV export =="

CSV=$(curl -s "$BASE/api/v1/products/export.csv")
check "csv: header row"            "$(printf '%s' "$CSV" | head -1 | tr -d '\r')" "Code,Name,Category,Lifecycle,Workflow,Completeness %,Languages,Variants,Owner,Updated (UTC)"
# The export is unpaged by design: one row per product in the filtered set.
check "csv: one row per product"   "$(printf '%s' "$CSV" | tail -n +2 | grep -c .)" "$TOTAL_AFTER_CREATE"
check "csv: content type"          "$(curl -s -o /dev/null -w '%{content_type}' "$BASE/api/v1/products/export.csv" | cut -d';' -f1)" "text/csv"

# The product created above is a Draft, so the filtered export is one more than
# the seeded draft count. Cross-checked against the grid's own count so the two
# code paths cannot disagree.
CSV=$(curl -s "$BASE/api/v1/products/export.csv?status=draft")
DRAFT_TOTAL=$(curl -s "$BASE/api/v1/products?status=draft" | jsonq 'd.totalCount')
check "csv: honours filters"       "$(printf '%s' "$CSV" | tail -n +2 | grep -c .)" "$DRAFT_TOTAL"
check "csv: filter agrees w/ grid" "$DRAFT_TOTAL" "$((SEED_DRAFT_COUNT + 1))"

CSV=$(curl -s "$BASE/api/v1/products/export.csv?codes=$SEED_REL_ACCESSORY_1,$SEED_REL_ACCESSORY_2")
check "csv: selection only"        "$(printf '%s' "$CSV" | tail -n +2 | grep -c .)" "2"
check "csv: selection content"     "$(printf '%s' "$CSV" | grep -c "$SEED_REL_ACCESSORY_1")" "1"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products/export.csv?sortBy=bogus")
check "csv: bad sort -> 400"       "$STATUS" "400"

echo
echo "== Channel readiness (derived) =="
# Readiness is DERIVED (ChannelReadiness.cs): Ready(2) at 100% and Published,
# Partial(1) at >=80%, None(0) otherwise. Asserting the rule across a whole
# page beats pinning two products whose completeness moves with the export.
BODY=$(curl -s "$BASE/api/v1/products?pageSize=100&sortBy=quality&sortDir=desc")
check "readiness: 4 channels"      "$(printf '%s' "$BODY" | jsonq 'd.items[0].channelReadiness.length')" "4"
check "readiness: matches rule"    "$(printf '%s' "$BODY" | jsonq '
  String(d.items.every(p => {
    const want = (p.completenessPct === 100 && p.workflowState === "Published") ? 2
               : (p.completenessPct >= 80) ? 1 : 0;
    return p.channelReadiness.length === 4 && p.channelReadiness.every(c => c === want);
  }))')" "true"
# The rule is only meaningful if the page actually contains more than one level.
BODY=$(curl -s "$BASE/api/v1/products?pageSize=100&sortBy=quality&sortDir=asc")
check "readiness: low end is None" "$(printf '%s' "$BODY" | jsonq 'String(d.items.some(p => p.channelReadiness.every(c => c === 0)))')" "true"

echo
echo "== Edit product (General tab) =="

BODY=$(curl -s "$BASE/api/v1/products/$LEAD")
RV=$(printf '%s' "$BODY" | jsonq 'd.rowVersion')
check "get: exposes rowVersion"    "$RV" "1"

EDIT_PAYLOAD="{\"name\":\"Craft Extended — bearbeitet\",\"ownerDisplayName\":\"Lena Vogt\",\"gtin\":\"4012345678901\",\"sapMaterialNo\":\"10000012345\",\"rowVersion\":$RV}"
STATUS=$(curl -s -o /tmp/pim-edit.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD" \
  -H 'Content-Type: application/json' -d "$EDIT_PAYLOAD")
check "edit -> 200"                "$STATUS" "200"
check "edit: name applied"         "$(jsonq 'd.name' < /tmp/pim-edit.json)" "Craft Extended — bearbeitet"
check "edit: owner applied"        "$(jsonq 'd.ownerDisplayName' < /tmp/pim-edit.json)" "Lena Vogt"
check "edit: gtin applied"         "$(jsonq 'd.gtin' < /tmp/pim-edit.json)" "4012345678901"
check "edit: rowVersion bumped"    "$(jsonq 'd.rowVersion' < /tmp/pim-edit.json)" "2"

# The grid must reflect the edit (read model and write model agree).
#
# de-DE explicitly: the General tab edits the base-locale master, so the English
# grid is CORRECT not to show it — the English translation did not change. This
# assertion is about the write reaching the read model, not about fallback, so
# it asks for the locale it just wrote.
BODY=$(curl -s "$BASE/api/v1/products?search=$LEAD&locale=de-DE")
check "grid shows edited name"     "$(printf '%s' "$BODY" | jsonq 'd.items[0].name')" "Craft Extended — bearbeitet"
check "grid shows edited owner"    "$(printf '%s' "$BODY" | jsonq 'd.items[0].owner')" "Lena Vogt"

# Replaying the same rowVersion must be rejected, not silently applied.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD" \
  -H 'Content-Type: application/json' -d "$EDIT_PAYLOAD")
check "stale rowVersion -> 409"    "$STATUS" "409"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD" \
  -H 'Content-Type: application/json' -d '{"name":"","ownerDisplayName":null,"gtin":null,"sapMaterialNo":null,"rowVersion":2}')
check "edit without name -> 400"   "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/NOPE-1" \
  -H 'Content-Type: application/json' -d "$EDIT_PAYLOAD")
check "edit unknown code -> 404"   "$STATUS" "404"

echo
echo "== Editorial workflow =="

# $SEED_DRAFT_PRODUCT is seeded in Draft and is not touched by any other
# section, so this can move it and put it back.
WF="$SEED_DRAFT_PRODUCT"
check "workflow: starts Draft"     "$(curl -s "$BASE/api/v1/products/$WF" | jsonq 'd.workflowState')" "Draft"
RV=$(curl -s "$BASE/api/v1/products/$WF" | jsonq 'd.rowVersion')
STATUS=$(curl -s -o /tmp/pim-wf.json -w '%{http_code}' -X POST "$BASE/api/v1/products/$WF/workflow" \
  -H 'Content-Type: application/json' -d "{\"targetState\":\"Review\",\"rowVersion\":$RV}")
check "Draft -> Review = 200"      "$STATUS" "200"
check "workflow state applied"     "$(jsonq 'd.workflowState' < /tmp/pim-wf.json)" "Review"

RV2=$(jsonq 'd.rowVersion' < /tmp/pim-wf.json)
# Review may go to Approval or back to Draft, never straight to Published.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$WF/workflow" \
  -H 'Content-Type: application/json' -d "{\"targetState\":\"Published\",\"rowVersion\":$RV2}")
check "Review -> Published = 409"  "$STATUS" "409"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$WF/workflow" \
  -H 'Content-Type: application/json' -d "{\"targetState\":\"Nonsense\",\"rowVersion\":$RV2}")
check "unknown state -> 400"       "$STATUS" "400"

# Status filter must see the new workflow state.
check "grid status filter updated" "$(curl -s "$BASE/api/v1/products?status=Review&search=$WF" | jsonq 'd.totalCount')" "1"

# Put it back, so a repeat run starts from the same state.
STATUS=$(curl -s -o /tmp/pim-wf.json -w '%{http_code}' -X POST "$BASE/api/v1/products/$WF/workflow" \
  -H 'Content-Type: application/json' -d "{\"targetState\":\"Draft\",\"rowVersion\":$RV2}")
check "Review -> Draft = 200"      "$STATUS" "200"
check "workflow restored"          "$(jsonq 'd.workflowState' < /tmp/pim-wf.json)" "Draft"

echo
echo "== Attributes tab =="

# The attribute vocabulary is generated from the client export, so the codes
# and the seeded values come from seed-facts.env rather than being written out
# here. $SPARE is used for the write tests: $LEAD's values are what the
# classification, compliance and completeness assertions depend on.
BODY=$(curl -s "$BASE/api/v1/products/$LEAD/attributes?locale=de-DE")
check "attrs: product echoed"      "$(printf '%s' "$BODY" | jsonq 'd.productCode')" "$LEAD"
check "attrs: required count"      "$(printf '%s' "$BODY" | jsonq 'd.requiredCount')" "$SEED_ATTR_REQUIRED_COUNT"
check "attrs: all attrs offered"   "$(printf '%s' "$BODY" | jsonq 'd.groups.reduce((n,g)=>n+g.attributes.length,0)')" "$SEED_ATTR_TOTAL_COUNT"
check "attrs: grouped"             "$(printf '%s' "$BODY" | jsonq 'd.groups.length >= 4 ? "yes" : "no"')" "yes"
# Completeness is DERIVED from the two counts beside it; pinning the arithmetic
# catches a divergence that a hard-coded percentage would hide.
check "attrs: completeness derived" "$(printf '%s' "$BODY" | jsonq 'String(d.completenessPct === Math.round(d.requiredFilledCount / d.requiredCount * 100))')" "true"
check "attrs: filled <= required"  "$(printf '%s' "$BODY" | jsonq 'String(d.requiredFilledCount <= d.requiredCount)')" "true"
check "attrs: list options"        "$(printf '%s' "$BODY" | jsonq 'd.groups.flatMap(g=>g.attributes).find(x=>x.code===a[0]).options.map(o=>o.code).join(",")' "$SEED_ATTR_LIST")" "$SEED_ATTR_LIST_OPTIONS"
check "attrs: seeded list value"   "$(printf '%s' "$BODY" | jsonq 'd.groups.flatMap(g=>g.attributes).find(x=>x.code===a[0]).value' "$SEED_ATTR_LIST")" "$SEED_ATTR_LIST_VALUE"
check "attrs: unit exposed"        "$(printf '%s' "$BODY" | jsonq 'd.groups.flatMap(g=>g.attributes).find(x=>x.code===a[0]).unitSymbol' "$SEED_ATTR_MEASURE")" "$SEED_ATTR_MEASURE_UNIT"
check "attrs: measure is numeric"  "$(printf '%s' "$BODY" | jsonq 'String(!Number.isNaN(Number(d.groups.flatMap(g=>g.attributes).find(x=>x.code===a[0]).value)))' "$SEED_ATTR_MEASURE")" "true"
check "attrs: option labelled"     "$(printf '%s' "$BODY" | jsonq 'String(d.groups.flatMap(g=>g.attributes).find(x=>x.code===a[0]).options.every(o=>o.label && o.label.length>0))' "$SEED_ATTR_LIST")" "true"
check "attrs: date attr present"   "$(printf '%s' "$BODY" | jsonq 'd.groups.flatMap(g=>g.attributes).find(x=>x.code===a[0]).dataType' "$SEED_ATTR_DATE")" "date"
# The tab showed 'material_type' where the seed had 'Materialart': attribute_tr
# was seeded from Sprint 6 but never joined. Falling back to the code is a
# silent failure, so assert no attribute is displaying one.
check "attrs: labels translated"   "$(printf '%s' "$BODY" | jsonq 'String(d.groups.flatMap(g=>g.attributes).every(x=>typeof x.label === "string" && x.label.length > 0 && x.label !== x.code))')" "true"
check "attrs: label in requested locale (de)" "$(printf '%s' "$BODY" | jsonq 'd.groups.flatMap(g=>g.attributes).find(x=>x.code===a[0]).label' "$SEED_ATTR_LIST")" "Materialart"
# The other direction, which is the half that was never asserted: asking for
# English must actually GIVE English. Without this the tab could be pinned to
# one language and still pass — which is exactly what it was doing until the
# endpoint learned to bind `locale` (Sprint 17).
BODY_EN=$(curl -s "$BASE/api/v1/products/$LEAD/attributes?locale=en-GB")
check "attrs: label in requested locale (en)" "$(printf '%s' "$BODY_EN" | jsonq 'd.groups.flatMap(g=>g.attributes).find(x=>x.code===a[0]).label' "$SEED_ATTR_LIST")" "Material type"
check "attrs: unknown locale rejected" "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products/$LEAD/attributes?locale=zz-ZZ")" "400"
check "attrs: groups translated"   "$(printf '%s' "$BODY" | jsonq 'String(d.groups.every(g=>g.name.length > 0 && g.name !== g.code))')" "true"

# ---- writes, on $SPARE
BODY=$(curl -s "$BASE/api/v1/products/$SPARE/attributes")
RV=$(printf '%s' "$BODY" | jsonq 'd.rowVersion')
BEFORE_PCT=$(printf '%s' "$BODY" | jsonq 'd.completenessPct')

# One value per supported data type: text, measure (number), decimal, boolean,
# date and list. If a type loses its write path this is where it shows.
SAVE="{\"values\":[
  {\"attributeCode\":\"$SEED_ATTR_OPTIONAL_FILLED\",\"value\":\"Smoke text\"},
  {\"attributeCode\":\"$SEED_ATTR_MEASURE\",\"value\":\"12\"},
  {\"attributeCode\":\"$SEED_ATTR_DECIMAL\",\"value\":\"87.5\"},
  {\"attributeCode\":\"$SEED_ATTR_BOOLEAN\",\"value\":\"true\"},
  {\"attributeCode\":\"$SEED_ATTR_DATE\",\"value\":\"2026-09-01\"},
  {\"attributeCode\":\"$SEED_ATTR_LIST\",\"value\":\"$SEED_ATTR_LIST_OTHER\"}
 ],\"rowVersion\":$RV}"

STATUS=$(curl -s -o /tmp/pim-attrs.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$SPARE/attributes" \
  -H 'Content-Type: application/json' -d "$SAVE")
check "attrs: save -> 200"         "$STATUS" "200"
check "attrs: text saved"          "$(jsonq 'd.groups.flatMap(g=>g.attributes).find(x=>x.code===a[0]).value' "$SEED_ATTR_OPTIONAL_FILLED" < /tmp/pim-attrs.json)" "Smoke text"
check "attrs: measure saved"       "$(jsonq 'd.groups.flatMap(g=>g.attributes).find(x=>x.code===a[0]).value' "$SEED_ATTR_MEASURE" < /tmp/pim-attrs.json)" "12"
check "attrs: decimal saved"       "$(jsonq 'd.groups.flatMap(g=>g.attributes).find(x=>x.code===a[0]).value' "$SEED_ATTR_DECIMAL" < /tmp/pim-attrs.json)" "87.5"
check "attrs: boolean saved"       "$(jsonq 'd.groups.flatMap(g=>g.attributes).find(x=>x.code===a[0]).value' "$SEED_ATTR_BOOLEAN" < /tmp/pim-attrs.json)" "true"
check "attrs: date saved"          "$(jsonq 'd.groups.flatMap(g=>g.attributes).find(x=>x.code===a[0]).value' "$SEED_ATTR_DATE" < /tmp/pim-attrs.json)" "2026-09-01"
check "attrs: option changed"      "$(jsonq 'd.groups.flatMap(g=>g.attributes).find(x=>x.code===a[0]).value' "$SEED_ATTR_LIST" < /tmp/pim-attrs.json)" "$SEED_ATTR_LIST_OTHER"
check "attrs: rowVersion bumped"   "$(jsonq 'd.rowVersion' < /tmp/pim-attrs.json)" "$((RV + 1))"
# Completeness must stay consistent with its own inputs after a write.
check "attrs: completeness derived after write" "$(jsonq 'String(d.completenessPct === Math.round(d.requiredFilledCount / d.requiredCount * 100))' < /tmp/pim-attrs.json)" "true"
check "attrs: completeness not lower" "$(jsonq 'String(d.completenessPct >= Number(a[0]))' "$BEFORE_PCT" < /tmp/pim-attrs.json)" "true"

RV2=$(jsonq 'd.rowVersion' < /tmp/pim-attrs.json)

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$SPARE/attributes" \
  -H 'Content-Type: application/json' -d "{\"values\":[{\"attributeCode\":\"$SEED_ATTR_MEASURE\",\"value\":\"thick\"}],\"rowVersion\":$RV2}")
check "attrs: bad number -> 400"   "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$SPARE/attributes" \
  -H 'Content-Type: application/json' -d "{\"values\":[{\"attributeCode\":\"$SEED_ATTR_DATE\",\"value\":\"not-a-date\"}],\"rowVersion\":$RV2}")
check "attrs: bad date -> 400"     "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$SPARE/attributes" \
  -H 'Content-Type: application/json' -d "{\"values\":[{\"attributeCode\":\"$SEED_ATTR_LIST\",\"value\":\"not_an_option\"}],\"rowVersion\":$RV2}")
check "attrs: bad option -> 400"   "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$SPARE/attributes" \
  -H 'Content-Type: application/json' -d "{\"values\":[{\"attributeCode\":\"nope\",\"value\":\"x\"}],\"rowVersion\":$RV2}")
check "attrs: unknown attr -> 400" "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$SPARE/attributes" \
  -H 'Content-Type: application/json' -d "{\"values\":[{\"attributeCode\":\"$SEED_ATTR_OPTIONAL_FILLED\",\"value\":\"x\"}],\"rowVersion\":1}")
check "attrs: stale version -> 409" "$STATUS" "409"

# An invalid value in a batch must not persist the valid ones alongside it.
curl -s -o /dev/null -X PUT "$BASE/api/v1/products/$SPARE/attributes" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[{\"attributeCode\":\"$SEED_ATTR_OPTIONAL_FILLED\",\"value\":\"Rolled back\"},{\"attributeCode\":\"$SEED_ATTR_MEASURE\",\"value\":\"oops\"}],\"rowVersion\":$RV2}"
check "attrs: batch is atomic"     "$(curl -s "$BASE/api/v1/products/$SPARE/attributes" | jsonq 'd.groups.flatMap(g=>g.attributes).find(x=>x.code===a[0]).value' "$SEED_ATTR_OPTIONAL_FILLED")" "Smoke text"

# Clearing a required attribute is refused; clearing an optional one works.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$SPARE/attributes" \
  -H 'Content-Type: application/json' -d "{\"values\":[{\"attributeCode\":\"$SEED_ATTR_REQUIRED_FILLED\",\"value\":null}],\"rowVersion\":$RV2}")
check "attrs: clear required -> 400" "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$SPARE/attributes" \
  -H 'Content-Type: application/json' -d "{\"values\":[{\"attributeCode\":\"$SEED_ATTR_DATE\",\"value\":null}],\"rowVersion\":$RV2}")
check "attrs: clear optional -> 200" "$STATUS" "200"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products/NOPE-1/attributes")
check "attrs: unknown code -> 404" "$STATUS" "404"

echo
echo "== Variants (SKUs) =="

BODY=$(curl -s "$BASE/api/v1/products/$SPARE/variants")
check "variants: product echoed"   "$(printf '%s' "$BODY" | jsonq 'd.productCode')" "$SPARE"
check "variants: seeded count"     "$(printf '%s' "$BODY" | jsonq 'd.skus.length')" "$SEED_SPARE_SKU_COUNT"
check "variants: status default"   "$(printf '%s' "$BODY" | jsonq 'd.skus[0].status')" "Active"

RV=$(printf '%s' "$BODY" | jsonq 'd.rowVersion')

STATUS=$(curl -s -o /tmp/pim-var.json -w '%{http_code}' -X POST "$BASE/api/v1/products/$SPARE/variants" \
  -H 'Content-Type: application/json' \
  -d "{\"code\":\"$SPARE-XL\",\"gtin\":\"4012345678901\",\"erpMaterialNo\":\"ERP-XL\",\"rowVersion\":$RV}")
check "variants: add -> 200"       "$STATUS" "200"
check "variants: count grew"       "$(jsonq 'd.skus.length' < /tmp/pim-var.json)" "$((SEED_SPARE_SKU_COUNT + 1))"
check "variants: gtin stored"      "$(jsonq 'd.skus.find(s=>s.code===a[0]).gtin' "$SPARE-XL" < /tmp/pim-var.json)" "4012345678901"
check "variants: rowVersion bumped" "$(jsonq 'd.rowVersion' < /tmp/pim-var.json)" "$((RV + 1))"

# The grid's variant count must reflect the new SKU (read and write agree).
check "grid variant count updated" "$(curl -s "$BASE/api/v1/products?search=$SPARE" | jsonq 'd.items[0].variantCount')" "$((SEED_SPARE_SKU_COUNT + 1))"

RV2=$(jsonq 'd.rowVersion' < /tmp/pim-var.json)

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$SPARE/variants" \
  -H 'Content-Type: application/json' -d "{\"code\":\"$SPARE-XL\",\"gtin\":null,\"erpMaterialNo\":null,\"rowVersion\":$RV2}")
check "variants: duplicate -> 409" "$STATUS" "409"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$SPARE/variants" \
  -H 'Content-Type: application/json' -d "{\"code\":\"bad code\",\"gtin\":null,\"erpMaterialNo\":null,\"rowVersion\":$RV2}")
check "variants: bad code -> 400"  "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$SPARE/variants" \
  -H 'Content-Type: application/json' -d "{\"code\":\"SMOKE-SKU-1\",\"gtin\":\"12\",\"erpMaterialNo\":null,\"rowVersion\":$RV2}")
check "variants: bad gtin -> 400"  "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$SPARE/variants" \
  -H 'Content-Type: application/json' -d "{\"code\":\"SMOKE-SKU-2\",\"gtin\":null,\"erpMaterialNo\":null,\"rowVersion\":1}")
check "variants: stale rv -> 409"  "$STATUS" "409"

STATUS=$(curl -s -o /tmp/pim-var2.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$SPARE/variants/$SPARE-XL" \
  -H 'Content-Type: application/json' \
  -d "{\"gtin\":null,\"erpMaterialNo\":\"ERP-EOL\",\"status\":\"EndOfLife\",\"rowVersion\":$RV2}")
check "variants: update -> 200"    "$STATUS" "200"
check "variants: status changed"   "$(jsonq 'd.skus.find(s=>s.code===a[0]).status' "$SPARE-XL" < /tmp/pim-var2.json)" "EndOfLife"
check "variants: gtin cleared"     "$(jsonq 'String(d.skus.find(s=>s.code===a[0]).gtin)' "$SPARE-XL" < /tmp/pim-var2.json)" "null"

RV3=$(jsonq 'd.rowVersion' < /tmp/pim-var2.json)

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$SPARE/variants/$SPARE-XL" \
  -H 'Content-Type: application/json' -d "{\"gtin\":null,\"erpMaterialNo\":null,\"status\":\"Nonsense\",\"rowVersion\":$RV3}")
check "variants: bad status -> 400" "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$SPARE/variants/NOPE-1" \
  -H 'Content-Type: application/json' -d "{\"gtin\":null,\"erpMaterialNo\":null,\"status\":\"Active\",\"rowVersion\":$RV3}")
check "variants: unknown sku -> 404" "$STATUS" "404"

STATUS=$(curl -s -o /tmp/pim-var3.json -w '%{http_code}' -X DELETE \
  "$BASE/api/v1/products/$SPARE/variants/$SPARE-XL?rowVersion=$RV3")
check "variants: delete -> 200"    "$STATUS" "200"
check "variants: back to seeded"   "$(jsonq 'd.skus.length' < /tmp/pim-var3.json)" "$SEED_SPARE_SKU_COUNT"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products/NOPE-1/variants")
check "variants: unknown code -> 404" "$STATUS" "404"

echo
echo "== Classifications (Sprint 8) =="
# $LEAD is seeded into all 5 schemes; ETIM declares 14 features with 12 filled.
BODY=$(curl -s "$BASE/api/v1/products/$LEAD/classifications")
check "class: product echoed"      "$(printf '%s' "$BODY" | jsonq 'd.productCode')" "$LEAD"
check "class: all schemes listed"  "$(printf '%s' "$BODY" | jsonq 'd.schemes.length')" "$SEED_SCHEME_COUNT"
check "class: ETIM class assigned" "$(printf '%s' "$BODY" | jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").classCode')" "$SEED_ETIM_CLASS"
check "class: feature count"       "$(printf '%s' "$BODY" | jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").featureCount')" "$SEED_ETIM_FEATURE_COUNT"
check "class: features filled"     "$(printf '%s' "$BODY" | jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").featureFilled')" "$SEED_ETIM_FEATURE_FILLED"
# Trailing-zero regression: a whole number must survive the read unchanged
# (numeric(12,4) in the DB would otherwise read back as "300.0000").
check "class: whole number intact" "$(printf '%s' "$BODY" | jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").features.find(f=>f.code===a[0]).value' "$SEED_ETIM_NUMERIC_FEATURE")" "$SEED_ETIM_NUMERIC_VALUE"
check "class: boolean canonical"   "$(printf '%s' "$BODY" | jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").features.find(f=>f.code===a[0]).value' "$SEED_ETIM_LOGICAL_FEATURE")" "$SEED_ETIM_LOGICAL_VALUE"
check "class: unfilled is null"    "$(printf '%s' "$BODY" | jsonq 'String(d.schemes.find(s=>s.schemeCode=="ETIM").features.find(f=>f.code===a[0]).value)' "$SEED_ETIM_UNFILLED_FEATURE")" "null"
# A scheme whose class declares no features reports 0/0, not an error.
check "class: UNSPSC has 0 features" "$(printf '%s' "$BODY" | jsonq 'd.schemes.find(s=>s.schemeCode=="UNSPSC").featureCount')" "0"
check "class: available classes"   "$(printf '%s' "$BODY" | jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").availableClasses.length')" "3"

RV=$(printf '%s' "$BODY" | jsonq 'd.rowVersion')

# Required feature EF000234 is missing, so the mapping must read 'partial'.
check "class: status partial"      "$(printf '%s' "$BODY" | jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").mappedStatus')" "partial"

# Filling the last required feature must flip the status to 'mapped' — the
# status is derived, so this proves it is not just echoing what was stored.
STATUS=$(curl -s -o /tmp/pim-cls.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/classifications/ETIM/features" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[{\"featureCode\":\"$SEED_ETIM_UNFILLED_FEATURE\",\"value\":\"true\"}],\"rowVersion\":$RV}")
check "class: fill feature -> 200" "$STATUS" "200"
check "class: filled count grew"   "$(jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").featureFilled' < /tmp/pim-cls.json)" "$((SEED_ETIM_FEATURE_FILLED + 1))"
check "class: status now mapped"   "$(jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").mappedStatus' < /tmp/pim-cls.json)" "mapped"
RV=$(jsonq 'd.rowVersion' < /tmp/pim-cls.json)

# Clearing a value removes the row so completeness stops counting it.
STATUS=$(curl -s -o /tmp/pim-cls.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/classifications/ETIM/features" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[{\"featureCode\":\"$SEED_ETIM_UNFILLED_FEATURE\",\"value\":null}],\"rowVersion\":$RV}")
check "class: clear -> 200"        "$STATUS" "200"
check "class: filled count fell"   "$(jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").featureFilled' < /tmp/pim-cls.json)" "$SEED_ETIM_FEATURE_FILLED"
RV=$(jsonq 'd.rowVersion' < /tmp/pim-cls.json)

# Typed validation must reject a non-numeric into a numeric feature.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/classifications/ETIM/features" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[{\"featureCode\":\"$SEED_ETIM_NUMERIC_FEATURE\",\"value\":\"wide\"}],\"rowVersion\":$RV}")
check "class: bad numeric -> 400"  "$STATUS" "400"

# A feature the assigned class does not declare must be rejected, not ignored.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/classifications/ETIM/features" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[{\"featureCode\":\"EF999999\",\"value\":\"x\"}],\"rowVersion\":$RV}")
check "class: unknown feature 400" "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/classifications/ETIM/features" \
  -H 'Content-Type: application/json' \
  -d '{"values":[{"featureCode":"EF000007","value":"Steel"}],"rowVersion":1}')
check "class: stale version -> 409" "$STATUS" "409"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/classifications/NOSUCH" \
  -H 'Content-Type: application/json' \
  -d "{\"classCode\":null,\"rowVersion\":$RV}")
check "class: unknown scheme 404"  "$STATUS" "404"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/classifications/ETIM" \
  -H 'Content-Type: application/json' \
  -d "{\"classCode\":\"EC999999\",\"rowVersion\":$RV}")
check "class: unknown class 404"   "$STATUS" "404"

# The destructive mapping tests run against $SPARE, which the seed leaves
# unmapped, so this script stays idempotent: $LEAD is left exactly as
# seeded and a second run asserts the same numbers.
BODY=$(curl -s "$BASE/api/v1/products/$SPARE/classifications")
check "class: unmapped by default" "$(printf '%s' "$BODY" | jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").mappedStatus')" "unmapped"
check "class: unmapped has 0 feat" "$(printf '%s' "$BODY" | jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").featureCount')" "0"
RV2=$(printf '%s' "$BODY" | jsonq 'd.rowVersion')

# Feature values cannot be set before a class is assigned.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$SPARE/classifications/ETIM/features" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[{\"featureCode\":\"EF000007\",\"value\":\"Steel\"}],\"rowVersion\":$RV2}")
check "class: no class -> 400"     "$STATUS" "400"

STATUS=$(curl -s -o /tmp/pim-cls.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$SPARE/classifications/ETIM" \
  -H 'Content-Type: application/json' \
  -d "{\"classCode\":\"$SEED_ETIM_CLASS\",\"rowVersion\":$RV2}")
check "class: assign -> 200"       "$STATUS" "200"
check "class: features appear"     "$(jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").featureCount' < /tmp/pim-cls.json)" "$SEED_ETIM_FEATURE_COUNT"
check "class: none filled yet"     "$(jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").featureFilled' < /tmp/pim-cls.json)" "0"
RV2=$(jsonq 'd.rowVersion' < /tmp/pim-cls.json)

# EF000045 (weight per m2) is the other numeric feature on this class, so both
# halves of the number formatting rule are covered against numeric columns.
STATUS=$(curl -s -o /tmp/pim-cls.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$SPARE/classifications/ETIM/features" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[{\"featureCode\":\"$SEED_ETIM_NUMERIC_FEATURE\",\"value\":\"1200\"},{\"featureCode\":\"EF000045\",\"value\":\"20.50\"}],\"rowVersion\":$RV2}")
check "class: set values -> 200"   "$STATUS" "200"
check "class: whole number kept"   "$(jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").features.find(f=>f.code===a[0]).value' "$SEED_ETIM_NUMERIC_FEATURE" < /tmp/pim-cls.json)" "1200"
# Trailing zeros are stripped from the FRACTION only.
check "class: fraction trimmed"    "$(jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").features.find(f=>f.code=="EF000045").value' < /tmp/pim-cls.json)" "20.5"
RV2=$(jsonq 'd.rowVersion' < /tmp/pim-cls.json)

# Reassigning must discard values the new class does not declare.
STATUS=$(curl -s -o /tmp/pim-cls.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$SPARE/classifications/ETIM" \
  -H 'Content-Type: application/json' \
  -d "{\"classCode\":\"EC010101\",\"rowVersion\":$RV2}")
check "class: reassign -> 200"     "$STATUS" "200"
check "class: orphans discarded"   "$(jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").featureFilled' < /tmp/pim-cls.json)" "0"
RV2=$(jsonq 'd.rowVersion' < /tmp/pim-cls.json)

# Restore the unmapped state so a repeat run sees the same starting point.
STATUS=$(curl -s -o /tmp/pim-cls.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$SPARE/classifications/ETIM" \
  -H 'Content-Type: application/json' \
  -d "{\"classCode\":null,\"rowVersion\":$RV2}")
check "class: clear -> 200"        "$STATUS" "200"
check "class: unmapped again"      "$(jsonq 'd.schemes.find(s=>s.schemeCode=="ETIM").mappedStatus' < /tmp/pim-cls.json)" "unmapped"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products/NOPE-1/classifications")
check "class: unknown code -> 404" "$STATUS" "404"

echo
echo "== Compliance (Sprint 8) =="
BODY=$(curl -s "$BASE/api/v1/products/$LEAD/compliance")
check "comp: product echoed"       "$(printf '%s' "$BODY" | jsonq 'd.productCode')" "$LEAD"
check "comp: seeded documents"     "$(printf '%s' "$BODY" | jsonq 'd.documents.length')" "$SEED_COMPLIANCE_DOC_COUNT"
check "comp: DoP has no expiry"    "$(printf '%s' "$BODY" | jsonq 'd.documents.find(x=>x.docType=="DOP").expiryState')" "NoExpiry"
check "comp: sustainability rows"  "$(printf '%s' "$BODY" | jsonq 'd.indicators.length')" "$SEED_INDICATOR_COUNT"
check "comp: material rows"        "$(printf '%s' "$BODY" | jsonq 'd.materials.length')" "$SEED_MATERIAL_ROW_COUNT"
check "comp: material pct sums"    "$(printf '%s' "$BODY" | jsonq 'String(d.materials.reduce((n,m)=>n+Number(m.pct),0))')" "100"
check "comp: substance rows"       "$(printf '%s' "$BODY" | jsonq 'd.substances.length')" "$SEED_SUBSTANCE_COUNT"
check "comp: green building rows"  "$(printf '%s' "$BODY" | jsonq 'd.greenBuilding.length')" "$SEED_GREEN_BUILDING_COUNT"
# Expiry must be one of the derived states — never a stored string.
check "comp: expiry is derived"    "$(printf '%s' "$BODY" | jsonq 'd.documents.every(x=>["NoExpiry","Valid","Expiring","Expired"].includes(x.expiryState)) ? "yes" : "no"')" "yes"
check "comp: banner matches rows"  "$(printf '%s' "$BODY" | jsonq 'String(d.expiringCount === d.documents.filter(x=>x.status==="valid" && x.expiryState==="Expiring").length)')" "true"
# The seed dates UKCA relative to now (+8 months) precisely so the warning path
# is exercised. A hard-coded date would silently stop covering this.
check "comp: one expiring cert"    "$(printf '%s' "$BODY" | jsonq 'd.expiringCount')" "1"
check "comp: UKCA is the expiring one" "$(printf '%s' "$BODY" | jsonq 'd.documents.find(x=>x.docType=="UKCA").expiryState')" "Expiring"
check "comp: nothing expired yet"  "$(printf '%s' "$BODY" | jsonq 'd.expiredCount')" "0"

RV=$(printf '%s' "$BODY" | jsonq 'd.rowVersion')

STATUS=$(curl -s -o /tmp/pim-cmp.json -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/compliance" \
  -H 'Content-Type: application/json' \
  -d "{\"docType\":\"CE\",\"reference\":\"SMOKE-CE-1\",\"standard\":\"EN 9999\",\"authority\":\"Smoke Body\",\"validFrom\":\"2026-01-01\",\"validTo\":\"2035-01-01\",\"rowVersion\":$RV}")
check "comp: add -> 200"           "$STATUS" "200"
check "comp: count grew"           "$(jsonq 'd.documents.length' < /tmp/pim-cmp.json)" "$((SEED_COMPLIANCE_DOC_COUNT + 1))"
# valid_to is a decade out, so it must classify as Valid, not Expiring.
check "comp: far future = Valid"   "$(jsonq 'd.documents.find(x=>x.reference=="SMOKE-CE-1").expiryState' < /tmp/pim-cmp.json)" "Valid"
DOC_ID=$(jsonq 'd.documents.find(x=>x.reference=="SMOKE-CE-1").id' < /tmp/pim-cmp.json)
RV=$(jsonq 'd.rowVersion' < /tmp/pim-cmp.json)

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/compliance" \
  -H 'Content-Type: application/json' \
  -d "{\"docType\":\"CE\",\"reference\":\"SMOKE-CE-1\",\"standard\":null,\"authority\":null,\"validFrom\":null,\"validTo\":null,\"rowVersion\":$RV}")
check "comp: duplicate ref -> 409" "$STATUS" "409"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/compliance" \
  -H 'Content-Type: application/json' \
  -d "{\"docType\":\"ISO9001\",\"reference\":\"X-1\",\"standard\":null,\"authority\":null,\"validFrom\":null,\"validTo\":null,\"rowVersion\":$RV}")
check "comp: bad docType -> 400"   "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/compliance" \
  -H 'Content-Type: application/json' \
  -d "{\"docType\":\"CE\",\"reference\":\"X-2\",\"standard\":null,\"authority\":null,\"validFrom\":\"2026-06-01\",\"validTo\":\"2026-01-01\",\"rowVersion\":$RV}")
check "comp: backwards dates 400"  "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/compliance" \
  -H 'Content-Type: application/json' \
  -d '{"docType":"CE","reference":"X-3","standard":null,"authority":null,"validFrom":null,"validTo":null,"rowVersion":1}')
check "comp: stale version -> 409" "$STATUS" "409"

# An expired certificate must be reported as Expired, and a withdrawn one must
# drop out of the counts even though its date has lapsed.
STATUS=$(curl -s -o /tmp/pim-cmp.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/compliance/$DOC_ID" \
  -H 'Content-Type: application/json' \
  -d "{\"docType\":\"CE\",\"reference\":\"SMOKE-CE-1\",\"standard\":\"EN 9999\",\"authority\":\"Smoke Body\",\"validFrom\":\"2015-01-01\",\"validTo\":\"2016-01-01\",\"status\":\"valid\",\"rowVersion\":$RV}")
check "comp: update -> 200"        "$STATUS" "200"
check "comp: lapsed = Expired"     "$(jsonq 'd.documents.find(x=>x.reference=="SMOKE-CE-1").expiryState' < /tmp/pim-cmp.json)" "Expired"
check "comp: expiredCount is 1"    "$(jsonq 'd.expiredCount' < /tmp/pim-cmp.json)" "1"
RV=$(jsonq 'd.rowVersion' < /tmp/pim-cmp.json)

STATUS=$(curl -s -o /tmp/pim-cmp.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/compliance/$DOC_ID" \
  -H 'Content-Type: application/json' \
  -d "{\"docType\":\"CE\",\"reference\":\"SMOKE-CE-1\",\"standard\":\"EN 9999\",\"authority\":\"Smoke Body\",\"validFrom\":\"2015-01-01\",\"validTo\":\"2016-01-01\",\"status\":\"withdrawn\",\"rowVersion\":$RV}")
check "comp: withdraw -> 200"      "$STATUS" "200"
check "comp: withdrawn not counted" "$(jsonq 'd.expiredCount' < /tmp/pim-cmp.json)" "0"
check "comp: still shown in list"  "$(jsonq 'd.documents.find(x=>x.reference=="SMOKE-CE-1").status' < /tmp/pim-cmp.json)" "withdrawn"
RV=$(jsonq 'd.rowVersion' < /tmp/pim-cmp.json)

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/compliance/$DOC_ID" \
  -H 'Content-Type: application/json' \
  -d "{\"docType\":\"CE\",\"reference\":\"SMOKE-CE-1\",\"standard\":null,\"authority\":null,\"validFrom\":null,\"validTo\":null,\"status\":\"expired\",\"rowVersion\":$RV}")
check "comp: 'expired' status 400" "$STATUS" "400"

# An id belonging to another product must 404 rather than edit that product.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X DELETE "$BASE/api/v1/products/$SPARE/compliance/$DOC_ID?rowVersion=1")
check "comp: cross-product -> 404 or 409" \
  "$([ "$STATUS" = "404" ] || [ "$STATUS" = "409" ] && echo yes || echo no)" "yes"

STATUS=$(curl -s -o /tmp/pim-cmp.json -w '%{http_code}' -X DELETE "$BASE/api/v1/products/$LEAD/compliance/$DOC_ID?rowVersion=$RV")
check "comp: delete -> 200"        "$STATUS" "200"
check "comp: back to seeded count" "$(jsonq 'd.documents.length' < /tmp/pim-cmp.json)" "$SEED_COMPLIANCE_DOC_COUNT"
RV=$(jsonq 'd.rowVersion' < /tmp/pim-cmp.json)

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X DELETE "$BASE/api/v1/products/$LEAD/compliance/999999?rowVersion=$RV")
check "comp: unknown doc -> 404"   "$STATUS" "404"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products/NOPE-1/compliance")
check "comp: unknown code -> 404"  "$STATUS" "404"

echo
echo "== Packaging (Sprint 9) =="
BODY=$(curl -s "$BASE/api/v1/products/$LEAD/packaging")
check "pkg: product echoed"        "$(printf '%s' "$BODY" | jsonq 'd.productCode')" "$LEAD"
# Every SKU is returned, including those with no packaging, so gaps are visible.
check "pkg: all skus listed"       "$(printf '%s' "$BODY" | jsonq 'd.skus.length')" "$SEED_LEAD_SKU_COUNT"
check "pkg: seeded levels"         "$(printf '%s' "$BODY" | jsonq 'd.skus[0].units.length')" "3"
# Ordered innermost first regardless of insertion order.
check "pkg: innermost first"       "$(printf '%s' "$BODY" | jsonq 'd.skus[0].units.map(u=>u.level).join(">")')" "EACH>CARTON>PALLET"
check "pkg: parent resolved"       "$(printf '%s' "$BODY" | jsonq 'd.skus[0].units.find(u=>u.level=="PALLET").parentLevel')" "CARTON"
check "pkg: base unit flagged"     "$(printf '%s' "$BODY" | jsonq 'd.skus[0].units.filter(u=>u.isBaseUnit).length')" "1"
check "pkg: empty sku has none"    "$(printf '%s' "$BODY" | jsonq 'd.skus[1].units.length')" "0"

SKU2=$(printf '%s' "$BODY" | jsonq 'd.skus[1].skuCode')
RV=$(printf '%s' "$BODY" | jsonq 'd.rowVersion')

STATUS=$(curl -s -o /tmp/pim-pkg.json -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/packaging" \
  -H 'Content-Type: application/json' \
  -d "{\"skuCode\":\"$SKU2\",\"level\":\"EACH\",\"parentLevel\":null,\"containsQty\":1,\"gtin\":\"9990000000017\",\"sscc\":null,\"grossWeight\":10,\"netWeight\":9,\"lengthMm\":100,\"widthMm\":200,\"heightMm\":50,\"isBaseUnit\":true,\"dangerousGoods\":false,\"unClass\":null,\"rowVersion\":$RV}")
check "pkg: add -> 200"            "$STATUS" "200"
check "pkg: level appeared"        "$(jsonq 'd.skus[1].units.length' < /tmp/pim-pkg.json)" "1"
PKG_ID=$(jsonq 'd.skus[1].units[0].id' < /tmp/pim-pkg.json)
RV=$(jsonq 'd.rowVersion' < /tmp/pim-pkg.json)

# A level may only appear once per SKU.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/packaging" \
  -H 'Content-Type: application/json' \
  -d "{\"skuCode\":\"$SKU2\",\"level\":\"EACH\",\"parentLevel\":null,\"containsQty\":1,\"gtin\":null,\"sscc\":null,\"grossWeight\":null,\"netWeight\":null,\"lengthMm\":null,\"widthMm\":null,\"heightMm\":null,\"isBaseUnit\":false,\"dangerousGoods\":false,\"unClass\":null,\"rowVersion\":$RV}")
check "pkg: duplicate level 409"   "$STATUS" "409"

# A GTIN identifies exactly one tradeable unit worldwide. $SEED_PKG_TAKEN_GTIN
# is already on the lead SKU's CARTON, so claiming it again must be refused.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/packaging" \
  -H 'Content-Type: application/json' \
  -d "{\"skuCode\":\"$SKU2\",\"level\":\"CARTON\",\"parentLevel\":null,\"containsQty\":1,\"gtin\":\"$SEED_PKG_TAKEN_GTIN\",\"sscc\":null,\"grossWeight\":null,\"netWeight\":null,\"lengthMm\":null,\"widthMm\":null,\"heightMm\":null,\"isBaseUnit\":false,\"dangerousGoods\":false,\"unClass\":null,\"rowVersion\":$RV}")
check "pkg: duplicate gtin 409"    "$STATUS" "409"

# Packaging must nest smaller-inside-larger: a PALLET cannot sit inside an EACH.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/packaging" \
  -H 'Content-Type: application/json' \
  -d "{\"skuCode\":\"$SKU2\",\"level\":\"PALLET\",\"parentLevel\":\"EACH\",\"containsQty\":1,\"gtin\":null,\"sscc\":null,\"grossWeight\":null,\"netWeight\":null,\"lengthMm\":null,\"widthMm\":null,\"heightMm\":null,\"isBaseUnit\":false,\"dangerousGoods\":false,\"unClass\":null,\"rowVersion\":$RV}")
check "pkg: bad containment 400"   "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/packaging/$PKG_ID" \
  -H 'Content-Type: application/json' \
  -d "{\"containsQty\":1,\"gtin\":null,\"sscc\":null,\"grossWeight\":5,\"netWeight\":9,\"lengthMm\":null,\"widthMm\":null,\"heightMm\":null,\"isBaseUnit\":true,\"dangerousGoods\":false,\"unClass\":null,\"rowVersion\":$RV}")
check "pkg: gross below net 400"   "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/packaging/$PKG_ID" \
  -H 'Content-Type: application/json' \
  -d "{\"containsQty\":1,\"gtin\":\"12\",\"sscc\":null,\"grossWeight\":null,\"netWeight\":null,\"lengthMm\":null,\"widthMm\":null,\"heightMm\":null,\"isBaseUnit\":false,\"dangerousGoods\":false,\"unClass\":null,\"rowVersion\":$RV}")
check "pkg: short gtin -> 400"     "$STATUS" "400"

STATUS=$(curl -s -o /tmp/pim-pkg.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/packaging/$PKG_ID" \
  -H 'Content-Type: application/json' \
  -d "{\"containsQty\":2,\"gtin\":\"9990000000017\",\"sscc\":null,\"grossWeight\":12,\"netWeight\":9,\"lengthMm\":null,\"widthMm\":null,\"heightMm\":null,\"isBaseUnit\":true,\"dangerousGoods\":true,\"unClass\":\"UN1950\",\"rowVersion\":$RV}")
check "pkg: update -> 200"         "$STATUS" "200"
check "pkg: dangerous goods set"   "$(jsonq 'd.skus[1].units[0].unClass' < /tmp/pim-pkg.json)" "UN1950"
RV=$(jsonq 'd.rowVersion' < /tmp/pim-pkg.json)

STATUS=$(curl -s -o /tmp/pim-pkg.json -w '%{http_code}' -X DELETE "$BASE/api/v1/products/$LEAD/packaging/$PKG_ID?rowVersion=$RV")
check "pkg: delete -> 200"         "$STATUS" "200"
check "pkg: back to none"          "$(jsonq 'd.skus[1].units.length' < /tmp/pim-pkg.json)" "0"
RV=$(jsonq 'd.rowVersion' < /tmp/pim-pkg.json)

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X DELETE "$BASE/api/v1/products/$LEAD/packaging/999999?rowVersion=$RV")
check "pkg: unknown unit -> 404"   "$STATUS" "404"

echo
echo "== Relationships (Sprint 9) =="
BODY=$(curl -s "$BASE/api/v1/products/$LEAD/relationships")
# Every type is returned, empty ones included, so the tab shows what can be filled.
check "rel: all types returned"    "$(printf '%s' "$BODY" | jsonq 'd.groups.length')" "5"
check "rel: seeded accessories"    "$(printf '%s' "$BODY" | jsonq 'd.groups.find(g=>g.relationType=="ACCESSORY").items.length')" "2"
check "rel: target name resolved"  "$(printf '%s' "$BODY" | jsonq 'd.groups.find(g=>g.relationType=="ACCESSORY").items[0].targetName.length > 0')" "true"
# The DB stores workflow states lowercase; every projection must expose the
# enum name. A lowercase chip on this tab was found in the browser check.
check "rel: workflow normalised"   "$(printf '%s' "$BODY" | jsonq 'd.groups.find(g=>g.relationType=="ACCESSORY").items[0].targetWorkflowState')" "Approval"
check "rel: replacement empty"     "$(printf '%s' "$BODY" | jsonq 'd.groups.find(g=>g.relationType=="REPLACEMENT").items.length')" "0"
RV=$(printf '%s' "$BODY" | jsonq 'd.rowVersion')

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/relationships" \
  -H 'Content-Type: application/json' \
  -d "{\"relationType\":\"ACCESSORY\",\"targetCode\":\"$LEAD\",\"priority\":1,\"rowVersion\":$RV}")
check "rel: self-reference -> 400" "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/relationships" \
  -H 'Content-Type: application/json' \
  -d "{\"relationType\":\"ACCESSORY\",\"targetCode\":\"$SEED_REL_ACCESSORY_1\",\"priority\":1,\"rowVersion\":$RV}")
check "rel: duplicate -> 409"      "$STATUS" "409"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/relationships" \
  -H 'Content-Type: application/json' \
  -d "{\"relationType\":\"ACCESSORY\",\"targetCode\":\"NOPE-1\",\"priority\":1,\"rowVersion\":$RV}")
check "rel: unknown target 404"    "$STATUS" "404"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/relationships" \
  -H 'Content-Type: application/json' \
  -d "{\"relationType\":\"FRIEND\",\"targetCode\":\"$SEED_REL_ACCESSORY_1\",\"priority\":1,\"rowVersion\":$RV}")
check "rel: bad type -> 400"       "$STATUS" "400"

STATUS=$(curl -s -o /tmp/pim-rel.json -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/relationships" \
  -H 'Content-Type: application/json' \
  -d "{\"relationType\":\"REPLACEMENT\",\"targetCode\":\"$SEED_REL_UNLINKED\",\"priority\":3,\"rowVersion\":$RV}")
check "rel: add -> 200"            "$STATUS" "200"
check "rel: replacement grew"      "$(jsonq 'd.groups.find(g=>g.relationType=="REPLACEMENT").items.length' < /tmp/pim-rel.json)" "1"
RV=$(jsonq 'd.rowVersion' < /tmp/pim-rel.json)

# Links are one-directional: adding A->B must not create B->A.
INVERSE=$(curl -s "$BASE/api/v1/products/$SEED_REL_UNLINKED/relationships" | jsonq 'd.groups.find(g=>g.relationType=="REPLACEMENT").items.length')
check "rel: not mirrored"          "$INVERSE" "0"

STATUS=$(curl -s -o /tmp/pim-rel.json -w '%{http_code}' -X DELETE "$BASE/api/v1/products/$LEAD/relationships/REPLACEMENT/$SEED_REL_UNLINKED?rowVersion=$RV")
check "rel: delete -> 200"         "$STATUS" "200"
check "rel: replacement empty"     "$(jsonq 'd.groups.find(g=>g.relationType=="REPLACEMENT").items.length' < /tmp/pim-rel.json)" "0"
RV=$(jsonq 'd.rowVersion' < /tmp/pim-rel.json)

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X DELETE "$BASE/api/v1/products/$LEAD/relationships/REPLACEMENT/$SEED_REL_UNLINKED?rowVersion=$RV")
check "rel: delete missing 404"    "$STATUS" "404"

echo
echo "== Applications (Sprint 9) =="
BODY=$(curl -s "$BASE/api/v1/products/$LEAD/applications")
check "app: four groups"           "$(printf '%s' "$BODY" | jsonq 'd.groups.length')" "$SEED_APP_GROUP_COUNT"
# The tab is a picker: unassigned options must come back too.
check "app: all options returned"  "$(printf '%s' "$BODY" | jsonq 'd.groups.reduce((n,g)=>n+g.options.length,0)')" "$SEED_APP_OPTION_COUNT"
check "app: seeded assignments"    "$(printf '%s' "$BODY" | jsonq 'd.groups.reduce((n,g)=>n+g.options.filter(o=>o.isAssigned).length,0)')" "$SEED_APP_ASSIGNED_COUNT"
# Labels come from application_tr, NOT from the code. A label equal to its code
# means the join silently missed. Locale-independent: whichever language is
# asked for, a raw code must never reach the screen.
check "app: labels localised"      "$(printf '%s' "$BODY" | jsonq 'String(d.groups.every(g=>g.options.every(o=>o.label && o.label.length>0 && o.label!==o.code)))')" "true"
# Named locale, since Sprint 18. This used to ask for nothing and expect German,
# which worked only while the endpoint ignored the selector — it was asserting
# the default, not the data. Both directions are covered in the R-15 Phase 2
# section further down; this keeps the one concrete German label as a canary
# that application_tr is actually reachable.
check "app: German label present"  "$(curl -s "$BASE/api/v1/products/$LEAD/applications?locale=$SEED_BASE_LOCALE" | jsonq 'String(d.groups.flatMap(g=>g.options).some(o=>o.label==="Gewerbe"))')" "true"
RV=$(printf '%s' "$BODY" | jsonq 'd.rowVersion')

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/applications" \
  -H 'Content-Type: application/json' \
  -d "{\"applicationCodes\":[\"not_a_real_application\"],\"rowVersion\":$RV}")
check "app: unknown code -> 400"   "$STATUS" "400"

# A rejected set must leave the previous assignment untouched.
check "app: unchanged after 400"   "$(curl -s "$BASE/api/v1/products/$LEAD/applications" | jsonq 'd.groups.reduce((n,g)=>n+g.options.filter(o=>o.isAssigned).length,0)')" "$SEED_APP_ASSIGNED_COUNT"

STATUS=$(curl -s -o /tmp/pim-app.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/applications" \
  -H 'Content-Type: application/json' \
  -d "{\"applicationCodes\":[\"commercial\",\"hotel\"],\"rowVersion\":$RV}")
check "app: replace set -> 200"    "$STATUS" "200"
check "app: set replaced wholly"   "$(jsonq 'd.groups.reduce((n,g)=>n+g.options.filter(o=>o.isAssigned).length,0)' < /tmp/pim-app.json)" "2"
RV=$(jsonq 'd.rowVersion' < /tmp/pim-app.json)

STATUS=$(curl -s -o /tmp/pim-app.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/applications" \
  -H 'Content-Type: application/json' \
  -d "{\"applicationCodes\":[],\"rowVersion\":$RV}")
check "app: empty set -> 200"      "$STATUS" "200"
check "app: cleared"               "$(jsonq 'd.groups.reduce((n,g)=>n+g.options.filter(o=>o.isAssigned).length,0)' < /tmp/pim-app.json)" "0"
RV=$(jsonq 'd.rowVersion' < /tmp/pim-app.json)

# Restore the seeded set so a repeat run starts from the same place. The code
# list is generated alongside the seed, so it cannot drift from what was
# actually inserted.
STATUS=$(curl -s -o /tmp/pim-app.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/applications" \
  -H 'Content-Type: application/json' \
  -d "{\"applicationCodes\":$SEED_APP_CODES_JSON,\"rowVersion\":$RV}")
check "app: restored -> 200"       "$STATUS" "200"
check "app: back to seeded set"    "$(jsonq 'd.groups.reduce((n,g)=>n+g.options.filter(o=>o.isAssigned).length,0)' < /tmp/pim-app.json)" "$SEED_APP_ASSIGNED_COUNT"

echo
echo "== Activity / audit trail (Sprint 10) =="
# Every write above should have left an entry. This is the load-bearing check:
# if the audit pipeline silently stopped recording, the count would not move.
BODY=$(curl -s "$BASE/api/v1/products/$LEAD/activity?page=1&pageSize=25")
check "act: product echoed"        "$(printf '%s' "$BODY" | jsonq 'd.productCode')" "$LEAD"
check "act: entries recorded"      "$(printf '%s' "$BODY" | jsonq 'd.entries.totalCount > 0')" "true"
check "act: newest first"          "$(printf '%s' "$BODY" | jsonq 'd.entries.items.length > 1 ? String(new Date(d.entries.items[0].occurredAt) >= new Date(d.entries.items[1].occurredAt)) : "true"')" "true"
# Actor is null until auth ships — it must never be invented.
check "act: actor is null"         "$(printf '%s' "$BODY" | jsonq 'String(d.entries.items[0].actor)')" "null"
check "act: source recorded"       "$(printf '%s' "$BODY" | jsonq 'd.entries.items[0].source')" "api"
check "act: detail present"        "$(printf '%s' "$BODY" | jsonq 'd.entries.items[0].detail.length > 0')" "true"
check "act: filter list offered"   "$(printf '%s' "$BODY" | jsonq 'd.availableActions.length > 0')" "true"
# The filter list must only offer actions that exist, or it can return nothing.
check "act: filters are real"      "$(printf '%s' "$BODY" | jsonq 'String(d.availableActions.every(a=>typeof a === "string" && a.length > 0))')" "true"

BEFORE=$(printf '%s' "$BODY" | jsonq 'd.entries.totalCount')
RV=$(curl -s "$BASE/api/v1/products/$LEAD" | jsonq 'd.rowVersion')
curl -s -o /dev/null -X PUT "$BASE/api/v1/products/$LEAD" \
  -H 'Content-Type: application/json' \
  -d "{\"name\":\"Craft Extended — bearbeitet\",\"ownerDisplayName\":\"Lena Vogt\",\"gtin\":\"4012345678901\",\"sapMaterialNo\":\"10000012345\",\"rowVersion\":$RV}"
AFTER=$(curl -s "$BASE/api/v1/products/$LEAD/activity?page=1&pageSize=1" | jsonq 'd.entries.totalCount')
check "act: write adds an entry"   "$((AFTER - BEFORE))" "1"

# A REJECTED command changed nothing, so it must not appear in the history.
curl -s -o /dev/null -X PUT "$BASE/api/v1/products/$LEAD" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Should not be recorded","ownerDisplayName":null,"gtin":null,"sapMaterialNo":null,"rowVersion":1}'
REJECTED=$(curl -s "$BASE/api/v1/products/$LEAD/activity?page=1&pageSize=1" | jsonq 'd.entries.totalCount')
check "act: failure not recorded"  "$REJECTED" "$AFTER"

check "act: action filter works"   "$(curl -s "$BASE/api/v1/products/$LEAD/activity?page=1&pageSize=50&action=updated" | jsonq 'String(d.entries.items.every(e=>e.action==="updated"))')" "true"
check "act: search filters"        "$(curl -s "$BASE/api/v1/products/$LEAD/activity?page=1&pageSize=50&search=zzzznomatch" | jsonq 'd.entries.totalCount')" "0"
check "act: paging honoured"       "$(curl -s "$BASE/api/v1/products/$LEAD/activity?page=1&pageSize=2" | jsonq 'd.entries.items.length')" "2"
check "act: pageSize echoed"       "$(curl -s "$BASE/api/v1/products/$LEAD/activity?page=1&pageSize=2" | jsonq 'd.entries.pageSize')" "2"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products/$LEAD/activity?page=1&pageSize=5000")
check "act: pageSize capped 400"   "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products/NOPE-1/activity")
check "act: unknown code -> 404"   "$STATUS" "404"

echo
echo "== Media & Documents (Sprint 12) =="
BODY=$(curl -s "$BASE/api/v1/products/$LEAD/assets")
check "asset: product echoed"      "$(printf '%s' "$BODY" | jsonq 'd.productCode')" "$LEAD"
check "asset: seeded images"       "$(printf '%s' "$BODY" | jsonq 'd.media.length')" "$SEED_LEAD_MEDIA_COUNT"
check "asset: seeded documents"    "$(printf '%s' "$BODY" | jsonq 'd.documents.length')" "$SEED_LEAD_DOCUMENT_COUNT"
# Images and documents are split by TYPE, because that decides whether a
# thumbnail can be rendered at all.
check "asset: media are visual"    "$(printf '%s' "$BODY" | jsonq 'String(d.media.every(a=>a.assetType==="IMAGE"||a.assetType==="SPIN_360"))')" "true"
check "asset: docs are not visual" "$(printf '%s' "$BODY" | jsonq 'String(d.documents.every(a=>a.assetType!=="IMAGE"))')" "true"
check "asset: exactly one MAIN"    "$(printf '%s' "$BODY" | jsonq 'd.media.filter(a=>a.role==="MAIN").length')" "1"
check "asset: MAIN is primary"     "$(printf '%s' "$BODY" | jsonq 'String(d.media.find(a=>a.role==="MAIN").isPrimary)')" "true"
# The client's images live on their server; the flag is what lets the UI say so
# rather than implying the record is broken.
check "asset: client images external" "$(printf '%s' "$BODY" | jsonq 'String(d.media.every(a=>a.isExternal))')" "true"
check "asset: url resolved"        "$(printf '%s' "$BODY" | jsonq 'String(d.media[0].url.startsWith("http"))')" "true"
check "asset: title localised"     "$(printf '%s' "$BODY" | jsonq 'String(d.documents.some(a=>a.title !== null))')" "true"

RV=$(printf '%s' "$BODY" | jsonq 'd.rowVersion')

# A second MAIN must be refused: channels that can show one image need it to be
# unambiguous.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/assets" \
  -H 'Content-Type: application/json' \
  -d "{\"storageKey\":\"https://example.test/second-main.jpg\",\"role\":\"MAIN\",\"assetType\":null,\"title\":null,\"altText\":null,\"rowVersion\":$RV}")
check "asset: second MAIN -> 409"  "$STATUS" "409"

# Type and role are validated as a pair: a PDF cannot be the main image.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/assets" \
  -H 'Content-Type: application/json' \
  -d "{\"storageKey\":\"https://example.test/sheet.pdf\",\"role\":\"GALLERY\",\"assetType\":null,\"title\":null,\"altText\":null,\"rowVersion\":$RV}")
check "asset: pdf as GALLERY 400"  "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/assets" \
  -H 'Content-Type: application/json' \
  -d "{\"storageKey\":\"https://example.test/x.jpg\",\"role\":\"NOSUCH\",\"assetType\":null,\"title\":null,\"altText\":null,\"rowVersion\":$RV}")
check "asset: bad role -> 400"     "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/assets" \
  -H 'Content-Type: application/json' \
  -d '{"storageKey":"https://example.test/y.jpg","role":"GALLERY","assetType":null,"title":null,"altText":null,"rowVersion":1}')
check "asset: stale version -> 409" "$STATUS" "409"

STATUS=$(curl -s -o /tmp/pim-ast.json -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/assets" \
  -H 'Content-Type: application/json' \
  -d "{\"storageKey\":\"https://example.test/smoke-gallery.jpg\",\"role\":\"GALLERY\",\"assetType\":null,\"title\":\"Smoke image\",\"altText\":\"Smoke alt\",\"rowVersion\":$RV}")
check "asset: add image -> 200"    "$STATUS" "200"
check "asset: media grew"          "$(jsonq 'd.media.length' < /tmp/pim-ast.json)" "$((SEED_LEAD_MEDIA_COUNT + 1))"
# Type inferred from the extension, so a steward pasting a URL need not classify it.
check "asset: type inferred"       "$(jsonq 'd.media.find(a=>a.title==="Smoke image").assetType' < /tmp/pim-ast.json)" "IMAGE"
LINK=$(jsonq 'd.media.find(a=>a.title==="Smoke image").linkId' < /tmp/pim-ast.json)
RV=$(jsonq 'd.rowVersion' < /tmp/pim-ast.json)

# Registering the same key again reuses the asset rather than duplicating it.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/assets" \
  -H 'Content-Type: application/json' \
  -d "{\"storageKey\":\"https://example.test/smoke-gallery.jpg\",\"role\":\"GALLERY\",\"assetType\":null,\"title\":null,\"altText\":null,\"rowVersion\":$RV}")
check "asset: duplicate link 409"  "$STATUS" "409"

STATUS=$(curl -s -o /tmp/pim-ast.json -w '%{http_code}' -X POST "$BASE/api/v1/products/$LEAD/assets/$LINK/primary" \
  -H 'Content-Type: application/json' -d "{\"rowVersion\":$RV}")
check "asset: set primary -> 200"  "$STATUS" "200"
check "asset: promoted"            "$(jsonq 'd.media.find(a=>a.linkId==='"$LINK"').isPrimary' < /tmp/pim-ast.json)" "true"
# Exactly one primary per role, or every consumer picks a different image.
check "asset: one GALLERY primary" "$(jsonq 'd.media.filter(a=>a.role==="GALLERY"&&a.isPrimary).length' < /tmp/pim-ast.json)" "1"
RV=$(jsonq 'd.rowVersion' < /tmp/pim-ast.json)

STATUS=$(curl -s -o /tmp/pim-ast.json -w '%{http_code}' -X DELETE "$BASE/api/v1/products/$LEAD/assets/$LINK?rowVersion=$RV")
check "asset: unlink -> 200"       "$STATUS" "200"
check "asset: back to seeded"      "$(jsonq 'd.media.length' < /tmp/pim-ast.json)" "$SEED_LEAD_MEDIA_COUNT"
# Removing a primary promotes the next, so a role is never left without one.
check "asset: primary reassigned"  "$(jsonq 'd.media.filter(a=>a.role==="GALLERY"&&a.isPrimary).length' < /tmp/pim-ast.json)" "1"
RV=$(jsonq 'd.rowVersion' < /tmp/pim-ast.json)

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X DELETE "$BASE/api/v1/products/$LEAD/assets/999999?rowVersion=$RV")
check "asset: unknown link -> 404" "$STATUS" "404"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products/NOPE-1/assets")
check "asset: unknown code -> 404" "$STATUS" "404"

echo
echo "== Planning & BIM (Sprint 13) =="
# Read-only by design: every value is written through Attributes,
# Classifications or Media & Documents. These assertions prove the composition,
# and that no write path was quietly added alongside it.
BODY=$(curl -s "$BASE/api/v1/products/$LEAD/planning")
check "plan: product echoed"       "$(printf '%s' "$BODY" | jsonq 'd.productCode')" "$LEAD"
check "plan: parameter count"      "$(printf '%s' "$BODY" | jsonq 'd.parameterCount')" "$SEED_PLANNING_ATTR_COUNT"
check "plan: filled <= total"      "$(printf '%s' "$BODY" | jsonq 'String(d.parameterFilledCount <= d.parameterCount)')" "true"
check "plan: filled matches rows"  "$(printf '%s' "$BODY" | jsonq 'String(d.parameterFilledCount === d.parameters.filter(p=>p.value !== null && p.value !== "").length)')" "true"
check "plan: params labelled"      "$(printf '%s' "$BODY" | jsonq 'String(d.parameters.every(p=>p.label && p.label !== p.code))')" "true"
check "plan: unit on the measure"  "$(printf '%s' "$BODY" | jsonq 'd.parameters.find(p=>p.code===a[0]).unitSymbol' "$SEED_PLANNING_MEASURE")" "mm"
# bimReady is projected from the client's own BIM_Generator flag, not derived
# from whether geometry files happen to exist.
check "plan: bimReady is boolean"  "$(printf '%s' "$BODY" | jsonq 'String(typeof d.bimReady === "boolean")')" "true"
check "plan: bimReady matches attr" "$(printf '%s' "$BODY" | jsonq 'String(String(d.bimReady) === d.parameters.find(p=>p.code===a[0]).value)' "$SEED_PLANNING_BOOLEAN")" "true"
check "plan: boolean not stringly" "$(printf '%s' "$BODY" | jsonq 'd.parameters.find(p=>p.code===a[0]).dataType' "$SEED_PLANNING_BOOLEAN")" "boolean"
check "plan: decimal declared"     "$(printf '%s' "$BODY" | jsonq 'd.parameters.find(p=>p.code===a[0]).dataType' "$SEED_PLANNING_DECIMAL")" "decimal"
# Whole numbers must survive as whole numbers here too — same rule as the
# Attributes tab, since both project the same column.
check "plan: whole number intact"  "$(printf '%s' "$BODY" | jsonq 'String(d.parameters.filter(p=>p.dataType==="measure" && p.value).every(p=>!/\.0$/.test(p.value)))')" "true"

check "plan: all BIM schemes"      "$(printf '%s' "$BODY" | jsonq 'd.bimReferences.map(r=>r.schemeCode).join(",")')" "$SEED_BIM_SCHEMES"
check "plan: primary scheme first" "$(printf '%s' "$BODY" | jsonq 'd.bimReferences[0].schemeCode')" "$SEED_BIM_PRIMARY_SCHEME"
check "plan: IFC class assigned"   "$(printf '%s' "$BODY" | jsonq 'd.bimReferences.find(r=>r.schemeCode===a[0]).classCode' "$SEED_BIM_PRIMARY_SCHEME")" "$SEED_BIM_CLASS"
# Unassigned schemes are returned too, so the tab can show the gap.
check "plan: unassigned returned"  "$(printf '%s' "$BODY" | jsonq 'String(d.bimReferences.some(r=>r.classCode === null))')" "true"
check "plan: geometry files"       "$(printf '%s' "$BODY" | jsonq 'd.geometryFiles.length')" "$SEED_LEAD_GEOMETRY_COUNT"
# Filtered by TYPE, not role: a datasheet is a document, never geometry.
check "plan: geometry is BIM/CAD"  "$(printf '%s' "$BODY" | jsonq 'String(d.geometryFiles.every(f=>["BIM","CAD"].includes(f.assetType)))')" "true"

# The BIM schemes are ordinary classification schemes, so the Classifications
# tab must see them too — if these two counts disagree, one of the tabs is
# reading a private copy.
check "plan: schemes shared w/ class tab" "$(curl -s "$BASE/api/v1/products/$LEAD/classifications" | jsonq 'd.schemes.length')" "$SEED_SCHEME_COUNT"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/planning" \
  -H 'Content-Type: application/json' -d '{}')
check "plan: no write path (405)"  "$STATUS" "405"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products/NOPE-1/planning")
check "plan: unknown code -> 404"  "$STATUS" "404"

echo
echo "== Localization (Sprint 13) =="
BODY=$(curl -s "$BASE/api/v1/products/$LEAD/localization")
check "loc: product echoed"        "$(printf '%s' "$BODY" | jsonq 'd.productCode')" "$LEAD"
check "loc: base locale"           "$(printf '%s' "$BODY" | jsonq 'd.baseLocale')" "$SEED_BASE_LOCALE"
# Base locale first, so the tab never has to sort to find the master.
check "loc: base listed first"     "$(printf '%s' "$BODY" | jsonq 'd.locales[0].localeCode')" "$SEED_BASE_LOCALE"
check "loc: exactly one base"      "$(printf '%s' "$BODY" | jsonq 'd.locales.filter(l=>l.isBase).length')" "1"
# Every configured locale is returned, including ones with no translation at
# all — an inner join would have hidden the untouched languages.
check "loc: all locales returned"  "$(printf '%s' "$BODY" | jsonq 'String(d.locales.length >= 3)')" "true"
check "loc: untouched is missing"  "$(printf '%s' "$BODY" | jsonq 'd.locales.find(l=>l.localeCode==="fr-FR").translationStatus')" "missing"
check "loc: localizable attr shown" "$(printf '%s' "$BODY" | jsonq 'String(d.locales[0].fields.some(f=>f.field===a[0] && f.isAttribute))' "$SEED_ATTR_LOCALIZABLE")" "true"
check "loc: attribute count"       "$(printf '%s' "$BODY" | jsonq 'd.locales[0].fields.filter(f=>f.isAttribute).length')" "$SEED_LOCALIZABLE_COUNT"
check "loc: name is required"      "$(printf '%s' "$BODY" | jsonq 'String(d.locales[0].fields.find(f=>f.field==="name").isRequired)')" "true"
# The German design copy has no English equivalent in the export. That gap is
# the tab's whole reason to exist, so assert it is visible rather than papered
# over with a copy of the German.
check "loc: German has design copy" "$(printf '%s' "$BODY" | jsonq 'String(d.locales[0].fields.find(f=>f.field==="shortDesc").value !== null)')" "$SEED_LEAD_HAS_DESIGN_COPY"
check "loc: English lacks it"      "$(printf '%s' "$BODY" | jsonq 'String(d.locales.find(l=>l.localeCode===a[0]).fields.find(f=>f.field==="shortDesc").value)' "$SEED_TARGET_LOCALE")" "null"
check "loc: English is pending"    "$(printf '%s' "$BODY" | jsonq 'd.locales.find(l=>l.localeCode===a[0]).translationStatus' "$SEED_TARGET_LOCALE")" "pending"
# The base column is context, so every target row carries the master text.
BASE_NAME=$(printf '%s' "$BODY" | jsonq 'd.locales[0].fields.find(f=>f.field==="name").value')
check "loc: base value carried"    "$(printf '%s' "$BODY" | jsonq 'd.locales.find(l=>l.localeCode===a[0]).fields.find(f=>f.field==="name").baseValue' "$SEED_TARGET_LOCALE")" "$BASE_NAME"
check "loc: completeness derived"  "$(printf '%s' "$BODY" | jsonq 'String(d.locales.every(l=>l.completeness === (l.fieldCount === 0 ? 100 : Math.round(l.filledCount / l.fieldCount * 100))))')" "true"
check "loc: pending count matches" "$(printf '%s' "$BODY" | jsonq 'String(d.pendingLocaleCount === d.locales.filter(l=>!l.isBase && l.translationStatus !== "approved").length)')" "true"

RV=$(printf '%s' "$BODY" | jsonq 'd.rowVersion')

# The master is edited on General and Attributes. Two screens writing the same
# column with different validation is how the two drift apart.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/localization/$SEED_BASE_LOCALE" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[{\"field\":\"name\",\"value\":\"Nicht hier\"}],\"translationStatus\":null,\"rowVersion\":$RV}")
check "loc: base locale -> 400"    "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/localization/xx-XX" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[{\"field\":\"name\",\"value\":\"x\"}],\"translationStatus\":null,\"rowVersion\":$RV}")
check "loc: unknown locale -> 404" "$STATUS" "404"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/localization/$SEED_TARGET_LOCALE" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[{\"field\":\"nonsense\",\"value\":\"x\"}],\"translationStatus\":null,\"rowVersion\":$RV}")
check "loc: unknown field -> 400"  "$STATUS" "400"

# A language-neutral attribute has one value for every locale; writing it here
# would silently create a second, locale-scoped one that no tab reads.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/localization/$SEED_TARGET_LOCALE" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[{\"field\":\"$SEED_ATTR_MEASURE\",\"value\":\"12\"}],\"translationStatus\":null,\"rowVersion\":$RV}")
check "loc: not localizable -> 400" "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/localization/$SEED_TARGET_LOCALE" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[{\"field\":\"name\",\"value\":\"\"}],\"translationStatus\":null,\"rowVersion\":$RV}")
check "loc: clear name -> 400"     "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/localization/$SEED_TARGET_LOCALE" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[{\"field\":\"shortDesc\",\"value\":\"x\"}],\"translationStatus\":\"invented\",\"rowVersion\":$RV}")
check "loc: bad status -> 400"     "$STATUS" "400"

# 'missing' is derived from the absence of a row. Letting a caller assert it
# would let the status contradict the data.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/localization/$SEED_TARGET_LOCALE" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[{\"field\":\"shortDesc\",\"value\":\"x\"}],\"translationStatus\":\"missing\",\"rowVersion\":$RV}")
check "loc: 'missing' not settable" "$STATUS" "400"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/localization/$SEED_TARGET_LOCALE" \
  -H 'Content-Type: application/json' \
  -d '{"values":[{"field":"shortDesc","value":"x"}],"translationStatus":null,"rowVersion":1}')
check "loc: stale version -> 409"  "$STATUS" "409"

# ---- the working path
STATUS=$(curl -s -o /tmp/pim-loc.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/localization/$SEED_TARGET_LOCALE" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[{\"field\":\"shortDesc\",\"value\":\"Fine structure in warm beige.\"},{\"field\":\"$SEED_ATTR_LOCALIZABLE\",\"value\":\"EN 14411 Annex L\"}],\"translationStatus\":\"translated\",\"rowVersion\":$RV}")
check "loc: save -> 200"           "$STATUS" "200"
check "loc: text saved"            "$(jsonq 'd.locales.find(l=>l.localeCode===a[0]).fields.find(f=>f.field==="shortDesc").value' "$SEED_TARGET_LOCALE" < /tmp/pim-loc.json)" "Fine structure in warm beige."
check "loc: attribute saved"       "$(jsonq 'd.locales.find(l=>l.localeCode===a[0]).fields.find(f=>f.field===a[1]).value' "$SEED_TARGET_LOCALE" "$SEED_ATTR_LOCALIZABLE" < /tmp/pim-loc.json)" "EN 14411 Annex L"
check "loc: status applied"        "$(jsonq 'd.locales.find(l=>l.localeCode===a[0]).translationStatus' "$SEED_TARGET_LOCALE" < /tmp/pim-loc.json)" "translated"
check "loc: rowVersion bumped"     "$(jsonq 'd.rowVersion' < /tmp/pim-loc.json)" "$((RV + 1))"
# The master must be untouched by a translation save.
check "loc: base untouched"        "$(jsonq 'd.locales[0].fields.find(f=>f.field==="name").value' < /tmp/pim-loc.json)" "$BASE_NAME"
check "loc: completeness rose"     "$(jsonq 'String(d.locales.find(l=>l.localeCode===a[0]).filledCount >= 3)' "$SEED_TARGET_LOCALE" < /tmp/pim-loc.json)" "true"

RV2=$(jsonq 'd.rowVersion' < /tmp/pim-loc.json)

# Writing an English value for a localizable attribute gives it two rows in
# attribute_value. The Attributes tab joins that table without a locale
# predicate unless one was added deliberately — which fans the join out and
# lists the attribute once per language. Caught here, on the one product that
# now has a translated attribute.
ATTRS=$(curl -s "$BASE/api/v1/products/$LEAD/attributes")
check "loc: no duplicate attrs"    "$(printf '%s' "$ATTRS" | jsonq 'String(new Set(d.groups.flatMap(g=>g.attributes).map(x=>x.code)).size === d.groups.reduce((n,g)=>n+g.attributes.length,0))')" "true"
check "loc: attr count unchanged"  "$(printf '%s' "$ATTRS" | jsonq 'd.groups.reduce((n,g)=>n+g.attributes.length,0)')" "$SEED_ATTR_TOTAL_COUNT"
# The Attributes tab shows the base locale, not the translation just saved.
check "loc: attrs show base locale" "$(printf '%s' "$ATTRS" | jsonq 'String(d.groups.flatMap(g=>g.attributes).find(x=>x.code===a[0]).value !== "EN 14411 Annex L")' "$SEED_ATTR_LOCALIZABLE")" "true"
check "loc: completeness sane"     "$(printf '%s' "$ATTRS" | jsonq 'String(d.completenessPct <= 100)')" "true"
check "loc: grid completeness sane" "$(curl -s "$BASE/api/v1/products?search=$LEAD" | jsonq 'String(d.items[0].completenessPct <= 100)')" "true"

# Clearing removes the row so the field reads as untranslated, not as
# translated-to-nothing.
STATUS=$(curl -s -o /tmp/pim-loc.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/localization/$SEED_TARGET_LOCALE" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[{\"field\":\"$SEED_ATTR_LOCALIZABLE\",\"value\":null}],\"translationStatus\":null,\"rowVersion\":$RV2}")
check "loc: clear attr -> 200"     "$STATUS" "200"
check "loc: cleared reads null"    "$(jsonq 'String(d.locales.find(l=>l.localeCode===a[0]).fields.find(f=>f.field===a[1]).value)' "$SEED_TARGET_LOCALE" "$SEED_ATTR_LOCALIZABLE" < /tmp/pim-loc.json)" "null"
RV2=$(jsonq 'd.rowVersion' < /tmp/pim-loc.json)

STATUS=$(curl -s -o /tmp/pim-loc.json -w '%{http_code}' -X PUT "$BASE/api/v1/products/$LEAD/localization/$SEED_TARGET_LOCALE" \
  -H 'Content-Type: application/json' \
  -d "{\"values\":[],\"translationStatus\":\"approved\",\"rowVersion\":$RV2}")
check "loc: approve -> 200"        "$STATUS" "200"
check "loc: approved applied"      "$(jsonq 'd.locales.find(l=>l.localeCode===a[0]).translationStatus' "$SEED_TARGET_LOCALE" < /tmp/pim-loc.json)" "approved"
check "loc: pending count fell"    "$(jsonq 'String(d.pendingLocaleCount)' < /tmp/pim-loc.json)" "1"

# A translation is a change to the product, so it must appear in Activity.
check "loc: write is audited"      "$(curl -s "$BASE/api/v1/products/$LEAD/activity?page=1&pageSize=50&action=translated" | jsonq 'String(d.entries.totalCount > 0)')" "true"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products/NOPE-1/localization")
check "loc: unknown code -> 404"   "$STATUS" "404"

echo
echo "== Detail tabs follow the reader's language (R-15 Phase 2) =="

# These assertions compare the SAME endpoint fetched twice, in two locales, and
# require the rendered labels to differ. That is deliberate: no expected string
# is written down, so a regenerated catalogue cannot silently rot them, and
# every one of them fails against the pre-Sprint-18 code — which returned the
# product's base locale whatever was asked for, making the two responses
# identical. A structural assertion that cannot pass by coincidence beats a
# literal that happens to be right today.
#
# Both halves are checked. "They differ" alone would pass if the endpoint were
# broken into returning nothing, so each side is also required to be non-empty.

labels_differ() { # human-name, de-values, en-values
  if [ -z "$2" ] || [ -z "$3" ]; then
    echo "empty"
  elif [ "$2" = "$3" ]; then
    echo "identical"
  else
    echo "differ"
  fi
}

# --- Planning: parameter labels are attribute labels, so they follow the reader
PLAN_DE=$(curl -s "$BASE/api/v1/products/$LEAD/planning?locale=$SEED_BASE_LOCALE" \
  | jsonq 'd.parameters.map(p=>p.label).join("~")')
PLAN_EN=$(curl -s "$BASE/api/v1/products/$LEAD/planning?locale=$SEED_TARGET_LOCALE" \
  | jsonq 'd.parameters.map(p=>p.label).join("~")')
check "planning: labels follow the locale" "$(labels_differ planning "$PLAN_DE" "$PLAN_EN")" "differ"

# ...and the VALUES beneath them do not. A measurement is not a translation:
# if these moved, the tab would be reporting different numbers per language.
PLANV_DE=$(curl -s "$BASE/api/v1/products/$LEAD/planning?locale=$SEED_BASE_LOCALE" \
  | jsonq 'd.parameters.map(p=>String(p.value)).join("~")')
PLANV_EN=$(curl -s "$BASE/api/v1/products/$LEAD/planning?locale=$SEED_TARGET_LOCALE" \
  | jsonq 'd.parameters.map(p=>String(p.value)).join("~")')
check "planning: values do NOT follow the locale" "$([ "$PLANV_DE" = "$PLANV_EN" ] && echo same || echo moved)" "same"

# --- Applications: labels on reference data a steward picks from
APP_DE=$(curl -s "$BASE/api/v1/products/$LEAD/applications?locale=$SEED_BASE_LOCALE" \
  | jsonq 'd.groups.flatMap(g=>g.options).map(o=>o.label).join("~")')
APP_EN=$(curl -s "$BASE/api/v1/products/$LEAD/applications?locale=$SEED_TARGET_LOCALE" \
  | jsonq 'd.groups.flatMap(g=>g.options).map(o=>o.label).join("~")')
check "applications: labels follow the locale" "$(labels_differ applications "$APP_DE" "$APP_EN")" "differ"

# The assignment set is data, not language — switching locale must not appear
# to change which applications are ticked.
APPA_DE=$(curl -s "$BASE/api/v1/products/$LEAD/applications?locale=$SEED_BASE_LOCALE" \
  | jsonq 'd.groups.flatMap(g=>g.options).filter(o=>o.isAssigned).map(o=>o.code).sort().join(",")')
APPA_EN=$(curl -s "$BASE/api/v1/products/$LEAD/applications?locale=$SEED_TARGET_LOCALE" \
  | jsonq 'd.groups.flatMap(g=>g.options).filter(o=>o.isAssigned).map(o=>o.code).sort().join(",")')
check "applications: assignments are locale-independent" "$([ "$APPA_DE" = "$APPA_EN" ] && echo same || echo moved)" "same"

# --- Relationships: the TARGET's name, resolved against the target's own base
# locale rather than this product's. Two products in one catalogue can carry
# different masters, and a link that crosses that boundary is exactly where
# resolving against the wrong side shows up.
REL_DE=$(curl -s "$BASE/api/v1/products/$LEAD/relationships?locale=$SEED_BASE_LOCALE" \
  | jsonq 'd.groups.flatMap(g=>g.items).map(i=>String(i.targetName)).join("~")')
REL_EN=$(curl -s "$BASE/api/v1/products/$LEAD/relationships?locale=$SEED_TARGET_LOCALE" \
  | jsonq 'd.groups.flatMap(g=>g.items).map(i=>String(i.targetName)).join("~")')
check "relationships: target names follow the locale" "$(labels_differ relationships "$REL_DE" "$REL_EN")" "differ"

# The link graph itself is data. Switching language must not appear to add or
# drop a link.
RELC_DE=$(curl -s "$BASE/api/v1/products/$LEAD/relationships?locale=$SEED_BASE_LOCALE" \
  | jsonq 'String(d.groups.reduce((n,g)=>n+g.items.length,0))')
RELC_EN=$(curl -s "$BASE/api/v1/products/$LEAD/relationships?locale=$SEED_TARGET_LOCALE" \
  | jsonq 'String(d.groups.reduce((n,g)=>n+g.items.length,0))')
check "relationships: link count is locale-independent" "$([ "$RELC_DE" = "$RELC_EN" ] && echo same || echo moved)" "same"

# --- Localization: the FIELD LABELS follow the reader, while the per-locale
# content each row carries is keyed by that row's own locale and must not move.
LOC_DE=$(curl -s "$BASE/api/v1/products/$LEAD/localization?locale=$SEED_BASE_LOCALE" \
  | jsonq 'd.locales.flatMap(l=>l.fields).map(f=>f.label).join("~")')
LOC_EN=$(curl -s "$BASE/api/v1/products/$LEAD/localization?locale=$SEED_TARGET_LOCALE" \
  | jsonq 'd.locales.flatMap(l=>l.fields).map(f=>f.label).join("~")')
check "localization: field labels follow the locale" "$(labels_differ localization "$LOC_DE" "$LOC_EN")" "differ"

LOCV_DE=$(curl -s "$BASE/api/v1/products/$LEAD/localization?locale=$SEED_BASE_LOCALE" \
  | jsonq 'd.locales.map(l=>l.localeCode+":"+l.completeness).join("~")')
LOCV_EN=$(curl -s "$BASE/api/v1/products/$LEAD/localization?locale=$SEED_TARGET_LOCALE" \
  | jsonq 'd.locales.map(l=>l.localeCode+":"+l.completeness).join("~")')
check "localization: per-locale content is unmoved" "$([ "$LOCV_DE" = "$LOCV_EN" ] && echo same || echo moved)" "same"

# --- Every one of these endpoints must REJECT a locale the catalogue does not
# have, rather than quietly serving the default. Falling back is the bug
# ADR-009 was written to prevent: "I selected French and it is still German",
# with nothing in the response to explain it.
for EP in planning relationships applications localization; do
  STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products/$LEAD/$EP?locale=zz-ZZ")
  check "$EP: unknown locale -> 400" "$STATUS" "400"
done

# --- The write paths answer in the language the form was sent from. A save
# that replies in a different locale flips the tab under the user.
ROWV=$(curl -s "$BASE/api/v1/products/$LEAD/applications" | jsonq 'String(d.rowVersion)')
CODES=$(curl -s "$BASE/api/v1/products/$LEAD/applications" \
  | jsonq 'JSON.stringify(d.groups.flatMap(g=>g.options).filter(o=>o.isAssigned).map(o=>o.code))')
STATUS=$(curl -s -o /tmp/pim-app-loc.json -w '%{http_code}' \
  -X PUT "$BASE/api/v1/products/$LEAD/applications?locale=$SEED_BASE_LOCALE" \
  -H 'Content-Type: application/json' \
  -d "{\"applicationCodes\": $CODES, \"rowVersion\": $ROWV}")
check "applications: save echoes -> 200"  "$STATUS" "200"
SAVED_DE=$(jsonq 'd.groups.flatMap(g=>g.options).map(o=>o.label).join("~")' < /tmp/pim-app-loc.json)
check "applications: save answers in the sent locale" \
  "$([ "$SAVED_DE" = "$APP_DE" ] && echo same || echo flipped)" "same"

echo
echo "== Third language and per-locale asset titles (Sprint 19) =="

# fr-FR was configured in catalog.locale from the beginning and had no rows in
# any _tr table, so French was selectable and completely empty. Two locales are
# also not enough to catch a bug that assumes exactly two. These assertions run
# against the third.

FR=fr-FR

check "fr: locale is offered"      "$(curl -s "$BASE/api/v1/locales" | jsonq 'String(d.some(l=>l.code==="fr-FR"))')" "true"

# Labels, in the language asked for. Compared against English rather than a
# written-down French string: a literal would rot when the vocabulary changes,
# and this fails just as loudly if the French pack stops loading.
APP_FR=$(curl -s "$BASE/api/v1/products/$LEAD/applications?locale=$FR" \
  | jsonq 'd.groups.flatMap(g=>g.options).map(o=>o.label).join("~")')
check "fr: application labels differ from English" "$([ -n "$APP_FR" ] && [ "$APP_FR" != "$APP_EN" ] && echo differ || echo same)" "differ"

ATTR_FR=$(curl -s "$BASE/api/v1/products/$LEAD/attributes?locale=$FR" \
  | jsonq 'd.groups.flatMap(g=>g.attributes).map(a=>a.label).join("~")')
ATTR_EN=$(curl -s "$BASE/api/v1/products/$LEAD/attributes?locale=$SEED_TARGET_LOCALE" \
  | jsonq 'd.groups.flatMap(g=>g.attributes).map(a=>a.label).join("~")')
check "fr: attribute labels differ from English" "$([ -n "$ATTR_FR" ] && [ "$ATTR_FR" != "$ATTR_EN" ] && echo differ || echo same)" "differ"

# No attribute may fall back to showing its own code, in any locale. This is the
# Sprint 12 rule extended to the third language, and it is the assertion that
# catches a translation table that loaded for one locale and not another.
check "fr: no attribute shows its code" "$(curl -s "$BASE/api/v1/products/$LEAD/attributes?locale=$FR" | jsonq 'String(d.groups.flatMap(g=>g.attributes).every(a=>a.label && a.label !== a.code))')" "true"

# The deliberate 20% gap. A grid in French must contain BOTH rows translated
# into French and rows that fell back -- if either count is zero the seed's
# partial-translation design has been lost, and with it the only way to see the
# fallback marker work.
FR_OWN=$(curl -s "$BASE/api/v1/products?locale=$FR&pageSize=100" \
  | jsonq 'String(d.items.filter(i=>i.nameLocale==="fr-FR").length)')
FR_BACK=$(curl -s "$BASE/api/v1/products?locale=$FR&pageSize=100" \
  | jsonq 'String(d.items.filter(i=>i.nameLocale && i.nameLocale!=="fr-FR").length)')
check "fr: grid has French rows"          "$([ "$FR_OWN" -gt 0 ] && echo yes || echo no)" "yes"
check "fr: grid also has fallback rows"   "$([ "$FR_BACK" -gt 0 ] && echo yes || echo no)" "yes"

# Search has to work in the third language too. The FTS vector aggregates across
# locales, so a French word must find a product even though the base is German.
check "fr: search finds a French term"    "$(curl -s "$BASE/api/v1/products?search=carreau&pageSize=1&locale=$FR" | jsonq 'String(d.totalCount > 0)')" "true"

echo
echo "-- per-locale asset titles (R-18) --"

# The invariant R-18 was opened for: a title edited in one locale must not
# overwrite another's, and must be readable back in the locale it was written
# to. Before Sprint 19 the read followed the selector while the write always
# went to the base locale, so this round trip lost data silently.
AROW=$(curl -s "$BASE/api/v1/products/$LEAD/assets" | jsonq 'String(d.rowVersion)')
ALINK=$(curl -s "$BASE/api/v1/products/$LEAD/assets" | jsonq 'String(d.media.concat(d.documents)[0].linkId)')
AROLE=$(curl -s "$BASE/api/v1/products/$LEAD/assets" | jsonq 'String(d.media.concat(d.documents)[0].role)')
ASORT=$(curl -s "$BASE/api/v1/products/$LEAD/assets" | jsonq 'String(d.media.concat(d.documents)[0].sortOrder)')
DE_TITLE_BEFORE=$(curl -s "$BASE/api/v1/products/$LEAD/assets?locale=$SEED_BASE_LOCALE" \
  | jsonq 'String(d.media.concat(d.documents).find(x=>x.linkId===Number(a[0]))?.title)' "$ALINK")

STATUS=$(curl -s -o /tmp/pim-asset-fr.json -w '%{http_code}' \
  -X PUT "$BASE/api/v1/products/$LEAD/assets/$ALINK?locale=$FR" \
  -H 'Content-Type: application/json' \
  -d "{\"role\":\"$AROLE\",\"title\":\"Titre francais de test\",\"altText\":null,\"sortOrder\":$ASORT,\"rowVersion\":$AROW}")
check "asset: French title saved -> 200" "$STATUS" "200"

check "asset: French title reads back in French" "$(curl -s "$BASE/api/v1/products/$LEAD/assets?locale=$FR" | jsonq 'String(d.media.concat(d.documents).find(x=>x.linkId===Number(a[0]))?.title)' "$ALINK")" "Titre francais de test"

# The whole point. The German master must be exactly as it was.
check "asset: German title untouched" "$(curl -s "$BASE/api/v1/products/$LEAD/assets?locale=$SEED_BASE_LOCALE" | jsonq 'String(d.media.concat(d.documents).find(x=>x.linkId===Number(a[0]))?.title)' "$ALINK")" "$DE_TITLE_BEFORE"

# An unconfigured locale must be refused BEFORE anything is written, because
# here the locale is not just presentation -- it is the row key.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' \
  -X PUT "$BASE/api/v1/products/$LEAD/assets/$ALINK?locale=zz-ZZ" \
  -H 'Content-Type: application/json' \
  -d "{\"role\":\"$AROLE\",\"title\":\"never\",\"altText\":null,\"sortOrder\":$ASORT,\"rowVersion\":1}")
check "asset: unknown locale rejected" "$STATUS" "400"
check "asset: nothing written for it"  "$(curl -s "$BASE/api/v1/products/$LEAD/assets?locale=$FR" | jsonq 'String(d.media.concat(d.documents).find(x=>x.linkId===Number(a[0]))?.title)' "$ALINK")" "Titre francais de test"

for EP in assets planning; do
  STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products/$LEAD/$EP?locale=zz-ZZ")
  check "$EP: unknown locale -> 400" "$STATUS" "400"
done

echo
echo "== The outbox actually delivers (Sprint 20) =="

if [ "$RABBIT_UP" -ne 1 ]; then
  # Not skipped quietly. An unreachable broker is exactly the condition under
  # which "no assertions ran" reads identically to "everything passed" -- R-10.
  check "rabbit: management API reachable" "no" "yes"
else
  # The relay polls every 250ms in Development. Poll rather than sleep a fixed
  # amount: a fixed wait is either flaky or slow, and usually both.
  RABBIT_BODY='[]'
  for _ in $(seq 1 40); do
    RABBIT_BODY=$(curl -s -u "$RABBIT_AUTH" -X POST "$RABBIT_MGMT/queues/%2F/$SMOKE_QUEUE/get" \
      -H 'content-type: application/json' \
      -d '{"count":50,"ackmode":"ack_requeue_false","encoding":"auto"}')
    [ "$(printf '%s' "$RABBIT_BODY" | jsonq 'String(d.length)')" != "0" ] && break
    sleep 0.5
  done

  check "rabbit: outbox delivered something"       "$(printf '%s' "$RABBIT_BODY" | jsonq 'String(d.length > 0)')" "true"

  # Routed by event type, which is what lets a consumer bind catalog.product.*
  # instead of receiving everything this service will ever publish.
  check "rabbit: routing key is the event type"    "$(printf '%s' "$RABBIT_BODY" | jsonq 'String(d.some(m=>m.routing_key==="catalog.product.created"))')" "true"

  # The product this run created must be in there. Without this the assertion
  # above would pass on a message left by an earlier run of anything.
  check "rabbit: carries the product created here" "$(printf '%s' "$RABBIT_BODY" | jsonq 'String(d.some(m=>m.payload.includes(a[0])))' "$NEW_CODE")" "true"

  # The consumer's idempotency key, in the AMQP property rather than only in the
  # body, so deduplication does not require parsing JSON the consumer may not yet
  # be able to read.
  check "rabbit: message_id set for dedup"         "$(printf '%s' "$RABBIT_BODY" | jsonq 'String(d.every(m=>m.properties && m.properties.message_id && m.properties.message_id.length===36))')" "true"
  check "rabbit: schema_version header present"    "$(printf '%s' "$RABBIT_BODY" | jsonq 'String(d.every(m=>m.properties.headers && m.properties.headers.schema_version >= 1))')" "true"
  check "rabbit: published persistent"             "$(printf '%s' "$RABBIT_BODY" | jsonq 'String(d.every(m=>m.properties.delivery_mode === 2))')" "true"
  check "rabbit: payload is the wire contract"     "$(printf '%s' "$RABBIT_BODY" | jsonq 'String(d.filter(m=>m.routing_key==="catalog.product.created").every(m=>{const p=JSON.parse(m.payload); return typeof p.productId==="string" && typeof p.productCode==="string";}))')" "true"

  # Nothing may be left dead-lettered, and nothing may be stuck pending. The
  # relay only dead-letters after MaxAttempts failures, so a non-zero dead count
  # means delivery is broken in a way the queue contents above would not reveal
  # -- some messages got through while others exhausted their retries.
  DEAD=$(docker compose -f "$ROOT/infra/docker-compose.yml" exec -T postgres \
    psql -U pim_admin -d pim -tAc \
    "SELECT count(*) FROM catalog.outbox_message WHERE status = 'dead'" 2>/dev/null | tr -d '[:space:]')
  check "rabbit: nothing dead-lettered"            "${DEAD:-unknown}" "0"

  # The queue drained, which is the difference between "the transport works" and
  # "the transport worked once". Anything still pending after the poll loop above
  # means the relay is not keeping up or has stopped.
  STUCK=$(docker compose -f "$ROOT/infra/docker-compose.yml" exec -T postgres \
    psql -U pim_admin -d pim -tAc \
    "SELECT count(*) FROM catalog.outbox_message WHERE status <> 'published'" 2>/dev/null | tr -d '[:space:]')
  check "rabbit: outbox fully drained"             "${STUCK:-unknown}" "0"
fi

echo
echo "== Product Cockpit (Sprint 21) =="

DASH=$(curl -s "$BASE/api/v1/dashboard")

check "dash: served"                "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/dashboard")" "200"
check "dash: four KPIs"             "$(printf '%s' "$DASH" | jsonq 'String(d.kpis.length)')" "4"

# CROSS-CHECKED, not merely present. A dashboard whose figures are computed by
# a second query is exactly where two answers to the same question diverge, and
# the reader has no way to tell which one is wrong. So every headline number is
# compared against what the products API independently reports.
GRID_TOTAL=$(curl -s "$BASE/api/v1/products?pageSize=1" | jsonq 'String(d.totalCount)')
DASH_TOTAL=$(printf '%s' "$DASH" | jsonq 'd.kpis.find(k=>k.code==="total_products").value.replace(/,/g,"")')
check "dash: total agrees with the grid" "$DASH_TOTAL" "$GRID_TOTAL"

# "Ready to publish" recomputed from the grid's own rows.
#
# The dashboard derives it in one SQL aggregate (state = approval AND
# completeness >= 90). Here it is counted from the per-row completeness the grid
# publishes, which comes from an identical but separately written lateral. Two
# implementations of one definition, compared — the only way a KPI expressed as
# a single number can be checked at all.
#
# It matters because the first version of this KPI filtered on 'approved', a
# value the domain never writes: it returned 0 on every run, and 0 is a
# perfectly plausible figure. Nothing but an independent recount could tell the
# difference between "none are ready" and "this query cannot match anything".
APPROVAL_TOTAL=$(curl -s "$BASE/api/v1/products?status=Approval&pageSize=1" | jsonq 'String(d.totalCount)')
READY_EXPECT=0
APPROVAL_PAGES=$(( (APPROVAL_TOTAL + 99) / 100 ))
for ((P=1; P<=APPROVAL_PAGES; P++)); do
  N=$(curl -s "$BASE/api/v1/products?status=Approval&pageSize=100&page=$P" \
      | jsonq 'String(d.items.filter(p=>p.completenessPct>=90).length)')
  READY_EXPECT=$(( READY_EXPECT + N ))
done
check "dash: ready-to-publish recounted from the grid" \
  "$(printf '%s' "$DASH" | jsonq 'd.kpis.find(k=>k.code==="ready_to_publish").value.replace(/,/g,"")')" \
  "$READY_EXPECT"

# And the seed must actually contain some, otherwise the check above passes by
# comparing two zeroes and proves nothing (R-10).
check "dash: the seed has products to publish" \
  "$([ "${READY_EXPECT:-0}" -gt 0 ] && echo yes || echo no)" "yes"

# The quality bands must partition the catalogue -- every product in exactly one
# band. If they do not sum to the total, some products are being counted twice
# or not at all, and the donut beside them is drawn from the same numbers.
check "dash: quality bands partition the catalogue" \
  "$(printf '%s' "$DASH" | jsonq 'String(d.qualityBands.reduce((n,b)=>n+b.productCount,0))')" "$GRID_TOTAL"
check "dash: all four bands returned" "$(printf '%s' "$DASH" | jsonq 'd.qualityBands.map(b=>b.code).join(",")')" "A,B,C,D"

# Same for the pipeline: a product is in exactly one workflow state.
#
# This one earned its keep immediately: it read 251 against a catalogue of 501,
# because the pipeline was built from three hand-written stage names against a
# four-state domain, one of which was misspelt. Both faults were invisible on
# screen — the panel looked complete, it was simply missing half the products.
check "dash: pipeline partitions the catalogue" \
  "$(printf '%s' "$DASH" | jsonq 'String(d.workflowPipeline.reduce((n,s)=>n+s.productCount,0))')" "$GRID_TOTAL"

# The stages themselves, named. Summing correctly is not enough: three stages
# could sum to the total while a fourth was folded into one of them.
check "dash: pipeline names the domain's four states" \
  "$(printf '%s' "$DASH" | jsonq 'd.workflowPipeline.map(s=>s.state).join(",")')" \
  "Draft,Review,Approval,Published"

# Every stage cross-checked against the grid's own filter. Two independent code
# paths over the same column: if the cockpit ever starts counting a state the
# grid disagrees about, one of them is lying and this says which.
for STATE in Draft Review Approval Published; do
  check "dash: $STATE count agrees with the grid" \
    "$(printf '%s' "$DASH" | jsonq 'String(d.workflowPipeline.find(s=>s.state===a[0]).productCount)' "$STATE")" \
    "$(curl -s "$BASE/api/v1/products?status=$STATE&pageSize=1" | jsonq 'String(d.totalCount)')"
done

# Locale coverage must match what the locales endpoint says exists -- a cockpit
# that silently drops a configured language is worse than one that shows it at
# 0%, because 0% is actionable and absence is invisible.
check "dash: one row per configured locale" \
  "$(printf '%s' "$DASH" | jsonq 'String(d.localeCoverage.length)')" \
  "$(curl -s "$BASE/api/v1/locales" | jsonq 'String(d.length)')"
check "dash: base locale flagged once" \
  "$(printf '%s' "$DASH" | jsonq 'String(d.localeCoverage.filter(l=>l.isBase).length)')" "1"

# The French pack left one product in five untranslated on purpose (Sprint 19),
# so coverage must be strictly between nothing and everything. Pinning both ends
# catches a seed that silently translated everything as well as one that
# translated nothing.
# jsonq evaluates a single EXPRESSION, so no statements and no `const` — the
# first version of this line declared one and every run reported
# "ERR:Unexpected token 'const'", which the check dutifully compared against
# "true" and failed. A broken assertion is at least loud; the same mistake in a
# check expecting the error string would have passed forever.
check "dash: fr coverage is partial" \
  "$(printf '%s' "$DASH" | jsonq 'String(d.localeCoverage.some(l=>l.code==="fr-FR" && l.percent>0 && l.percent<100))')" "true"

# Completeness by area is derived from attribute groups, so it must name groups
# that exist rather than a hard-coded list, and every percentage must be a
# percentage.
check "dash: areas are real groups"  "$(printf '%s' "$DASH" | jsonq 'String(d.completenessByArea.length > 0 && d.completenessByArea.every(a=>a.groupCode && a.label && a.label.length>0))')" "true"
check "dash: percentages in range"   "$(printf '%s' "$DASH" | jsonq 'String(d.completenessByArea.every(a=>a.percent>=0 && a.percent<=100))')" "true"

# Activity comes from the audit trail this run has been writing to all along.
check "dash: activity reflects this run" "$(printf '%s' "$DASH" | jsonq 'String(d.recentActivity.length > 0)')" "true"
check "dash: activity names products"    "$(printf '%s' "$DASH" | jsonq 'String(d.recentActivity.every(a=>a.productCode && a.productCode.length>0))')" "true"

# The panels this build deliberately does not draw are part of the contract, so
# the screen can say WHY rather than leaving a gap the reader interprets as
# "zero". An empty list here would mean the cockpit had quietly started claiming
# completeness it does not have.
check "dash: omissions are declared"  "$(printf '%s' "$DASH" | jsonq 'String(d.omittedPanels.length >= 4 && d.omittedPanels.every(p=>p.reason && p.reason.length > 20))')" "true"

# Labels follow the reader, like every other screen (ADR-009).
AREA_EN=$(curl -s "$BASE/api/v1/dashboard?locale=$SEED_TARGET_LOCALE" | jsonq 'd.completenessByArea.map(a=>a.label).join("~")')
AREA_FR=$(curl -s "$BASE/api/v1/dashboard?locale=fr-FR" | jsonq 'd.completenessByArea.map(a=>a.label).join("~")')
check "dash: area labels follow the locale" "$([ -n "$AREA_FR" ] && [ "$AREA_FR" != "$AREA_EN" ] && echo differ || echo same)" "differ"

# ...but the FIGURES do not. A catalogue does not become more complete because
# somebody switched language, and this is the assertion that would catch a
# locale filter leaking into an aggregate.
PCT_EN=$(curl -s "$BASE/api/v1/dashboard?locale=$SEED_TARGET_LOCALE" | jsonq 'd.completenessByArea.map(a=>String(a.percent)).join("~")')
PCT_FR=$(curl -s "$BASE/api/v1/dashboard?locale=fr-FR" | jsonq 'd.completenessByArea.map(a=>String(a.percent)).join("~")')
check "dash: figures do NOT follow the locale" "$([ "$PCT_EN" = "$PCT_FR" ] && echo same || echo moved)" "same"

check "dash: unknown locale -> 400"  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/dashboard?locale=zz-ZZ")" "400"

echo

echo
echo "== Categories (Sprint 22) =="

TREE=$(curl -s "$BASE/api/v1/categories/tree")

check "cat: tree served" "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/categories/tree")" "200"
check "cat: tree has roots" "$(printf '%s' "$TREE" | jsonq 'String(d.length > 0)')" "true"

# The tree is NESTED, not a flat list with depth numbers. A flat payload would
# render identically on the first screen and then get the indentation wrong the
# moment a node has more than one parent generation.
check "cat: children are nested" \
  "$(printf '%s' "$TREE" | jsonq 'String(d.some(n=>Array.isArray(n.children) && n.children.length>0))')" "true"

# Every node's subtree count must equal its own products plus its children's
# subtree counts. Recomputed here from the payload: this is the assertion that
# fails if the ltree containment ever stops meaning what the screen says it
# means, and it cannot pass by accident on a tree of this depth.
check "cat: subtree counts add up at every level" \
  "$(printf '%s' "$TREE" | jsonq 'String((function walk(ns){return ns.every(n=>{const kids=n.children||[];const sum=n.directProductCount+kids.reduce((t,c)=>t+c.subtreeProductCount,0);return sum===n.subtreeProductCount && walk(kids)})})(d))')" \
  "true"

# ...and the roots together must account for the whole catalogue. Cross-checked
# against the products API rather than against another figure from the same
# query: a tree that agrees with itself and disagrees with the catalogue is the
# failure this catches.
GRID_TOTAL=$(curl -s "$BASE/api/v1/products?pageSize=1" | jsonq 'String(d.totalCount)')
check "cat: roots account for every product" \
  "$(printf '%s' "$TREE" | jsonq 'String(d.reduce((t,n)=>t+n.subtreeProductCount,0))')" "$GRID_TOTAL"

# The taxonomy does not change shape when the reader changes language. Only the
# labels move. A tree that shrank in French would make products unreachable.
TREE_FR=$(curl -s "$BASE/api/v1/categories/tree?locale=fr-FR")
COUNT_EN=$(printf '%s' "$TREE" | jsonq 'String((function c(ns){return ns.reduce((t,n)=>t+1+c(n.children||[]),0)})(d))')
COUNT_FR=$(printf '%s' "$TREE_FR" | jsonq 'String((function c(ns){return ns.reduce((t,n)=>t+1+c(n.children||[]),0)})(d))')
check "cat: same node count in every language" "$COUNT_FR" "$COUNT_EN"
check "cat: same product totals in every language" \
  "$(printf '%s' "$TREE_FR" | jsonq 'String(d.reduce((t,n)=>t+n.subtreeProductCount,0))')" "$GRID_TOTAL"

check "cat: unknown locale -> 400" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/categories/tree?locale=zz-ZZ")" "400"

# --- one node in detail ------------------------------------------------------
# The node is picked FROM THE TREE, not written out here: a hard-coded category
# code is exactly what made perf-check report green while measuring 404s (R-10).
#
# It is picked by PROBING, and that matters. The first version took the first
# leaf with any products at all and landed on a one-product node — so "template
# rows carry a real denominator", "coverage is a percentage" and "required count
# matches the template" all passed over an empty array, and two more checks
# passed by comparing one jsonq error string with an identical one. Six green
# lines measuring nothing: the exact shape of R-10 and R-19.
CANDIDATES=$(printf '%s' "$TREE" | jsonq '(function f(ns,acc){for(const n of ns){const kids=n.children||[];if(kids.length===0 && n.subtreeProductCount>0)acc.push(n);f(kids,acc)}return acc})(d,[]).sort((x,y)=>y.subtreeProductCount-x.subtreeProductCount).slice(0,8).map(n=>n.code).join(" ")')

NODE=""
DETAIL=""
for CANDIDATE in $CANDIDATES; do
  BODY=$(curl -s "$BASE/api/v1/categories/$CANDIDATE")
  if [ "$(printf '%s' "$BODY" | jsonq 'String((d.template||[]).length > 0)')" = "true" ]; then
    NODE="$CANDIDATE"
    DETAIL="$BODY"
    break
  fi
done

check "cat: found a leaf carrying an attribute template" \
  "$([ -n "$NODE" ] && echo yes || echo no)" "yes"

# Everything below reads $DETAIL. Without this guard an empty body turns every
# one of them into "ERR:Unexpected end of JSON input", and any check whose
# EXPECTED side is also derived from $DETAIL then compares one error with an
# identical error and passes. One honest failure beats twelve misleading ones
# plus two false greens.
if [ -z "$DETAIL" ]; then
  echo "  SKIP  cat: detail assertions — no node with a template to test against"
else

NODE_COUNT=$(printf '%s' "$TREE" | jsonq '(function f(ns){for(const n of ns){if(n.code===a[0])return String(n.subtreeProductCount);const c=f(n.children||[]);if(c)return c}return ""})(d)' "$NODE")

check "cat: that node has enough products to measure over" \
  "$([ "${NODE_COUNT:-0}" -gt 1 ] && echo yes || echo no)" "yes"
check "cat: its template is not empty" \
  "$(printf '%s' "$DETAIL" | jsonq 'String(d.template.length > 0)')" "true"

# Provenance, and it is not decoration. No node in the client's catalogue
# declares an attribute set, so every template on this screen is inferred from
# the products beneath it. If this ever comes back "declared" the seed has
# changed and the screen is saying something different from what it used to.
check "cat: template provenance is stated" \
  "$(printf '%s' "$DETAIL" | jsonq 'String(["declared","in-use","mixed","none"].includes(d.templateSource))')" "true"
check "cat: an inferred template names the set it came from" \
  "$(printf '%s' "$DETAIL" | jsonq 'String(d.templateSource!=="in-use" || (d.setsInUse.length===1 && d.attributeSetCode===d.setsInUse[0].code))')" "true"
check "cat: sets in use account for the branch's products" \
  "$(printf '%s' "$DETAIL" | jsonq 'String(d.setsInUse.reduce((t,s)=>t+s.productCount,0))')" "$NODE_COUNT"

check "cat: detail served"        "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/categories/$NODE")" "200"
check "cat: detail code matches"  "$(printf '%s' "$DETAIL" | jsonq 'd.code')" "$NODE"
check "cat: detail agrees with the tree on product count" \
  "$(printf '%s' "$DETAIL" | jsonq 'String(d.subtreeProductCount)')" "$NODE_COUNT"

# The grid filters by category with subtree semantics, so filtering to this node
# must return exactly the number the category screen promised. Two code paths
# over the same relationship, compared.
NODE_ID=$(printf '%s' "$DETAIL" | jsonq 'String(d.id)')
check "cat: grid filtered to this node returns the same count" \
  "$(curl -s "$BASE/api/v1/products?categoryId=$NODE_ID&pageSize=1" | jsonq 'String(d.totalCount)')" \
  "$NODE_COUNT"

# One row per configured locale, including languages with nothing in them —
# that absence is the fact the panel exists to show.
check "cat: one label row per configured locale" \
  "$(printf '%s' "$DETAIL" | jsonq 'String(d.labels.length)')" \
  "$(curl -s "$BASE/api/v1/locales" | jsonq 'String(d.length)')"
check "cat: exactly one base locale flagged" \
  "$(printf '%s' "$DETAIL" | jsonq 'String(d.labels.filter(l=>l.isBase).length)')" "1"

# Attribute template: coverage is a percentage of a stated denominator, and the
# denominator must be the node's own product count. A coverage figure computed
# over the whole catalogue would look plausible and mean nothing.
check "cat: template rows carry a real denominator" \
  "$(printf '%s' "$DETAIL" | jsonq 'String(d.template.length===0 || d.template.every(t=>t.productsInScope===d.subtreeProductCount))')" "true"
check "cat: coverage is a percentage" \
  "$(printf '%s' "$DETAIL" | jsonq 'String(d.template.every(t=>t.coveragePct>=0 && t.coveragePct<=100))')" "true"
check "cat: required count matches the template" \
  "$(printf '%s' "$DETAIL" | jsonq 'String(d.requiredAttributeCount)')" \
  "$(printf '%s' "$DETAIL" | jsonq 'String(d.template.filter(t=>t.isRequired).length)')"

# Labels follow the reader; figures do not (ADR-009), same rule as every other
# screen. Compared on the SAME node so nothing else can explain a difference.
DET_FR=$(curl -s "$BASE/api/v1/categories/$NODE?locale=fr-FR")
check "cat: figures do NOT follow the locale" \
  "$(printf '%s' "$DET_FR" | jsonq 'String(d.subtreeProductCount)')" "$NODE_COUNT"
check "cat: template size does NOT follow the locale" \
  "$(printf '%s' "$DET_FR" | jsonq 'String(d.template.length)')" \
  "$(printf '%s' "$DETAIL" | jsonq 'String(d.template.length)')"

check "cat: unknown code -> 404" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/categories/no-such-node-xyz")" "404"

fi   # end of the "we have a node with a template" guard


echo
echo "== Attribute Library (Sprint 23) =="

LIB=$(curl -s "$BASE/api/v1/attributes")

check "attr: library served" "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/attributes")" "200"
check "attr: library is not empty" "$(printf '%s' "$LIB" | jsonq 'String(d.attributes.length > 0)')" "true"
check "attr: totalAttributes matches the list" \
  "$(printf '%s' "$LIB" | jsonq 'String(d.totalAttributes)')" \
  "$(printf '%s' "$LIB" | jsonq 'String(d.attributes.length)')"

# Groups come from attribute_group, not from the attributes on screen. If they
# were inferred from the rows, a group nobody has put anything in would vanish —
# and "this group is empty" is a fact somebody needs.
check "attr: groups include empty ones" \
  "$(printf '%s' "$LIB" | jsonq 'String(d.groups.length >= new Set(d.attributes.map(a=>a.groupCode)).size)')" "true"
check "attr: every group count matches the attributes in it" \
  "$(printf '%s' "$LIB" | jsonq 'String(d.groups.every(g=>g.attributeCount === d.attributes.filter(a=>a.groupCode===g.code).length))')" "true"

# Coverage is measured against the products the attribute APPLIES to. These two
# assertions are what stop that denominator quietly becoming "the whole
# catalogue", which would make every specialised attribute look abandoned.
check "attr: filled never exceeds applicable" \
  "$(printf '%s' "$LIB" | jsonq 'String(d.attributes.every(a=>a.productsWithValue <= a.appliesToProducts))')" "true"
check "attr: coverage is that ratio, not another one" \
  "$(printf '%s' "$LIB" | jsonq 'String(d.attributes.every(a=>a.appliesToProducts===0 ? a.coveragePct===0 : a.coveragePct===Math.round(100*a.productsWithValue/a.appliesToProducts)))')" "true"
check "attr: some attribute is actually filled in" \
  "$(printf '%s' "$LIB" | jsonq 'String(d.attributes.some(a=>a.productsWithValue > 0))')" "true"

# An attribute in a set every product uses must apply to every product. Checked
# against the products API, not against another figure from the same query.
GRID_TOTAL=$(curl -s "$BASE/api/v1/products?pageSize=1" | jsonq 'String(d.totalCount)')
# NOTE the callback parameter: x, not a. jsonq passes its extra arguments as
# `a`, so `a=>...a[0]` shadows the argument array and reads index 0 of the
# attribute object instead — undefined, then NaN. That exact mistake shipped in
# Sprint 19 and made an assertion compare undefined with undefined and pass.
check "attr: applicable count never exceeds the catalogue" \
  "$(printf '%s' "$LIB" | jsonq 'String(d.attributes.every(x=>x.appliesToProducts <= Number(a[0])))' "$GRID_TOTAL")" "true"

check "attr: unknown locale -> 400" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/attributes?locale=zz-ZZ")" "400"

# Labels follow the reader; the library does not. An attribute exists in French
# whether or not anyone has written its French label — and the missing label is
# the thing this screen is for finding.
LIB_FR=$(curl -s "$BASE/api/v1/attributes?locale=fr-FR")
check "attr: same attributes in every language" \
  "$(printf '%s' "$LIB_FR" | jsonq 'String(d.attributes.length)')" \
  "$(printf '%s' "$LIB" | jsonq 'String(d.attributes.length)')"
check "attr: coverage does NOT follow the locale" \
  "$(printf '%s' "$LIB_FR" | jsonq 'd.attributes.map(a=>a.code+":"+a.coveragePct).join(",")')" \
  "$(printf '%s' "$LIB" | jsonq 'd.attributes.map(a=>a.code+":"+a.coveragePct).join(",")')"

# --- one attribute in detail -------------------------------------------------
# Chosen from the payload, and chosen to be a LIST attribute that is actually in
# use: options and usage counts are the two things most likely to be silently
# empty, and an attribute with neither would let every assertion below pass over
# nothing (R-10).
ATTR=$(printf '%s' "$LIB" | jsonq '(d.attributes.filter(a=>a.optionCount>0 && a.productsWithValue>0).sort((x,y)=>y.productsWithValue-x.productsWithValue)[0]||{}).code || ""')

check "attr: found a list attribute in use to test against" \
  "$([ -n "$ATTR" ] && echo yes || echo no)" "yes"

if [ -z "$ATTR" ]; then
  echo "  SKIP  attr: detail assertions — no list attribute with values"
else

DET=$(curl -s "$BASE/api/v1/attributes/$ATTR")

check "attr: detail served"       "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/attributes/$ATTR")" "200"
check "attr: detail code matches" "$(printf '%s' "$DET" | jsonq 'd.code')" "$ATTR"
check "attr: detail agrees with the list on coverage" \
  "$(printf '%s' "$DET" | jsonq 'String(d.coveragePct)')" \
  "$(printf '%s' "$LIB" | jsonq 'String(d.attributes.find(x=>x.code===a[0]).coveragePct)' "$ATTR")"

check "attr: one label row per configured locale" \
  "$(printf '%s' "$DET" | jsonq 'String(d.labels.length)')" \
  "$(curl -s "$BASE/api/v1/locales" | jsonq 'String(d.length)')"
check "attr: exactly one base locale flagged" \
  "$(printf '%s' "$DET" | jsonq 'String(d.labels.filter(l=>l.isBase).length)')" "1"

check "attr: options are returned"  "$(printf '%s' "$DET" | jsonq 'String(d.options.length > 0)')" "true"
check "attr: option count agrees with the list" \
  "$(printf '%s' "$DET" | jsonq 'String(d.options.length)')" \
  "$(printf '%s' "$LIB" | jsonq 'String(d.attributes.find(x=>x.code===a[0]).optionCount)' "$ATTR")"

# Per-option usage must not exceed the products carrying the attribute at all.
# This is the assertion that catches a usage count computed without the
# attribute_id predicate — which would return a plausible, larger number.
check "attr: no option is used by more products than the attribute is" \
  "$(printf '%s' "$DET" | jsonq 'String(d.options.every(o=>o.usedByProducts <= d.productsWithValue))')" "true"
check "attr: option usage is not uniformly zero" \
  "$(printf '%s' "$DET" | jsonq 'String(d.options.some(o=>o.usedByProducts > 0))')" "true"

check "attr: set membership is stated" \
  "$(printf '%s' "$DET" | jsonq 'String(d.sets.length > 0)')" "true"
check "attr: set count agrees with the list" \
  "$(printf '%s' "$DET" | jsonq 'String(d.sets.length)')" \
  "$(printf '%s' "$LIB" | jsonq 'String(d.attributes.find(x=>x.code===a[0]).setCount)' "$ATTR")"

check "attr: unknown code -> 404" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/attributes/no-such-attribute-xyz")" "404"

fi   # end of the "we have a list attribute in use" guard


echo
echo "== Translation Workbench (Sprint 24) =="

WB=$(curl -s "$BASE/api/v1/translations")

check "tr: workbench served" "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/translations")" "200"
check "tr: one row per configured locale" \
  "$(printf '%s' "$WB" | jsonq 'String(d.locales.length)')" \
  "$(curl -s "$BASE/api/v1/locales" | jsonq 'String(d.length)')"
check "tr: exactly one base locale" \
  "$(printf '%s' "$WB" | jsonq 'String(d.locales.filter(l=>l.isBase).length)')" "1"

# Every locale's own numbers must add up before anything else is believed:
# a product either has text in a language or does not.
check "tr: withText + missing = total, per locale" \
  "$(printf '%s' "$WB" | jsonq 'String(d.locales.every(l=>l.withText + l.missing === l.totalProducts))')" "true"
check "tr: approved + awaitingReview = withText, per locale" \
  "$(printf '%s' "$WB" | jsonq 'String(d.locales.every(l=>l.approved + l.awaitingReview === l.withText))')" "true"
check "tr: percent is that ratio" \
  "$(printf '%s' "$WB" | jsonq 'String(d.locales.every(l=>l.totalProducts===0 ? l.percentTranslated===0 : l.percentTranslated===Math.round(100*l.withText/l.totalProducts)))')" "true"

# CROSS-CHECKED against the dashboard, which counts translated products with a
# separately written query. Two screens disagreeing about translation coverage
# would make both worthless, and nobody would know which to believe.
DASH=$(curl -s "$BASE/api/v1/dashboard")
check "tr: coverage agrees with the dashboard" \
  "$(printf '%s' "$WB" | jsonq 'd.locales.map(l=>l.code+":"+l.withText).sort().join(",")')" \
  "$(printf '%s' "$DASH" | jsonq 'd.localeCoverage.map(l=>l.code+":"+l.translatedProducts).sort().join(",")')"
check "tr: total agrees with the grid" \
  "$(printf '%s' "$WB" | jsonq 'String(d.totalProducts)')" \
  "$(curl -s "$BASE/api/v1/products?pageSize=1" | jsonq 'String(d.totalCount)')"

# The base locale is the source of translations, not a target for them.
check "tr: base locale has nothing missing" \
  "$(printf '%s' "$WB" | jsonq 'String(d.locales.find(l=>l.isBase).missing)')" "0"
check "tr: base locale is never outdated against itself" \
  "$(printf '%s' "$WB" | jsonq 'String(d.locales.find(l=>l.isBase).outdated)')" "0"

check "tr: omissions are declared" \
  "$(printf '%s' "$WB" | jsonq 'String(d.omittedPanels.length >= 2 && d.omittedPanels.every(p=>p.reason.length > 20))')" "true"

# --- the queue ---------------------------------------------------------------
# Target chosen from the payload: the locale with the most missing products is
# the one whose queue is worth asserting against.
TARGET=$(printf '%s' "$WB" | jsonq 'd.locales.filter(l=>!l.isBase).sort((x,y)=>y.missing-x.missing)[0].code')
TARGET_MISSING=$(printf '%s' "$WB" | jsonq 'String(d.locales.find(l=>l.code===a[0]).missing)' "$TARGET")

check "tr: picked a target with missing translations" \
  "$([ "${TARGET_MISSING:-0}" -gt 0 ] && echo yes || echo no)" "yes"

check "tr: queue served" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/translations/queue?target=$TARGET")" "200"

# The queue's own total for state=missing must equal the summary's missing
# count. Two queries, one number — this is the assertion that catches a filter
# drifting away from the figure that sent the user here.
check "tr: missing queue matches the summary" \
  "$(curl -s "$BASE/api/v1/translations/queue?target=$TARGET&state=missing&pageSize=1" | jsonq 'String(d.totalCount)')" \
  "$TARGET_MISSING"
check "tr: every row in the missing queue really is missing" \
  "$(curl -s "$BASE/api/v1/translations/queue?target=$TARGET&state=missing&pageSize=100" | jsonq 'String(d.items.every(i=>i.state==="missing" && i.targetUpdatedAtUtc===null))')" "true"

# state=all must be the whole catalogue: the queue is a filter over products,
# not a separate list that could drift out of step with one.
check "tr: state=all covers the catalogue" \
  "$(curl -s "$BASE/api/v1/translations/queue?target=$TARGET&state=all&pageSize=1" | jsonq 'String(d.totalCount)')" \
  "$(printf '%s' "$WB" | jsonq 'String(d.totalProducts)')"

# attention is the UNION of missing, outdated and awaiting review, so it is
# bounded by the largest of them below and their sum above. Asserted as bounds
# rather than as an equality: a row can be both outdated and unapproved, and an
# equality would be passing here by luck of the seed rather than by arithmetic.
ATTENTION=$(curl -s "$BASE/api/v1/translations/queue?target=$TARGET&state=attention&pageSize=1" | jsonq 'String(d.totalCount)')
check "tr: attention is the union of the three problem states" \
  "$(printf '%s' "$WB" | jsonq '(function(){const l=d.locales.find(x=>x.code===a[0]);const n=Number(a[1]);const lo=Math.max(l.missing,l.outdated,l.awaitingReview);const hi=l.missing+l.outdated+l.awaitingReview;return String(n>=lo && n<=hi)})()' "$TARGET" "$ATTENTION")" \
  "true"
check "tr: and there is actually work in it" \
  "$([ "${ATTENTION:-0}" -gt 0 ] && echo yes || echo no)" "yes"

check "tr: paging is honest" \
  "$(curl -s "$BASE/api/v1/translations/queue?target=$TARGET&state=all&page=2&pageSize=10" | jsonq 'String(d.items.length===10 && d.page===2)')" "true"
check "tr: pageSize over the cap is rejected" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/translations/queue?target=$TARGET&pageSize=500")" "400"
check "tr: unknown target -> 400" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/translations/queue?target=zz-ZZ")" "400"
check "tr: unknown state -> 400" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/translations/queue?target=$TARGET&state=sideways")" "400"

# Translating INTO the base locale is meaningless — it is the source. Rejected
# rather than answered with an empty queue, which would read as "nothing left
# to translate".
BASE_LOCALE=$(printf '%s' "$WB" | jsonq 'd.baseLocaleCode')
check "tr: base locale as target -> 400" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/translations/queue?target=$BASE_LOCALE")" "400"

# Labels follow the reader; the queue does not. Asking for the same queue in a
# different reading language must not change which products need work.
check "tr: the queue does not change with the reading language" \
  "$(curl -s "$BASE/api/v1/translations/queue?target=$TARGET&state=missing&pageSize=1&locale=fr-FR" | jsonq 'String(d.totalCount)')" \
  "$TARGET_MISSING"

# --- outdated, proven end to end ---------------------------------------------
# "Outdated" means the base text was edited after the translation. A figure that
# only ever reads zero is indistinguishable from a broken query (R-19), so this
# does not assert the number — it MOVES it, and then checks it moved.
#
# The product picked already has text in the target locale, so it cannot be
# counted as missing, and the only thing that can change its state is the edit.
VICTIM=$(curl -s "$BASE/api/v1/translations/queue?target=$TARGET&state=all&pageSize=100" | jsonq 'String((d.items.find(i=>i.state!=="missing")||{}).productCode||"")')
check "tr: found a translated product to age" \
  "$([ -n "$VICTIM" ] && echo yes || echo no)" "yes"

OUTDATED_BEFORE=$(curl -s "$BASE/api/v1/translations/queue?target=$TARGET&state=outdated&pageSize=1" | jsonq 'String(d.totalCount)')

# Rename the base-locale text. ProductTranslation.Rename stamps updated_at, so
# every translation of this product becomes older than its source.
# The payload is built from the product's OWN current values with only the name
# changed. UpdateProductGeneralRequest takes the whole General panel, so a
# partial body would null the owner, GTIN and SAP number as a side effect — and
# rowVersion is an int, not a string, so it must not be quoted.
#
# The route is PUT /products/{code}. The first draft of this block invented
# /products/{code}/general, got a 404, and the two assertions after it then
# reported "outdated did not move" — which was true, and had nothing to do with
# the query they were written to test. Three mistakes in one edit call, all
# caught by asserting the edit's own status code rather than assuming it worked.
VROW=$(curl -s "$BASE/api/v1/products/$VICTIM")
VPAYLOAD=$(printf '%s' "$VROW" | jsonq 'JSON.stringify({name:d.name+" (aged)",ownerDisplayName:d.ownerDisplayName??null,gtin:d.gtin??null,sapMaterialNo:d.sapMaterialNo??null,rowVersion:d.rowVersion})')

check "tr: the ageing edit was accepted" \
  "$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/v1/products/$VICTIM" \
      -H 'Content-Type: application/json' -d "$VPAYLOAD")" "200"

OUTDATED_AFTER=$(curl -s "$BASE/api/v1/translations/queue?target=$TARGET&state=outdated&pageSize=1" | jsonq 'String(d.totalCount)')

check "tr: editing the source makes its translation outdated" \
  "$([ "${OUTDATED_AFTER:-0}" -gt "${OUTDATED_BEFORE:-0}" ] && echo rose || echo "did not move: $OUTDATED_BEFORE -> $OUTDATED_AFTER")" "rose"
check "tr: and it is that product that turned outdated" \
  "$(curl -s "$BASE/api/v1/translations/queue?target=$TARGET&state=outdated&pageSize=100" | jsonq 'String(d.items.some(i=>i.productCode===a[0]))' "$VICTIM")" "true"
check "tr: an outdated row keeps its stored status" \
  "$(curl -s "$BASE/api/v1/translations/queue?target=$TARGET&state=outdated&pageSize=100" | jsonq 'String((d.items.find(i=>i.productCode===a[0])||{}).translationStatus !== null)' "$VICTIM")" "true"

# ...and the summary must agree with the queue about it, since they are separate
# queries over the same definition.
check "tr: the summary sees the same outdated count" \
  "$(curl -s "$BASE/api/v1/translations" | jsonq 'String(d.locales.find(l=>l.code===a[0]).outdated)' "$TARGET")" \
  "$OUTDATED_AFTER"


echo
echo "== Digital Assets library (Sprint 25) =="

ASUM=$(curl -s "$BASE/api/v1/assets/summary")

check "dam: summary served" "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/assets/summary")" "200"
check "dam: library is not empty" "$(printf '%s' "$ASUM" | jsonq 'String(d.totalAssets > 0)')" "true"

# The type breakdown must account for every asset. A facet row that does not add
# up means either a type is missing from the row or the total is counting
# something the facets are not.
check "dam: type counts add up to the total" \
  "$(printf '%s' "$ASUM" | jsonq 'String(d.byType.reduce((n,t)=>n+t.count,0))')" \
  "$(printf '%s' "$ASUM" | jsonq 'String(d.totalAssets)')"
check "dam: BIM and CAD are present as types" \
  "$(printf '%s' "$ASUM" | jsonq 'String(["BIM","CAD"].every(t=>d.byType.some(x=>x.assetType===t && x.count>0)))')" "true"
check "dam: omissions are declared" \
  "$(printf '%s' "$ASUM" | jsonq 'String(d.omittedPanels.length >= 2 && d.omittedPanels.every(p=>p.reason.length > 20))')" "true"
# Sprint 26 shipped upload and renditions, so they must have LEFT this list. A
# panel that keeps declaring a delivered capability missing is lying in the
# other direction.
check "dam: shipped capabilities are no longer declared missing" \
  "$(printf '%s' "$ASUM" | jsonq 'String(d.omittedPanels.every(p=>!/upload|thumbnail|rendition/i.test(p.panel)))')" "true"

# --- the list ----------------------------------------------------------------
ALIST=$(curl -s "$BASE/api/v1/assets?pageSize=100")
check "dam: list served" "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/assets")" "200"
check "dam: list total agrees with the summary" \
  "$(printf '%s' "$ALIST" | jsonq 'String(d.totalCount)')" \
  "$(printf '%s' "$ASUM" | jsonq 'String(d.totalAssets)')"

# Every URL must name the rendition it points at, and in this build that is
# always the original: nothing generates derivatives. The seed used to claim a
# 'web' rendition per image whose URI WAS the original, which is why this is
# asserted rather than assumed.
check "dam: every item names the rendition it serves" \
  "$(printf '%s' "$ALIST" | jsonq 'String(d.items.every(i=>typeof i.servedRendition === "string" && i.servedRendition.length > 0))')" "true"
check "dam: no item claims a web rendition this build cannot produce" \
  "$(printf '%s' "$ALIST" | jsonq 'String(d.items.every(i=>i.servedRendition !== "web"))')" "true"

# Filtering by type must partition the library: the sum of every facet's own
# filtered total has to equal the unfiltered total. Two code paths, one number.
TYPE_SUM=0
for T in $(printf '%s' "$ASUM" | jsonq 'd.byType.map(t=>t.assetType).join(" ")'); do
  N=$(curl -s "$BASE/api/v1/assets?type=$T&pageSize=1" | jsonq 'String(d.totalCount)')
  TYPE_SUM=$(( TYPE_SUM + N ))
done
check "dam: type filters partition the library" \
  "$TYPE_SUM" "$(printf '%s' "$ASUM" | jsonq 'String(d.totalAssets)')"

# orphan + linked must also partition it — an asset is referenced or it is not.
ORPHANS=$(curl -s "$BASE/api/v1/assets?usage=orphan&pageSize=1" | jsonq 'String(d.totalCount)')
LINKED=$(curl -s "$BASE/api/v1/assets?usage=linked&pageSize=1" | jsonq 'String(d.totalCount)')
check "dam: orphan + linked = all" \
  "$(( ORPHANS + LINKED ))" "$(printf '%s' "$ASUM" | jsonq 'String(d.totalAssets)')"
check "dam: the orphan count agrees with the summary" \
  "$ORPHANS" "$(printf '%s' "$ASUM" | jsonq 'String(d.orphanCount)')"
check "dam: every orphan really is unlinked" \
  "$(curl -s "$BASE/api/v1/assets?usage=orphan&pageSize=100" | jsonq 'String(d.items.every(i=>i.linkCount === 0))')" "true"
check "dam: every linked asset really is linked" \
  "$(curl -s "$BASE/api/v1/assets?usage=linked&pageSize=100" | jsonq 'String(d.items.every(i=>i.linkCount > 0))')" "true"

check "dam: unknown type -> 400" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/assets?type=HOLOGRAM")" "400"
check "dam: unknown usage -> 400" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/assets?usage=sideways")" "400"
check "dam: pageSize over the cap -> 400" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/assets?pageSize=500")" "400"
check "dam: unknown locale -> 400" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/assets?locale=zz-ZZ")" "400"

# --- one asset in detail -----------------------------------------------------
# Picked from the payload, and picked to be one that is actually LINKED, so the
# usage table below has something to assert against rather than passing over an
# empty array (R-10).
AID=$(printf '%s' "$ALIST" | jsonq 'String((d.items.find(i=>i.linkCount>0 && i.assetType==="IMAGE")||{}).publicId||"")')
check "dam: found a linked image to test against" \
  "$([ -n "$AID" ] && echo yes || echo no)" "yes"

if [ -z "$AID" ]; then
  echo "  SKIP  dam: detail assertions — no linked image in the library"
else

ADET=$(curl -s "$BASE/api/v1/assets/$AID")
check "dam: detail served"       "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/assets/$AID")" "200"
check "dam: detail id matches"   "$(printf '%s' "$ADET" | jsonq 'd.publicId')" "$AID"

check "dam: renditions are labelled honestly" \
  "$(printf '%s' "$ADET" | jsonq 'String(d.renditions.length > 0 && d.renditions.every(r=>["original","thumb","web","print"].includes(r.role)))')" "true"
check "dam: the original rendition points at the stored key" \
  "$(printf '%s' "$ADET" | jsonq 'String((d.renditions.find(r=>r.role==="original")||{}).uri === d.storageKey)')" "true"
# Format is read off the file name rather than assumed to be jpg — the export
# contains .png and .tif too.
check "dam: rendition format matches the file extension" \
  "$(printf '%s' "$ADET" | jsonq 'String(d.renditions.filter(r=>r.format).every(r=>d.fileName.toLowerCase().endsWith("."+r.format)))')" "true"

check "dam: one text row per configured locale" \
  "$(printf '%s' "$ADET" | jsonq 'String(d.texts.length)')" \
  "$(curl -s "$BASE/api/v1/locales" | jsonq 'String(d.length)')"
check "dam: exactly one base locale flagged" \
  "$(printf '%s' "$ADET" | jsonq 'String(d.texts.filter(t=>t.isBase).length)')" "1"

check "dam: usage is listed"     "$(printf '%s' "$ADET" | jsonq 'String(d.usedBy.length > 0)')" "true"
check "dam: usage count agrees with the list" \
  "$(printf '%s' "$ADET" | jsonq 'String(d.usedBy.length)')" \
  "$(printf '%s' "$ALIST" | jsonq 'String(d.items.find(i=>i.publicId===a[0]).linkCount)' "$AID")"

# ...and the product it names must really carry this asset. Cross-checked
# against the product's own assets endpoint, which is a separately written query
# over the same link table.
#
# Matched on the URL rather than on an id: ProductAssetsDto exposes the internal
# asset id, not the public one, and its shape is {media[], documents[]} rather
# than a bare list. The first draft of this check assumed `items` and would have
# failed for a reason that had nothing to do with the data.
UPROD=$(printf '%s' "$ADET" | jsonq 'd.usedBy[0].productCode')
ASTORE=$(printf '%s' "$ADET" | jsonq 'd.storageKey')
check "dam: the product it names really carries this asset" \
  "$(curl -s "$BASE/api/v1/products/$UPROD/assets" | jsonq 'String([...(d.media||[]),...(d.documents||[])].some(x=>x.url===a[0]))' "$ASTORE")" \
  "true"

check "dam: unknown id -> 404" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/assets/11111111-2222-4333-8444-555555555555")" "404"
check "dam: malformed id -> 404" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/assets/not-a-guid")" "404"

fi   # end of the "we have a linked image" guard


echo
echo "== Asset ingest and renditions (Sprint 26) =="

# Fixtures are GENERATED here rather than committed: a binary in the repo is a
# thing that rots, and a 1200x900 PNG is three lines of node.
UPDIR=$(mktemp -d)
node -e '
const fs=require("fs"),zlib=require("zlib");
// Minimal, valid, uncompressed-ish PNG written by hand: width x height of a
// two-tone pattern. No image library needed to MAKE one, which keeps this
// script free of the dependency it is testing.
function png(w,h,file){
  const raw=Buffer.alloc((w*3+1)*h);
  for(let y=0;y<h;y++){raw[y*(w*3+1)]=0;
    for(let x=0;x<w;x++){const o=y*(w*3+1)+1+x*3;
      raw[o]=(x*255/w)|0; raw[o+1]=(y*255/h)|0; raw[o+2]=128;}}
  const idat=zlib.deflateSync(raw);
  const chunk=(t,d)=>{const len=Buffer.alloc(4);len.writeUInt32BE(d.length);
    const td=Buffer.concat([Buffer.from(t),d]);const crc=Buffer.alloc(4);
    crc.writeUInt32BE(require("zlib").crc32?require("zlib").crc32(td)>>>0:crc32(td)>>>0);
    return Buffer.concat([len,td,crc]);};
  function crc32(buf){let c,t=[];for(let n=0;n<256;n++){c=n;for(let k=0;k<8;k++)c=c&1?0xEDB88320^(c>>>1):c>>>1;t[n]=c;}
    c=0xFFFFFFFF;for(let i=0;i<buf.length;i++)c=t[(c^buf[i])&0xFF]^(c>>>8);return (c^0xFFFFFFFF)>>>0;}
  const ihdr=Buffer.alloc(13);ihdr.writeUInt32BE(w,0);ihdr.writeUInt32BE(h,4);
  ihdr[8]=8;ihdr[9]=2;ihdr[10]=0;ihdr[11]=0;ihdr[12]=0;
  fs.writeFileSync(file,Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),
    chunk("IHDR",ihdr),chunk("IDAT",idat),chunk("IEND",Buffer.alloc(0))]));
}
png(2400,1800,process.argv[1]+"/big.png");
png(64,48,process.argv[1]+"/tiny.png");
fs.writeFileSync(process.argv[1]+"/model.ifc","ISO-10303-21;\nHEADER;\nENDSEC;\nDATA;\nENDSEC;\nEND-ISO-10303-21;\n");
fs.writeFileSync(process.argv[1]+"/notes.txt","hello");
' "$UPDIR"

check "ing: fixtures created" \
  "$([ -s "$UPDIR/big.png" ] && [ -s "$UPDIR/model.ifc" ] && echo yes || echo no)" "yes"

BEFORE=$(curl -s "$BASE/api/v1/assets/summary" | jsonq 'String(d.totalAssets)')

# --- an image, which must produce derivatives --------------------------------
UP=$(curl -s -X POST "$BASE/api/v1/assets?assetType=IMAGE&title=Smoke%20upload&altText=A%20test%20pattern" \
      -F "file=@$UPDIR/big.png")
UPID=$(printf '%s' "$UP" | jsonq 'd.publicId')

check "ing: upload accepted"  "$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/assets?assetType=IMAGE" -F "file=@$UPDIR/tiny.png")" "201"
check "ing: returns an id"    "$([ -n "$UPID" ] && [ "$UPID" != "undefined" ] && echo yes || echo no)" "yes"
check "ing: not a duplicate"  "$(printf '%s' "$UP" | jsonq 'String(d.deduplicated)')" "false"

ADET=$(curl -s "$BASE/api/v1/assets/$UPID")
check "ing: stored locally"   "$(printf '%s' "$ADET" | jsonq 'd.storageProvider')" "local"
check "ing: checksum recorded" \
  "$(printf '%s' "$ADET" | jsonq 'String(/^[0-9a-f]{64}$/.test(d.checksum||""))')" "true"
check "ing: dimensions read from the file" \
  "$(printf '%s' "$ADET" | jsonq 'String(d.width)+"x"+String(d.height)')" "2400x1800"

# The point of the sprint: real derivatives, actually smaller.
check "ing: thumb and web renditions generated" \
  "$(printf '%s' "$ADET" | jsonq 'String(["thumb","web","original"].every(r=>d.renditions.some(x=>x.role===r)))')" "true"
check "ing: thumb is 320px on its longest edge" \
  "$(printf '%s' "$ADET" | jsonq '(function(){const t=d.renditions.find(r=>r.role==="thumb");return String(Math.max(t.width,t.height))})()')" "320"
# Requires the rendition to EXIST and to be exactly the target. The first
# version of this passed when `web` was absent — which it was, because the
# fixture was 1200px and the generator correctly refused to upscale it to 1600.
# A 2400px source makes both derivatives real, and this assertion can now fail.
check "ing: web is exactly 1600px on its longest edge" \
  "$(printf '%s' "$ADET" | jsonq '(function(){const w=d.renditions.find(r=>r.role==="web");return w?String(Math.max(w.width,w.height)):"no web rendition"})()')" "1600"
check "ing: renditions keep the aspect ratio" \
  "$(printf '%s' "$ADET" | jsonq '(function(){const t=d.renditions.find(r=>r.role==="thumb");return String(Math.abs((t.width/t.height)-(d.width/d.height))<0.02)})()')" "true"

# --- the bytes actually come back --------------------------------------------
check "ing: original streams"  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/assets/$UPID/file")" "200"
check "ing: thumb streams"     "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/assets/$UPID/file?rendition=thumb")" "200"
check "ing: thumb is served as JPEG" \
  "$(curl -s -o /dev/null -w '%{content_type}' "$BASE/api/v1/assets/$UPID/file?rendition=thumb" | cut -d';' -f1)" "image/jpeg"

# A thumbnail that is not smaller than the original is not a thumbnail. This is
# the assertion that would catch a generator that stored the source unchanged.
ORIG_BYTES=$(curl -s "$BASE/api/v1/assets/$UPID/file" | wc -c | tr -d ' ')
THUMB_BYTES=$(curl -s "$BASE/api/v1/assets/$UPID/file?rendition=thumb" | wc -c | tr -d ' ')
check "ing: the thumb is genuinely smaller than the original" \
  "$([ "${THUMB_BYTES:-0}" -gt 0 ] && [ "${THUMB_BYTES:-0}" -lt "${ORIG_BYTES:-0}" ] && echo yes || echo "no: $THUMB_BYTES vs $ORIG_BYTES")" "yes"

# --- de-duplication ----------------------------------------------------------
# The same bytes under a different name must not become a second asset.
cp "$UPDIR/big.png" "$UPDIR/renamed.png"
DUP=$(curl -s -X POST "$BASE/api/v1/assets?assetType=IMAGE" -F "file=@$UPDIR/renamed.png")
check "ing: re-upload is de-duplicated" "$(printf '%s' "$DUP" | jsonq 'String(d.deduplicated)')" "true"
check "ing: and returns the SAME asset" "$(printf '%s' "$DUP" | jsonq 'd.publicId')" "$UPID"
check "ing: duplicate answers 200, not 201" \
  "$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/assets?assetType=IMAGE" -F "file=@$UPDIR/renamed.png")" "200"

# --- no upscaling ------------------------------------------------------------
TINY=$(curl -s -X POST "$BASE/api/v1/assets?assetType=IMAGE" -F "file=@$UPDIR/tiny.png" | jsonq 'd.publicId')
check "ing: a small image gets no invented derivatives" \
  "$(curl -s "$BASE/api/v1/assets/$TINY" | jsonq 'String(d.renditions.map(r=>r.role).sort().join(","))')" "original"

# --- BIM: stored, served, and NOT given a fake preview -----------------------
BIM=$(curl -s -X POST "$BASE/api/v1/assets?assetType=BIM&title=Smoke%20model" -F "file=@$UPDIR/model.ifc")
BIMID=$(printf '%s' "$BIM" | jsonq 'd.publicId')
check "ing: BIM upload accepted" "$([ -n "$BIMID" ] && [ "$BIMID" != "undefined" ] && echo yes || echo no)" "yes"
check "ing: BIM has only an original" \
  "$(curl -s "$BASE/api/v1/assets/$BIMID" | jsonq 'String(d.renditions.map(r=>r.role).sort().join(","))')" "original"
check "ing: BIM downloads"       "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/assets/$BIMID/file")" "200"
# Asking for a thumb of a BIM file falls back to the file rather than 404ing —
# there is no thumbnail of an IFC and there never will be.
check "ing: asking a BIM for a thumb returns the file" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/assets/$BIMID/file?rendition=thumb")" "200"
check "ing: BIM appears under its own type" \
  "$(curl -s "$BASE/api/v1/assets?type=BIM&pageSize=1" | jsonq 'String(d.totalCount > 0)')" "true"

# --- rejections --------------------------------------------------------------
check "ing: extension must match the declared type" \
  "$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/assets?assetType=IMAGE" -F "file=@$UPDIR/notes.txt")" "400"
check "ing: unknown type rejected" \
  "$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/assets?assetType=HOLOGRAM" -F "file=@$UPDIR/tiny.png")" "400"
check "ing: unknown locale rejected" \
  "$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/assets?assetType=IMAGE&locale=zz-ZZ" -F "file=@$UPDIR/tiny.png")" "400"
check "ing: unknown asset id -> 404" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/assets/11111111-2222-4333-8444-555555555555/file")" "404"

# --- the library sees the new assets -----------------------------------------
AFTER=$(curl -s "$BASE/api/v1/assets/summary" | jsonq 'String(d.totalAssets)')
check "ing: the library grew by exactly the new files" \
  "$([ "${AFTER:-0}" -gt "${BEFORE:-0}" ] && echo grew || echo "did not: $BEFORE -> $AFTER")" "grew"
# Uploaded assets are linked to nothing yet, which is what an orphan IS. This
# also proves Sprint 25's orphan count is live rather than a seeded constant.
check "ing: a fresh upload counts as an orphan" \
  "$(curl -s "$BASE/api/v1/assets?usage=orphan&pageSize=100" | jsonq 'String(d.items.some(i=>i.publicId===a[0]))' "$UPID")" "true"
check "ing: and it names a real rendition" \
  "$(curl -s "$BASE/api/v1/assets?usage=orphan&pageSize=100" | jsonq 'String((d.items.find(i=>i.publicId===a[0])||{}).servedRendition)' "$UPID")" "web"

rm -rf "$UPDIR"

echo
echo "== Workflow engine (Sprint 27) =="

# The pipeline is rows in workflow_definition, not code. These assertions are
# therefore about CONFIGURATION reaching the API intact — if the seed is edited,
# they fail here rather than as a button that quietly stopped appearing.
WFDEF=$(curl -s "$BASE/api/v1/workflow/definition")
check "wf: the pipeline is served" \
  "$(printf '%s' "$WFDEF" | jsonq 'd.code')" "product_publishing"
check "wf: four steps, in order" \
  "$(printf '%s' "$WFDEF" | jsonq 'String(d.steps.map(s=>s.code).join(","))')" "draft,review,approval,published"
check "wf: six transitions" \
  "$(printf '%s' "$WFDEF" | jsonq 'String(d.transitions.length)')" "6"
# Prototype v4.2 draws three buttons on Approval, which is why there are six
# transitions and not the five the old hardcoded table implied.
check "wf: approval offers three verbs" \
  "$(printf '%s' "$WFDEF" | jsonq 'String(d.transitions.filter(t=>t.fromStep==="approval").map(t=>t.action).sort().join(","))')" \
  "APPROVE,REJECT,REQUEST_CHANGES"
check "wf: published is terminal" \
  "$(printf '%s' "$WFDEF" | jsonq 'String(d.transitions.some(t=>t.fromStep==="published"))')" "false"
check "wf: nothing reaches published except from approval" \
  "$(printf '%s' "$WFDEF" | jsonq 'String(d.transitions.filter(t=>t.toStep==="published").map(t=>t.fromStep).join(","))')" "approval"

# A SECOND draft product, so this section never collides with the
# state-oriented block above, which owns $SEED_DRAFT_PRODUCT.
WFP=$(curl -s "$BASE/api/v1/products?status=Draft&pageSize=5&sortBy=code&sortDir=asc" \
  | jsonq 'String((d.items.map(i=>i.code).filter(c=>c!==a[0])[0])||"")' "$SEED_DRAFT_PRODUCT")
check "wf: a spare draft product was found" \
  "$([ -n "$WFP" ] && [ "$WFP" != "undefined" ] && echo yes || echo "no: '$WFP'")" "yes"

WFDOC=$(curl -s "$BASE/api/v1/products/$WFP/workflow")
check "wf: the tab reports where the product stands" \
  "$(printf '%s' "$WFDOC" | jsonq 'd.currentStep')" "draft"
check "wf: and which pipeline it is standing in" \
  "$(printf '%s' "$WFDOC" | jsonq 'd.definitionName')" "Product Publishing"
# The seed opens a task assigned to the product's OWN owner. A null here would
# mean the seeded tasks did not land.
check "wf: the seeded task carries an assignee" \
  "$(printf '%s' "$WFDOC" | jsonq 'String(d.assignee !== null && d.assignee.length > 0)')" "true"
check "wf: draft offers exactly one move" \
  "$(printf '%s' "$WFDOC" | jsonq 'String(d.availableActions.map(a=>a.action).join(","))')" "SUBMIT"
check "wf: labelled as the pipeline labels it" \
  "$(printf '%s' "$WFDOC" | jsonq 'd.availableActions[0].label')" "Submit for review"
# R-26 disclosure travels with the document, not just with the UI.
check "wf: the document states the trail is unverified" \
  "$(printf '%s' "$WFDOC" | jsonq 'String(/unverified/i.test(d.actorAttribution))')" "true"

WFRV=$(printf '%s' "$WFDOC" | jsonq 'String(d.rowVersion)')
WFH0=$(printf '%s' "$WFDOC" | jsonq 'String(d.history.length)')

WFR=$(curl -s -X POST "$BASE/api/v1/products/$WFP/workflow/actions" \
  -H 'Content-Type: application/json' \
  --data-binary '{"action":"SUBMIT","comment":"smoke: ready for review","actor":"Smoke Runner","rowVersion":'"$WFRV"'}')
check "wf: SUBMIT lands on review" \
  "$(printf '%s' "$WFR" | jsonq 'd.currentStep')" "review"
check "wf: the trail grew by exactly one" \
  "$(printf '%s' "$WFR" | jsonq 'String(d.history.length)' )" "$((WFH0 + 1))"
check "wf: the entry keeps the verb" \
  "$(printf '%s' "$WFR" | jsonq 'String(d.history[d.history.length-1].action)')" "SUBMIT"
check "wf: and the reviewer note" \
  "$(printf '%s' "$WFR" | jsonq 'String(d.history[d.history.length-1].comment)')" "smoke: ready for review"
check "wf: and the claimed actor" \
  "$(printf '%s' "$WFR" | jsonq 'String(d.history[d.history.length-1].actor)')" "Smoke Runner"
check "wf: review offers two verbs" \
  "$(printf '%s' "$WFR" | jsonq 'String(d.availableActions.map(a=>a.action).sort().join(","))')" "APPROVE,REQUEST_CHANGES"
# The projection the grid reads must have moved with the task.
check "wf: the product row moved too" \
  "$(curl -s "$BASE/api/v1/products/$WFP" | jsonq 'd.workflowState')" "Review"

# Rejections.
#
# Written as a helper with the status captured on its own line, NOT as a
# multi-line "$(curl ... \ ... -d "{\"a\":...}")" argument. The first draft of
# these three used that form and printed
#
#     PASS  wf: a stale row version -> 409 (= 500)
#
# -- a pass whose stated expectation was not the one in the assertion's own
# name. Whatever bash was doing with an escaped-quote heredoc body inside a
# line-continued command substitution, an assertion that cannot be read is not
# evidence, so the form is gone. The body is echoed on a mismatch so a failure
# is diagnosable from one run instead of two.
wf_reject() { # name, action, rowVersion, expected-status
  local status
  status=$(curl -s -o /tmp/pim-wf27.json -w '%{http_code}' \
    -X POST "$BASE/api/v1/products/$WFP/workflow/actions" \
    -H 'Content-Type: application/json' \
    --data-binary '{"action":"'"$2"'","rowVersion":'"$3"'}')
  check "$1" "$status" "$4"
  if [ "$status" != "$4" ]; then
    echo "        body: $(cat /tmp/pim-wf27.json)"
  fi
}

# The row version the caller held is now stale.
wf_reject "wf: a stale row version -> 409" "APPROVE" "$WFRV" "409"

WFRV=$(printf '%s' "$WFR" | jsonq 'String(d.rowVersion)')

wf_reject "wf: an action not available from here -> 409" "SUBMIT" "$WFRV" "409"
wf_reject "wf: an action that does not exist -> 400" "ESCALATE" "$WFRV" "400"

WF404=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/products/NO-SUCH-PRODUCT-27/workflow")
check "wf: an unknown product -> 404" "$WF404" "404"

# REJECT and REQUEST_CHANGES land on the same step. The entire point of keeping
# them apart is that the trail can be read back, so assert the trail.
WFR=$(curl -s -X POST "$BASE/api/v1/products/$WFP/workflow/actions" \
  -H 'Content-Type: application/json' \
  --data-binary '{"action":"REQUEST_CHANGES","comment":"smoke: fire rating missing","rowVersion":'"$WFRV"'}')
check "wf: request changes returns it to draft" \
  "$(printf '%s' "$WFR" | jsonq 'd.currentStep')" "draft"
check "wf: recorded as its own verb, not as a destination" \
  "$(printf '%s' "$WFR" | jsonq 'String(d.history[d.history.length-1].action)')" "REQUEST_CHANGES"
check "wf: an omitted actor is stored as nothing, not as a blank" \
  "$(printf '%s' "$WFR" | jsonq 'String(d.history[d.history.length-1].actor === null)')" "true"

# All the way to the end, because a terminal step is where the interesting
# behaviour is: the task closes and the trail has to survive it.
WFRV=$(printf '%s' "$WFR" | jsonq 'String(d.rowVersion)')
WFR=$(curl -s -X POST "$BASE/api/v1/products/$WFP/workflow/actions" \
  -H 'Content-Type: application/json' --data-binary '{"action":"SUBMIT","rowVersion":'"$WFRV"'}')
WFRV=$(printf '%s' "$WFR" | jsonq 'String(d.rowVersion)')
WFR=$(curl -s -X POST "$BASE/api/v1/products/$WFP/workflow/actions" \
  -H 'Content-Type: application/json' --data-binary '{"action":"APPROVE","rowVersion":'"$WFRV"'}')
check "wf: approve from review reaches approval" \
  "$(printf '%s' "$WFR" | jsonq 'd.currentStep')" "approval"
WFRV=$(printf '%s' "$WFR" | jsonq 'String(d.rowVersion)')
WFR=$(curl -s -X POST "$BASE/api/v1/products/$WFP/workflow/actions" \
  -H 'Content-Type: application/json' --data-binary '{"action":"APPROVE","rowVersion":'"$WFRV"'}')
check "wf: approve from approval publishes" \
  "$(printf '%s' "$WFR" | jsonq 'd.currentStep')" "published"
check "wf: the task closes rather than vanishing" \
  "$(printf '%s' "$WFR" | jsonq 'd.taskStatus')" "completed"
check "wf: the trail survives the task closing" \
  "$(printf '%s' "$WFR" | jsonq 'String(d.history.length >= 5)')" "true"
check "wf: nothing is offered from a terminal step" \
  "$(printf '%s' "$WFR" | jsonq 'String(d.availableActions.length)')" "0"
check "wf: and it says so" \
  "$(printf '%s' "$WFR" | jsonq 'String(d.isComplete)')" "true"

# The audit action is resolved AFTER the handler runs, because an action-oriented
# command cannot know its own destination: APPROVE publishes from Approval and
# merely passes review from Review. If that ever regresses, "when was this
# published" has no answer in the activity log.
check "wf: publishing is recorded as a publish in the activity log" \
  "$(curl -s "$BASE/api/v1/products/$WFP/activity?pageSize=25" | jsonq 'String(d.entries.items.some(e=>e.action==="published"))')" "true"
check "wf: the earlier moves are recorded as workflow, not as publishes" \
  "$(curl -s "$BASE/api/v1/products/$WFP/activity?pageSize=25" | jsonq 'String(d.entries.items.filter(e=>e.action==="published").length)')" "1"

# The state-oriented endpoint the detail header used until Sprint 27 now runs
# through the same engine. It must therefore leave the same kind of trail --
# and resolve a DESTINATION back to a VERB.
check "wf: the state-oriented endpoint writes the same trail" \
  "$(curl -s "$BASE/api/v1/products/$SEED_DRAFT_PRODUCT/workflow" | jsonq 'String(d.history.slice(0,2).map(h=>h.action).join(","))')" \
  "SUBMIT,REQUEST_CHANGES"

# A product that never entered the pipeline still reads correctly: the current
# step comes from the product row, not from a task that does not exist.
WFPUB=$(curl -s "$BASE/api/v1/products?status=Published&pageSize=1&sortBy=code&sortDir=asc" | jsonq 'd.items[0].code')
check "wf: a published product offers nothing" \
  "$(curl -s "$BASE/api/v1/products/$WFPUB/workflow" | jsonq 'String(d.availableActions.length)')" "0"
check "wf: and reports no task rather than inventing one" \
  "$(curl -s "$BASE/api/v1/products/$WFPUB/workflow" | jsonq 'd.taskStatus')" "none"

# THE assertion this module's design depends on. product.workflow_state is a
# denormalised projection of approval_task.current_step_id, written in the same
# transaction. A denormalisation nobody checks is a bug with a rationale
# attached, so this counts the rows where the two disagree.
WFDRIFT=$(docker compose -f "$ROOT/infra/docker-compose.yml" exec -T postgres \
  psql -U pim_admin -d pim -tAc \
  "SELECT count(*) FROM catalog.approval_task t
     JOIN catalog.product p ON p.id = t.product_id
     JOIN catalog.workflow_step s ON s.id = t.current_step_id
    WHERE t.status = 'open' AND s.code <> p.workflow_state" 2>/dev/null | tr -d '[:space:]')
check "wf: no open task disagrees with its product" "${WFDRIFT:-unknown}" "0"

# A completed task must sit on a terminal step. One left completed halfway
# would silently remove a product from every queue.
WFSTRANDED=$(docker compose -f "$ROOT/infra/docker-compose.yml" exec -T postgres \
  psql -U pim_admin -d pim -tAc \
  "SELECT count(*) FROM catalog.approval_task t
     JOIN catalog.workflow_step s ON s.id = t.current_step_id
    WHERE t.status = 'completed' AND NOT s.is_terminal" 2>/dev/null | tr -d '[:space:]')
check "wf: no task was completed short of the end" "${WFSTRANDED:-unknown}" "0"

# Every history row must belong to a transition the pipeline actually declares.
# A row that does not is a trail describing a move that could not have happened.
WFBADHIST=$(docker compose -f "$ROOT/infra/docker-compose.yml" exec -T postgres \
  psql -U pim_admin -d pim -tAc \
  "SELECT count(*) FROM catalog.approval_history h
    WHERE h.from_step_id IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM catalog.workflow_transition wt
                       WHERE wt.from_step_id = h.from_step_id
                         AND wt.to_step_id = h.to_step_id
                         AND wt.action = h.action)" 2>/dev/null | tr -d '[:space:]')
check "wf: every recorded move is one the pipeline declares" "${WFBADHIST:-unknown}" "0"


echo
echo "== Workflows board and queue (Sprint 28) =="

BOARD=$(curl -s "$BASE/api/v1/workflow/board")
check "board: columns come from the pipeline, in order" \
  "$(printf '%s' "$BOARD" | jsonq 'String(d.columns.map(c=>c.step).join(","))')" "draft,review,approval,published"
check "board: every column is one the pipeline defines" \
  "$(printf '%s' "$BOARD" | jsonq 'String(d.columns.every(c=>c.isRecognised))')" "true"
check "board: the terminal step is marked" \
  "$(printf '%s' "$BOARD" | jsonq 'String(d.columns.filter(c=>c.isTerminal).map(c=>c.step).join(","))')" "published"

# Recomputed from the PRODUCTS GRID rather than checked for presence: two
# implementations of one definition, compared. The habit Sprint 21 acquired
# after shipping a KPI that could only ever be 0.
GRIDTOTAL=$(curl -s "$BASE/api/v1/products?pageSize=1" | jsonq 'String(d.totalCount)')
check "board: totalProducts matches the catalogue" \
  "$(printf '%s' "$BOARD" | jsonq 'String(d.totalProducts)')" "$GRIDTOTAL"
check "board: the columns sum to the catalogue" \
  "$(printf '%s' "$BOARD" | jsonq 'String(d.columns.reduce((n,c)=>n+c.totalCount,0))')" "$GRIDTOTAL"

for STEP in draft review approval published; do
  GRIDN=$(curl -s "$BASE/api/v1/products?pageSize=1&status=$STEP" | jsonq 'String(d.totalCount)')
  BOARDN=$(printf '%s' "$BOARD" | jsonq 'String((d.columns.find(c=>c.step===a[0])||{}).totalCount)' "$STEP")
  check "board: the $STEP column matches the grid's $STEP filter" "$BOARDN" "$GRIDN"
done

# A column's count is the WHOLE column; its cards are a window onto it. The two
# differ for every real catalogue, and conflating them is the defect R-19 names.
check "board: no column returns more cards than asked for" \
  "$(printf '%s' "$BOARD" | jsonq 'String(d.columns.every(c=>c.cards.length <= d.cardsPerColumn))')" "true"
check "board: a column shows every card it has, up to the cap" \
  "$(printf '%s' "$BOARD" | jsonq 'String(d.columns.every(c=>c.cards.length === Math.min(c.totalCount, d.cardsPerColumn)))')" "true"
check "board: at least one column is genuinely truncated, so the cap is exercised" \
  "$(printf '%s' "$BOARD" | jsonq 'String(d.columns.some(c=>c.totalCount > c.cards.length))')" "true"

BOARD1=$(curl -s "$BASE/api/v1/workflow/board?cardsPerColumn=1")
check "board: the cap is honoured at 1" \
  "$(printf '%s' "$BOARD1" | jsonq 'String(d.columns.every(c=>c.cards.length <= 1))')" "true"
check "board: and the totals do not move with it" \
  "$(printf '%s' "$BOARD1" | jsonq 'String(d.columns.reduce((n,c)=>n+c.totalCount,0))')" "$GRIDTOTAL"

check "board: cardsPerColumn=0 -> 400" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/workflow/board?cardsPerColumn=0")" "400"
check "board: cardsPerColumn=999 -> 400" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/workflow/board?cardsPerColumn=999")" "400"
check "board: an unknown locale is rejected, not silently defaulted" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/workflow/board?locale=zz-ZZ")" "400"

# Labels follow the reader; the pipeline does not (ADR-009). A board that
# changed shape with the language would hide products from whoever switched.
BOARD_DE=$(curl -s "$BASE/api/v1/workflow/board?locale=de-DE&cardsPerColumn=6")
BOARD_EN=$(curl -s "$BASE/api/v1/workflow/board?locale=en-GB&cardsPerColumn=6")
DE_CODES=$(printf '%s' "$BOARD_DE" | jsonq 'String(d.columns.flatMap(c=>c.cards.map(x=>x.code)).join(","))')
EN_CODES=$(printf '%s' "$BOARD_EN" | jsonq 'String(d.columns.flatMap(c=>c.cards.map(x=>x.code)).join(","))')
check "board: the same products in both languages" "$DE_CODES" "$EN_CODES"

DE_TOTALS=$(printf '%s' "$BOARD_DE" | jsonq 'String(d.columns.map(c=>c.totalCount).join(","))')
EN_TOTALS=$(printf '%s' "$BOARD_EN" | jsonq 'String(d.columns.map(c=>c.totalCount).join(","))')
check "board: and the same totals" "$DE_TOTALS" "$EN_TOTALS"

# Written with the two sides in variables rather than as nested substitutions:
# Sprint 27 shipped three assertions in that form that compared a value to
# itself and could never fail.
DE_NAMES=$(printf '%s' "$BOARD_DE" | jsonq 'String(d.columns.flatMap(c=>c.cards.map(x=>x.name)).join("|"))')
EN_NAMES=$(printf '%s' "$BOARD_EN" | jsonq 'String(d.columns.flatMap(c=>c.cards.map(x=>x.name)).join("|"))')
check "board: names were actually read (neither side is empty)" \
  "$([ -n "$DE_NAMES" ] && [ -n "$EN_NAMES" ] && echo yes || echo no)" "yes"
check "board: but the names are translated" \
  "$([ "$DE_NAMES" != "$EN_NAMES" ] && echo different || echo same)" "different"

# The terminal column cannot hold an open task: reaching it completes the task.
check "board: nothing in the terminal column has an open task" \
  "$(printf '%s' "$BOARD" | jsonq 'String((d.columns.find(c=>c.isTerminal)||{cards:[]}).cards.every(x=>x.hasOpenTask===false))')" "true"
check "board: draft cards include at least one with an open task" \
  "$(printf '%s' "$BOARD" | jsonq 'String((d.columns.find(c=>c.step==="draft")||{cards:[]}).cards.some(x=>x.hasOpenTask===true))')" "true"

# --- the list view -----------------------------------------------------------
QUEUE=$(curl -s "$BASE/api/v1/workflow/queue?pageSize=1")
check "queue: covers the whole catalogue, not just products with tasks" \
  "$(printf '%s' "$QUEUE" | jsonq 'String(d.page.totalCount)')" "$GRIDTOTAL"
check "queue: offers the pipeline's steps as filters" \
  "$(printf '%s' "$QUEUE" | jsonq 'String(d.steps.map(s=>s.code).join(","))')" "draft,review,approval,published"
check "queue: offers real assignees" \
  "$(printf '%s' "$QUEUE" | jsonq 'String(d.assignees.length > 0)')" "true"

QDRAFT=$(curl -s "$BASE/api/v1/workflow/queue?step=draft&pageSize=100")
GRIDDRAFT=$(curl -s "$BASE/api/v1/products?pageSize=1&status=draft" | jsonq 'String(d.totalCount)')
check "queue: the step filter agrees with the grid" \
  "$(printf '%s' "$QDRAFT" | jsonq 'String(d.page.totalCount)')" "$GRIDDRAFT"
check "queue: and returns only that step" \
  "$(printf '%s' "$QDRAFT" | jsonq 'String(d.page.items.every(i=>i.step==="draft"))')" "true"
check "queue: the step name comes from the pipeline" \
  "$(printf '%s' "$QDRAFT" | jsonq 'String(d.page.items[0].stepName)')" "Draft"

QASSIGNEE=$(printf '%s' "$QUEUE" | jsonq 'String(d.assignees[0])')
QASSIGNEE_ENC=$(printf '%s' "$QASSIGNEE" | sed 's/ /%20/g')
QMINE=$(curl -s "$BASE/api/v1/workflow/queue?pageSize=100&assignee=$QASSIGNEE_ENC")
check "queue: filtering by assignee returns some of their products" \
  "$(printf '%s' "$QMINE" | jsonq 'String(d.page.items.length > 0)')" "true"
check "queue: and nobody else's" \
  "$(printf '%s' "$QMINE" | jsonq 'String(d.page.items.every(i=>i.assignee===a[0]))' "$QASSIGNEE")" "true"

check "queue: search finds a product by code" \
  "$(curl -s "$BASE/api/v1/workflow/queue?search=$LEAD" | jsonq 'String(d.page.items.some(i=>i.code===a[0]))' "$LEAD")" "true"

# Rejected rather than answered with an empty list: an empty queue reads as
# "nothing to do", which is the most misleading thing this screen could say.
check "queue: an unknown step -> 400" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/workflow/queue?step=nonsense")" "400"
check "queue: an unknown priority -> 400" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/workflow/queue?priority=screaming")" "400"
check "queue: an unwhitelisted sort -> 400" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/workflow/queue?sortBy=p.id")" "400"
check "queue: pageSize>100 -> 400" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/workflow/queue?pageSize=500")" "400"

# Every column the validator advertises must actually sort, or the API's own
# error message names a value that produces a 500.
for COL in code name category step owner assignee due updated; do
  SORTSTATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/workflow/queue?pageSize=5&sortBy=$COL&sortDir=asc")
  check "queue: sorts by $COL" "$SORTSTATUS" "200"
done

# The product driven all the way to Published earlier in this run carries the
# only approval trail in the catalogue, so its last action is readable here.
QLAST=$(curl -s "$BASE/api/v1/workflow/queue?pageSize=100&search=$WFP")
check "queue: the last recorded action is surfaced" \
  "$(printf '%s' "$QLAST" | jsonq 'String((d.page.items.find(i=>i.code===a[0])||{}).lastAction)' "$WFP")" "APPROVE"

echo "== Swagger =="
STATUS=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/swagger/v1/swagger.json")
check "swagger.json served"        "$STATUS" "200"
HAS_EP=$(curl -s "$BASE/swagger/v1/swagger.json" | jsonq 'Object.keys(d.paths).includes("/api/v1/products") ? "yes" : "no"')
check "list endpoint documented"   "$HAS_EP" "yes"

echo
echo "== Summary =="
echo "  passed: $PASS   failed: $FAIL"
if [ $FAIL -eq 0 ]; then
  echo "API SMOKE: GREEN"
  exit 0
else
  echo "API SMOKE: RED — see failures above; API log: api.log"
  exit 1
fi
