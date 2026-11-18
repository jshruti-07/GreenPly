-- =============================================================================
-- MODULE 07: PURGE CERAMIC TILES TAXONOMY & REBUILD PLYWOOD TAXONOMY
-- =============================================================================
-- Completely eliminates the legacy 'agrob' tile brand, all 9 tile categories,
-- all 70+ tile series nodes, and obsolete tile attribute sets.
-- Builds the comprehensive 11-category Plywood & Engineered Panels hierarchy
-- and maps all 28 products to their designated subcategories.
--
-- Execute via:
-- docker exec -i infra-postgres-1 psql -U pim_admin -d pim -f /pim-init/catalog/07_cleanup_tiles_and_rebuild_plywood_taxonomy.sql
-- =============================================================================

BEGIN;
SET search_path TO catalog, public;

-- 1. ENSURE PLYWOOD ATTRIBUTE SET & ROOT BRAND EXIST FIRST ---------------------
INSERT INTO brand (code, legal_entity, website, country_code, status)
VALUES ('greenply', 'Greenply Industries Limited', 'https://www.greenply.com', 'IN', 'active')
ON CONFLICT (code) DO NOTHING;

INSERT INTO brand_tr (brand_id, locale_code, name, description, tagline)
SELECT b.id, 'en-GB', 'Greenply', 'Premier Plywood and Engineered Wood Manufacturer', 'Zero Emission E0 Plywood'
FROM brand b WHERE b.code = 'greenply'
ON CONFLICT (brand_id, locale_code) DO UPDATE SET name = EXCLUDED.name;

INSERT INTO attribute_set (code, name) VALUES
  ('plywood_core', 'Plywood & Engineered Panels — Core Attributes')
ON CONFLICT (code) DO NOTHING;

-- 2. ENSURE ROOT & BASE CATEGORY NODES EXIST -----------------------------------
INSERT INTO taxonomy_node (code, node_type, parent_id, path, depth, brand_id, attribute_set_id)
SELECT 'greenply', 'FAMILY', NULL, 'greenply'::ltree, 0, b.id, (SELECT id FROM attribute_set WHERE code = 'plywood_core')
FROM brand b WHERE b.code = 'greenply'
ON CONFLICT (code) DO UPDATE 
SET attribute_set_id = (SELECT id FROM attribute_set WHERE code = 'plywood_core');

INSERT INTO taxonomy_node_tr (node_id, locale_code, name, description, translation_status)
SELECT n.id, v.locale, v.name, v.descr, 'approved'
FROM (VALUES
  ('greenply', 'en-GB', 'Plywood & Building Panels Collection', 'All structural, decorative, and performance panel lines'),
  ('greenply', 'de-DE', 'Sperrholz & Bauplatten Kollektion', 'Alle strukturellen, dekorativen und Hochleistungs-Plattenlinien')
) AS v(code, locale, name, descr)
JOIN taxonomy_node n ON n.code = v.code
ON CONFLICT (node_id, locale_code) DO UPDATE 
SET name = EXCLUDED.name, description = EXCLUDED.description;

-- Base category branches
INSERT INTO taxonomy_node (code, node_type, parent_id, path, depth, brand_id, attribute_set_id)
SELECT c.code, 'CATEGORY', r.id, ('greenply.' || c.code)::ltree, 1, r.brand_id, (SELECT id FROM attribute_set WHERE code = 'plywood_core')
FROM (VALUES
  ('plywood_blockboard'),
  ('flush_doors'),
  ('decorative_veneers'),
  ('pvc_wpc'),
  ('mdf_boards')
) AS c(code)
CROSS JOIN (SELECT id, brand_id FROM taxonomy_node WHERE code = 'greenply') r
ON CONFLICT (code) DO UPDATE
SET parent_id = EXCLUDED.parent_id,
    path = EXCLUDED.path,
    depth = EXCLUDED.depth,
    brand_id = EXCLUDED.brand_id,
    attribute_set_id = EXCLUDED.attribute_set_id;

INSERT INTO taxonomy_node_tr (node_id, locale_code, name, description, translation_status)
SELECT n.id, v.locale, v.name, v.descr, 'approved'
FROM (VALUES
  ('plywood_blockboard', 'en-GB', 'Plywood & Blockboards', 'Structural, Marine, Fire Retardant and Commercial panels'),
  ('plywood_blockboard', 'de-DE', 'Sperrholz & Tischlerplatten', 'Struktur-, Marine- und feuerhemmende Platten'),
  ('flush_doors',        'en-GB', 'Doors & Openings', 'High dimensional stability Flush & Decorative Doors'),
  ('flush_doors',        'de-DE', 'Türen & Zugänge', 'Formstabile Glatt- und Designtüren'),
  ('decorative_veneers', 'en-GB', 'Decorative Veneers', 'Natural Wood and engineered architectural face veneers'),
  ('decorative_veneers', 'de-DE', 'Echtholzfurniere', 'Natürliche und veredelte Echtholzfurniere'),
  ('pvc_wpc',            'en-GB', 'PVC & WPC Panels', 'Lead-free 100% waterproof composite boards'),
  ('pvc_wpc',            'de-DE', 'PVC- & WPC-Platten', 'Bleifreie, 100% wasserfeste Verbundplatten'),
  ('mdf_boards',         'en-GB', 'MDF & Engineered Panels', 'HDMR 710 and exterior grade fiberboards'),
  ('mdf_boards',         'de-DE', 'MDF & Faserplatten', 'HDMR 710 und feuchtebeständige Faserplatten')
) AS v(code, locale, name, descr)
JOIN taxonomy_node n ON n.code = v.code
ON CONFLICT (node_id, locale_code) DO UPDATE 
SET name = EXCLUDED.name, description = EXCLUDED.description;

-- 3. RE-ASSIGN ALL PRODUCTS TEMPORARILY TO PLYWOOD_BLOCKBOARD -----------------
UPDATE product 
SET primary_node_id = (SELECT id FROM taxonomy_node WHERE code = 'plywood_blockboard')
WHERE primary_node_id IN (
    SELECT id FROM taxonomy_node 
    WHERE path <@ 'agrob' OR code = 'agrob' 
       OR code IN ('green_club', 'green_platinum', 'green_710', 'green_gold', 'green_ecotec',
                   'green_club_cat', 'green_plat_cat', 'green_710_cat', 'green_gold_cat', 'green_mr_cat',
                   'ecotec_cat', 'optima_cat', 'calibrated_cat', 'shuttering_cat', 'blockboard_cat', 'veneers_cat')
);

-- 4. PURGE AGROB TILE TAXONOMY TREE & OBSOLETE SERIES -------------------------
-- Cascade automatically deletes taxonomy_node_tr records
DELETE FROM taxonomy_node WHERE path <@ 'agrob' OR code = 'agrob';

DELETE FROM taxonomy_node 
WHERE code IN ('green_club', 'green_platinum', 'green_710', 'green_gold', 'green_ecotec',
               'green_club_cat', 'green_plat_cat', 'green_710_cat', 'green_gold_cat', 'green_mr_cat',
               'ecotec_cat', 'optima_cat', 'calibrated_cat', 'shuttering_cat', 'blockboard_cat', 'veneers_cat');

-- Purge tile brands and tile attribute sets
DELETE FROM brand_tr WHERE brand_id IN (SELECT id FROM brand WHERE code IN ('agrob_buchtal', 'drytile'));
DELETE FROM brand WHERE code IN ('agrob_buchtal', 'drytile');

DELETE FROM attribute_set_attribute WHERE set_id IN (SELECT id FROM attribute_set WHERE code = 'tile_core');
DELETE FROM attribute_set WHERE code = 'tile_core';

-- 5. BUILD CLEAN PLYWOOD SUBCATEGORIES UNDER PLYWOOD_BLOCKBOARD ---------------
INSERT INTO taxonomy_node (code, node_type, parent_id, path, depth, brand_id, attribute_set_id)
SELECT v.code, 'SUBCATEGORY', p.id, (p.path::text || '.' || v.code)::ltree, (p.depth + 1)::smallint, COALESCE(p.brand_id, (SELECT id FROM brand WHERE code = 'greenply')), (SELECT id FROM attribute_set WHERE code = 'plywood_core')
FROM (VALUES
  ('club_plywood_cat'),
  ('platinum_bwp_cat'),
  ('marine_710_cat'),
  ('gold_calibrated_cat'),
  ('commercial_mr_cat'),
  ('ecotec_ply_cat'),
  ('optima_utility_cat'),
  ('calibrated_core_cat'),
  ('shuttering_ply_cat'),
  ('blockboards_doors_cat')
) AS v(code)
CROSS JOIN (SELECT id, path, depth, brand_id FROM taxonomy_node WHERE code = 'plywood_blockboard') p
ON CONFLICT (code) DO UPDATE
SET parent_id = EXCLUDED.parent_id,
    path = EXCLUDED.path,
    depth = EXCLUDED.depth,
    brand_id = EXCLUDED.brand_id,
    attribute_set_id = EXCLUDED.attribute_set_id;

-- Natural Decorative Veneers subcategory under decorative_veneers
INSERT INTO taxonomy_node (code, node_type, parent_id, path, depth, brand_id, attribute_set_id)
SELECT 'decorative_veneers_cat', 'SUBCATEGORY', p.id, (p.path::text || '.decorative_veneers_cat')::ltree, (p.depth + 1)::smallint, COALESCE(p.brand_id, (SELECT id FROM brand WHERE code = 'greenply')), (SELECT id FROM attribute_set WHERE code = 'plywood_core')
FROM taxonomy_node p WHERE p.code = 'decorative_veneers'
ON CONFLICT (code) DO UPDATE
SET parent_id = EXCLUDED.parent_id,
    path = EXCLUDED.path,
    depth = EXCLUDED.depth,
    brand_id = EXCLUDED.brand_id,
    attribute_set_id = EXCLUDED.attribute_set_id;

-- 6. INSERT APPROVED TRANSLATIONS (WITHOUT 'GREEN' PREFIXES) ------------------
INSERT INTO taxonomy_node_tr (node_id, locale_code, name, description, translation_status)
SELECT n.id, v.locale, v.name, v.descr, 'approved'
FROM (VALUES
  ('club_plywood_cat',       'en-GB', 'Club 700 / 500 Plywood',       'Premium Structural Zero Emission E0 Plywood and Cold-Press Flexiply'),
  ('club_plywood_cat',       'de-DE', 'Club 700 / 500 Sperrholz',     'Premium Struktur-Sperrholz E0 und formstabiles Flexiply'),
  ('platinum_bwp_cat',       'en-GB', 'Platinum Series BWP',           '2X Fire Retardant 100% BWP Boiling Waterproof Marine Plywood'),
  ('platinum_bwp_cat',       'de-DE', 'Platinum BWP Serie',            '2X feuerhemmendes kochwasserfestes BWP-Marinesperrholz'),
  ('marine_710_cat',         'en-GB', '710 Marine Series',             'Certified IS:710 Marine Grade Heavy Duty Plywood'),
  ('marine_710_cat',         'de-DE', '710 Marine Sperrholz',          'Zertifiziertes IS:710 Bootsbau- und Marinesperrholz'),
  ('gold_calibrated_cat',    'en-GB', 'Gold & Calibrated Boards',      'Superior Screw Holding Structural Calibrated Plywood'),
  ('gold_calibrated_cat',    'de-DE', 'Gold & Kalibrierte Platten',    'Struktursperrholz mit hoher Schraubenausreißfestigkeit'),
  ('commercial_mr_cat',      'en-GB', 'Commercial MR 303',             'Moisture Resistant Interior Commercial Plywood per IS:303'),
  ('commercial_mr_cat',      'de-DE', 'Handelsklasse MR 303',          'Feuchtebeständiges Innenausbau-Sperrholz nach IS:303'),
  ('ecotec_ply_cat',         'en-GB', 'Ecotec Plywood Line',           'Kaam Sahi Daam Sahi Sustainable Plantation Timber Plywood'),
  ('ecotec_ply_cat',         'de-DE', 'Ecotec Sperrholzlinie',         'Nachhaltiges Plantagenholz-Sperrholz für Innenausbau'),
  ('optima_utility_cat',     'en-GB', 'Optima & Utility Boards',       'Selected Core High-Density Utility Building Boards'),
  ('optima_utility_cat',     'de-DE', 'Optima & Universelle Platten',  'Ausgewählte universelle Trägerplatten für den Möbelbau'),
  ('calibrated_core_cat',    'en-GB', 'Calibrated CNC Core Panels',    'Penta-Tech Four-Side Calibrated Uniform Thickness Panels (6mm-25mm)'),
  ('calibrated_core_cat',    'de-DE', 'Kalibrierte CNC-Trägerplatten',  'Präzisionskalibrierte Trägerplatten mit gleichmäßiger Stärke'),
  ('shuttering_ply_cat',     'en-GB', 'Film-Faced Shuttering Ply',     'Mirror Finish High-Density Concrete Formwork Shuttering Ply'),
  ('shuttering_ply_cat',     'de-DE', 'Film-Beschichtetes Schalholz',  'Hochdichtes Betonschalungssperrholz mit Spiegelfläche'),
  ('blockboards_doors_cat',  'en-GB', 'Blockboards & Flush Doors',     'Solid Pine Core Blockboards and High Stability Flush Doors'),
  ('blockboards_doors_cat',  'de-DE', 'Tischlerplatten & Glatte Türen', 'Massivholzkern-Tischlerplatten und formstabile Türen'),
  ('decorative_veneers_cat', 'en-GB', 'Natural Decorative Veneers',    'Quarter & Crown Cut Architectural Face Veneers'),
  ('decorative_veneers_cat', 'de-DE', 'Echtholz-Designfurniere',       'Hochwertige Echtholz- und Edelholzfurniere')
) AS v(code, locale, name, descr)
JOIN taxonomy_node n ON n.code = v.code
ON CONFLICT (node_id, locale_code) DO UPDATE 
SET name = EXCLUDED.name, description = EXCLUDED.description, translation_status = 'approved';

-- 7. RE-ASSIGN ALL 28 PRODUCTS TO THEIR DEDICATED CATEGORY NODES -------------
UPDATE product SET primary_node_id = (SELECT id FROM taxonomy_node WHERE code = 'club_plywood_cat')
WHERE product_code IN ('GP-CLUB-700', 'GP-CLUB-500', 'GP-FLEX-PLY');

UPDATE product SET primary_node_id = (SELECT id FROM taxonomy_node WHERE code = 'platinum_bwp_cat')
WHERE product_code IN ('GP-PLAT-BWP', 'GP-FIRE-RET');

UPDATE product SET primary_node_id = (SELECT id FROM taxonomy_node WHERE code = 'marine_710_cat')
WHERE product_code = 'GP-710-MAR';

UPDATE product SET primary_node_id = (SELECT id FROM taxonomy_node WHERE code = 'gold_calibrated_cat')
WHERE product_code = 'GP-GOLD-BWP';

UPDATE product SET primary_node_id = (SELECT id FROM taxonomy_node WHERE code = 'commercial_mr_cat')
WHERE product_code = 'GP-303-MR';

UPDATE product SET primary_node_id = (SELECT id FROM taxonomy_node WHERE code = 'ecotec_ply_cat')
WHERE product_code IN ('GP-ECO-PLAT', 'GP-ECO-710', 'GP-ECO-MR', 'GP-ECO-TMR');

UPDATE product SET primary_node_id = (SELECT id FROM taxonomy_node WHERE code = 'optima_utility_cat')
WHERE product_code IN ('GP-OPTIMA-G', 'GP-BHAROSA', 'GP-JANSATHI');

UPDATE product SET primary_node_id = (SELECT id FROM taxonomy_node WHERE code = 'calibrated_core_cat')
WHERE product_code IN ('GP-CALIB-25', 'GP-CALIB-19', 'GP-CALIB-16', 'GP-CALIB-12', 'GP-CALIB-9', 'GP-CALIB-6');

UPDATE product SET primary_node_id = (SELECT id FROM taxonomy_node WHERE code = 'shuttering_ply_cat')
WHERE product_code = 'GP-SHUTTER-BWP';

UPDATE product SET primary_node_id = (SELECT id FROM taxonomy_node WHERE code = 'blockboards_doors_cat')
WHERE product_code IN ('GP-BLOCK-25', 'GP-BLOCK-19', 'GP-DOOR-FLUSH');

UPDATE product SET primary_node_id = (SELECT id FROM taxonomy_node WHERE code = 'decorative_veneers_cat')
WHERE product_code IN ('GP-VENEER-TEAK', 'GP-VENEER-WALNUT', 'GP-VENEER-OAK');

-- 8. ENSURE ALL PLYWOOD NODES DECLARE THE PLYWOOD ATTRIBUTE SET ---------------
UPDATE taxonomy_node
SET attribute_set_id = (SELECT id FROM attribute_set WHERE code = 'plywood_core')
WHERE path <@ 'greenply';

COMMIT;
