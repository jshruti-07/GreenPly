-- =============================================================================
-- MIGRATION: REBRAND FROM GREENPLY TO PLY GALLERY (ZERO DATA LOSS)
-- =============================================================================
-- Updates Brand metadata, Taxonomy paths, Product & SKU codes (GP-* -> PG-*),
-- and sample Asset URIs while fully preserving all user-uploaded images and documents.
-- =============================================================================

BEGIN;

-- 1. UPDATE BRAND -------------------------------------------------------------
UPDATE catalog.brand
SET code = 'plygallery',
    legal_entity = 'Ply Gallery Industries Limited',
    website = 'https://www.plygallery.com'
WHERE code = 'greenply';

-- 2. UPDATE BRAND TRANSLATIONS ------------------------------------------------
UPDATE catalog.brand_tr
SET name = 'Ply Gallery',
    tagline = 'Zero Emission (E0) Pioneer',
    description = 'Premier manufacturer and curator of zero-emission plywood, blockboards, decorative veneers, and architectural panels.'
WHERE brand_id IN (SELECT id FROM catalog.brand WHERE code = 'plygallery')
   OR name ILIKE '%greenply%';

-- 3. UPDATE TAXONOMY ROOT NODE & TREE PATHS -----------------------------------
-- A. Update the root node code and path
UPDATE catalog.taxonomy_node
SET code = 'plygallery',
    path = 'plygallery'::ltree
WHERE code = 'greenply';

-- B. Update all descendant ltree paths from greenply.* to plygallery.*
UPDATE catalog.taxonomy_node
SET path = ('plygallery.' || subpath(path, 1))::ltree
WHERE path <@ 'greenply' AND code <> 'plygallery';

-- C. Update taxonomy node translations
UPDATE catalog.taxonomy_node_tr
SET name = replace(name, 'Greenply', 'Ply Gallery'),
    description = replace(description, 'Greenply', 'Ply Gallery')
WHERE name ILIKE '%greenply%' OR description ILIKE '%greenply%';

UPDATE catalog.taxonomy_node_tr
SET name = 'Ply Gallery Collection',
    description = 'All Ply Gallery product lines and structural materials'
WHERE node_id IN (SELECT id FROM catalog.taxonomy_node WHERE code = 'plygallery');

-- 4. UPDATE PRODUCT CODES & ERP/SAP REFERENCES (GP-* -> PG-*) -----------------
UPDATE catalog.product
SET product_code = regexp_replace(product_code, '^GP-', 'PG-'),
    sap_material_no = regexp_replace(COALESCE(sap_material_no, ''), '^SAP-GP-', 'SAP-PG-')
WHERE product_code LIKE 'GP-%';

-- 5. UPDATE SKU CODES (GP-* -> PG-*) ------------------------------------------
UPDATE catalog.sku
SET sku_code = regexp_replace(sku_code, '^GP-', 'PG-')
WHERE sku_code LIKE 'GP-%';

-- 6. UPDATE ASSET URIs & STORAGE KEYS -----------------------------------------
UPDATE catalog.asset
SET uri = replace(uri, 'greenply.com', 'plygallery.com'),
    storage_key = replace(storage_key, 'greenply.com', 'plygallery.com')
WHERE uri LIKE '%greenply.com%' OR storage_key LIKE '%greenply.com%';

UPDATE catalog.asset_rendition
SET uri = replace(uri, 'greenply.com', 'plygallery.com')
WHERE uri LIKE '%greenply.com%';

-- 7. REFRESH SEARCH VECTOR CACHE FOR RENAMED PRODUCTS -------------------------
UPDATE catalog.product
SET attributes_cache = attributes_cache
WHERE product_code LIKE 'PG-%';

COMMIT;

-- 8. VERIFICATION SUMMARY -----------------------------------------------------
SELECT 'Brands' AS entity, code, legal_entity, website FROM catalog.brand WHERE code = 'plygallery';
SELECT 'Taxonomy Root' AS entity, code, path::text, depth FROM catalog.taxonomy_node WHERE code = 'plygallery';
SELECT 'Products Migrated' AS entity, count(*) AS count FROM catalog.product WHERE product_code LIKE 'PG-%';
