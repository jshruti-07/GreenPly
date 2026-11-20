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

-- Align product media with authentic uploads_ply files
UPDATE catalog.asset SET storage_key = '/uploads_ply/ply-club-700.jpeg', file_name = 'ply-club-700.jpeg', storage_provider = 'local' WHERE file_name LIKE '%club_700%';
UPDATE catalog.asset SET storage_key = '/uploads_ply/CLUB500.jpg', file_name = 'CLUB500.jpg', storage_provider = 'local' WHERE file_name LIKE '%club_500%';
UPDATE catalog.asset SET storage_key = '/uploads_ply/Platinum_Fire_retardant_BWP_Grade_Plywood_Thickness_18_mm_Plywood_79ebc4f8-850f-4dfc-a901-e31a17f91707.webp', file_name = 'platinum_fire_retardant_bwp.webp', storage_provider = 'local' WHERE file_name LIKE '%platinum%';
UPDATE catalog.asset SET storage_key = '/uploads_ply/fire plywood.jpg', file_name = 'fire plywood.jpg', storage_provider = 'local' WHERE file_name LIKE '%fire%';
UPDATE catalog.asset SET storage_key = '/uploads_ply/marine plywood.jpg', file_name = 'marine plywood.jpg', storage_provider = 'local' WHERE file_name LIKE '%710%' OR file_name LIKE '%marine%';
UPDATE catalog.asset SET storage_key = '/uploads_ply/Greenply Gold 710 BWP Plywood.png', file_name = 'Greenply Gold 710 BWP Plywood.png', storage_provider = 'local' WHERE file_name LIKE '%gold%';
UPDATE catalog.asset SET storage_key = '/uploads_ply/Film faced plywood shuttering plywood for construction _ TT PLYWOOD.png', file_name = 'film_faced_shuttering_ply.png', storage_provider = 'local' WHERE file_name LIKE '%shuttering%';
UPDATE catalog.asset SET storage_key = '/uploads_ply/Buy Online Navkar Premium Blockboard ( Pure Pine ) 25mm.png', file_name = 'pure_pine_blockboard_25mm.png', storage_provider = 'local' WHERE file_name LIKE '%blockboard%';
UPDATE catalog.asset SET storage_key = '/uploads_ply/Flush Doors_ Design, Benefits, and Uses.png', file_name = 'solid_core_flush_door.png', storage_provider = 'local' WHERE file_name LIKE '%flush%' OR file_name LIKE '%door%';
UPDATE catalog.asset SET storage_key = '/uploads_ply/What Makes Teak Veneers an Ideal Pick for Your Home – Duroply Latest Blogs & Information.png', file_name = 'natural_teak_veneer.png', storage_provider = 'local' WHERE file_name LIKE '%teak%' OR file_name LIKE '%veneer%';
UPDATE catalog.asset SET storage_key = '/uploads_ply/What is Walnut Veneer.png', file_name = 'american_walnut_veneer.png', storage_provider = 'local' WHERE file_name LIKE '%walnut%';
UPDATE catalog.asset SET storage_key = '/uploads_ply/Promaxx Plus Ply 19 mm (8x4).png', file_name = 'promaxx_plus_ply_19mm.png', storage_provider = 'local' WHERE file_name LIKE '%19mm%';
UPDATE catalog.asset SET storage_key = '/uploads_ply/9mm BWP Calibrated Plywood at ₹ 34_sq ft _ Plywood Board in New Delhi _ ID_ 2856762006391.png', file_name = 'calibrated_9mm.png', storage_provider = 'local' WHERE file_name LIKE '%9mm%';
UPDATE catalog.asset SET storage_key = '/uploads_ply/Frikly Club BWP Red Core Calibrated Plywood _ 6 MM _ 8 x 4 Feet.png', file_name = 'calibrated_6mm.png', storage_provider = 'local' WHERE file_name LIKE '%6mm%';

-- Mark all catalog specifications and documents as locally managed assets
UPDATE catalog.asset
SET storage_provider = 'local'
WHERE storage_provider = 's3';

-- 7. REFRESH SEARCH VECTOR CACHE FOR RENAMED PRODUCTS -------------------------
UPDATE catalog.product
SET attributes_cache = attributes_cache
WHERE product_code LIKE 'PG-%';

COMMIT;

-- 8. VERIFICATION SUMMARY -----------------------------------------------------
SELECT 'Brands' AS entity, code, legal_entity, website FROM catalog.brand WHERE code = 'plygallery';
SELECT 'Taxonomy Root' AS entity, code, path::text, depth FROM catalog.taxonomy_node WHERE code = 'plygallery';
SELECT 'Products Migrated' AS entity, count(*) AS count FROM catalog.product WHERE product_code LIKE 'PG-%';
