-- =============================================================================
-- MODULE 08: PURGE CERAMIC TILE DIGITAL ASSETS & SEED PLYWOOD ASSET SUITE
-- =============================================================================
-- Completely eliminates legacy ceramic tile assets (Alcina, Craft Extended,
-- pool edge systems, grooved tiles, etc.) from catalog.asset and catalog.asset_tr.
-- Populates authentic Plywood & Engineered Panels digital assets (Hero images,
-- calibrated cross-sections, IS:710/IS:5509 test certificates, BIM models)
-- and links every asset directly to the 28 plywood products.
--
-- Execute via:
-- docker exec -i infra-postgres-1 psql -U pim_admin -d pim -f /pim-init/catalog/08_replace_tile_assets_with_plywood_assets.sql
-- =============================================================================

BEGIN;
SET search_path TO catalog, public;

-- 1. PURGE OBSOLETE CERAMIC TILE ASSETS & LINKS --------------------------------
-- Cascade automatically deletes asset_tr, asset_rendition, and asset_link records
DELETE FROM asset 
WHERE storage_provider = 'client-magento'
   OR storage_key LIKE '%agrob-buchtal%'
   OR storage_key LIKE '%drytile%'
   OR file_name LIKE '%.rvt'
   OR file_name LIKE '%tile%';

-- 2. INSERT HIGH-RESOLUTION PLYWOOD ASSET CATALOGUE ---------------------------
INSERT INTO asset (storage_provider, storage_key, file_name, mime_type, file_size, asset_type, width, height, status)
VALUES
  -- Club 700 / 500 Assets
  ('s3', 'https://assets.plygallery.com/products/club_700_sheet_hero.jpg',
   'club_700_sheet_hero.jpg', 'image/jpeg', 2450000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/club_700_cross_section_layers.jpg',
   'club_700_cross_section_layers.jpg', 'image/jpeg', 1890000, 'IMAGE', 1920, 1280, 'active'),
  ('s3', 'https://assets.plygallery.com/products/club_700_application_kitchen.jpg',
   'club_700_application_kitchen.jpg', 'image/jpeg', 2150000, 'IMAGE', 1920, 1280, 'active'),
  ('s3', 'https://www.plygallery.com/datasheets/Club_700_Technical_Datasheet_2026.pdf',
   'Club_700_Technical_Datasheet_2026.pdf', 'application/pdf', 1450200, 'DOCUMENT', NULL, NULL, 'active'),
  ('s3', 'https://www.plygallery.com/certifications/IS710_72hr_Boiling_Water_Test_Report.pdf',
   'IS710_72hr_Boiling_Water_Test_Report.pdf', 'application/pdf', 890400, 'DOCUMENT', NULL, NULL, 'active'),
  ('s3', 'https://www.plygallery.com/certifications/E0_Emission_CARB_P2_Certificate.pdf',
   'E0_Emission_CARB_P2_Certificate.pdf', 'application/pdf', 670100, 'DOCUMENT', NULL, NULL, 'active'),
  ('s3', 'https://bim.plygallery.com/objects/club_700_panel_3d.ifc',
   'club_700_panel_3d.ifc', 'application/x-step', 4210000, 'BIM', NULL, NULL, 'active'),
  ('s3', 'https://bim.plygallery.com/cad/club_700_cad_section.dwg',
   'club_700_cad_section.dwg', 'application/acad', 1120000, 'CAD', NULL, NULL, 'active'),
  ('s3', 'https://assets.plygallery.com/products/club_500_sheet_hero.jpg',
   'club_500_sheet_hero.jpg', 'image/jpeg', 2240000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/club_flexiply_sheet_hero.jpg',
   'club_flexiply_sheet_hero.jpg', 'image/jpeg', 2180000, 'IMAGE', 2400, 1600, 'active'),

  -- Platinum Series Assets
  ('s3', 'https://assets.plygallery.com/products/platinum_bwp_sheet_hero.jpg',
   'platinum_bwp_sheet_hero.jpg', 'image/jpeg', 2310000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/fire_retardant_sheet_hero.jpg',
   'fire_retardant_sheet_hero.jpg', 'image/jpeg', 2420000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://www.plygallery.com/datasheets/IS5509_Fire_Retardant_Test_Report.pdf',
   'IS5509_Fire_Retardant_Test_Report.pdf', 'application/pdf', 1340000, 'DOCUMENT', NULL, NULL, 'active'),

  -- 710 Marine & Gold Assets
  ('s3', 'https://assets.plygallery.com/products/710_marine_sheet_hero.jpg',
   '710_marine_sheet_hero.jpg', 'image/jpeg', 2290000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/gold_bwp_sheet_hero.jpg',
   'gold_bwp_sheet_hero.jpg', 'image/jpeg', 2190000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/mr_303_sheet_hero.jpg',
   'mr_303_sheet_hero.jpg', 'image/jpeg', 2140000, 'IMAGE', 2400, 1600, 'active'),

  -- Ecotec Plywood Line Assets
  ('s3', 'https://assets.plygallery.com/products/ecotec_platinum_sheet_hero.jpg',
   'ecotec_platinum_sheet_hero.jpg', 'image/jpeg', 2180000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/ecotec_710_sheet_hero.jpg',
   'ecotec_710_sheet_hero.jpg', 'image/jpeg', 2150000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/ecotec_mr_sheet_hero.jpg',
   'ecotec_mr_sheet_hero.jpg', 'image/jpeg', 2120000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/ecotec_truemr_sheet_hero.jpg',
   'ecotec_truemr_sheet_hero.jpg', 'image/jpeg', 2160000, 'IMAGE', 2400, 1600, 'active'),

  -- Optima & Utility Assets
  ('s3', 'https://assets.plygallery.com/products/optima_g_sheet_hero.jpg',
   'optima_g_sheet_hero.jpg', 'image/jpeg', 2050000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/bharosa_mr_sheet_hero.jpg',
   'bharosa_mr_sheet_hero.jpg', 'image/jpeg', 2020000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/jansathi_utility_sheet_hero.jpg',
   'jansathi_utility_sheet_hero.jpg', 'image/jpeg', 1980000, 'IMAGE', 2400, 1600, 'active'),

  -- Calibrated Core Panels Assets
  ('s3', 'https://assets.plygallery.com/products/calibrated_25mm_sheet_hero.jpg',
   'calibrated_25mm_sheet_hero.jpg', 'image/jpeg', 2350000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/calibrated_19mm_sheet_hero.jpg',
   'calibrated_19mm_sheet_hero.jpg', 'image/jpeg', 2310000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/calibrated_16mm_sheet_hero.jpg',
   'calibrated_16mm_sheet_hero.jpg', 'image/jpeg', 2280000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/calibrated_12mm_sheet_hero.jpg',
   'calibrated_12mm_sheet_hero.jpg', 'image/jpeg', 2220000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/calibrated_9mm_sheet_hero.jpg',
   'calibrated_9mm_sheet_hero.jpg', 'image/jpeg', 2160000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/calibrated_6mm_sheet_hero.jpg',
   'calibrated_6mm_sheet_hero.jpg', 'image/jpeg', 2090000, 'IMAGE', 2400, 1600, 'active'),

  -- Shuttering & Doors Assets
  ('s3', 'https://assets.plygallery.com/products/shuttering_30kg_sheet_hero.jpg',
   'shuttering_30kg_sheet_hero.jpg', 'image/jpeg', 2480000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/blockboard_25mm_sheet_hero.jpg',
   'blockboard_25mm_sheet_hero.jpg', 'image/jpeg', 2290000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/blockboard_19mm_sheet_hero.jpg',
   'blockboard_19mm_sheet_hero.jpg', 'image/jpeg', 2250000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/flush_door_30mm_hero.jpg',
   'flush_door_30mm_hero.jpg', 'image/jpeg', 2340000, 'IMAGE', 2400, 1600, 'active'),

  -- Architectural Decorative Veneers Assets
  ('s3', 'https://assets.plygallery.com/products/veneer_royal_teak_hero.jpg',
   'veneer_royal_teak_hero.jpg', 'image/jpeg', 2520000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/veneer_american_walnut_hero.jpg',
   'veneer_american_walnut_hero.jpg', 'image/jpeg', 2480000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.plygallery.com/products/veneer_smoked_oak_hero.jpg',
   'veneer_smoked_oak_hero.jpg', 'image/jpeg', 2460000, 'IMAGE', 2400, 1600, 'active')
ON CONFLICT (storage_provider, storage_key) DO UPDATE
SET file_name = EXCLUDED.file_name,
    status = 'active';

-- 3. ENSURE WEB RENDITIONS FOR ALL IMAGES -------------------------------------
INSERT INTO asset_rendition (asset_id, role, uri, width, height, format)
SELECT a.id, 'web', a.storage_key, a.width, a.height, 'jpeg'
FROM asset a
WHERE a.asset_type = 'IMAGE'
ON CONFLICT (asset_id, role) DO UPDATE
SET uri = EXCLUDED.uri;

-- 4. INSERT MULTILINGUAL ASSET TITLES & ALT TEXTS (NO "GREEN" PREFIXES) -------
INSERT INTO asset_tr (asset_id, locale_code, title, alt_text, caption)
SELECT a.id, v.locale, v.title, v.alt, v.cap
FROM (VALUES
  ('club_700_sheet_hero.jpg', 'en-GB', 'Club 700 Plywood Master Sheet', 'Calibrated Club 700 Plywood sheet', 'Flagship structural zero-emission E0 plywood'),
  ('club_700_sheet_hero.jpg', 'de-DE', 'Club 700 Sperrholz Hauptplatte', 'Kalibrierte Club 700 Sperrholzplatte', 'Premium E0-Struktursperrholz Hauptansicht'),
  ('club_700_cross_section_layers.jpg', 'en-GB', 'Penta-Tech Calibration Layers', 'Cross section of calibrated hardwood core veneers', 'High precision joinery core structure'),
  ('club_700_application_kitchen.jpg', 'en-GB', 'Modular Kitchen Cabinetry Carcass', 'Waterproof BWP kitchen carcass in Club 700', 'Heavy-duty cabinetry installation'),
  ('Club_700_Technical_Datasheet_2026.pdf', 'en-GB', 'Club 700 Technical Data Sheet (2026)', 'Club 700 Product Engineering TDS PDF', 'Complete physical, chemical and structural properties'),
  ('IS710_72hr_Boiling_Water_Test_Report.pdf', 'en-GB', 'IS:710 72-Hour Boiling Water Test Report', 'Laboratory test report proving zero delamination', 'Independent laboratory test certificate'),
  ('E0_Emission_CARB_P2_Certificate.pdf', 'en-GB', 'CARB Phase 2 / E0 Zero Emission Certificate', 'Formaldehyde emission test certificate', 'Safe indoor air quality certification'),
  ('club_700_panel_3d.ifc', 'en-GB', '3D BIM Parametric Panel Object (IFC)', 'BIM model of Club 700 panel', 'Parametric object for Revit and ArchiCAD'),
  ('club_700_cad_section.dwg', 'en-GB', 'CAD Joinery Detail Section (DWG)', '2D CAD edge joinery drawing', 'Detailed edge profile and screw-holding joinery'),
  ('club_500_sheet_hero.jpg', 'en-GB', 'Club 500 Plywood Panel', 'Club 500 structural BWP hardwood panel', 'Heavy-duty customized joinery panel'),
  ('club_flexiply_sheet_hero.jpg', 'en-GB', 'Club Flexiply Cold-Press Contour Sheet', 'Flexible plywood for pillars and arches', 'Contour joinery curved plywood'),
  ('platinum_bwp_sheet_hero.jpg', 'en-GB', 'Platinum Series BWP Marine Panel', 'Platinum BWP calibrated marine plywood', '100% boiling waterproof marine plywood'),
  ('fire_retardant_sheet_hero.jpg', 'en-GB', 'Fire-Retardant Low Flammability Sheet', 'IS:5509 treated fire retardant plywood', 'Treated low flame spread panel'),
  ('IS5509_Fire_Retardant_Test_Report.pdf', 'en-GB', 'IS:5509 Fire Delay Test Certification', 'Laboratory flame penetration delay report', '50+ minutes flame penetration delay test'),
  ('710_marine_sheet_hero.jpg', 'en-GB', '710 Marine Series Plywood Sheet', 'Certified IS:710 marine plywood', 'Wet and marine environment structural board'),
  ('gold_bwp_sheet_hero.jpg', 'en-GB', 'Gold BWP Structural Calibrated Board', 'Superior screw holding calibrated plywood', 'Zero hollow pockets wardrobe core'),
  ('mr_303_sheet_hero.jpg', 'en-GB', 'Commercial MR 303 Interior Joinery Sheet', 'Moisture resistant commercial plywood', 'Melamine urea bonded interior board'),
  ('ecotec_platinum_sheet_hero.jpg', 'en-GB', 'Ecotec Platinum BWP Panel', 'Ecotec Platinum calibrated board', 'Sustainable timber BWP plywood'),
  ('ecotec_710_sheet_hero.jpg', 'en-GB', 'Ecotec 710 Marine Grade Timber Sheet', 'Ecotec 710 marine plywood', 'Waterproof marine utility board'),
  ('ecotec_mr_sheet_hero.jpg', 'en-GB', 'Ecotec MR Commercial Timber Board', 'Ecotec MR commercial plywood', 'Interior furniture utility panel'),
  ('ecotec_truemr_sheet_hero.jpg', 'en-GB', 'Ecotec True MR Moisture Resistant Panel', 'Ecotec True MR interior plywood', 'Moisture resistant partition panel'),
  ('optima_g_sheet_hero.jpg', 'en-GB', 'Optima G Selected Core Utility Sheet', 'Optima G utility building board', 'High-density utility framing sheet'),
  ('bharosa_mr_sheet_hero.jpg', 'en-GB', 'Bharosa MR Commercial Board', 'Bharosa commercial interior plywood', 'Reliable framing interior panel'),
  ('jansathi_utility_sheet_hero.jpg', 'en-GB', 'Jansathi Utility Interior Plywood', 'Jansathi economy interior sheet', 'Carpentry grade economy sheet'),
  ('calibrated_25mm_sheet_hero.jpg', 'en-GB', 'Calibrated CNC Core 25mm Panel', '25mm four-side calibrated core panel', 'Heavy architectural joinery panel'),
  ('calibrated_19mm_sheet_hero.jpg', 'en-GB', 'Calibrated CNC Core 19mm Panel', '19mm uniform thickness core panel', 'Standard furniture carcass panel'),
  ('calibrated_16mm_sheet_hero.jpg', 'en-GB', 'Calibrated CNC Core 16mm Panel', '16mm calibrated core panel', 'Precision wardrobe shelf panel'),
  ('calibrated_12mm_sheet_hero.jpg', 'en-GB', 'Calibrated CNC Core 12mm Panel', '12mm calibrated core panel', 'Drawer and partition board'),
  ('calibrated_9mm_sheet_hero.jpg', 'en-GB', 'Calibrated CNC Core 9mm Panel', '9mm calibrated core panel', 'Underlayment and backing board'),
  ('calibrated_6mm_sheet_hero.jpg', 'en-GB', 'Calibrated CNC Core 6mm Panel', '6mm calibrated core panel', 'Curved backing and lining board'),
  ('shuttering_30kg_sheet_hero.jpg', 'en-GB', 'Film-Faced Shuttering Ply 30kg Mirror Finish', 'Mirror concrete construction shuttering ply', 'High repetition concrete formwork board'),
  ('blockboard_25mm_sheet_hero.jpg', 'en-GB', 'Gold BWP Pine Blockboard 25mm', '25mm solid pine core blockboard', 'Heavy-duty non-sagging shelf board'),
  ('blockboard_19mm_sheet_hero.jpg', 'en-GB', 'Club Pine Blockboard 19mm', '19mm pine core blockboard', 'High stability wardrobe shutter board'),
  ('flush_door_30mm_hero.jpg', 'en-GB', 'BWP Solid Core 30mm Flush Door Shutter', '30mm solid core flush door', 'Dimensional stability interior/exterior door'),
  ('veneer_royal_teak_hero.jpg', 'en-GB', 'Royal Teak Architectural Face Veneer', 'Royal Teak crown cut face veneer', 'Luxury architectural face veneer'),
  ('veneer_american_walnut_hero.jpg', 'en-GB', 'American Walnut Face Veneer', 'American Walnut quarter cut veneer', 'Premium furniture face veneer'),
  ('veneer_smoked_oak_hero.jpg', 'en-GB', 'Smoked White Oak Architectural Veneer', 'Smoked White Oak face veneer', 'Contemporary architectural wood finish')
) AS v(fname, locale, title, alt, cap)
JOIN asset a ON a.file_name = v.fname
ON CONFLICT (asset_id, locale_code) DO UPDATE
SET title = EXCLUDED.title,
    alt_text = EXCLUDED.alt_text,
    caption = EXCLUDED.caption;

-- 5. LINK ASSETS TO PRODUCTS (POPULATING "USED BY" IN DETAIL DRAWER) -----------
DELETE FROM asset_link; -- Reset links to connect cleanly to all 28 products

INSERT INTO asset_link (product_id, asset_id, role, is_primary, sort_order)
SELECT p.id, a.id, v.role, v.is_pri, v.sort_order
FROM (VALUES
  ('PG-CLUB-700',     'club_700_sheet_hero.jpg',                     'MAIN',      true,  1),
  ('PG-CLUB-700',     'club_700_cross_section_layers.jpg',           'GALLERY',   false, 2),
  ('PG-CLUB-700',     'club_700_application_kitchen.jpg',           'GALLERY',   false, 3),
  ('PG-CLUB-700',     'Club_700_Technical_Datasheet_2026.pdf',       'DATASHEET', false, 4),
  ('PG-CLUB-700',     'IS710_72hr_Boiling_Water_Test_Report.pdf', 'DATASHEET', false, 5),
  ('PG-CLUB-700',     'E0_Emission_CARB_P2_Certificate.pdf',         'DATASHEET', false, 6),
  ('PG-CLUB-700',     'club_700_panel_3d.ifc',                       'BIM',       false, 7),
  ('PG-CLUB-700',     'club_700_cad_section.dwg',                    'DRAWING',   false, 8),
  ('PG-CLUB-500',     'club_500_sheet_hero.jpg',                     'MAIN',      true,  1),
  ('PG-FLEX-PLY',     'club_flexiply_sheet_hero.jpg',                'MAIN',      true,  1),
  ('PG-PLAT-BWP',     'platinum_bwp_sheet_hero.jpg',                 'MAIN',      true,  1),
  ('PG-FIRE-RET',     'fire_retardant_sheet_hero.jpg',               'MAIN',      true,  1),
  ('PG-FIRE-RET',     'IS5509_Fire_Retardant_Test_Report.pdf',       'DATASHEET', false, 2),
  ('PG-710-MAR',      '710_marine_sheet_hero.jpg',                   'MAIN',      true,  1),
  ('PG-GOLD-BWP',     'gold_bwp_sheet_hero.jpg',                     'MAIN',      true,  1),
  ('PG-303-MR',       'mr_303_sheet_hero.jpg',                       'MAIN',      true,  1),
  ('PG-ECO-PLAT',     'ecotec_platinum_sheet_hero.jpg',              'MAIN',      true,  1),
  ('PG-ECO-710',      'ecotec_710_sheet_hero.jpg',                   'MAIN',      true,  1),
  ('PG-ECO-MR',       'ecotec_mr_sheet_hero.jpg',                    'MAIN',      true,  1),
  ('PG-ECO-TMR',      'ecotec_truemr_sheet_hero.jpg',                'MAIN',      true,  1),
  ('PG-OPTIMA-G',     'optima_g_sheet_hero.jpg',                     'MAIN',      true,  1),
  ('PG-BHAROSA',      'bharosa_mr_sheet_hero.jpg',                   'MAIN',      true,  1),
  ('PG-JANSATHI',     'jansathi_utility_sheet_hero.jpg',             'MAIN',      true,  1),
  ('PG-CALIB-25',     'calibrated_25mm_sheet_hero.jpg',              'MAIN',      true,  1),
  ('PG-CALIB-19',     'calibrated_19mm_sheet_hero.jpg',              'MAIN',      true,  1),
  ('PG-CALIB-16',     'calibrated_16mm_sheet_hero.jpg',              'MAIN',      true,  1),
  ('PG-CALIB-12',     'calibrated_12mm_sheet_hero.jpg',              'MAIN',      true,  1),
  ('PG-CALIB-9',      'calibrated_9mm_sheet_hero.jpg',               'MAIN',      true,  1),
  ('PG-CALIB-6',      'calibrated_6mm_sheet_hero.jpg',               'MAIN',      true,  1),
  ('PG-SHUTTER-BWP',  'shuttering_30kg_sheet_hero.jpg',              'MAIN',      true,  1),
  ('PG-BLOCK-25',     'blockboard_25mm_sheet_hero.jpg',              'MAIN',      true,  1),
  ('PG-BLOCK-19',     'blockboard_19mm_sheet_hero.jpg',              'MAIN',      true,  1),
  ('PG-DOOR-FLUSH',   'flush_door_30mm_hero.jpg',                    'MAIN',      true,  1),
  ('PG-VENEER-TEAK',  'veneer_royal_teak_hero.jpg',                  'MAIN',      true,  1),
  ('PG-VENEER-WALNUT','veneer_american_walnut_hero.jpg',             'MAIN',      true,  1),
  ('PG-VENEER-OAK',   'veneer_smoked_oak_hero.jpg',                  'MAIN',      true,  1)
) AS v(p_code, fname, role, is_pri, sort_order)
JOIN product p ON p.product_code = v.p_code
JOIN asset a ON a.file_name = v.fname
ON CONFLICT (product_id, asset_id, role) DO UPDATE
SET is_primary = EXCLUDED.is_primary,
    sort_order = EXCLUDED.sort_order;

COMMIT;
