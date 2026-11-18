-- =============================================================
-- Catalog service schema — carved from approved pim_database_v2.0.sql
-- Owned exclusively by catalog-service (schema-per-service, ADR-005).
-- Cross-domain FKs (identity, assets, pricing, channels) removed;
-- columns retained as logical references. Source of truth remains
-- pim_database_v2.0.sql; keep in sync via reviewed migrations.
-- NOT YET APPLIED/TESTED against a live PostgreSQL — validate on first
-- `docker compose up`.
-- =============================================================
CREATE SCHEMA IF NOT EXISTS catalog;
SET search_path TO catalog;

CREATE EXTENSION IF NOT EXISTS ltree;

CREATE TABLE locale (
  id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  code                text NOT NULL UNIQUE,            -- BCP-47: 'de-DE','en-GB'
  name                text NOT NULL,                   -- endonym / display
  fallback_locale_id  bigint REFERENCES locale(id),    -- fallback chain
  is_base             boolean NOT NULL DEFAULT false,  -- master authoring language
  direction           text NOT NULL DEFAULT 'ltr' CHECK (direction IN ('ltr','rtl')),
  is_active           boolean NOT NULL DEFAULT true
);

CREATE INDEX ix_locale_active ON locale(is_active) WHERE is_active;

CREATE TABLE unit (
  id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  code          text NOT NULL UNIQUE,                  -- 'mm','kg','min'
  symbol        text,                                  -- neutral glyph
  dimension     text,                                  -- length / mass / time ...
  to_si_factor  numeric                                -- conversion to SI base
);

CREATE TABLE unit_tr (
  unit_id     bigint NOT NULL REFERENCES unit(id) ON DELETE CASCADE,
  locale_code text   NOT NULL REFERENCES locale(code),
  name        text   NOT NULL,
  PRIMARY KEY (unit_id, locale_code)
);

CREATE TABLE brand (
  id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  public_id     uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  code          text NOT NULL UNIQUE,
  legal_entity  text,
  logo_asset_id bigint,                                -- FK added after asset exists
  website       text,
  country_code  text,
  status        text NOT NULL DEFAULT 'active'
);

CREATE TABLE brand_tr (
  brand_id    bigint NOT NULL REFERENCES brand(id) ON DELETE CASCADE,
  locale_code text   NOT NULL REFERENCES locale(code),
  name        text   NOT NULL,
  description text,
  tagline     text,
  PRIMARY KEY (brand_id, locale_code)
);

CREATE TABLE taxonomy_node (
  id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  public_id        uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  code             text NOT NULL UNIQUE,               -- must be a valid ltree label
  node_type        text NOT NULL CHECK (node_type IN
                     ('BRAND','FAMILY','CATEGORY','SUBCATEGORY','SERIES')),
  parent_id        bigint REFERENCES taxonomy_node(id),
  path             ltree NOT NULL,                     -- maintained by trigger
  depth            smallint NOT NULL DEFAULT 0,        -- maintained by trigger
  brand_id         bigint REFERENCES brand(id),        -- BRAND nodes 1:1
  attribute_set_id bigint,                             -- FK added after attribute_set
  sort_order       int NOT NULL DEFAULT 0,
  status           text NOT NULL DEFAULT 'active',
  created_at       timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_node_parent ON taxonomy_node(parent_id);

-- Subtree containment (path <@ path) drives the Categories screen's product
-- counts and its attribute-coverage scope. Without a GiST index every node's
-- count is a sequential scan over the whole table: invisible at 40 nodes,
-- quadratic beyond. classification_class already carries the same index for
-- the same reason (ix_cc_path).
CREATE INDEX ix_node_path ON taxonomy_node USING gist(path);

CREATE TABLE taxonomy_node_tr (
  node_id            bigint NOT NULL REFERENCES taxonomy_node(id) ON DELETE CASCADE,
  locale_code        text   NOT NULL REFERENCES locale(code),
  name               text   NOT NULL,
  description        text,
  translation_status text NOT NULL DEFAULT 'missing',
  PRIMARY KEY (node_id, locale_code)
);

CREATE INDEX ix_node_tr_status ON taxonomy_node_tr(translation_status)
  WHERE translation_status <> 'approved';

CREATE TABLE attribute_group (
  id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  code       text NOT NULL UNIQUE,
  sort_order int NOT NULL DEFAULT 0
);

CREATE TABLE attribute_group_tr (
  group_id    bigint NOT NULL REFERENCES attribute_group(id) ON DELETE CASCADE,
  locale_code text   NOT NULL REFERENCES locale(code),
  name        text   NOT NULL,
  PRIMARY KEY (group_id, locale_code)
);

CREATE TABLE attribute (
  id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  code             text NOT NULL UNIQUE,               -- 'fire_rating'
  data_type        text NOT NULL CHECK (data_type IN ('text','number','decimal',
                     'boolean','date','datetime','list','multilist','measure','html','reference')),
  group_id         bigint REFERENCES attribute_group(id),
  unit_id          bigint REFERENCES unit(id),         -- measure/number
  is_localizable   boolean NOT NULL DEFAULT false,
  is_variant_axis  boolean NOT NULL DEFAULT false,     -- true -> defines a SKU axis
  scope            text NOT NULL DEFAULT 'PRODUCT' CHECK (scope IN ('PRODUCT','SKU','BOTH')),
  validation_json  jsonb,                              -- regex/range/length ...
  source_authority text NOT NULL DEFAULT 'PIM'         -- PIM | ERP — survivorship owner
);

CREATE INDEX ix_attr_group ON attribute(group_id);

CREATE TABLE attribute_tr (
  attribute_id bigint NOT NULL REFERENCES attribute(id) ON DELETE CASCADE,
  locale_code  text   NOT NULL REFERENCES locale(code),
  label        text   NOT NULL,
  help_text    text,
  placeholder  text,
  PRIMARY KEY (attribute_id, locale_code)
);

CREATE TABLE attribute_option (
  id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  attribute_id bigint NOT NULL REFERENCES attribute(id) ON DELETE CASCADE,
  code         text NOT NULL,                          -- 'powder_coated'
  sort_order   int NOT NULL DEFAULT 0,
  UNIQUE (attribute_id, code)
);

CREATE TABLE attribute_option_tr (
  option_id   bigint NOT NULL REFERENCES attribute_option(id) ON DELETE CASCADE,
  locale_code text   NOT NULL REFERENCES locale(code),
  label       text   NOT NULL,
  PRIMARY KEY (option_id, locale_code)
);

CREATE TABLE attribute_set (
  id   bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  code text NOT NULL UNIQUE,
  name text NOT NULL
);

CREATE TABLE attribute_set_attribute (
  set_id       bigint NOT NULL REFERENCES attribute_set(id) ON DELETE CASCADE,
  attribute_id bigint NOT NULL REFERENCES attribute(id),
  is_required  boolean NOT NULL DEFAULT false,
  sort_order   int NOT NULL DEFAULT 0,
  PRIMARY KEY (set_id, attribute_id)
);

CREATE INDEX ix_asa_attr ON attribute_set_attribute(attribute_id);

CREATE TABLE product (
  id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  public_id        uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  product_code     text NOT NULL UNIQUE,               -- 'FD-90-GS-1000'
  primary_node_id  bigint NOT NULL REFERENCES taxonomy_node(id),
  brand_id         bigint REFERENCES brand(id),        -- denormalized filter
  attribute_set_id bigint REFERENCES attribute_set(id),-- resolved at assignment
  base_locale      text NOT NULL REFERENCES locale(code),
  -- lifecycle & successor (§33)
  lifecycle_state  text NOT NULL DEFAULT 'ACTIVE'
                     CHECK (lifecycle_state IN ('PLANNED','ACTIVE','PHASE_OUT','DISCONTINUED')),
  successor_id     bigint REFERENCES product(id),
  predecessor_id   bigint REFERENCES product(id),
  available_from   date,
  phase_out_date   date,
  discontinue_date date,
  -- governance & commerce
  workflow_state   text NOT NULL DEFAULT 'draft',      -- draft·review·approval·published
  owner_user_id    bigint,  -- FK to app_user removed: owned by another service (logical ref only)
  -- DEVIATION from pim_database_v2.0.sql (decision 2026-08-14): interim owner
  -- as display text because no identity service exists yet. Migrate into
  -- owner_user_id and drop this column when auth ships.
  owner_display_name text,
  gtin             text,
  sap_material_no  text,
  -- read projections
  attributes_cache jsonb NOT NULL DEFAULT '{}',        -- published projection
  -- Maintained by catalog.product_search_refresh(), via triggers on this table
  -- and on product_tr. See "FULL-TEXT SEARCH" below for what goes in it and
  -- why the trigger deliberately does not touch row_version.
  search_vector    tsvector,
  -- standard audit columns
  created_at       timestamptz NOT NULL DEFAULT now(),
  updated_at       timestamptz NOT NULL DEFAULT now(),
  created_by       bigint,
  updated_by       bigint,
  row_version      int NOT NULL DEFAULT 1
);

CREATE INDEX ix_product_brand ON product(brand_id);

CREATE INDEX ix_product_cache ON product USING gin(attributes_cache);

-- The Workflows board groups the whole catalogue by workflow_state and then
-- takes each column's most recently touched rows. Both halves are served by
-- this one index; without it the board is two sequential scans of `product`,
-- which is fine at 500 rows and is exactly the shape that stops being fine.
CREATE INDEX ix_product_workflow_board ON product(workflow_state, updated_at DESC);

CREATE TABLE product_tr (
  product_id         bigint NOT NULL REFERENCES product(id) ON DELETE CASCADE,
  locale_code        text   NOT NULL REFERENCES locale(code),
  name               text   NOT NULL,
  short_desc         text,
  long_desc          text,
  marketing          text,
  seo_title          text,
  seo_desc           text,
  seo_keywords       text,
  slug               text,
  translation_status text NOT NULL DEFAULT 'missing',
  source             text,                             -- human · machine · memory
  updated_at         timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (product_id, locale_code)
);

CREATE UNIQUE INDEX ux_product_slug ON product_tr(locale_code, slug) WHERE slug IS NOT NULL;

CREATE INDEX ix_product_tr_status ON product_tr(translation_status)
  WHERE translation_status <> 'approved';

-- =============================================================================
-- FULL-TEXT SEARCH (Sprint 14)
--
-- Replaces the ILIKE scan behind the products grid. Two things made ILIKE
-- inadequate for this catalogue rather than merely slow:
--
--   * No stemming. A steward searching "Fliesen" did not match "Fliese".
--   * No multi-word handling. "craft abdeckplatte" matched nothing, because
--     ILIKE '%craft abdeckplatte%' needs those words adjacent in that order.
--
-- The searchable text lives in product_tr, one row per locale, so the vector
-- has to aggregate ACROSS locales: the same product must be findable by its
-- German name and by its English one. That is why this is a trigger writing a
-- denormalised column and not a generated column — a generated column cannot
-- read another table.
--
-- Weights: A = identity (code, names), B = descriptive copy. Marketing copy is
-- deliberately included; SEO fields are not, because they exist to be stuffed
-- with keywords and would drown the ranking.
--
-- NOT covered, so nobody assumes otherwise:
--   * German compound splitting. The built-in 'german' config is a snowball
--     STEMMER; decompounding needs an ispell dictionary that is not installed.
--     "Abdeckplatte" will not match a search for "Platte".
--   * Diacritic folding ("Grosse" vs "Größe") — needs the unaccent extension
--     and a custom configuration. Deferred, not forgotten.
-- =============================================================================

CREATE OR REPLACE FUNCTION catalog.product_search_document(p_product_id bigint)
RETURNS tsvector
LANGUAGE sql
STABLE
AS $$
  SELECT
      -- The code with separators as spaces, so 'craft' finds
      -- AB-CRAFT-EXTENDED-COVER-PLATE. 'simple' because a part number must not
      -- be stemmed.
      setweight(to_tsvector('simple',
        coalesce(replace(replace(p.product_code, '-', ' '), '_', ' '), '')), 'A')
    || coalesce((
         SELECT
             setweight(to_tsvector('german',  string_agg(coalesce(tr.name, ''), ' ')), 'A')
           || setweight(to_tsvector('english', string_agg(coalesce(tr.name, ''), ' ')), 'A')
           || setweight(to_tsvector('german',
                string_agg(concat_ws(' ', tr.short_desc, tr.long_desc, tr.marketing), ' ')), 'B')
           || setweight(to_tsvector('english',
                string_agg(concat_ws(' ', tr.short_desc, tr.long_desc, tr.marketing), ' ')), 'B')
         FROM catalog.product_tr tr
         WHERE tr.product_id = p.id
       ), ''::tsvector)
  FROM catalog.product p
  WHERE p.id = p_product_id;
$$;

-- Both configurations are applied to the same text on purpose. Running the
-- German stemmer over English (and vice versa) costs a few extra lexemes and
-- means one column serves both languages, instead of a column per locale that
-- the query would then have to choose between.

-- One definition of "what the user typed, as a query", used by BOTH the WHERE
-- clause and the relevance ranking. Two copies of this expression would drift,
-- and the failure mode is silent: rows that match would rank as though they
-- did not.
--
-- websearch_to_tsquery is used rather than to_tsquery because it never raises
-- on malformed input — a steward typing `&` or an unbalanced quote gets no
-- results, not a 500.
CREATE OR REPLACE FUNCTION catalog.product_search_query(p_raw text)
RETURNS tsquery
LANGUAGE sql
IMMUTABLE
AS $$
  SELECT CASE
           WHEN coalesce(trim(p_raw), '') = '' THEN NULL::tsquery
           ELSE websearch_to_tsquery('german',  p_raw)
             || websearch_to_tsquery('english', p_raw)
             || websearch_to_tsquery('simple',  p_raw)
         END;
$$;

CREATE OR REPLACE FUNCTION catalog.product_search_refresh()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = catalog, pg_catalog
AS $$
DECLARE
  target bigint;
BEGIN
  -- Branch on TG_OP, not on COALESCE(NEW..., OLD...): on a DELETE the NEW
  -- record is unassigned, and merely naming NEW.product_id inside COALESCE
  -- raises "record new is not assigned yet" at runtime. The seed never deletes
  -- a translation, so that would have stayed hidden until a steward removed a
  -- locale in production.
  IF TG_TABLE_NAME = 'product' THEN
    target := NEW.id;
  ELSIF TG_OP = 'DELETE' THEN
    target := OLD.product_id;
  ELSE
    target := NEW.product_id;
  END IF;

  -- ONLY search_vector. Not updated_at, and above all not row_version:
  -- that column is the optimistic-concurrency token the API hands to clients,
  -- and bumping it here would make every translation save invalidate a token
  -- some other tab was still holding, producing 409s nobody caused.
  UPDATE catalog.product
  SET search_vector = catalog.product_search_document(target)
  WHERE id = target;

  RETURN NULL;
END;
$$;

CREATE TRIGGER trg_product_search
  AFTER INSERT OR UPDATE OF product_code ON product
  FOR EACH ROW EXECUTE FUNCTION catalog.product_search_refresh();

CREATE TRIGGER trg_product_tr_search
  AFTER INSERT OR UPDATE OF name, short_desc, long_desc, marketing OR DELETE ON product_tr
  FOR EACH ROW EXECUTE FUNCTION catalog.product_search_refresh();

CREATE INDEX ix_product_search ON product USING gin(search_vector);

CREATE TABLE sku (
  id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  product_id       bigint NOT NULL REFERENCES product(id) ON DELETE CASCADE,
  sku_code         text NOT NULL UNIQUE,
  gtin             text,
  erp_material_no  text,
  status           text NOT NULL DEFAULT 'active',     -- active·blocked·eol
  attributes_cache jsonb NOT NULL DEFAULT '{}',        -- resolved (product ⊕ sku)
  sort_order       int NOT NULL DEFAULT 0
);

CREATE INDEX ix_sku_product ON sku(product_id);

CREATE TABLE sku_tr (
  sku_id       bigint NOT NULL REFERENCES sku(id) ON DELETE CASCADE,
  locale_code  text   NOT NULL REFERENCES locale(code),
  variant_name text,
  PRIMARY KEY (sku_id, locale_code)
);

CREATE TABLE attribute_value (
  id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  product_id      bigint REFERENCES product(id) ON DELETE CASCADE,
  sku_id          bigint REFERENCES sku(id) ON DELETE CASCADE,
  attribute_id    bigint NOT NULL REFERENCES attribute(id),
  locale_code     text REFERENCES locale(code),        -- NULL = language-neutral
  value_text      text,
  value_number    numeric,
  value_boolean   boolean,
  value_date      date,
  value_datetime  timestamptz,
  value_option_id bigint REFERENCES attribute_option(id),
  value_json      jsonb,                               -- measure / reference / composite
  data_state      text NOT NULL DEFAULT 'draft',       -- draft / approved
  source          text,                                -- manual · SAP · AI
  confidence      numeric,
  updated_at      timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT ck_av_owner CHECK (
    (product_id IS NOT NULL)::int + (sku_id IS NOT NULL)::int = 1)
);

CREATE UNIQUE INDEX ux_av ON attribute_value
  (coalesce(product_id,0), coalesce(sku_id,0), attribute_id,
   coalesce(locale_code,''), coalesce(value_option_id,0));

CREATE INDEX ix_av_approved ON attribute_value(attribute_id) WHERE data_state = 'approved';

-- Serves the completeness computation (approved values for a product's
-- required attributes). ux_av above is an EXPRESSION index on coalesce(...),
-- so it cannot satisfy a plain product_id = ? AND attribute_id = ? predicate:
-- without this index the grid degraded to a sequential scan per product
-- (measured 12s at ~2,900 products, 2026-08-14).
CREATE INDEX ix_av_product_attr ON attribute_value(product_id, attribute_id)
  WHERE data_state = 'approved';

CREATE TABLE product_relationship (
  id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  from_product_id bigint NOT NULL REFERENCES product(id) ON DELETE CASCADE,
  to_product_id   bigint NOT NULL REFERENCES product(id) ON DELETE CASCADE,
  relation_type   text NOT NULL CHECK (relation_type IN
                    ('ACCESSORY','COMPATIBLE','REPLACEMENT','CROSS_SELL','UP_SELL')),
  priority        int NOT NULL DEFAULT 0,
  UNIQUE (from_product_id, to_product_id, relation_type)
);

CREATE INDEX ix_rel_to ON product_relationship(to_product_id);

-- =============================================================================
-- MODULE 13 · CLASSIFICATION STANDARDS (Sprint 8)
--
-- Ported verbatim in shape from pim_database_v2.0.sql module 13. This is
-- PARALLEL classification: taxonomy_node above is our own merchandising tree
-- and stays the single primary_node_id on product; these tables carry the
-- external standards trade partners actually consume (ETIM/BMEcat), planners
-- consume (UNSPSC) and SAP consumes (DataNorm). A product is mapped into all
-- of them at once, which is why this is a separate table and not more columns.
-- =============================================================================

CREATE TABLE classification_scheme (
  id      bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  code    text NOT NULL,                               -- 'ETIM','DATANORM','BMECAT'
  version text NOT NULL,                               -- '9.0'
  name    text NOT NULL,
  purpose text,
  status  text NOT NULL DEFAULT 'active',
  sort_order int NOT NULL DEFAULT 0,
  UNIQUE (code, version)
);

CREATE TABLE classification_class (
  id        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  scheme_id bigint NOT NULL REFERENCES classification_scheme(id) ON DELETE CASCADE,
  code      text NOT NULL,                             -- 'EC010225'
  label     text,
  parent_id bigint REFERENCES classification_class(id),
  path      ltree,
  UNIQUE (scheme_id, code)
);

CREATE INDEX ix_cc_path ON classification_class USING gist(path);

CREATE TABLE classification_feature (
  id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  scheme_id  bigint NOT NULL REFERENCES classification_scheme(id) ON DELETE CASCADE,
  code       text NOT NULL,                            -- 'EF000008'
  name       text,
  data_type  text NOT NULL DEFAULT 'alpha'
               CHECK (data_type IN ('numeric','alpha','logical','range')),
  unit       text,
  class_id   bigint REFERENCES classification_class(id),-- applicability
  is_required boolean NOT NULL DEFAULT false,
  sort_order int NOT NULL DEFAULT 0,
  UNIQUE (scheme_id, code)
);

-- Feature completeness is read per (product, class); without this index the
-- Classifications tab would scan the whole feature table per scheme row.
CREATE INDEX ix_cf_class ON classification_feature(class_id);

CREATE TABLE product_classification (
  id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  product_id    bigint NOT NULL REFERENCES product(id) ON DELETE CASCADE,
  scheme_id     bigint NOT NULL REFERENCES classification_scheme(id),
  class_id      bigint REFERENCES classification_class(id),
  is_primary    boolean NOT NULL DEFAULT false,
  mapped_status text NOT NULL DEFAULT 'partial'
                  CHECK (mapped_status IN ('mapped','partial','unmapped')),
  UNIQUE (product_id, scheme_id)      -- one mapping per scheme; many schemes in parallel
);

CREATE INDEX ix_pc_product ON product_classification(product_id);

CREATE TABLE product_classification_value (
  product_id   bigint NOT NULL REFERENCES product(id) ON DELETE CASCADE,
  feature_id   bigint NOT NULL REFERENCES classification_feature(id) ON DELETE CASCADE,
  value_text   text,
  value_number numeric,
  value_bool   boolean,
  value_unit   text,
  status       text NOT NULL DEFAULT 'approved'
                 CHECK (status IN ('approved','draft')),
  PRIMARY KEY (product_id, feature_id)
);

-- =============================================================================
-- MODULE 16 · COMPLIANCE & SUSTAINABILITY (Sprint 8)
--
-- Certification evidence is contractually required in this industry: a fire
-- door cannot be sold without a valid DoP. Note there is deliberately NO
-- 'expired'/'expiring' column — expiry is a pure function of valid_to and the
-- current date, so storing it would create a value that silently goes stale
-- the moment nobody runs a nightly job. It is derived on read instead.
-- =============================================================================

CREATE TABLE compliance_document (
  id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  product_id bigint NOT NULL REFERENCES product(id) ON DELETE CASCADE,
  doc_type   text NOT NULL CHECK (doc_type IN ('DOP','CE','EPD','DPP','TEST','UKCA','ISI','FSC','CARB','EPA','IGBC')),
  reference  text,                                     -- DoP / certificate number
  standard   text,                                     -- 'EN 1634-1', 'EN 15804+A2' ...
  authority  text,                                     -- 'TÜV Rheinland', notified body
  asset_id   bigint,        -- logical ref to the asset service; no FK across services
  valid_from date,
  valid_to   date,
  status     text NOT NULL DEFAULT 'valid'
               CHECK (status IN ('valid','withdrawn','superseded')),
  payload    jsonb,                                    -- structured extract / DPP link
  CHECK (valid_to IS NULL OR valid_from IS NULL OR valid_to >= valid_from)
);

CREATE INDEX ix_compdoc        ON compliance_document(product_id, doc_type);
CREATE INDEX ix_compdoc_expiry ON compliance_document(valid_to) WHERE valid_to IS NOT NULL;

-- One certificate number may not be recorded twice against the same product.
CREATE UNIQUE INDEX ux_compdoc_ref ON compliance_document(product_id, reference)
  WHERE reference IS NOT NULL;

CREATE TABLE sustainability_indicator (
  id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  product_id bigint NOT NULL REFERENCES product(id) ON DELETE CASCADE,
  epd_doc_id bigint REFERENCES compliance_document(id) ON DELETE SET NULL,
  indicator  text NOT NULL,                            -- GWP·AP·EP·recycled_content
  value      numeric,
  unit       text,                                     -- 'kg CO2-eq'
  method     text,                                     -- 'EN 15804'
  stage      text                                      -- 'A1-A3'
);

CREATE INDEX ix_sustain ON sustainability_indicator(product_id, indicator);

CREATE TABLE material_composition (
  id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  product_id    bigint NOT NULL REFERENCES product(id) ON DELETE CASCADE,
  material      text NOT NULL,
  pct           numeric CHECK (pct IS NULL OR (pct >= 0 AND pct <= 100)),
  is_recyclable boolean,
  is_hazardous  boolean NOT NULL DEFAULT false,
  sort_order    int NOT NULL DEFAULT 0
);

CREATE INDEX ix_matcomp ON material_composition(product_id);

CREATE TABLE substance_compliance (
  id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  product_id bigint NOT NULL REFERENCES product(id) ON DELETE CASCADE,
  regime     text NOT NULL,                            -- REACH_SVHC · RoHS ...
  result     text NOT NULL,                            -- compliant · contains · exempt
  threshold  text,
  notes      text
);

CREATE INDEX ix_substance ON substance_compliance(product_id);

CREATE TABLE green_building_contribution (
  id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  product_id bigint NOT NULL REFERENCES product(id) ON DELETE CASCADE,
  scheme     text NOT NULL,                            -- DGNB · LEED · BREEAM
  credit     text NOT NULL,
  note       text,
  UNIQUE (product_id, scheme, credit)
);

-- =============================================================================
-- MODULE 15 · PACKAGING (Sprint 9)
--
-- Packaging hangs off the SKU, not the product: two variants of the same
-- product can ship in different cartons, and the GTIN that a wholesaler scans
-- belongs to the packaging level, not to the article.
-- =============================================================================

CREATE TABLE packaging_unit (
  id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  sku_id          bigint NOT NULL REFERENCES sku(id) ON DELETE CASCADE,
  parent_id       bigint REFERENCES packaging_unit(id) ON DELETE CASCADE,
  level           text NOT NULL CHECK (level IN ('EACH','INNER','CARTON','LAYER','PALLET')),
  contains_qty    numeric NOT NULL DEFAULT 1 CHECK (contains_qty > 0),
  gtin            text,
  sscc            text,
  gross_weight    numeric CHECK (gross_weight IS NULL OR gross_weight >= 0),
  net_weight      numeric CHECK (net_weight IS NULL OR net_weight >= 0),
  length_mm       numeric CHECK (length_mm IS NULL OR length_mm > 0),
  width_mm        numeric CHECK (width_mm IS NULL OR width_mm > 0),
  height_mm       numeric CHECK (height_mm IS NULL OR height_mm > 0),
  is_base_unit    boolean NOT NULL DEFAULT false,      -- the orderable unit
  dangerous_goods boolean NOT NULL DEFAULT false,
  un_class        text,
  -- Net weight is the goods; gross includes the packaging. Gross below net is
  -- always a data-entry error and would corrupt freight calculations.
  CHECK (gross_weight IS NULL OR net_weight IS NULL OR gross_weight >= net_weight),
  -- One packaging level may not appear twice for the same SKU.
  UNIQUE (sku_id, level)
);

CREATE INDEX ix_pkg_sku    ON packaging_unit(sku_id);
CREATE INDEX ix_pkg_parent ON packaging_unit(parent_id);

-- A GTIN identifies exactly one tradeable unit worldwide; reusing one across
-- packaging levels breaks every scanner downstream.
CREATE UNIQUE INDEX ux_pkg_gtin ON packaging_unit(gtin) WHERE gtin IS NOT NULL;
CREATE UNIQUE INDEX ux_pkg_sscc ON packaging_unit(sscc) WHERE sscc IS NOT NULL;

-- Exactly one orderable unit per SKU.
CREATE UNIQUE INDEX ux_pkg_base ON packaging_unit(sku_id) WHERE is_base_unit;

-- =============================================================================
-- MODULE · APPLICATIONS (Sprint 9)
--
-- Where a product may be used: industry, building type, area, use case. This is
-- reference data assigned as a set, which is why product_application is a pure
-- join table with no attributes of its own.
-- =============================================================================

CREATE TABLE application (
  id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  group_code text NOT NULL CHECK (group_code IN
               ('INDUSTRY','BUILDING_TYPE','AREA','USE_CASE')),
  code       text NOT NULL,
  sort_order int NOT NULL DEFAULT 0,
  UNIQUE (group_code, code)
);

CREATE TABLE application_tr (
  application_id bigint NOT NULL REFERENCES application(id) ON DELETE CASCADE,
  locale_code    text   NOT NULL REFERENCES locale(code),
  label          text   NOT NULL,
  PRIMARY KEY (application_id, locale_code)
);

CREATE TABLE product_application (
  product_id     bigint NOT NULL REFERENCES product(id) ON DELETE CASCADE,
  application_id bigint NOT NULL REFERENCES application(id) ON DELETE CASCADE,
  PRIMARY KEY (product_id, application_id)
);

CREATE INDEX ix_prodapp_app ON product_application(application_id);

-- product_relationship already exists above (declared with the product module).
-- A product must not be related to itself; without this a UI mistake creates a
-- self-referencing accessory that renders as an infinite loop downstream.
ALTER TABLE product_relationship
  ADD CONSTRAINT ck_rel_not_self CHECK (from_product_id <> to_product_id);

-- =============================================================================
-- AUDIT LOG (Sprint 10)
--
-- Append-only history behind the Activity tab. Partitioned by month per the
-- v2.0 design: this table grows without bound and is queried almost exclusively
-- over recent time, so partitioning keeps both the index and the retention
-- story manageable.
--
-- actor_user_id is NULLABLE and currently always null: no identity service
-- exists yet. The Activity tab renders those as 'Unknown'. When auth ships the
-- column fills in with no schema change and no backfill of false attribution.
-- =============================================================================

CREATE TABLE audit_log (
  id             bigint GENERATED ALWAYS AS IDENTITY,
  occurred_at    timestamptz NOT NULL DEFAULT now(),
  actor_user_id  bigint,
  actor_display  text,        -- interim, alongside owner_display_name; see ADR
  action         text NOT NULL,                        -- created·updated·published ...
  entity_type    text NOT NULL,
  entity_id      bigint NOT NULL,
  attribute_code text,                                 -- field-level (nullable)
  old_value      jsonb,
  new_value      jsonb,
  source         text,
  request_id     uuid,
  PRIMARY KEY (id, occurred_at)
) PARTITION BY RANGE (occurred_at);

-- Partition automation (pg_partman or a scheduled job) is an operational task
-- that must be in place before production; the DEFAULT partition below keeps
-- local and test runs correct in the meantime but must NOT be relied on in
-- production, where it silently becomes an unpartitioned hot table.
CREATE TABLE audit_log_2026_08 PARTITION OF audit_log
  FOR VALUES FROM ('2026-08-01') TO ('2026-09-01');
CREATE TABLE audit_log_2026_09 PARTITION OF audit_log
  FOR VALUES FROM ('2026-09-01') TO ('2026-10-01');
CREATE TABLE audit_log_default PARTITION OF audit_log DEFAULT;

-- The Activity tab reads one entity's history newest-first; this index serves
-- exactly that and nothing else.
CREATE INDEX ix_audit_entity ON audit_log(entity_type, entity_id, occurred_at DESC);

-- =============================================================================
-- MODULE 06 · DIGITAL ASSETS (Sprint 12)
--
-- Binaries are never stored in the database. An asset row records WHERE the
-- file lives (provider + key) and what it is; the bytes stay in the DAM, on
-- S3, or — for the seeded client data — on the client's own Magento server.
--
-- Documents are not a separate module: a datasheet is an asset whose type is
-- DOCUMENT. Splitting them would duplicate the linking, rights and versioning
-- machinery for no gain.
-- =============================================================================

CREATE TABLE asset (
  id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  public_id        uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  storage_provider text NOT NULL,                      -- s3 · azure · dam · client-magento · local
  storage_key      text NOT NULL,                      -- object key or URL; binaries never in DB
  file_name        text,
  mime_type        text,
  file_size        bigint CHECK (file_size IS NULL OR file_size >= 0),
  asset_type       text NOT NULL CHECK (asset_type IN
                     ('IMAGE','CAD','BIM','DOCUMENT','VIDEO','SPIN_360')),
  checksum         text,                               -- dedupe
  width            int,
  height           int,
  locale           text REFERENCES locale(code),       -- NULL = locale-neutral
  usage_rights     text,
  rights_expiry    date,
  supersedes_id    bigint REFERENCES asset(id),        -- version chain
  status           text NOT NULL DEFAULT 'active'
                     CHECK (status IN ('active','archived')),
  created_at       timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_asset_type     ON asset(asset_type);
CREATE INDEX ix_asset_checksum ON asset(checksum) WHERE checksum IS NOT NULL;

-- Rights expiry is the query operations actually runs ("what falls out of
-- licence next month"); partial because most assets never expire.
CREATE INDEX ix_asset_expiry   ON asset(rights_expiry) WHERE rights_expiry IS NOT NULL;

-- The same binary must not be registered twice: two rows pointing at one file
-- means deleting one leaves a dangling reference nobody expects.
CREATE UNIQUE INDEX ux_asset_key ON asset(storage_provider, storage_key);

CREATE TABLE asset_rendition (
  id       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  asset_id bigint NOT NULL REFERENCES asset(id) ON DELETE CASCADE,
  role     text NOT NULL CHECK (role IN ('thumb','web','print','original')),
  uri      text NOT NULL,
  width    int,
  height   int,
  format   text,
  UNIQUE (asset_id, role)
);

CREATE TABLE asset_tr (
  asset_id    bigint NOT NULL REFERENCES asset(id) ON DELETE CASCADE,
  locale_code text   NOT NULL REFERENCES locale(code),
  title       text,
  alt_text    text,
  caption     text,
  PRIMARY KEY (asset_id, locale_code)
);

CREATE TABLE asset_link (
  id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  product_id bigint REFERENCES product(id) ON DELETE CASCADE,
  sku_id     bigint REFERENCES sku(id) ON DELETE CASCADE,
  asset_id   bigint NOT NULL REFERENCES asset(id) ON DELETE CASCADE,
  role       text NOT NULL CHECK (role IN
               ('MAIN','GALLERY','DATASHEET','INSTALL','DRAWING','BIM','VIDEO','SPIN')),
  is_primary boolean NOT NULL DEFAULT false,
  sort_order int NOT NULL DEFAULT 0,
  valid_from date,
  valid_to   date,
  -- An asset hangs off a product OR a SKU, never both and never neither.
  -- Without this a link with both set would appear on two screens and be
  -- editable from either, with no defined winner.
  CONSTRAINT ck_link_owner CHECK (
    (product_id IS NOT NULL)::int + (sku_id IS NOT NULL)::int = 1),
  CONSTRAINT ck_link_dates CHECK (
    valid_to IS NULL OR valid_from IS NULL OR valid_to >= valid_from),
  UNIQUE (product_id, asset_id, role),
  UNIQUE (sku_id, asset_id, role)
);

-- At most one primary per role per owner: "the" main image must be singular,
-- or every consumer picks a different one.
CREATE UNIQUE INDEX ux_primary_asset_product ON asset_link(product_id, role)
  WHERE is_primary AND product_id IS NOT NULL;
CREATE UNIQUE INDEX ux_primary_asset_sku ON asset_link(sku_id, role)
  WHERE is_primary AND sku_id IS NOT NULL;

CREATE INDEX ix_link_asset   ON asset_link(asset_id);
CREATE INDEX ix_link_product ON asset_link(product_id, role);
CREATE INDEX ix_link_sku     ON asset_link(sku_id, role);

-- =============================================================================
-- MODULE 07 · TRANSACTIONAL OUTBOX (Sprint 15, ADR-004 / ADR-007)
--
-- ADR-004 made this REQUIRED, not optional: in-process dispatch cannot reach
-- another service reliably. A row is written here in the SAME transaction as
-- the aggregate that produced it, so "the product changed" and "the world was
-- told the product changed" cannot disagree. The relay then publishes
-- at-least-once — consumers MUST deduplicate on message_id.
--
-- This table belongs to catalog and to nothing else. When asset-service lands
-- it gets its own outbox in its own schema (ADR-005). There is no shared
-- outbox: a shared one is a shared write path between services, which is the
-- coupling schema-per-service exists to prevent.
-- =============================================================================

CREATE TABLE outbox_message (
  id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

  -- Consumer-facing idempotency key. Survives retries and redelivery; a
  -- consumer that has seen this id has seen this message.
  message_id      uuid        NOT NULL UNIQUE,

  -- When the domain event happened, not when it was enqueued. These differ
  -- under retry and the difference is the only way to measure publish lag.
  occurred_at     timestamptz NOT NULL,
  enqueued_at     timestamptz NOT NULL DEFAULT now(),

  -- Wire contract, not a CLR type name. Consumers bind to this string and to
  -- schema_version; renaming a C# class must not break a consumer.
  event_type      text        NOT NULL,
  schema_version  int         NOT NULL DEFAULT 1,

  -- aggregate_id is the PUBLIC id (uuid), never the bigint surrogate: the
  -- surrogate is private to this service (ADR-004 rule 2).
  aggregate_type  text        NOT NULL,
  aggregate_id    uuid        NOT NULL,

  payload         jsonb       NOT NULL,
  correlation_id  uuid,

  status          text        NOT NULL DEFAULT 'pending',
  attempt_count   int         NOT NULL DEFAULT 0,
  next_attempt_at timestamptz NOT NULL DEFAULT now(),

  -- Lease, not a lock. The relay publishes over a network; holding a database
  -- transaction open across that call would tie DB liveness to broker latency.
  -- Instead a claim marks the row 'publishing' until lease_expires_at, and a
  -- relay that dies mid-publish has its rows reclaimed when the lease lapses.
  -- This is precisely why delivery is at-least-once and not exactly-once.
  lease_expires_at timestamptz,
  leased_by        text,

  last_error      text,
  published_at    timestamptz,

  CONSTRAINT ck_outbox_status
    CHECK (status IN ('pending', 'publishing', 'published', 'dead')),
  CONSTRAINT ck_outbox_attempts
    CHECK (attempt_count >= 0),
  -- A published row has a publish time and vice versa. Without this, a partial
  -- write shows as published with no evidence of when.
  CONSTRAINT ck_outbox_published_at
    CHECK ((status = 'published') = (published_at IS NOT NULL))
);

-- The relay's claim query and nothing else. Partial: published rows are the
-- overwhelming majority over time and must not be walked to find work.
CREATE INDEX ix_outbox_claim
  ON outbox_message (next_attempt_at, id)
  WHERE status = 'pending';

-- Serves the per-aggregate ordering guard (see ADR-007): a message is only
-- eligible once no earlier message for the same aggregate is still in flight.
CREATE INDEX ix_outbox_aggregate_inflight
  ON outbox_message (aggregate_id, id)
  WHERE status IN ('pending', 'publishing');

-- Lease reclaim sweep.
CREATE INDEX ix_outbox_lease
  ON outbox_message (lease_expires_at)
  WHERE status = 'publishing';

-- Dead letters are an operational queue: small, and read by humans.
CREATE INDEX ix_outbox_dead
  ON outbox_message (enqueued_at DESC)
  WHERE status = 'dead';

COMMENT ON TABLE outbox_message IS
  'Transactional outbox (ADR-007). Written in the aggregate''s transaction; '
  'relayed at-least-once. Consumers must deduplicate on message_id.';

-- Retention is an operational task that does NOT exist yet: nothing prunes
-- published rows. At the seeded volume this is harmless, but the table grows
-- without bound and must have a prune job before production. Tracked as R-13.

-- =============================================================================
-- MODULE 09 · WORKFLOW & GOVERNANCE (Sprint 27)
--
-- Database Design v2.0 §12 and §22 specify a CONFIGURABLE engine, and this is
-- it: the pipeline is rows, not code. Until this sprint the editorial pipeline
-- existed in three hardcoded places — a Dictionary inside the Product
-- aggregate, a Record<string,string[]> in the React app, and the CHECK
-- constraint on product.workflow_state. Three copies of one rule is two too
-- many; the first two are deleted by this sprint and this table becomes the
-- single source.
--
-- Technology note. PIM-Project-Technology-Stack-Matrix v1.0 puts
-- "Workflow Provider Interface" in the platform-neutral column and names Elsa,
-- Temporal, Logic Apps, Durable Functions and Step Functions only inside
-- specific deployment columns. They are PROVIDERS behind the neutral
-- interface. This module is the neutral implementation: a state machine over
-- these five tables, in-process, with no workflow package added to
-- Directory.Packages.props (client instruction, 2026-08-19).
--
-- WHAT THIS MODULE DELIBERATELY DOES NOT DO
--   * No guard conditions on transitions. The UNIQUE(definition, from_step,
--     action) constraint below assumes one action resolves to exactly one
--     target. Guards would break that assumption, so adding them later is a
--     schema change and not a data change. Stated here so nobody discovers it
--     by writing a second row.
--   * No parallel or multi-approver steps. One open task per product.
--   * No SLA escalation. due_date is recorded and shown; nothing acts on it.
-- =============================================================================

CREATE TABLE workflow_definition (
  id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  public_id   uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  code        text NOT NULL,
  name        text NOT NULL,
  entity_type text NOT NULL DEFAULT 'PRODUCT'
                CHECK (entity_type IN ('PRODUCT', 'SYSTEM', 'ASSET')),
  version     int  NOT NULL DEFAULT 1,
  is_active   boolean NOT NULL DEFAULT true,
  created_at  timestamptz NOT NULL DEFAULT now(),
  UNIQUE (code, version)
);

-- At most one ACTIVE definition per entity type. Two would mean two answers to
-- "what is the pipeline", resolved by whichever row the query happened to
-- return first. Versioning is by adding a row and flipping is_active, so old
-- definitions stay readable for the tasks that ran under them.
CREATE UNIQUE INDEX ux_workflow_active
  ON workflow_definition(entity_type) WHERE is_active;

CREATE TABLE workflow_step (
  id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  definition_id bigint NOT NULL REFERENCES workflow_definition(id) ON DELETE CASCADE,
  code          text NOT NULL,        -- matches WorkflowState: draft·review·approval·published
  name          text NOT NULL,
  sort_order    int  NOT NULL,
  -- Advisory only until RBAC ships. Recorded so the pipeline is complete on
  -- paper and so the eventual auth sprint has somewhere to bind; NOTHING reads
  -- it to make a decision today, because there are no users to check against
  -- and a role check that always passes is worse than no role check (R-10).
  owner_role    text,
  is_initial    boolean NOT NULL DEFAULT false,
  is_terminal   boolean NOT NULL DEFAULT false,
  UNIQUE (definition_id, code),
  UNIQUE (definition_id, sort_order)
);

CREATE UNIQUE INDEX ux_step_initial
  ON workflow_step(definition_id) WHERE is_initial;

CREATE TABLE workflow_transition (
  id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  definition_id bigint NOT NULL REFERENCES workflow_definition(id) ON DELETE CASCADE,
  from_step_id  bigint NOT NULL REFERENCES workflow_step(id) ON DELETE CASCADE,
  to_step_id    bigint NOT NULL REFERENCES workflow_step(id) ON DELETE CASCADE,
  action        text NOT NULL
                  CHECK (action IN ('SUBMIT', 'APPROVE', 'REJECT', 'REQUEST_CHANGES', 'PUBLISH')),
  label         text NOT NULL,
  sort_order    int  NOT NULL DEFAULT 0,
  -- One action out of one step goes to exactly one place. See the guard note
  -- in the module header.
  UNIQUE (definition_id, from_step_id, action),
  CHECK (from_step_id <> to_step_id)
);

CREATE INDEX ix_transition_from ON workflow_transition(definition_id, from_step_id);

-- -----------------------------------------------------------------------------
-- approval_task — one live instance per product.
--
-- product.workflow_state is a DENORMALISED PROJECTION of this table's
-- current_step_id, and that is a deliberate duplication, not an oversight: the
-- grid, the dashboard cockpit and the board all filter and group on workflow
-- state over the whole catalogue, and making them join through two tables to
-- get it would cost more than the redundancy does. The two are written in ONE
-- transaction (approval_task and approval_history are EF-mapped for exactly
-- this reason, not Dapper), and scripts/smoke-api.sh asserts they never
-- disagree — because a denormalisation nobody checks is just a bug with a
-- design rationale attached.
-- -----------------------------------------------------------------------------
CREATE TABLE approval_task (
  id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  public_id       uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  product_id      bigint NOT NULL REFERENCES product(id) ON DELETE CASCADE,
  definition_id   bigint NOT NULL REFERENCES workflow_definition(id),
  current_step_id bigint NOT NULL REFERENCES workflow_step(id),
  status          text NOT NULL DEFAULT 'open'
                    CHECK (status IN ('open', 'completed', 'cancelled')),
  priority        text NOT NULL DEFAULT 'normal'
                    CHECK (priority IN ('low', 'normal', 'high', 'urgent')),
  due_date        date,
  -- Same interim as product.owner_display_name (decision 2026-08-14): there is
  -- no app_user table, so an assignee is a name on a card. assignee_user_id
  -- fills in with no schema change when authentication ships, and until then
  -- stays NULL rather than being back-filled with invented attribution.
  assignee_user_id      bigint,
  assignee_display_name text,
  created_at      timestamptz NOT NULL DEFAULT now(),
  updated_at      timestamptz NOT NULL DEFAULT now()
);

-- One OPEN task per product. Two open tasks is two answers to "where is this in
-- the pipeline", and the board would draw the product in two columns.
CREATE UNIQUE INDEX ux_task_open_product
  ON approval_task(product_id) WHERE status = 'open';

-- "My queue" per §22. Both variants exist because the id column is empty until
-- auth ships and the name column is what the UI actually filters on today.
CREATE INDEX ix_task_queue_user ON approval_task(assignee_user_id, status);
CREATE INDEX ix_task_queue_name ON approval_task(assignee_display_name)
  WHERE status = 'open';
CREATE INDEX ix_task_step ON approval_task(current_step_id) WHERE status = 'open';

-- -----------------------------------------------------------------------------
-- approval_history — append-only. Never updated, never deleted.
--
-- This is the approval trail, and it is worth being blunt about what it is
-- worth right now: with no authentication (R-3), actor_user_id is always NULL
-- and actor_display is whatever the caller typed. The sequence of transitions
-- is real; the attribution is not. Recorded as R-26.
-- -----------------------------------------------------------------------------
CREATE TABLE approval_history (
  id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  task_id       bigint NOT NULL REFERENCES approval_task(id) ON DELETE CASCADE,
  from_step_id  bigint REFERENCES workflow_step(id),   -- NULL on the opening row
  to_step_id    bigint NOT NULL REFERENCES workflow_step(id),
  action        text NOT NULL,
  actor_user_id bigint,
  actor_display text,
  comment       text,
  occurred_at   timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_history_task ON approval_history(task_id, occurred_at);

-- =============================================================================
-- REFERENCE DATA — the default "Product Publishing" pipeline.
--
-- This lives in the SCHEMA file, not in 02_dev_seed.sql, on purpose: a
-- deployment with no workflow definition has no pipeline at all and every
-- workflow command fails. It is configuration the product cannot run without,
-- which makes it reference data in the same sense the locale table is.
--
-- The four steps reach exactly the states the Product aggregate hardcoded up to
-- Sprint 26 — draft→{review}, review→{approval,draft}, approval→{published,
-- draft}, published→{} — so this sprint changes WHERE the rule lives and not
-- WHICH states are reachable. Pim.Catalog.Integration.Tests asserts that
-- against the real database rather than against a copy of this comment.
--
-- There are six transitions and not five because Approval offers both REJECT
-- and REQUEST_CHANGES, per prototype v4.2 paneWorkflow() which draws three
-- buttons on that step (ADR-006: the prototype wins). They land on the same
-- step and differ in intent, which is precisely what approval_history.action
-- exists to record.
--
-- Published is TERMINAL, which is the pipeline's real limitation: a published
-- product cannot be routed back through review. Design v2.0 §12 solves that
-- with the draft/publication split (product_revision + product_publication),
-- and neither table exists yet. Editing a published product is still possible
-- — workflow does not gate edits — it simply is not re-approved.
-- =============================================================================

INSERT INTO workflow_definition (code, name, entity_type, version, is_active)
VALUES ('product_publishing', 'Product Publishing', 'PRODUCT', 1, true);

INSERT INTO workflow_step (definition_id, code, name, sort_order, owner_role, is_initial, is_terminal)
SELECT d.id, v.code, v.name, v.sort_order, v.owner_role, v.is_initial, v.is_terminal
FROM workflow_definition d
CROSS JOIN (VALUES
  ('draft',     'Draft',     1, 'Data Steward',       true,  false),
  ('review',    'Review',    2, 'Product Manager',    false, false),
  ('approval',  'Approval',  3, 'Compliance Manager', false, false),
  ('published', 'Published', 4, NULL,                 false, true)
) AS v(code, name, sort_order, owner_role, is_initial, is_terminal)
WHERE d.code = 'product_publishing' AND d.version = 1;

INSERT INTO workflow_transition (definition_id, from_step_id, to_step_id, action, label, sort_order)
SELECT d.id, fs.id, ts.id, v.action, v.label, v.sort_order
FROM workflow_definition d
CROSS JOIN (VALUES
  ('draft',    'review',    'SUBMIT',          'Submit for review',   1),
  ('review',   'approval',  'APPROVE',         'Pass review',         1),
  ('review',   'draft',     'REQUEST_CHANGES', 'Request changes',     2),
  ('approval', 'published', 'APPROVE',         'Approve and publish', 1),
  ('approval', 'draft',     'REJECT',          'Reject',              2),
  ('approval', 'draft',     'REQUEST_CHANGES', 'Request changes',     3)
) AS v(from_code, to_code, action, label, sort_order)
JOIN workflow_step fs ON fs.definition_id = d.id AND fs.code = v.from_code
JOIN workflow_step ts ON ts.definition_id = d.id AND ts.code = v.to_code
WHERE d.code = 'product_publishing' AND d.version = 1;

COMMENT ON TABLE workflow_definition IS
  'Configurable editorial pipeline (Design v2.0 §12). One active definition per '
  'entity type; superseding one means inserting a new version and flipping is_active.';
COMMENT ON TABLE approval_task IS
  'Live workflow instance. product.workflow_state is a denormalised projection of '
  'current_step_id, written in the same transaction.';
COMMENT ON TABLE approval_history IS
  'Append-only approval trail. actor_user_id is NULL until authentication ships (R-26).';

-- =============================================================================
-- Ownership & least-privilege grants for the service role
-- =============================================================================
ALTER SCHEMA catalog OWNER TO catalog_svc;
GRANT USAGE ON SCHEMA catalog TO catalog_svc;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA catalog TO catalog_svc;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA catalog TO catalog_svc;
ALTER DEFAULT PRIVILEGES IN SCHEMA catalog
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO catalog_svc;
