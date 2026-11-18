#!/usr/bin/env python3
"""
Generates the dev seed from the client's own catalogue export.

WHY A GENERATOR AND NOT HAND-WRITTEN SQL
----------------------------------------
The seed is now derived from two client files totalling ~16 MB. Hand-editing
the resulting SQL would make it impossible to answer "where did this value come
from", and impossible to refresh when the client sends a newer export. The
generator is the source of truth; 02_dev_seed.sql is a build artifact that
happens to be committed so a fresh clone can `docker compose up` without Python.

SOURCES
-------
  Meta - Product Catelogue file shared with Yavi.xlsx  (sheet 'With ID')
      English-normalised, one row per article, 94 columns including ImageURL.
  Agrob Buchtal Product Data_20250414.csv
      The German master, 228 columns, with paired DE/EN text.

Joined on article number (xlsx ResourceName == CSV Artikelnummer).

MODELLING DECISION
------------------
An article number is NOT a product. The data groups naturally as:

    product = Series + Product type + Format        (667 groups)
    SKU     = article number, varying by colour     (6,170 articles)

e.g. "Plural Mosaic 10 x 10 cm" is one product with 127 colour variants. This
is the structure a PIM exists to manage, and it makes the Variants tab show
real variant axes instead of synthetic V1/V2 codes.

OUTPUTS
-------
  services/catalog/db/init/02_dev_seed.sql   the seed itself
  scripts/seed-facts.env                     constants the smoke suite sources,
                                             so a seed refresh does not require
                                             editing hundreds of assertions.

USAGE
-----
  python3 scripts/generate-seed.py --source-dir <dir containing the two files>
"""

from __future__ import annotations

import argparse
import csv
import re
import shlex
import sys
from collections import defaultdict
from pathlib import Path

try:
    import openpyxl
except ImportError:  # pragma: no cover - operator-facing message
    sys.exit("openpyxl is required: pip install openpyxl --break-system-packages")

csv.field_size_limit(10**9)

# Target number of products. Every series and category is represented first,
# then the largest groups fill the remainder — so the grid, the series facet
# and the variant counts all look like the real catalogue.
TARGET_PRODUCTS = 500

# Largest groups have 174 articles. Keeping all of them would make the seed
# file enormous for no demo value; 12 is enough to show a real variant axis.
MAX_SKUS_PER_PRODUCT = 12

XLSX_SHEET = "With ID"

# CSV column indices (the header row carries emoji prefixes, so index is safer
# than name matching).
C_ARTICLE = 0
C_BRAND_AB = 7
C_BRAND_DRYTILE = 8
C_SERIES_DE = 25
C_SERIES_EN = 26
C_CATEGORY_DE = 43
C_CATEGORY_EN = 45
C_TYPE_DE = 59
C_TYPE_EN = 62
C_FORMAT_DE = 85
C_FORMAT_EN = 88
C_COLOR_DE = 122
C_COLOR_EN = 123
C_GLOSS = 129
C_STRUCTURE = 130
C_DESIGN_DE = 136

# Planning / BIM (Sprint 13). The export has NO facade property set — all 16
# `Fassade:` columns are empty in every one of the 6,170 rows — so the planning
# parameters below are the dimensional and surface properties a planner
# actually specifies against, taken from columns that are genuinely populated.
C_BIM_READY = 21          # 'ja/yes' | 'nein/no', filled on every row
C_MFG_WIDTH_MM = 96       # Herstellmaß 1 (Breite)
C_MFG_LENGTH_MM = 97      # Herstellmaß 2 (Länge)
C_SLIP_COMMERCIAL = 111   # R-rating (Gewerblich)
C_SLIP_BAREFOOT = 112     # A / B / C (Barfuß)
C_SLIP_DISPLACEMENT = 113 # V4 etc. (Verdrängungsraum)
C_LOAD_GROUP = 114        # Belastungsgruppe
C_WEAR_GROUP = 115        # Beanspruchungsgruppe
C_LRV = 125               # Hellbezugswert Y — light reflectance value
C_RAL = 126
C_NCS = 127

# Localization (Sprint 13). Fields with a genuine German/English pair in the
# export. Anything without one is left untranslated rather than machine-filled:
# an empty target with status 'missing' is the truth, and it is what gives the
# tab something real to do.
C_STANDARD_TEXT_DE = 70
C_STANDARD_TEXT_EN = 71
C_USE_WALL_FLOOR_DE = 78
C_USE_WALL_FLOOR_EN = 79
C_USE_INDOOR_DE = 81
C_USE_INDOOR_EN = 82
C_FINISHING_DE = 118
C_FINISHING_EN = 119


def sql_str(value: str | None) -> str:
    """A SQL literal, or NULL. Doubles quotes; the inputs are client text."""
    if value is None:
        return "NULL"
    text = str(value).strip()
    if text == "" or text.upper() == "NULL":
        return "NULL"
    return "'" + text.replace("'", "''") + "'"


def slug(value: str, max_length: int = 24) -> str:
    """
    A product-code segment: uppercase, alphanumeric, hyphen separated.

    Must satisfy the same rule the domain enforces on ProductCode, otherwise the
    seed would contain codes the API itself would reject — which has happened in
    other projects and is very confusing to debug.
    """
    text = (
        value.upper()
        .replace("Ä", "AE").replace("Ö", "OE").replace("Ü", "UE").replace("ß", "SS")
    )
    text = re.sub(r"[^A-Z0-9]+", "-", text).strip("-")
    return text[:max_length].strip("-") or "X"


def unique_code(value: str, taken: set[str], max_length: int = 28) -> str:
    """
    A code slug guaranteed not to collide with anything already in `taken`.

    Slugging is lossy: 'Light beige' and 'light-beige' both reduce to
    'light_beige', and the database rightly rejects the second. Two collisions
    of this kind got through before this helper existed (a series named after a
    category, and four duplicate colour names), each costing a full DB reset to
    discover. Every derived code now goes through here.
    """
    base = slug(value, max_length).lower().replace("-", "_") or "x"
    candidate = base
    n = 1
    while candidate in taken:
        n += 1
        candidate = f"{base}_{n}"
    taken.add(candidate)
    return candidate


def read_xlsx(path: Path) -> dict[str, dict[str, str]]:
    workbook = openpyxl.load_workbook(path, read_only=True, data_only=True)
    sheet = workbook[XLSX_SHEET]
    rows = sheet.iter_rows(values_only=True)
    header = [str(h) if h is not None else "" for h in next(rows)]

    by_article: dict[str, dict[str, str]] = {}
    for row in rows:
        record = {
            header[i]: ("" if v is None or str(v) == "NULL" else str(v).strip())
            for i, v in enumerate(row)
            if i < len(header)
        }
        article = record.get("ResourceName", "").strip()
        if article:
            by_article[article] = record

    workbook.close()
    return by_article


def read_csv(path: Path) -> dict[str, list[str]]:
    by_article: dict[str, list[str]] = {}
    with path.open(encoding="utf-8-sig", newline="") as handle:
        reader = csv.reader(handle)
        next(reader)
        for row in reader:
            if not row:
                continue
            article = row[C_ARTICLE].strip()
            if article:
                by_article[article] = row
    return by_article


def cell(row: list[str], index: int) -> str:
    return row[index].strip() if index < len(row) else ""


class Article:
    """One article number, joined across both sources."""

    __slots__ = ("code", "csv_row", "xl")

    def __init__(self, code: str, csv_row: list[str], xl: dict[str, str]):
        self.code = code
        self.csv_row = csv_row
        self.xl = xl

    def c(self, index: int) -> str:
        return cell(self.csv_row, index)

    def x(self, key: str) -> str:
        return self.xl.get(key, "")


def build_groups(articles: list[Article]) -> dict[tuple[str, str, str], list[Article]]:
    groups: dict[tuple[str, str, str], list[Article]] = defaultdict(list)
    for article in articles:
        series = article.c(C_SERIES_EN) or article.c(C_SERIES_DE)
        type_en = article.c(C_TYPE_EN) or article.c(C_TYPE_DE)
        fmt = article.c(C_FORMAT_EN) or article.c(C_FORMAT_DE)
        if not (series and type_en):
            continue
        groups[(series, type_en, fmt)].append(article)
    return groups


def select_groups(groups: dict) -> list[tuple]:
    """
    Every series and every category gets at least one product before size is
    considered. A purely size-ranked selection dropped 40 of the 94 series,
    which would have left the series facet looking broken.
    """
    by_series: dict[str, list[tuple]] = defaultdict(list)
    by_category: dict[str, list[tuple]] = defaultdict(list)

    for key, members in groups.items():
        by_series[key[0]].append(key)
        category = members[0].x("ProductCategory") or members[0].c(C_CATEGORY_EN)
        by_category[category].append(key)

    chosen: list[tuple] = []
    seen: set[tuple] = set()

    def take(key: tuple) -> None:
        if key not in seen:
            seen.add(key)
            chosen.append(key)

    for keys in by_series.values():
        take(max(keys, key=lambda k: len(groups[k])))
    for keys in by_category.values():
        take(max(keys, key=lambda k: len(groups[k])))

    for key in sorted(groups, key=lambda k: -len(groups[k])):
        if len(chosen) >= TARGET_PRODUCTS:
            break
        take(key)

    return chosen


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-dir", required=True, type=Path)
    parser.add_argument(
        "--out",
        type=Path,
        default=Path(__file__).resolve().parents[1]
        / "services/catalog/db/init/02_dev_seed.sql",
    )
    parser.add_argument(
        "--facts",
        type=Path,
        default=Path(__file__).resolve().parent / "seed-facts.env",
    )
    args = parser.parse_args()

    xlsx = next(args.source_dir.glob("*.xlsx"), None)
    csv_path = next(args.source_dir.glob("*.csv"), None)
    if not xlsx or not csv_path:
        return print(f"Need one .xlsx and one .csv in {args.source_dir}") or 2

    print(f"reading {xlsx.name}")
    xl_rows = read_xlsx(xlsx)
    print(f"reading {csv_path.name}")
    csv_rows = read_csv(csv_path)

    articles = [
        Article(code, row, xl_rows.get(code, {}))
        for code, row in csv_rows.items()
        if code in xl_rows
    ]
    print(f"joined {len(articles)} articles")

    groups = build_groups(articles)
    chosen = select_groups(groups)
    print(f"{len(groups)} groups -> {len(chosen)} products")

    emit(args.out, args.facts, groups, chosen)
    return 0


def emit(out: Path, facts_path: Path, groups: dict, chosen: list[tuple]) -> None:
    # ---- reference data gathered from the chosen products only, so the seed
    # ---- never references a series or colour that has no products.
    categories: dict[str, str] = {}     # en -> de
    series_map: dict[str, tuple[str, str]] = {}   # en -> (de, category_en)
    colours: dict[str, str] = {}        # en -> de

    products: list[dict] = []

    for key in chosen:
        members = groups[key]
        series_en, type_en, fmt = key
        lead = members[0]

        category_en = lead.x("ProductCategory") or lead.c(C_CATEGORY_EN) or "Other"
        category_de = lead.c(C_CATEGORY_DE) or category_en
        categories.setdefault(category_en, category_de)

        series_de = lead.c(C_SERIES_DE) or series_en
        series_map.setdefault(series_en, (series_de, category_en))

        code = "-".join(
            p for p in ("AB", slug(series_en, 18), slug(type_en, 12), slug(fmt, 12)) if p
        )[:64].strip("-")

        skus = members[:MAX_SKUS_PER_PRODUCT]
        for member in skus:
            colour_en = member.c(C_COLOR_EN)
            if colour_en:
                colours.setdefault(colour_en, member.c(C_COLOR_DE) or colour_en)

        products.append(
            {
                "code": code,
                "category_en": category_en,
                "series_en": series_en,
                "type_en": type_en,
                "type_de": lead.c(C_TYPE_DE) or type_en,
                "format_en": fmt,
                "format_de": lead.c(C_FORMAT_DE) or fmt,
                "name_en": " ".join(x for x in (series_en, type_en, fmt) if x),
                "name_de": " ".join(
                    x for x in (series_de, lead.c(C_TYPE_DE) or type_en,
                                lead.c(C_FORMAT_DE) or fmt) if x
                ),
                # The client's own German design copy. There is no English
                # equivalent anywhere in the export, which is precisely why the
                # Localization tab has real work to show: German approved,
                # English missing, on every product that has one.
                "design_de": lead.c(C_DESIGN_DE),
                "brand": "drytile" if lead.c(C_BRAND_DRYTILE) else "agrob_buchtal",
                "lead": lead,
                "skus": skus,
                "total_variants": len(members),
            }
        )

    # De-duplicate product codes: two groups can slug to the same code when the
    # series names differ only past the truncation point.
    seen_codes: dict[str, int] = {}
    for product in products:
        base = product["code"]
        if base in seen_codes:
            seen_codes[base] += 1
            product["code"] = f"{base}-{seen_codes[base]}"[:64]
        else:
            seen_codes[base] = 0

    lines: list[str] = []
    w = lines.append

    w("-- =============================================================")
    w("-- DEV SEED — GENERATED FILE. Do not edit by hand.")
    w("--")
    w("--   Regenerate:  python3 scripts/generate-seed.py --source-dir <dir>")
    w("--")
    w("-- Derived from the client's own catalogue export (Agrob Buchtal,")
    w("-- 2025-04-14): 6,170 articles in 667 product groups, of which the")
    w(f"-- {len(products)} below are seeded. Every series and product category is")
    w("-- represented so the grid, facets and variant counts look like the real")
    w("-- catalogue rather than like test data.")
    w("--")
    w("-- A product is Series + Product type + Format; its SKUs are the article")
    w(f"-- numbers, which vary by colour. Capped at {MAX_SKUS_PER_PRODUCT} SKUs per product.")
    w("--")
    w("-- Product/series/colour names are the client's own, in German and")
    w("-- English, so their team recognises the data on sight.")
    w("-- NOT for production.")
    w("-- =============================================================")
    w("SET search_path TO catalog;")
    w("")

    w("INSERT INTO locale (code, name, is_base) VALUES")
    w("  ('de-DE', 'Deutsch',      true),")
    w("  ('en-GB', 'English (UK)', false),")
    w("  ('fr-FR', 'Français',     false);")
    w("")

    w("INSERT INTO brand (code, status) VALUES")
    w("  ('agrob_buchtal', 'active'),")
    w("  ('drytile',       'active');")
    w("")
    w("INSERT INTO brand_tr (brand_id, locale_code, name)")
    w("SELECT b.id, l.code,")
    w("       CASE b.code WHEN 'agrob_buchtal' THEN 'Agrob Buchtal' ELSE 'DryTile' END")
    w("FROM brand b CROSS JOIN locale l WHERE l.code IN ('de-DE','en-GB');")
    w("")

    # ---- taxonomy: FAMILY -> CATEGORY -> SERIES
    w("-- Taxonomy mirrors the client's own hierarchy: product category, then series.")
    w("INSERT INTO taxonomy_node (code, node_type, parent_id, path, depth) VALUES")
    w("  ('agrob', 'FAMILY', NULL, 'agrob', 0);")
    w("")

    # taxonomy_node.code is unique across the WHOLE tree, so categories and
    # series share one namespace.
    taxonomy_taken: set[str] = set()
    category_codes = {en: unique_code(en, taxonomy_taken) for en in sorted(categories)}
    w("INSERT INTO taxonomy_node (code, node_type, parent_id, path, depth)")
    w("SELECT c.code, 'CATEGORY', r.id, ('agrob.' || c.code)::ltree, 1")
    w("FROM (VALUES")
    w(",\n".join(f"  ({sql_str(c)})" for c in sorted(category_codes.values())))
    w(") AS c(code)")
    w("CROSS JOIN (SELECT id FROM taxonomy_node WHERE code = 'agrob') r;")
    w("")

    series_codes = {en: unique_code(en, taxonomy_taken) for en in sorted(series_map)}

    w("INSERT INTO taxonomy_node (code, node_type, parent_id, path, depth)")
    w("SELECT s.code, 'SERIES', p.id, (p.path::text || '.' || s.code)::ltree, 2")
    w("FROM (VALUES")
    w(",\n".join(
        f"  ({sql_str(series_codes[en])}, {sql_str(category_codes[series_map[en][1]])})"
        for en in sorted(series_map)
    ))
    w(") AS s(code, parent_code)")
    w("JOIN taxonomy_node p ON p.code = s.parent_code;")
    w("")

    w("INSERT INTO taxonomy_node_tr (node_id, locale_code, name, translation_status)")
    w("SELECT tn.id, v.locale, v.name, 'approved'")
    w("FROM (VALUES")
    rows = []
    for en, de in sorted(categories.items()):
        rows.append(f"  ({sql_str(category_codes[en])}, 'de-DE', {sql_str(de)})")
        rows.append(f"  ({sql_str(category_codes[en])}, 'en-GB', {sql_str(en)})")
    for en in sorted(series_map):
        de = series_map[en][0]
        rows.append(f"  ({sql_str(series_codes[en])}, 'de-DE', {sql_str(de)})")
        rows.append(f"  ({sql_str(series_codes[en])}, 'en-GB', {sql_str(en)})")
    w(",\n".join(rows))
    w(") AS v(code, locale, name)")
    w("JOIN taxonomy_node tn ON tn.code = v.code;")
    w("")

    emit_attributes(w, colours)
    emit_products(w, products, series_codes, colours)

    # Refuse to emit a seed that the database would reject. Catching this here
    # costs a second; catching it in Postgres costs a five-minute DB reset and
    # an error buried in a container log.
    validate_unique(
        "taxonomy code",
        list(category_codes.values()) + list(series_codes.values()),
    )
    validate_unique("product code", [p["code"] for p in products])
    validate_unique(
        "sku code", [sku.code for p in products for sku in p["skus"]]
    )

    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"wrote {out} ({out.stat().st_size // 1024} KB)")

    lead = products[0]
    multi = max(products, key=lambda p: len(p["skus"]))
    # A product with SKUs but NO packaging, so the destructive packaging tests
    # have somewhere to work without disturbing the lead product's seeded data.
    spare = next(
        (p for p in products[1:] if len(p["skus"]) >= 2 and p["code"] != multi["code"]),
        products[1],
    )

    by_code = sorted(p["code"] for p in products)
    by_workflow: dict[str, int] = {}
    workflow_of: dict[str, str] = {}
    for i, p in enumerate(products):
        state = WORKFLOWS[i % len(WORKFLOWS)]
        by_workflow[state] = by_workflow.get(state, 0) + 1
        workflow_of[p["code"]] = state

    # A product seeded in Draft, for the workflow-transition checks. The suite
    # moves it Draft -> Review and back again, so it must not be one of the
    # products the other destructive sections use.
    reserved = {lead["code"], spare["code"], multi["code"]} | {
        p["code"] for p in products[:8]
    }
    draft_product = next(
        (p["code"] for p in products
         if workflow_of[p["code"]] == "draft" and p["code"] not in reserved),
        products[3]["code"],
    )

    # Attribute facts. The suite needs, for the lead product: a list attribute
    # with a seeded option, a measure attribute with a unit, a required
    # attribute that IS filled (so clearing it is refused), and an optional
    # attribute that is filled (so clearing it succeeds). Picking these here
    # rather than in the shell means a refreshed export re-picks them.
    required_codes = [c for c, _d, _u, req, _g, _de, _en in ATTRIBUTES if req]
    optional_codes = [c for c, _d, _u, req, _g, _de, _en in ATTRIBUTES if not req]
    lead_attrs: dict[str, str] = lead["attr_values"]

    def pick(codes: list[str], predicate) -> str | None:
        return next((c for c in codes if predicate(c)), None)

    attr_required_filled = pick(required_codes, lambda c: c in lead_attrs)
    attr_optional_filled = pick(optional_codes, lambda c: c in lead_attrs)
    if attr_required_filled is None or attr_optional_filled is None:
        print(
            "FATAL: the lead product has no filled required/optional attribute; "
            "the smoke suite cannot exercise the clear-value paths.",
            file=sys.stderr,
        )
        raise SystemExit(2)

    measure_code = "thickness_mm"
    list_code = "material_type"
    list_options = [code for code, _de, _en in LIST_OPTIONS[list_code]]

    planning_codes = [
        code for code, _d, _u, _r, group, _de, _en in ATTRIBUTES if group == "planning"
    ]
    pending_translations = sum(1 for p in products if p["design_de"])

    def fact(name: str, value: object) -> str:
        """One `NAME='value'` line, shell-quoted.

        The file is `.`-sourced by smoke-api.sh under `set -u`. Product names
        carry spaces ("Craft Extended Abdeckplatte ...") and an unquoted value
        makes the shell read the first word as an assignment and the rest as a
        command, leaving the variable unset. That failed the suite at check 26
        with "SEED_LEAD_NAME_DE: unbound variable". Quote everything, always —
        codes look safe today but are derived from client data.
        """
        return f"{name}={shlex.quote(str(value))}"

    facts = [
        "# GENERATED by scripts/generate-seed.py — sourced by scripts/smoke-api.sh.",
        "# The suite reads these instead of hard-coding product codes and counts,",
        "# so refreshing the seed from a newer client export does not mean editing",
        "# 250 assertions. Regenerate the seed and the suite follows.",
        fact("SEED_PRODUCT_COUNT", len(products)),
        fact("SEED_SERIES_COUNT", len(series_map)),
        fact("SEED_CATEGORY_COUNT", len(categories)),
        "",
        "# The lead product carries the classification, compliance, packaging,",
        "# application and relationship seed data.",
        fact("SEED_LEAD_PRODUCT", lead["code"]),
        fact("SEED_LEAD_SKU_COUNT", len(lead["skus"])),
        fact("SEED_LEAD_SKU", lead["skus"][0].code),
        fact("SEED_LEAD_NAME_DE", lead["name_de"]),
        "",
        "# A second product used for destructive tests, so the lead product is",
        "# left exactly as seeded and the suite stays idempotent.",
        fact("SEED_SPARE_PRODUCT", spare["code"]),
        fact("SEED_SPARE_SKU", spare["skus"][0].code),
        fact("SEED_SPARE_SKU_COUNT", len(spare["skus"])),
        "",
        fact("SEED_MULTI_PRODUCT", multi["code"]),
        fact("SEED_MULTI_SKU_COUNT", len(multi["skus"])),
        "",
        "# Relationship targets seeded against the lead product.",
        fact("SEED_REL_ACCESSORY_1", products[1]["code"]),
        fact("SEED_REL_ACCESSORY_2", products[2]["code"]),
        fact("SEED_REL_UNLINKED", products[7]["code"]),
        "",
        "# Grid assertions.",
        fact("SEED_FIRST_BY_CODE", by_code[0]),
        fact("SEED_DRAFT_COUNT", by_workflow.get("draft", 0)),
        fact("SEED_APPROVAL_COUNT", by_workflow.get("approval", 0)),
        fact("SEED_PUBLISHED_COUNT", by_workflow.get("published", 0)),
        fact("SEED_DRAFT_PRODUCT", draft_product),
        "",
        "# Reference data behind the create wizard.",
        fact("SEED_BRAND_COUNT", 2),
        fact("SEED_BRAND_NAMES", "Agrob Buchtal,DryTile"),
        fact("SEED_FAMILY_COUNT", 1),
        fact("SEED_CATEGORY_CODE", category_codes[lead["category_en"]]),
        "",
        "# Attributes. Codes are picked from the generated set, so a refreshed",
        "# export cannot leave the suite asserting an attribute that is gone.",
        fact("SEED_ATTR_REQUIRED_COUNT", len(required_codes)),
        fact("SEED_ATTR_TOTAL_COUNT", len(ATTRIBUTES)),
        fact("SEED_ATTR_LIST", list_code),
        fact("SEED_ATTR_LIST_OPTIONS", ",".join(list_options)),
        fact("SEED_ATTR_LIST_VALUE", lead_attrs.get(list_code, "")),
        fact("SEED_ATTR_LIST_OTHER", next(o for o in list_options
                                          if o != lead_attrs.get(list_code))),
        fact("SEED_ATTR_MEASURE", measure_code),
        fact("SEED_ATTR_MEASURE_UNIT", "mm"),
        fact("SEED_ATTR_MEASURE_VALUE", lead_attrs.get(measure_code, "")),
        fact("SEED_ATTR_DECIMAL", "pieces_per_m2"),
        fact("SEED_ATTR_BOOLEAN", "frost_resistant"),
        fact("SEED_ATTR_DATE", "available_from"),
        fact("SEED_ATTR_REQUIRED_FILLED", attr_required_filled),
        fact("SEED_ATTR_OPTIONAL_FILLED", attr_optional_filled),
        "",
        "# Supporting modules (representative, not from the client export).",
        fact("SEED_ETIM_CLASS", "EC003001"),
        fact("SEED_ETIM_FEATURE_COUNT", 14),
        fact("SEED_ETIM_FEATURE_FILLED", 12),
        fact("SEED_ETIM_NUMERIC_FEATURE", "EF000009"),
        fact("SEED_ETIM_NUMERIC_VALUE", 300),
        fact("SEED_ETIM_LOGICAL_FEATURE", "EF000110"),
        fact("SEED_ETIM_LOGICAL_VALUE", "false"),
        fact("SEED_ETIM_UNFILLED_FEATURE", "EF000234"),
        fact("SEED_COMPLIANCE_DOC_COUNT", 6),
        fact("SEED_MATERIAL_ROW_COUNT", 3),
        fact("SEED_INDICATOR_COUNT", 2),
        fact("SEED_SUBSTANCE_COUNT", 2),
        fact("SEED_GREEN_BUILDING_COUNT", 3),
        fact("SEED_PKG_TAKEN_GTIN", "4045270000024"),
        fact("SEED_APP_GROUP_COUNT", APPLICATION_GROUP_COUNT),
        fact("SEED_APP_OPTION_COUNT", APPLICATION_OPTION_COUNT),
        fact("SEED_APP_ASSIGNED_COUNT", len(LEAD_APPLICATION_CODES)),
        fact("SEED_APP_CODES_JSON",
             "[" + ",".join(f'"{c}"' for c in LEAD_APPLICATION_CODES) + "]"),
        "",
        "# Asset seed (Sprint 12).",
        fact("SEED_LEAD_MEDIA_COUNT", 4),
        fact("SEED_LEAD_DOCUMENT_COUNT", 4),
        "",
        "# Planning & BIM (Sprint 13). Parameters are ordinary attributes in a",
        "# 'planning' group; the BIM references are classification schemes.",
        fact("SEED_PLANNING_GROUP", "planning"),
        fact("SEED_PLANNING_ATTR_COUNT", len(planning_codes)),
        fact("SEED_PLANNING_MEASURE", "mfg_width_mm"),
        fact("SEED_PLANNING_BOOLEAN", "bim_ready"),
        fact("SEED_PLANNING_DECIMAL", "lrv"),
        fact("SEED_BIM_SCHEMES", "IFC,OMNICLASS,UNICLASS"),
        fact("SEED_BIM_PRIMARY_SCHEME", "IFC"),
        fact("SEED_BIM_CLASS", "IfcCovering.CLADDING"),
        fact("SEED_SCHEME_COUNT", 8),
        # BIM + CAD, seeded on the first 40 products alongside the documents.
        fact("SEED_LEAD_GEOMETRY_COUNT", 2),
        "",
        "# Localization (Sprint 13).",
        fact("SEED_BASE_LOCALE", "de-DE"),
        fact("SEED_TARGET_LOCALE", "en-GB"),
        fact("SEED_ATTR_LOCALIZABLE", sorted(LOCALIZABLE_ATTRIBUTES)[0]),
        fact("SEED_LOCALIZABLE_COUNT", len(LOCALIZABLE_ATTRIBUTES)),
        # Products whose German carries design copy the English lacks — the
        # English row is seeded 'pending' for exactly these.
        fact("SEED_PENDING_TR_COUNT", pending_translations),
        fact("SEED_LEAD_HAS_DESIGN_COPY", "true" if lead["design_de"] else "false"),
    ]
    facts_path.write_text("\n".join(facts) + "\n", encoding="utf-8")
    print(f"wrote {facts_path}")
    print(f"  lead product: {lead['code']} ({len(lead['skus'])} skus)")
    print(f"  series: {len(series_map)}  categories: {len(categories)}  colours: {len(colours)}")


# --------------------------------------------------------------------------
# Attributes
# --------------------------------------------------------------------------

# (code, data_type, unit, is_required, group, label_de, label_en)
#
# WHICH ATTRIBUTES ARE REQUIRED
# Chosen to reflect what a tile actually cannot be sold without — thickness,
# weight per m2 and slip resistance are all commercially essential — and NOT
# tuned to produce a pleasing completeness number. The client's export happens
# to be sparse in exactly those fields, so the resulting completeness spread
# (roughly 58-100%) is a true picture of their data rather than a demo effect.
# An earlier set marked only the always-populated fields required, and every
# one of the 500 products scored 100%, which made the quality bar meaningless.
ATTRIBUTES = [
    ("material_type",    "list",    None, True,  "technical", "Materialart",        "Material type"),
    ("surface_gloss",    "list",    None, True,  "surface",   "Glanzgrad",          "Surface gloss"),
    ("surface_structure","list",    None, True,  "surface",   "Struktur",           "Surface structure"),
    ("colour",           "list",    None, True,  "appearance","Farbe",              "Colour"),
    ("colour_variation", "text",    None, False, "appearance","Farbabweichung",     "Colour shade variation"),
    ("optics",           "text",    None, False, "appearance","Optik",              "Optics"),
    ("glaze",            "list",    None, True,  "technical", "Glasur",             "Glaze"),
    ("frost_resistant",  "boolean", None, True,  "technical", "Frostbeständig",     "Frost resistant"),
    ("slip_resistance",  "text",    None, True , "technical", "Rutschhemmung",      "Slip resistance"),
    ("rectified",        "text",    None, False, "technical", "Kalibriert",         "Rectified"),
    ("finishing",        "list",    None, False, "technical", "Veredelung",         "Finishing"),
    ("use_wall_floor",   "text",    None, True,  "usage",     "Verwendung Wand/Boden", "Use wall/floor"),
    ("use_indoor",       "text",    None, False, "usage",     "Innen/Aussen",       "Indoor/outdoor"),
    ("nominal_size",     "text",    None, True,  "dimensions","Nennmaß",            "Nominal size"),
    ("thickness_mm",     "measure", "mm", True , "dimensions","Materialstärke",     "Material thickness"),
    ("joint_width_mm",   "measure", "mm", False, "dimensions","Fugenbreite",        "Joint width"),
    ("weight_per_m2",    "measure", "kg", True , "logistics", "Gewicht je m²",      "Weight per m²"),
    ("pieces_per_m2",    "decimal", None, True , "logistics", "Stück je m²",        "Pieces per m²"),
    ("price_group",      "text",    None, False, "commercial","Preisgruppe",        "Price group"),
    ("product_standard", "text",    None, False, "technical", "Produktnorm",        "Product standard"),
    # The client export carries no date column, but the attribute engine
    # supports `date` and that path needs coverage in the smoke suite. Declared
    # optional and left unseeded: it is a schema element, not invented data, so
    # it cannot move any product's completeness.
    ("available_from",   "date",    None, False, "commercial","Verfügbar ab",       "Available from"),
    # --- Planning & BIM (Sprint 13) -----------------------------------------
    # Modelled as ordinary attributes in a 'planning' group rather than a
    # separate property-set module (decision 2026-08-16). They are typed
    # product properties like any other; a second module would have duplicated
    # the value storage, the typed write path and the concurrency handling to
    # gain only a different heading. All optional: a planner's parameter set is
    # not a precondition for a product being complete.
    ("bim_ready",        "boolean", None, False, "planning", "BIM-Objekt verfügbar", "BIM object available"),
    ("mfg_width_mm",     "measure", "mm", False, "planning", "Herstellmaß Breite",   "Manufacturing width"),
    ("mfg_length_mm",    "measure", "mm", False, "planning", "Herstellmaß Länge",    "Manufacturing length"),
    ("slip_commercial",  "text",    None, False, "planning", "Rutschhemmung gewerblich (R)", "Slip resistance, commercial (R)"),
    ("slip_barefoot",    "text",    None, False, "planning", "Rutschhemmung barfuß (A/B/C)", "Slip resistance, barefoot (A/B/C)"),
    ("slip_displacement","text",    None, False, "planning", "Verdrängungsraum (V)", "Displacement space (V)"),
    ("load_group",       "text",    None, False, "planning", "Belastungsgruppe",     "Load group"),
    ("wear_group",       "text",    None, False, "planning", "Beanspruchungsgruppe", "Wear group"),
    # Light reflectance value drives daylight and accessibility calculations,
    # which is why a planner wants it on this tab and not buried in Appearance.
    ("lrv",              "decimal", None, False, "planning", "Hellbezugswert Y",     "Light reflectance value Y"),
    ("ral_number",       "text",    None, False, "planning", "RAL-Nummer",           "RAL number"),
    ("ncs_number",       "text",    None, False, "planning", "NCS-Nummer",           "NCS number"),
]

# Attributes whose values differ by language. The Localization tab edits these
# per locale; everything else is language-neutral and is edited once on the
# Attributes tab.
#
# Deliberately just one, and deliberately an OPTIONAL one:
#
#  * The export only supplies a genuine German/English pair for a handful of
#    fields. A "localizable" attribute with one language is a permanently red
#    row no translator can ever clear.
#  * `use_wall_floor` and `use_indoor` also have both languages, but they are
#    REQUIRED. Splitting a required attribute per locale means completeness has
#    to decide whether a product with a German value and no English one counts
#    as filled. That is a real product question (is completeness per-locale?)
#    and it has not been asked yet — so this release does not answer it by
#    accident. See the open question in the release notes.
LOCALIZABLE_ATTRIBUTES = {"product_standard"}

GROUPS = [
    ("technical",  "Technische Daten", "Technical data",  1),
    ("appearance", "Optik",            "Appearance",      2),
    ("surface",    "Oberfläche",       "Surface",         3),
    ("dimensions", "Abmessungen",      "Dimensions",      4),
    ("usage",      "Verwendung",       "Usage",           5),
    ("logistics",  "Logistik",         "Logistics",       6),
    ("commercial", "Kaufmännisch",     "Commercial",      7),
    ("planning",   "Planung & BIM",    "Planning & BIM",  8),
]

LIST_OPTIONS = {
    "material_type": [
        ("stoneware", "Steinzeugplatten", "Stoneware tiles"),
        ("porcelain", "Feinsteinzeug", "Porcelain stoneware tiles"),
        ("earthenware", "Steingutfliesen", "Earthenware tiles"),
        ("conductive", "Ableitfähige Keramikplatten", "Static conducting panels"),
    ],
    "surface_gloss": [
        ("matt", "matt", "matt"),
        ("silky_matt", "seidenmatt", "silky-matt"),
        ("glossy", "glänzend", "glossy"),
        ("stone_matt", "steinmatt", "stone-matt"),
        ("semi_polished", "teilpoliert", "semi-polished"),
        ("polished", "poliert", "polished"),
    ],
    "surface_structure": [
        ("even", "eben", "even"),
        ("structured", "strukturiert", "structured"),
        ("relief", "Relief", "relief"),
    ],
    "glaze": [
        ("glazed", "glasiert", "Glazed"),
        ("unglazed", "unglasiert", "Unglazed"),
    ],
    "finishing": [
        ("hytect", "Hytect", "Hytect"),
        ("protecta", "Protecta", "Protecta"),
        ("without", "ohne", "without"),
    ],
}

GLOSS_MAP = {
    "matt": "matt", "silky-matt": "silky_matt", "glossy": "glossy",
    "stone-matt": "stone_matt", "semi-polished": "semi_polished", "polished": "polished",
}
MATERIAL_MAP = {
    "Stoneware tiles": "stoneware",
    "Porcelain stoneware tiles": "porcelain",
    "Earthenware tiles": "earthenware",
    "Static electricity conducting ceramic panels": "conductive",
}
STRUCTURE_MAP = {"eben": "even", "strukturiert": "structured", "Relief": "relief"}
FINISHING_MAP = {"Hytect": "hytect", "Protecta": "protecta", "without": "without"}


def validate_unique(label: str, values: list[str]) -> None:
    """Aborts generation if any derived code repeats."""
    seen: set[str] = set()
    duplicates = sorted({v for v in values if v in seen or seen.add(v)})
    if duplicates:
        raise SystemExit(
            f"FATAL: duplicate {label}(s) would violate a unique constraint: "
            + ", ".join(duplicates[:10])
        )
    print(f"  {label}: {len(values)} values, all unique")


def emit_attributes(w, colours: dict[str, str]) -> None:
    w("-- Attribute model built from the client's own tile properties: typed,")
    w("-- grouped and unit-bearing, so the Attributes tab renders real controls")
    w("-- (selects for lists, numerics with units) rather than free text.")
    w("INSERT INTO attribute_group (code, sort_order) VALUES")
    w(",\n".join(f"  ({sql_str(c)}, {n})" for c, _, _, n in GROUPS) + ";")
    w("")
    w("INSERT INTO attribute_group_tr (group_id, locale_code, name)")
    w("SELECT g.id, v.locale, v.name FROM (VALUES")
    rows = []
    for code, de, en, _ in GROUPS:
        rows.append(f"  ({sql_str(code)}, 'de-DE', {sql_str(de)})")
        rows.append(f"  ({sql_str(code)}, 'en-GB', {sql_str(en)})")
    w(",\n".join(rows))
    w(") AS v(code, locale, name) JOIN attribute_group g ON g.code = v.code;")
    w("")

    w("INSERT INTO unit (code, symbol) VALUES ('mm', 'mm'), ('kg', 'kg')")
    w("ON CONFLICT DO NOTHING;")
    w("")

    w("INSERT INTO attribute (code, data_type, group_id, unit_id, is_localizable)")
    w("SELECT v.code, v.data_type, g.id, u.id, v.localizable FROM (VALUES")
    rows = []
    for code, dtype, unit, _req, group, _de, _en in ATTRIBUTES:
        localizable = "true" if code in LOCALIZABLE_ATTRIBUTES else "false"
        rows.append(
            f"  ({sql_str(code)}, {sql_str(dtype)}, {sql_str(group)}, "
            f"{sql_str(unit)}, {localizable})"
        )
    w(",\n".join(rows))
    w(") AS v(code, data_type, group_code, unit_code, localizable)")
    w("JOIN attribute_group g ON g.code = v.group_code")
    w("LEFT JOIN unit u ON u.code = v.unit_code;")
    w("")

    w("INSERT INTO attribute_tr (attribute_id, locale_code, label)")
    w("SELECT a.id, v.locale, v.label FROM (VALUES")
    rows = []
    for code, _d, _u, _r, _g, de, en in ATTRIBUTES:
        rows.append(f"  ({sql_str(code)}, 'de-DE', {sql_str(de)})")
        rows.append(f"  ({sql_str(code)}, 'en-GB', {sql_str(en)})")
    w(",\n".join(rows))
    w(") AS v(code, locale, label) JOIN attribute a ON a.code = v.code;")
    w("")

    # Colour is a list attribute whose options are the client's own colour names.
    w("-- Colour options are the client's own colour names, in both locales.")
    w("INSERT INTO attribute_option (attribute_id, code, sort_order)")
    w("SELECT a.id, v.code, v.sort_order FROM (VALUES")
    colour_codes = {}
    colour_taken: set[str] = set()
    rows = []
    for i, en in enumerate(sorted(colours), start=1):
        colour_codes[en] = unique_code(en, colour_taken, 40)
        rows.append(f"  ({sql_str(colour_codes[en])}, {i})")
    w(",\n".join(rows))
    w(") AS v(code, sort_order) JOIN attribute a ON a.code = 'colour';")
    w("")
    w("INSERT INTO attribute_option_tr (option_id, locale_code, label)")
    w("SELECT o.id, v.locale, v.label")
    w("FROM (VALUES")
    rows = []
    for en in sorted(colours):
        rows.append(f"  ({sql_str(colour_codes[en])}, 'de-DE', {sql_str(colours[en])})")
        rows.append(f"  ({sql_str(colour_codes[en])}, 'en-GB', {sql_str(en)})")
    w(",\n".join(rows))
    w(") AS v(code, locale, label)")
    w("JOIN attribute a ON a.code = 'colour'")
    w("JOIN attribute_option o ON o.attribute_id = a.id AND o.code = v.code;")
    w("")

    for attr, options in LIST_OPTIONS.items():
        w(f"INSERT INTO attribute_option (attribute_id, code, sort_order)")
        w("SELECT a.id, v.code, v.sort_order FROM (VALUES")
        w(",\n".join(
            f"  ({sql_str(c)}, {i})" for i, (c, _de, _en) in enumerate(options, start=1)
        ))
        w(f") AS v(code, sort_order) JOIN attribute a ON a.code = {sql_str(attr)};")
        w("INSERT INTO attribute_option_tr (option_id, locale_code, label)")
        w("SELECT o.id, v.locale, v.label FROM (VALUES")
        rows = []
        for c, de, en in options:
            rows.append(f"  ({sql_str(c)}, 'de-DE', {sql_str(de)})")
            rows.append(f"  ({sql_str(c)}, 'en-GB', {sql_str(en)})")
        w(",\n".join(rows))
        w(") AS v(code, locale, label)")
        w(f"JOIN attribute a ON a.code = {sql_str(attr)}")
        w("JOIN attribute_option o ON o.attribute_id = a.id AND o.code = v.code;")
        w("")

    w("INSERT INTO attribute_set (code, name) VALUES")
    w("  ('tile_core', 'Keramik – Kernattribute');")
    w("")
    w("INSERT INTO attribute_set_attribute (set_id, attribute_id, is_required, sort_order)")
    w("SELECT s.id, a.id, v.required, v.sort_order FROM (VALUES")
    rows = []
    for i, (code, _d, _u, req, _g, _de, _en) in enumerate(ATTRIBUTES, start=1):
        rows.append(f"  ({sql_str(code)}, {'true' if req else 'false'}, {i})")
    w(",\n".join(rows))
    w(") AS v(code, required, sort_order)")
    w("JOIN attribute a ON a.code = v.code")
    w("CROSS JOIN attribute_set s WHERE s.code = 'tile_core';")
    w("")

    # Stash for emit_products.
    emit_attributes.colour_codes = colour_codes  # type: ignore[attr-defined]


# --------------------------------------------------------------------------
# Products, SKUs, attribute values
# --------------------------------------------------------------------------

WORKFLOWS = ["published", "approval", "review", "draft"]

# 18 application options are defined; the lead product is assigned these 12.
# Shared between the INSERT and seed-facts.env so the smoke suite's "restore
# the seeded set" step cannot drift from what was actually seeded.
LEAD_APPLICATION_CODES = [
    "commercial", "healthcare", "education", "office", "hospital", "school",
    "facade", "wet_room", "circulation", "wall_cladding", "floor_covering",
    "hygiene_surface",
]
APPLICATION_OPTION_COUNT = 18
APPLICATION_GROUP_COUNT = 4


def numeric(value: str) -> str | None:
    """Client numerics use a comma decimal separator ('8,904')."""
    text = value.replace(".", "").replace(",", ".").strip()
    try:
        return str(float(text))
    except ValueError:
        return None


def emit_products(w, products: list[dict], series_codes: dict, colours: dict) -> None:
    colour_codes = emit_attributes.colour_codes  # type: ignore[attr-defined]

    w("-- Products. workflow_state is spread deterministically across the four")
    w("-- states so the grid's status filter and the workflow stepper have")
    w("-- something to show; it is not part of the client's data.")
    w("CREATE TEMP TABLE seed_products (")
    w("  code text, series_code text, brand text, wf text,")
    w("  name_de text, name_en text, variants int, design_de text")
    w(");")
    w("")
    w("INSERT INTO seed_products VALUES")
    rows = []
    for i, p in enumerate(products):
        rows.append(
            "  ({}, {}, {}, {}, {}, {}, {}, {})".format(
                sql_str(p["code"]),
                sql_str(series_codes[p["series_en"]]),
                sql_str(p["brand"]),
                sql_str(WORKFLOWS[i % len(WORKFLOWS)]),
                sql_str(p["name_de"]),
                sql_str(p["name_en"]),
                p["total_variants"],
                sql_str(p["design_de"]),
            )
        )
    w(",\n".join(rows) + ";")
    w("")

    w("INSERT INTO product (product_code, primary_node_id, brand_id, attribute_set_id,")
    w("                     base_locale, lifecycle_state, workflow_state, owner_display_name, updated_at)")
    w("SELECT sp.code, tn.id, b.id, s.id, 'de-DE', 'ACTIVE', sp.wf,")
    w("       (ARRAY['Anna Becker','Markus Klein','Lena Vogt','Sophie Adler'])[1 + (row_number() OVER (ORDER BY sp.code) % 4)],")
    w("       now() - ((row_number() OVER (ORDER BY sp.code)) || ' hours')::interval")
    w("FROM seed_products sp")
    w("JOIN taxonomy_node tn ON tn.code = sp.series_code")
    w("JOIN brand b ON b.code = sp.brand")
    w("CROSS JOIN attribute_set s WHERE s.code = 'tile_core';")
    w("")

    w("-- Names in both locales: the client team reads German, the export is English.")
    w("-- German is the base locale and carries the design copy; English has a")
    w("-- name but no description anywhere in the export. That gap is real and")
    w("-- is left in place — it is what the Localization tab exists to show.")
    w("INSERT INTO product_tr (product_id, locale_code, name, short_desc,")
    w("                        translation_status, source)")
    w("SELECT p.id, 'de-DE', sp.name_de, nullif(sp.design_de, ''), 'approved', 'human'")
    w("FROM seed_products sp JOIN product p ON p.product_code = sp.code;")
    w("")
    w("INSERT INTO product_tr (product_id, locale_code, name, short_desc,")
    w("                        translation_status, source)")
    w("SELECT p.id, 'en-GB', sp.name_en, NULL,")
    w("       CASE WHEN nullif(sp.design_de, '') IS NULL THEN 'approved'")
    w("            ELSE 'pending' END,")
    w("       'human'")
    w("FROM seed_products sp JOIN product p ON p.product_code = sp.code;")
    w("")

    # ---- SKUs
    w("-- SKUs are the client's real article numbers; colour is the variant axis.")
    w("INSERT INTO sku (product_id, sku_code, gtin, sort_order)")
    w("SELECT p.id, v.sku_code, NULL, v.sort_order FROM (VALUES")
    rows = []
    for p in products:
        for n, sku in enumerate(p["skus"], start=1):
            rows.append(f"  ({sql_str(p['code'])}, {sql_str(sku.code)}, {n})")
    w(",\n".join(rows))
    w(") AS v(product_code, sku_code, sort_order)")
    w("JOIN product p ON p.product_code = v.product_code;")
    w("")

    # ---- attribute values
    w("-- Attribute values, straight from the client's export.")
    text_rows: list[str] = []
    number_rows: list[str] = []
    bool_rows: list[str] = []
    option_rows: list[str] = []
    # attribute_value.locale_code is NULL for language-neutral values; these
    # carry one row per locale instead. ux_av includes coalesce(locale_code,'')
    # so both shapes coexist for different attributes without colliding.
    locale_text_rows: list[str] = []

    for p in products:
        lead = p["lead"]
        # Which attributes this product ends up with, and what value the API
        # will read back. The smoke suite asserts against these instead of
        # against literals, so a refreshed export cannot leave the suite
        # asserting values that are no longer in the database.
        seeded: dict[str, str] = {}
        p["attr_values"] = seeded

        def add_text(attr: str, value: str) -> None:
            if value:
                text_rows.append(
                    f"  ({sql_str(p['code'])}, {sql_str(attr)}, {sql_str(value)})"
                )
                seeded[attr] = value

        def add_option(attr: str, code: str | None) -> None:
            if code:
                option_rows.append(
                    f"  ({sql_str(p['code'])}, {sql_str(attr)}, {sql_str(code)})"
                )
                seeded[attr] = code

        def add_localized_text(attr: str, de: str, en: str) -> None:
            """One row per locale for a localizable attribute.

            A locale with no value in the export gets NO row, not an empty one:
            'missing' is the honest state and it is what the Localization tab
            exists to surface.
            """
            for locale, value in (("de-DE", de), ("en-GB", en)):
                if value:
                    locale_text_rows.append(
                        f"  ({sql_str(p['code'])}, {sql_str(attr)}, "
                        f"{sql_str(locale)}, {sql_str(value)})"
                    )
                    if locale == "de-DE":
                        seeded[attr] = value

        def add_number(attr: str, value: str) -> None:
            parsed = numeric(value)
            if parsed:
                number_rows.append(
                    f"  ({sql_str(p['code'])}, {sql_str(attr)}, {parsed})"
                )
                seeded[attr] = parsed

        add_option("material_type", MATERIAL_MAP.get(lead.x("MaterialType")))
        add_option("surface_gloss", GLOSS_MAP.get(lead.x("SurfaceGloss")))
        add_option("surface_structure", STRUCTURE_MAP.get(lead.c(C_STRUCTURE)))
        add_option("glaze", "glazed" if lead.x("Standard: Glaze [EN]") == "Glazed"
                   else "unglazed" if lead.x("Standard: Glaze [EN]") else None)
        add_option("finishing", FINISHING_MAP.get(lead.x("Finishing")))

        colour_en = lead.c(C_COLOR_EN)
        add_option("colour", colour_codes.get(colour_en))

        add_text("colour_variation", lead.x("Color: Color Shade Variation [Maintenance]"))
        add_text("optics", lead.x("Optics"))
        add_text("slip_resistance", lead.x("SlipResistance"))
        add_text("rectified", lead.x("Rectified"))
        add_text("use_wall_floor", lead.x("Use: Wall|Floor [EN] | [AUTO]"))
        add_text("use_indoor", lead.x("Use: Indoor|Outdoor [EN] | [Auto]"))
        add_text("nominal_size", p["format_en"])
        add_text("price_group", lead.x("Price group total"))
        add_localized_text(
            "product_standard",
            lead.c(C_STANDARD_TEXT_DE),
            lead.c(C_STANDARD_TEXT_EN),
        )

        # --- Planning & BIM ------------------------------------------------
        add_number("mfg_width_mm", lead.c(C_MFG_WIDTH_MM))
        add_number("mfg_length_mm", lead.c(C_MFG_LENGTH_MM))
        add_number("lrv", lead.c(C_LRV))
        # '-' is how the export writes "rated, but not in this dimension".
        for attr, column in (
            ("slip_commercial", C_SLIP_COMMERCIAL),
            ("slip_barefoot", C_SLIP_BAREFOOT),
            ("slip_displacement", C_SLIP_DISPLACEMENT),
            ("load_group", C_LOAD_GROUP),
            ("wear_group", C_WEAR_GROUP),
            ("ral_number", C_RAL),
            ("ncs_number", C_NCS),
        ):
            value = lead.c(column)
            if value and value != "-":
                add_text(attr, value)

        bim = lead.c(C_BIM_READY)
        if bim in ("ja/yes", "nein/no"):
            bool_rows.append(
                f"  ({sql_str(p['code'])}, 'bim_ready', {'true' if bim == 'ja/yes' else 'false'})"
            )
            seeded["bim_ready"] = "true" if bim == "ja/yes" else "false"

        thickness = lead.x("MaterialThickness").replace(" mm", "")
        add_number("thickness_mm", thickness)
        add_number("joint_width_mm", lead.x("Format: Joint width+mm [Auto]").replace(" mm", ""))
        add_number("weight_per_m2", lead.x("Weight per piece/pair/m²/m [Logistics]|2"))
        add_number("pieces_per_m2", lead.x("Number of pieces/sheets per m²/m [AUTO] | [Logistics]"))

        frost = lead.x("ResistancetoFrost")
        if frost in ("Yes", "No"):
            bool_rows.append(
                f"  ({sql_str(p['code'])}, 'frost_resistant', {'true' if frost == 'Yes' else 'false'})"
            )
            seeded["frost_resistant"] = "true" if frost == "Yes" else "false"

    def emit_values(rows: list[str], column: str, cast: str = "") -> None:
        if not rows:
            return
        w(f"INSERT INTO attribute_value (product_id, attribute_id, {column}, data_state)")
        w(f"SELECT p.id, a.id, v.val{cast}, 'approved' FROM (VALUES")
        w(",\n".join(rows))
        w(") AS v(product_code, attr_code, val)")
        w("JOIN product p ON p.product_code = v.product_code")
        w("JOIN attribute a ON a.code = v.attr_code;")
        w("")

    emit_values(text_rows, "value_text")
    emit_values(number_rows, "value_number", "::numeric")
    emit_values(bool_rows, "value_boolean", "::boolean")

    if locale_text_rows:
        w("-- Localizable attribute values: one row per locale.")
        w("INSERT INTO attribute_value (product_id, attribute_id, locale_code,")
        w("                             value_text, data_state)")
        w("SELECT p.id, a.id, v.locale, v.val, 'approved' FROM (VALUES")
        w(",\n".join(locale_text_rows))
        w(") AS v(product_code, attr_code, locale, val)")
        w("JOIN product p ON p.product_code = v.product_code")
        w("JOIN attribute a ON a.code = v.attr_code;")
        w("")

    if option_rows:
        w("INSERT INTO attribute_value (product_id, attribute_id, value_option_id, data_state)")
        w("SELECT p.id, a.id, o.id, 'approved' FROM (VALUES")
        w(",\n".join(option_rows))
        w(") AS v(product_code, attr_code, option_code)")
        w("JOIN product p ON p.product_code = v.product_code")
        w("JOIN attribute a ON a.code = v.attr_code")
        w("JOIN attribute_option o ON o.attribute_id = a.id AND o.code = v.option_code;")
        w("")

    emit_assets(w, products)
    emit_supporting_modules(w, products)


def emit_assets(w, products: list[dict]) -> None:
    """
    Digital assets from the client's own ImageURL column.

    The URLs point at the client's Magento server. They are stored as the
    storage_key with provider 'client-magento'; the UI loads them directly and
    handles an unreachable host explicitly, rather than pretending the asset
    does not exist.
    """
    w("-- =============================================================")
    w("-- Digital assets. IMAGE rows are the client's real ImageURL values;")
    w("-- provider 'client-magento' records that the binary lives on their")
    w("-- server, not ours. DOCUMENT/BIM/CAD rows are representative — the")
    w("-- export carries no document URLs, and inventing plausible-looking")
    w("-- links to their server would be worse than clearly-synthetic ones.")
    w("-- =============================================================")

    asset_rows: list[str] = []
    link_rows: list[str] = []
    tr_rows: list[str] = []
    seen: set[str] = set()

    for p in products:
        urls: list[str] = []
        for sku in p["skus"]:
            raw = sku.x("ImageURL")
            for url in (u.strip() for u in raw.split(",") if u.strip()):
                if url not in seen:
                    seen.add(url)
                    urls.append(url)
            if len(urls) >= 4:
                break

        for n, url in enumerate(urls[:4]):
            file_name = url.rsplit("/", 1)[-1]
            asset_rows.append(
                "  ({}, {}, 'image/jpeg', 'IMAGE')".format(sql_str(url), sql_str(file_name))
            )
            link_rows.append(
                "  ({}, {}, {}, {}, {})".format(
                    sql_str(p["code"]), sql_str(url),
                    sql_str("MAIN" if n == 0 else "GALLERY"),
                    "true" if n == 0 else "false", n,
                )
            )
            tr_rows.append(
                "  ({}, 'de-DE', {}, {})".format(
                    sql_str(url), sql_str(p["name_de"]), sql_str(f"{p['name_de']} – Produktbild")
                )
            )
            tr_rows.append(
                "  ({}, 'en-GB', {}, {})".format(
                    sql_str(url), sql_str(p["name_en"]), sql_str(f"{p['name_en']} – product image")
                )
            )

    # A representative document set on the first 40 products, so the Documents
    # half of the tab is populated without fabricating client URLs.
    doc_specs = [
        ("DATASHEET", "DOCUMENT", "application/pdf", "Technisches Datenblatt", "Technical datasheet"),
        ("INSTALL", "DOCUMENT", "application/pdf", "Verlegeanleitung", "Installation guide"),
        ("BIM", "BIM", "application/octet-stream", "BIM-Objekt", "BIM object"),
        ("DRAWING", "CAD", "image/vnd.dxf", "CAD-Zeichnung", "CAD drawing"),
    ]
    for p in products[:40]:
        for role, atype, mime, title_de, title_en in doc_specs:
            key = f"local://docs/{p['code']}/{role.lower()}"
            ext = {"DOCUMENT": "pdf", "BIM": "ifc", "CAD": "dxf"}[atype]
            asset_rows.append(
                "  ({}, {}, {}, {})".format(
                    sql_str(key), sql_str(f"{p['code']}-{role.lower()}.{ext}"),
                    sql_str(mime), sql_str(atype),
                )
            )
            link_rows.append(
                "  ({}, {}, {}, false, 0)".format(sql_str(p["code"]), sql_str(key), sql_str(role))
            )
            tr_rows.append(f"  ({sql_str(key)}, 'de-DE', {sql_str(title_de)}, NULL)")
            tr_rows.append(f"  ({sql_str(key)}, 'en-GB', {sql_str(title_en)}, NULL)")

    w("INSERT INTO asset (storage_provider, storage_key, file_name, mime_type, asset_type)")
    w("SELECT CASE WHEN v.storage_key LIKE 'http%' THEN 'client-magento' ELSE 'local' END,")
    w("       v.storage_key, v.file_name, v.mime_type, v.asset_type")
    w("FROM (VALUES")
    w(",\n".join(asset_rows))
    w(") AS v(storage_key, file_name, mime_type, asset_type);")
    w("")

    w("INSERT INTO asset_tr (asset_id, locale_code, title, alt_text)")
    w("SELECT a.id, v.locale, v.title, v.alt_text FROM (VALUES")
    w(",\n".join(tr_rows))
    w(") AS v(storage_key, locale, title, alt_text)")
    w("JOIN asset a ON a.storage_key = v.storage_key;")
    w("")

    w("INSERT INTO asset_link (product_id, asset_id, role, is_primary, sort_order)")
    w("SELECT p.id, a.id, v.role, v.is_primary, v.sort_order FROM (VALUES")
    w(",\n".join(link_rows))
    w(") AS v(product_code, storage_key, role, is_primary, sort_order)")
    w("JOIN product p ON p.product_code = v.product_code")
    w("JOIN asset a ON a.storage_key = v.storage_key;")
    w("")

    w("-- A web rendition per image so the gallery can request a smaller file")
    w("-- later without changing the link. Same URI for now: the client's server")
    w("-- exposes one size.")
    w("INSERT INTO asset_rendition (asset_id, role, uri, format)")
    w("SELECT a.id, 'web', a.storage_key, 'jpg'")
    w("FROM asset a WHERE a.asset_type = 'IMAGE';")
    w("")


def emit_supporting_modules(w, products: list[dict]) -> None:
    """
    Classification, compliance, packaging, applications and relationships for
    the lead products.

    The client export does not carry this data, so it is representative rather
    than real — but it is representative OF CERAMIC TILES (ETIM EC003001, DIN EN
    14411, R-ratings, m2-per-pallet) rather than of the fire doors the previous
    seed used. A steward opening these tabs should recognise the shape of the
    data even where the values are ours.
    """
    lead = products[0]
    demo = products[:12]

    w("-- =============================================================")
    w("-- Supporting modules for the remaining tabs. NOT from the client")
    w("-- export — representative values, but representative of ceramic")
    w("-- tiles (ETIM EC003001, DIN EN 14411, R-ratings) rather than of")
    w("-- the fire doors the earlier demo seed used.")
    w("-- =============================================================")

    w("INSERT INTO classification_scheme (code, version, name, purpose, sort_order) VALUES")
    w("  ('ETIM',     '9.0',  'ETIM 9.0',              'Trade partner exchange (BMEcat/ETIM)', 1),")
    w("  ('DATANORM', '5.0',  'DataNorm 5.0',          'SAP / wholesaler price and article feed', 2),")
    w("  ('BMECAT',   '2005', 'BMEcat 2005',           'Catalogue interchange format', 3),")
    w("  ('UNSPSC',   '23',   'UNSPSC',                'Procurement and spend classification', 4),")
    w("  ('SALESHIER','1.0',  'Custom Sales Hierarchy','Internal sales reporting rollup', 5),")
    # The three schemes a BIM authoring tool reads. They are classifications
    # like any other, so they reuse the Sprint 8 module rather than getting a
    # bespoke 'bim refs' table; the Planning tab shows this subset.
    w("  ('IFC',      '4.3',  'IFC 4.3',               'BIM authoring — entity and predefined type', 6),")
    w("  ('OMNICLASS','2012', 'OmniClass Table 23',    'BIM product classification (North America)', 7),")
    w("  ('UNICLASS', '2015', 'Uniclass 2015',         'BIM product classification (UK)', 8);")
    w("")
    w("INSERT INTO classification_class (scheme_id, code, label, path)")
    w("SELECT s.id, v.code, v.label, v.path::ltree FROM (VALUES")
    w("  ('ETIM',     'EC003001', 'Ceramic tile',            'etim.ec003001'),")
    w("  ('ETIM',     'EC003002', 'Mosaic tile',             'etim.ec003002'),")
    w("  ('ETIM',     'EC010101', 'Skirting',                'etim.ec010101'),")
    w("  ('DATANORM', '100',      'Satzart 100 / 200',       'datanorm.a100'),")
    w("  ('BMECAT',   'T-CER-01', 'Catalog group T-CER-01',  'bmecat.t_cer_01'),")
    w("  ('UNSPSC',   '30161702', 'Ceramic tiles',           'unspsc.n30161702'),")
    w("  ('SALESHIER','TILE-WALL-CER', 'Tiles > Wall > Ceramic', 'sales.tile.wall.cer'),")
    w("  ('IFC',      'IfcCovering.FLOORING', 'IfcCovering / FLOORING', 'ifc.covering.flooring'),")
    w("  ('IFC',      'IfcCovering.CLADDING', 'IfcCovering / CLADDING', 'ifc.covering.cladding'),")
    w("  ('OMNICLASS','23-15 11 17 11', 'Ceramic Tile Flooring',  'omniclass.n23_15_11_17_11'),")
    w("  ('UNICLASS', 'Pr_25_57_90_18', 'Ceramic tiles',          'uniclass.pr_25_57_90_18')")
    w(") AS v(scheme, code, label, path) JOIN classification_scheme s ON s.code = v.scheme;")
    w("")
    w("-- ETIM 9.0 declares 14 features for the ceramic tile class.")
    w("INSERT INTO classification_feature (scheme_id, class_id, code, name, data_type, unit, is_required, sort_order)")
    w("SELECT s.id, c.id, v.code, v.name, v.data_type, v.unit, v.req, v.sort_order")
    w("FROM classification_scheme s")
    w("JOIN classification_class c ON c.scheme_id = s.id AND c.code = 'EC003001'")
    w("CROSS JOIN (VALUES")
    w("  ('EF000008', 'Length',                'numeric', 'mm', true,   1),")
    w("  ('EF000009', 'Width',                 'numeric', 'mm', true,   2),")
    w("  ('EF000011', 'Thickness',             'numeric', 'mm', true,   3),")
    w("  ('EF000007', 'Material',              'alpha',   NULL, true,   4),")
    w("  ('EF000234', 'Frost resistant',       'logical', NULL, true,   5),")
    w("  ('EF000099', 'Surface finish',        'alpha',   NULL, false,  6),")
    w("  ('EF000012', 'Colour',                'alpha',   NULL, false,  7),")
    w("  ('EF000045', 'Weight per m2',         'numeric', 'kg', false,  8),")
    w("  ('EF000067', 'Slip resistance class', 'alpha',   NULL, false,  9),")
    w("  ('EF000088', 'Suitable for walls',    'logical', NULL, false, 10),")
    w("  ('EF000110', 'Suitable for floors',   'logical', NULL, false, 11),")
    w("  ('EF000121', 'Glazed',                'logical', NULL, false, 12),")
    w("  ('EF000132', 'Abrasion class',        'alpha',   NULL, false, 13),")
    w("  ('EF000143', 'Water absorption',      'range',   NULL, false, 14)")
    w(") AS v(code, name, data_type, unit, req, sort_order)")
    w("WHERE s.code = 'ETIM';")
    w("")

    w("INSERT INTO product_classification (product_id, scheme_id, class_id, is_primary, mapped_status)")
    w("SELECT p.id, s.id, c.id, v.is_primary, 'partial'")
    w("FROM product p")
    w("JOIN (VALUES")
    w("  ('ETIM',     'EC003001',      true),")
    w("  ('DATANORM', '100',           false),")
    w("  ('BMECAT',   'T-CER-01',      false),")
    w("  ('UNSPSC',   '30161702',      false),")
    w("  ('SALESHIER','TILE-WALL-CER', false),")
    w("  ('IFC',      'IfcCovering.CLADDING', false),")
    # Uniclass is left unassigned on purpose: it is the UK scheme and this is a
    # German catalogue, so the Planning tab has a real gap to report rather
    # than three green rows that prove nothing.
    w("  ('OMNICLASS','23-15 11 17 11', false)")
    w(") AS v(scheme, class_code, is_primary) ON true")
    w("JOIN classification_scheme s ON s.code = v.scheme")
    w("JOIN classification_class c ON c.scheme_id = s.id AND c.code = v.class_code")
    w(f"WHERE p.product_code = {sql_str(lead['code'])};")
    w("")
    w("-- 12 of the 14 features filled, so the tab's completeness figure is")
    w("-- computed from real rows and the missing-feature path has coverage.")
    w("INSERT INTO product_classification_value (product_id, feature_id, value_text, value_number, value_bool, value_unit)")
    w("SELECT p.id, f.id, v.t, v.n, v.b, f.unit")
    w("FROM product p")
    w("JOIN classification_scheme s ON s.code = 'ETIM'")
    w("JOIN classification_feature f ON f.scheme_id = s.id")
    w("JOIN (VALUES")
    w("  ('EF000008', NULL,               310::numeric, NULL::boolean),")
    w("  ('EF000009', NULL,               300,          NULL),")
    w("  ('EF000011', NULL,               15,           NULL),")
    w("  ('EF000007', 'Stoneware',        NULL,         NULL),")
    w("  ('EF000099', 'Glazed, glossy',   NULL,         NULL),")
    w("  ('EF000012', 'Reed green',       NULL,         NULL),")
    w("  ('EF000045', NULL,               22.08,        NULL),")
    w("  ('EF000067', 'no slip resistance', NULL,       NULL),")
    w("  ('EF000088', NULL,               NULL,         true),")
    w("  ('EF000110', NULL,               NULL,         false),")
    w("  ('EF000121', NULL,               NULL,         true),")
    w("  ('EF000132', 'PEI IV',           NULL,         NULL)")
    w(") AS v(code, t, n, b) ON v.code = f.code")
    w(f"WHERE p.product_code = {sql_str(lead['code'])};")
    w("")

    w("-- Certifications. The UKCA row is dated RELATIVE to seed time so the")
    w("-- 'expiring within 12 months' banner always has exactly one case to find;")
    w("-- a fixed date would stop exercising that path as time passed.")
    w("INSERT INTO compliance_document (product_id, doc_type, reference, standard, authority, valid_from, valid_to)")
    w("SELECT p.id, v.doc_type, v.reference, v.standard, v.authority, v.valid_from::date, v.valid_to")
    w("FROM product p JOIN (VALUES")
    w("  ('CE',   'CE-AB-2025-0411', 'EN 14411',             'Notified Body 0761',  '2025-01-01', '2029-12-31'::date),")
    w("  ('TEST', 'EN-ISO-10545-2',  'EN ISO 10545-2',       'FGK Höhr-Grenzhausen','2025-01-01', '2029-12-31'::date),")
    w("  ('TEST', 'DIN-51130-2025',  'DIN 51130 (R rating)', 'TÜV Rheinland',       '2025-01-01', '2029-12-31'::date),")
    w("  ('UKCA', 'UKCA-AB-2025-77', 'UKCA',                 'BM TRADA',            '2025-01-01',")
    w("           (now() + interval '8 months')::date),")
    w("  ('DOP',  'DoP-AB-14411-01', 'CPR 305/2011 / EN 14411', 'Agrob Buchtal GmbH', '2025-01-01', NULL),")
    w("  ('EPD',  'EPD-AB-00417',    'ISO 14025 / EN 15804+A2', 'IBU',                '2025-01-01', '2029-12-31'::date)")
    w(") AS v(doc_type, reference, standard, authority, valid_from, valid_to) ON true")
    w(f"WHERE p.product_code = {sql_str(lead['code'])};")
    w("")
    w("INSERT INTO sustainability_indicator (product_id, epd_doc_id, indicator, value, unit, method, stage)")
    w("SELECT p.id, d.id, v.indicator, v.value, v.unit, 'EN 15804+A2', v.stage")
    w("FROM product p")
    w("LEFT JOIN compliance_document d ON d.product_id = p.id AND d.doc_type = 'EPD'")
    w("JOIN (VALUES ('GWP', 9.7::numeric, 'kg CO2-eq', 'A1-A3'), ('recycled_content', 18, '%', NULL))")
    w("  AS v(indicator, value, unit, stage) ON true")
    w(f"WHERE p.product_code = {sql_str(lead['code'])};")
    w("")
    w("INSERT INTO material_composition (product_id, material, pct, is_recyclable, sort_order)")
    w("SELECT p.id, v.material, v.pct, v.recyclable, v.sort_order")
    w("FROM product p JOIN (VALUES")
    w("  ('Clay and feldspar body', 88::numeric, true,  1),")
    w("  ('Glaze',                   9,          false, 2),")
    w("  ('Pigments',                3,          false, 3)")
    w(") AS v(material, pct, recyclable, sort_order) ON true")
    w(f"WHERE p.product_code = {sql_str(lead['code'])};")
    w("")
    w("INSERT INTO substance_compliance (product_id, regime, result, threshold, notes)")
    w("SELECT p.id, v.regime, v.result, v.threshold, v.notes")
    w("FROM product p JOIN (VALUES")
    w("  ('REACH_SVHC', 'compliant', '0.1% w/w', 'No SVHC above threshold'),")
    w("  ('RoHS',       'compliant', NULL,       NULL)")
    w(") AS v(regime, result, threshold, notes) ON true")
    w(f"WHERE p.product_code = {sql_str(lead['code'])};")
    w("")
    w("INSERT INTO green_building_contribution (product_id, scheme, credit, note)")
    w("SELECT p.id, v.scheme, v.credit, v.note")
    w("FROM product p JOIN (VALUES")
    w("  ('DGNB',      'ENV1.1, ENV1.2',  'Contributes'),")
    w("  ('LEED v4.1', 'MR Credit (EPD)', NULL),")
    w("  ('BREEAM',    'Mat 01',          NULL)")
    w(") AS v(scheme, credit, note) ON true")
    w(f"WHERE p.product_code = {sql_str(lead['code'])};")
    w("")

    w("-- Packaging on the first SKU of the lead product: tiles ship in cartons")
    w("-- on pallets, priced and shipped by m2.")
    w("INSERT INTO packaging_unit (sku_id, level, contains_qty, gtin, gross_weight, net_weight,")
    w("                            length_mm, width_mm, height_mm, is_base_unit)")
    w("SELECT s.id, 'EACH', 1, '4045270000017', 2.1, 2.0, 310, 300, 15, true")
    w("FROM sku s JOIN product p ON p.id = s.product_id")
    w(f"WHERE p.product_code = {sql_str(lead['code'])} AND s.sort_order = 1;")
    w("")
    w("INSERT INTO packaging_unit (sku_id, parent_id, level, contains_qty, gtin, gross_weight,")
    w("                            net_weight, length_mm, width_mm, height_mm)")
    w("SELECT s.id, pu.id, 'CARTON', 8, '4045270000024', 18.5, 17.2, 330, 320, 140")
    w("FROM sku s JOIN product p ON p.id = s.product_id")
    w("JOIN packaging_unit pu ON pu.sku_id = s.id AND pu.level = 'EACH'")
    w(f"WHERE p.product_code = {sql_str(lead['code'])} AND s.sort_order = 1;")
    w("")
    w("INSERT INTO packaging_unit (sku_id, parent_id, level, contains_qty, sscc, gross_weight,")
    w("                            net_weight, length_mm, width_mm, height_mm)")
    w("SELECT s.id, pu.id, 'PALLET', 40, '340452700000019', 765, 688, 1200, 800, 1050")
    w("FROM sku s JOIN product p ON p.id = s.product_id")
    w("JOIN packaging_unit pu ON pu.sku_id = s.id AND pu.level = 'CARTON'")
    w(f"WHERE p.product_code = {sql_str(lead['code'])} AND s.sort_order = 1;")
    w("")

    w("INSERT INTO application (group_code, code, sort_order) VALUES")
    w("  ('INDUSTRY',      'commercial',   1), ('INDUSTRY',      'healthcare',   2),")
    w("  ('INDUSTRY',      'education',    3), ('INDUSTRY',      'hospitality',  4),")
    w("  ('INDUSTRY',      'residential',  5),")
    w("  ('BUILDING_TYPE', 'office',       1), ('BUILDING_TYPE', 'hospital',     2),")
    w("  ('BUILDING_TYPE', 'school',       3), ('BUILDING_TYPE', 'hotel',        4),")
    w("  ('BUILDING_TYPE', 'swimming_pool',5),")
    w("  ('AREA',          'facade',       1), ('AREA',          'wet_room',     2),")
    w("  ('AREA',          'kitchen',      3), ('AREA',          'circulation',  4),")
    w("  ('USE_CASE',      'wall_cladding',1), ('USE_CASE',      'floor_covering',2),")
    w("  ('USE_CASE',      'pool_lining',  3), ('USE_CASE',      'hygiene_surface',4);")
    w("")
    w("INSERT INTO application_tr (application_id, locale_code, label)")
    w("SELECT a.id, 'en-GB', v.label FROM application a JOIN (VALUES")
    w("  ('commercial','Commercial'), ('healthcare','Healthcare'), ('education','Education'),")
    w("  ('hospitality','Hospitality'), ('residential','Residential'),")
    w("  ('office','Office'), ('hospital','Hospital'), ('school','School'),")
    w("  ('hotel','Hotel'), ('swimming_pool','Swimming pool'),")
    w("  ('facade','Facade'), ('wet_room','Wet room'), ('kitchen','Kitchen'),")
    w("  ('circulation','Circulation area'),")
    w("  ('wall_cladding','Wall cladding'), ('floor_covering','Floor covering'),")
    w("  ('pool_lining','Pool lining'), ('hygiene_surface','Hygiene surface')")
    w(") AS v(code, label) ON v.code = a.code;")
    w("")
    w("INSERT INTO application_tr (application_id, locale_code, label)")
    w("SELECT a.id, 'de-DE', v.label FROM application a JOIN (VALUES")
    w("  ('commercial','Gewerbe'), ('healthcare','Gesundheitswesen'), ('education','Bildung'),")
    w("  ('hospitality','Hotellerie'), ('residential','Wohnbau'),")
    w("  ('office','Büro'), ('hospital','Krankenhaus'), ('school','Schule'),")
    w("  ('hotel','Hotel'), ('swimming_pool','Schwimmbad'),")
    w("  ('facade','Fassade'), ('wet_room','Nassraum'), ('kitchen','Küche'),")
    w("  ('circulation','Verkehrsfläche'),")
    w("  ('wall_cladding','Wandbekleidung'), ('floor_covering','Bodenbelag'),")
    w("  ('pool_lining','Beckenauskleidung'), ('hygiene_surface','Hygienefläche')")
    w(") AS v(code, label) ON v.code = a.code;")
    w("")
    w("INSERT INTO product_application (product_id, application_id)")
    w("SELECT p.id, a.id FROM product p JOIN application a ON a.code IN")
    w("  (" + ", ".join(sql_str(c) for c in LEAD_APPLICATION_CODES) + ")")
    w(f"WHERE p.product_code = {sql_str(lead['code'])};")
    w("")

    w("-- Relationships between products that exist in this seed, so every link")
    w("-- resolves to a real record rather than a dangling name.")
    w("INSERT INTO product_relationship (from_product_id, to_product_id, relation_type, priority)")
    w("SELECT f.id, t.id, v.relation_type, v.priority FROM (VALUES")
    rels = []
    types = ["ACCESSORY", "ACCESSORY", "COMPATIBLE", "COMPATIBLE", "CROSS_SELL", "UP_SELL"]
    for n, other in enumerate(demo[1:7]):
        rels.append(
            f"  ({sql_str(lead['code'])}, {sql_str(other['code'])}, {sql_str(types[n])}, {n + 1})"
        )
    w(",\n".join(rels))
    w(") AS v(from_code, to_code, relation_type, priority)")
    w("JOIN product f ON f.product_code = v.from_code")
    w("JOIN product t ON t.product_code = v.to_code;")
    w("")


if __name__ == "__main__":
    sys.exit(main())
