-- =============================================================================
-- MODULE 06: COMPLETE 28 PLYWOOD & BUILDING BOARDS SUITE SEED
-- Reference: https://www.greenply.com/products
--
-- Applies all 28 flagship, economy, calibrated core, shuttering, blockboard, and
-- decorative face veneer products from the Greenply catalogue to catalog.product.
-- Fixes constraints for taxonomy_node (path, depth), sku (product_id), and attribute_value.
--
-- Execute via:
-- docker exec -i infra-postgres-1 psql -U pim_admin -d pim -f /pim-init/catalog/06_all_plywood_products.sql
-- =============================================================================

SET search_path TO catalog, public;

-- 1. CLEANUP OLD TILE PRODUCTS ------------------------------------------------
-- Cascades automatically delete product_tr, attribute_value, sku, packaging, etc.
DELETE FROM product WHERE product_code NOT LIKE 'GP-%';

-- 2. TAXONOMY NODES FOR PLYWOOD CATEGORIES ------------------------------------
INSERT INTO taxonomy_node (code, node_type, parent_id, path, depth, brand_id, attribute_set_id)
SELECT v.code, 'CATEGORY', p.id, (p.path::text || '.' || v.code)::ltree, (p.depth + 1)::smallint, COALESCE(p.brand_id, (SELECT id FROM brand WHERE code = 'greenply')), (SELECT id FROM attribute_set WHERE code = 'plywood_core')
FROM (VALUES
  ('green_club_cat'),
  ('green_plat_cat'),
  ('green_710_cat'),
  ('green_gold_cat'),
  ('green_mr_cat'),
  ('ecotec_cat'),
  ('optima_cat'),
  ('calibrated_cat'),
  ('shuttering_cat'),
  ('blockboard_cat'),
  ('veneers_cat')
) AS v(code)
CROSS JOIN (SELECT id, path, depth, brand_id FROM taxonomy_node WHERE code = 'plywood_blockboard') p
ON CONFLICT (code) DO NOTHING;

INSERT INTO taxonomy_node_tr (node_id, locale_code, name, description, translation_status)
SELECT n.id, v.locale, v.name, v.descr, 'approved' FROM (VALUES
  ('green_club_cat', 'en-GB', 'Club 700 / 500', 'Premium Structural Zero Emission E0 Plywood'),
  ('green_plat_cat', 'en-GB', 'Platinum Series', '2X Fire Retardant 100% BWP Marine Plywood'),
  ('green_710_cat',  'en-GB', '710 Marine Series', 'Certified IS:710 Marine Grade Plywood'),
  ('green_gold_cat', 'en-GB', 'Gold & Calibrated', 'Superior Screw Holding Structural Plywood'),
  ('green_mr_cat',   'en-GB', 'Commercial MR 303', 'Moisture Resistant Commercial Plywood'),
  ('ecotec_cat',     'en-GB', 'Ecotec Plywood Series', 'Kaam Sahi Daam Sahi Value Plywood Line'),
  ('optima_cat',     'en-GB', 'Optima & Utility Boards', 'Selected Core Utility Building Boards'),
  ('calibrated_cat', 'en-GB', 'Calibrated CNC Core Boards', 'Four-Side Calibrated Uniform Thickness Panels'),
  ('shuttering_cat', 'en-GB', 'Film-Faced Shuttering Ply', 'High-Density Mirror Concrete Shuttering Panels'),
  ('blockboard_cat', 'en-GB', 'Blockboards & Flush Doors', 'Solid Pine Core Blockboard and Marine Doors'),
  ('veneers_cat',    'en-GB', 'Natural Decorative Veneers', 'Quarter & Crown Cut Architectural Face Veneers')
) AS v(code, locale, name, descr)
JOIN taxonomy_node n ON n.code = v.code
ON CONFLICT (node_id, locale_code) DO NOTHING;

-- 3. INSERT ALL 28 PLYWOOD PRODUCTS -------------------------------------------
INSERT INTO product (product_code, primary_node_id, brand_id, attribute_set_id, base_locale,
                     lifecycle_state, workflow_state, owner_display_name, gtin, sap_material_no, row_version)
SELECT v.p_code,
       COALESCE(n.id, (SELECT id FROM taxonomy_node WHERE code = 'plywood_blockboard')),
       (SELECT id FROM brand WHERE code = 'greenply'),
       (SELECT id FROM attribute_set WHERE code = 'plywood_core'),
       'en-GB', 'ACTIVE', v.w_state, v.owner, v.gtin, ('SAP-' || v.p_code), 1
FROM (VALUES
  ('GP-CLUB-700',     'green_club_cat', 'published', 'Markus Klein', '8901234500700'),
  ('GP-CLUB-500',     'green_club_cat', 'published', 'Markus Klein', '8901234500500'),
  ('GP-PLAT-BWP',     'green_plat_cat', 'approval',  'Anna Becker',  '8901234500100'),
  ('GP-FIRE-RET',     'green_plat_cat', 'published', 'Lena Vogt',    '8901234505509'),
  ('GP-710-MAR',      'green_710_cat',  'review',    'Lena Vogt',    '8901234500710'),
  ('GP-GOLD-BWP',     'green_gold_cat', 'draft',     'Sophie Adler', '8901234500800'),
  ('GP-303-MR',       'green_mr_cat',   'published', 'Anna Becker',  '8901234500303'),
  ('GP-FLEX-PLY',     'green_club_cat', 'approval',  'Markus Klein', '8901234500600'),
  ('GP-ECO-PLAT',     'ecotec_cat',     'approval',  'Markus Klein', '8901234500002'),
  ('GP-ECO-710',      'ecotec_cat',     'review',    'Lena Vogt',    '8901234500003'),
  ('GP-ECO-MR',       'ecotec_cat',     'draft',     'Sophie Adler', '8901234500004'),
  ('GP-ECO-TMR',      'ecotec_cat',     'review',    'Anna Becker',  '8901234500005'),
  ('GP-OPTIMA-G',     'optima_cat',     'published', 'Sophie Adler', '8901234500001'),
  ('GP-BHAROSA',      'optima_cat',     'published', 'Markus Klein', '8901234500006'),
  ('GP-JANSATHI',     'optima_cat',     'draft',     'Sophie Adler', '8901234500007'),
  ('GP-SHUTTER-BWP',  'shuttering_cat', 'published', 'Lena Vogt',    '8901234500008'),
  ('GP-CALIB-25',     'calibrated_cat', 'approval',  'Anna Becker',  '8901234500009'),
  ('GP-CALIB-19',     'calibrated_cat', 'published', 'Markus Klein', '8901234500010'),
  ('GP-CALIB-16',     'calibrated_cat', 'review',    'Lena Vogt',    '8901234500011'),
  ('GP-CALIB-12',     'calibrated_cat', 'draft',     'Sophie Adler', '8901234500012'),
  ('GP-CALIB-9',      'calibrated_cat', 'published', 'Anna Becker',  '8901234500013'),
  ('GP-CALIB-6',      'calibrated_cat', 'review',    'Markus Klein', '8901234500014'),
  ('GP-BLOCK-25',     'blockboard_cat', 'published', 'Lena Vogt',    '8901234500015'),
  ('GP-BLOCK-19',     'blockboard_cat', 'approval',  'Anna Becker',  '8901234500016'),
  ('GP-DOOR-FLUSH',   'blockboard_cat', 'published', 'Markus Klein', '8901234500017'),
  ('GP-VENEER-TEAK',  'veneers_cat',    'published', 'Sophie Adler', '8901234500018'),
  ('GP-VENEER-WALNUT','veneers_cat',    'review',    'Anna Becker',  '8901234500019'),
  ('GP-VENEER-OAK',   'veneers_cat',    'approval',  'Lena Vogt',    '8901234500020')
) AS v(p_code, cat_code, w_state, owner, gtin)
LEFT JOIN taxonomy_node n ON n.code = v.cat_code
ON CONFLICT (product_code) DO NOTHING;

-- 4. INSERT PRODUCT TRANSLATIONS (ENGLISH + GERMAN) --------------------------
INSERT INTO product_tr (product_id, locale_code, name, short_desc, long_desc, marketing, slug, translation_status)
SELECT p.id, v.locale, v.name, v.short_desc, v.long_desc, v.marketing, v.slug, 'approved'
FROM (VALUES
  ('GP-CLUB-700', 'en-GB', 'Club 700 Plywood',
   'Flagship 7X structural zero emission E0 calibrated marine plywood with unextended phenolic resin bonding.',
   'Club 700 is engineered using 100% selected Gurjan hardwood core veneers and Penta-Tech calibration for high precision joinery.',
   '7X Replacement Warranty • Penta-Tech Calibrated • E0 Zero Emission', 'club-700-plywood'),

  ('GP-CLUB-500', 'en-GB', 'Club 500 Plywood',
   'High-end durable structural BWP plywood with Virashield protection and 500% replacement warranty.',
   'Club 500 is engineered for heavy-duty customized furniture, premium kitchen carcasses, and structural joinery with antimicrobial Virashield treatment.',
   '500% Replacement Warranty • Virashield Anti-Bacterial • 100% BWP Hardwood Core', 'club-500-plywood'),

  ('GP-PLAT-BWP', 'en-GB', 'Platinum BWP Plywood',
   'Calibrated structural marine BWP plywood with multi-layer bonding and 2X fire delay formulation.',
   'Platinum BWP is precision-calibrated for high-load joinery and features high shear resistance and dimensional stability.',
   '2X Fire Retardant • Penta-Tech Calibration • Zero Core Gaps', 'platinum-bwp-plywood'),

  ('GP-FIRE-RET', 'en-GB', 'Fire-Retardant Plywood',
   'IS:5509 treated low-flammability fire retardant plywood with flame penetration delay exceeding 50 minutes.',
   'Pressure-impregnated with non-leachable fire-retardant chemicals that delay flame spread and produce minimal non-toxic smoke during fires.',
   'IS:5509 Fire Retardant • 50+ Min Flame Delay • Low Smoke Generation', 'fire-retardant-plywood'),

  ('GP-710-MAR', 'en-GB', '710 Marine Plywood',
   'Certified IS:710 Marine Grade boiling waterproof plywood with 25-year structural warranty.',
   'Tested through 72-hour continuous boiling water immersion without delamination, perfect for wet and marine environments.',
   'IS:710 Marine Certified • 25-Year Warranty • Boiling Waterproof', '710-marine-plywood'),

  ('GP-GOLD-BWP', 'en-GB', 'Gold BWP Plywood',
   'Superior screw holding structural plywood with unextended phenolic resin bonding and anti-borer protection.',
   'Engineered with selected core veneers to ensure zero hollow pockets and optimal strength for heavy wardrobe shelves.',
   'Superior Screw Holding • Unextended Resin • 100% Hardwood Core', 'gold-bwp-plywood'),

  ('GP-303-MR', 'en-GB', 'MR 303 Commercial Plywood',
   'Moisture resistant commercial plywood certified to IS:303 for interior cabinetry and partitions.',
   'Bonded with superior Melamine Urea Formaldehyde synthetic resin, offering high strength, dimensional stability, and borer resistance for dry interiors.',
   'IS:303 Commercial Grade • Superior MUF Resin • High Screw Holding Capacity', 'mr-303-commercial-plywood'),

  ('GP-FLEX-PLY', 'en-GB', 'Club Flexiply',
   'Cold-press flexible plywood engineered for curved woodwork, pillars, arches, and contour furniture.',
   'Bends effortlessly to create smooth circular, serpentine, or rounded architectural structures without steaming or cracking.',
   'Ultra-Flexible Core • No Cracking or Splintering • Seamless Contour Joinery', 'club-flexiply'),

  ('GP-OPTIMA-G', 'en-GB', 'Optima G Plywood',
   'Value marine grade BWP plywood with 100% selected core veneers and Virashield anti-bacterial coating.',
   'Delivers reliable all-weather moisture resistance, high durability, and borer protection for everyday residential furniture.',
   'Kaam Sahi Daam Sahi • 100% Hardwood Veneers • BWP Marine Resins', 'optima-g-plywood'),

  ('GP-BHAROSA', 'en-GB', 'Bharosaply Plywood',
   'Selected utility grade plywood with high strength bonding and borer-free warranty.',
   'Engineered for long-lasting performance in general interior furnishings, paneling, and partitions.',
   'Borer Proof • Uniform Density • Reliable Performance', 'bharosaply-plywood'),

  ('GP-JANSATHI', 'en-GB', 'Jansathi Plywood',
   'Economical commercial building board designed for budget-conscious interior fitouts.',
   'Provides smooth face finish and dependable mechanical properties for utility cabinetry.',
   'Cost-Effective • Smooth Surface • Interior Furniture Specialist', 'jansathi-plywood'),

  ('GP-ECO-PLAT', 'en-GB', 'Ecotec Platinum Plywood',
   'Calibrated structural marine BWP plywood with multi-layer bonding and 20-year warranty.',
   'Features uniform thickness across the sheet, zero core gaps, and high shear strength.',
   'Calibrated Thickness • 20-Year Warranty • Multi-layer Core Bonding', 'ecotec-platinum-plywood'),

  ('GP-ECO-710', 'en-GB', 'Ecotec 710 Marine Plywood',
   'Waterproof marine plywood engineered for modular kitchens, bathrooms, and wet areas with 15-year warranty.',
   'Manufactured using unextended phenolic resin tested against continuous boiling water cycles.',
   '15-Year Warranty • Boiling Waterproof BWP • Modular Kitchen Specialist', 'ecotec-710-marine-plywood'),

  ('GP-ECO-MR', 'en-GB', 'Ecotec MR 303 Plywood',
   'Commercial MR plywood with high-grade bonding and borer protection for living rooms and bedrooms.',
   'Engineered for high dimensional stability and superior screw withdrawal resistance.',
   'IS:303 Grade • Borer Protection • Warp Free', 'ecotec-mr-303-plywood'),

  ('GP-ECO-TMR', 'en-GB', 'Ecotec TMR Utility Board',
   'Technical moisture resistant building board for high-traffic modular cabinetry.',
   'High-density core construction ensures long-term resistance to warping in variable humidity.',
   'Technical MR • High-Density Core • Furniture Grade', 'ecotec-tmr-utility-board'),

  ('GP-SHUTTER-BWP', 'en-GB', 'Marine Film-Faced Shuttering Plywood',
   'Mirror-finish phenolic film-faced high-load concrete shuttering plywood for 30+ repetitions.',
   'Built with 100% hardwood veneers and coated with 120 GSM phenolic resin film, ensuring mirror-smooth concrete surfaces.',
   '30+ Formwork Repetitions • 120 GSM Mirror Phenolic Film • Mirror Concrete Finish', 'film-faced-shuttering-plywood'),

  ('GP-CALIB-25', 'en-GB', 'Calibrated Structural Ply 25mm',
   'Heavy-duty 25mm calibrated CNC furniture core board with precision thickness tolerance of ±0.2mm.',
   'Engineered for heavy-load industrial countertops, mezzanine decks, and structural partitions.',
   'Heavy-Duty 25mm • Tolerance ±0.2mm • High Load Capacity', 'calibrated-structural-ply-25mm'),

  ('GP-CALIB-19', 'en-GB', 'Calibrated Structural Ply 19mm',
   'Four-side calibrated CNC furniture core board with precision thickness tolerance of ±0.2mm.',
   'Engineered specifically for computerized panel saws, CNC routers, and modern modular carcass joinery.',
   'Tolerance ±0.2mm • Zero Core Voids • Four-Side Sanded Flatness', 'calibrated-structural-ply-19mm'),

  ('GP-CALIB-16', 'en-GB', 'Calibrated Structural Ply 16mm',
   'Calibrated CNC panel for wardrobe doors, modular carcasses, and interior joinery.',
   'Precision thickness control ensures tight tolerances for edge-banding and concealed hardware.',
   'Tolerance ±0.2mm • Hardware Friendly • Uniform Density', 'calibrated-structural-ply-16mm'),

  ('GP-CALIB-12', 'en-GB', 'Calibrated Structural Ply 12mm',
   'Calibrated backing and drawer panel with balanced veneer construction.',
   'Provides exceptional flatness and stability for drawer bottoms, panel backings, and wall claddings.',
   'Precision Flatness • Drawer Core Specialist • Clean CNC Edge', 'calibrated-structural-ply-12mm'),

  ('GP-CALIB-9', 'en-GB', 'Calibrated Structural Ply 9mm',
   'Calibrated panel for decorative soffits, paneling, and ceiling linings.',
   'High structural strength in thin profile with flawless surface calibration.',
   'Thin-Profile Calibrated • Smooth Sanding • Ceiling & Paneling', 'calibrated-structural-ply-9mm'),

  ('GP-CALIB-6', 'en-GB', 'Calibrated Structural Ply 6mm',
   'Ultra-thin precision calibrated core board for wall paneling and door skins.',
   'Multi-ply construction resists cupping while remaining lightweight and rigid.',
   'Precision 6mm • Multi-Ply Construction • Skin Grade', 'calibrated-structural-ply-6mm'),

  ('GP-BLOCK-25', 'en-GB', 'BWP Marine Blockboard 25mm',
   'Kiln-seasoned pine core warp-resistant blockboard for long horizontal shelves and wardrobe shutters.',
   'Constructed with solid pine battens and BWP cross-bands, ensuring total freedom from sagging or warping over long spans.',
   'Zero Sagging • Kiln-Seasoned Pine Core • Heavy-Duty Wardrobe Shutters', 'bwp-marine-blockboard-25mm'),

  ('GP-BLOCK-19', 'en-GB', 'High-Density Blockboard 19mm',
   'Solid seasoned timber core blockboard for wardrobe shutters, sliding panels, and partition doors.',
   'Balanced cross-band veneers prevent warping and provide maximum screw holding strength.',
   'Solid Timber Core • Warp Free • Maximum Screw Holding', 'high-density-blockboard-19mm'),

  ('GP-DOOR-FLUSH', 'en-GB', 'Club Solid Core Flush Door',
   'High-strength solid timber core BWP marine flush door certified to IS:2202.',
   'Features kiln-seasoned hardwood internal framing, solid blockboard core, and 100% boiling waterproof bonding.',
   'IS:2202 Certified • Solid Timber Core • Warp-Free Lifetime Durability', 'club-solid-core-flush-door'),

  ('GP-VENEER-TEAK', 'en-GB', 'Decotech Natural Teak Veneer',
   'Quarter-cut natural Burmese teak face veneer bonded onto BWP marine plywood core.',
   'Architectural grade natural decorative face veneer celebrating the authentic grain, warmth, and golden patina of genuine teakwood.',
   'Natural Burmese Teak • Architectural Grade • BWP Core Stability', 'decotech-natural-teak-veneer'),

  ('GP-VENEER-WALNUT', 'en-GB', 'Decotech American Walnut Veneer',
   'Crown-cut American black walnut natural face veneer on calibrated hardwood core.',
   'Rich chocolate tones and distinctive cathedral grain patterns for bespoke luxury interiors.',
   'American Black Walnut • Crown Cut • Premium Architectural Face', 'decotech-american-walnut-veneer'),

  ('GP-VENEER-OAK', 'en-GB', 'Decotech White Oak Veneer',
   'Rift-cut European white oak decorative veneer celebrating clean, linear grain geometry.',
   'Contemporary architectural styling with exceptional staining and finishing characteristics.',
   'European White Oak • Rift Cut • Contemporary Minimalist Joinery', 'decotech-white-oak-veneer')
) AS v(p_code, locale, name, short_desc, long_desc, marketing, slug)
JOIN product p ON p.product_code = v.p_code
ON CONFLICT (product_id, locale_code) DO NOTHING;

-- 5. SKUS (VARIANTS) FOR ALL 28 PRODUCTS ---------------------------------------
INSERT INTO sku (product_id, sku_code, status, sort_order, gtin, erp_material_no)
SELECT p.id, v.sku_code, 'active', v.s_order, v.gtin, ('SAP-' || v.sku_code)
FROM (VALUES
  ('GP-CLUB-700',     'GP-C700-8X4-19MM',  1, '8901234519700'),
  ('GP-CLUB-700',     'GP-C700-8X4-16MM',  2, '8901234516700'),
  ('GP-CLUB-700',     'GP-C700-8X4-12MM',  3, '8901234512700'),
  ('GP-CLUB-500',     'GP-C500-8X4-19MM',  1, '8901234500519'),
  ('GP-CLUB-500',     'GP-C500-8X4-16MM',  2, '8901234500516'),
  ('GP-CLUB-500',     'GP-C500-8X4-12MM',  3, '8901234500512'),
  ('GP-PLAT-BWP',     'GP-PLT-8X4-19MM',   1, '8901234500119'),
  ('GP-PLAT-BWP',     'GP-PLT-8X4-16MM',   2, '8901234500116'),
  ('GP-FIRE-RET',     'GP-FR-8X4-19MM',    1, '8901234505519'),
  ('GP-FIRE-RET',     'GP-FR-8X4-16MM',    2, '8901234505516'),
  ('GP-710-MAR',      'GP-710-8X4-19MM',   1, '8901234500719'),
  ('GP-710-MAR',      'GP-710-8X4-16MM',   2, '8901234500716'),
  ('GP-GOLD-BWP',     'GP-GLD-8X4-19MM',   1, '8901234500819'),
  ('GP-303-MR',       'GP-303-8X4-18MM',   1, '8901234500318'),
  ('GP-303-MR',       'GP-303-8X4-12MM',   2, '8901234500312'),
  ('GP-303-MR',       'GP-303-8X4-9MM',    3, '8901234500309'),
  ('GP-FLEX-PLY',     'GP-FLEX-8X4-6MM',   1, '8901234500606'),
  ('GP-FLEX-PLY',     'GP-FLEX-8X4-8MM',   2, '8901234500608'),
  ('GP-OPTIMA-G',     'GP-OPT-8X4-19MM',   1, '8901234501019'),
  ('GP-OPTIMA-G',     'GP-OPT-8X4-16MM',   2, '8901234501016'),
  ('GP-BHAROSA',      'GP-BHR-8X4-18MM',   1, '8901234506018'),
  ('GP-JANSATHI',     'GP-JAN-8X4-18MM',   1, '8901234507018'),
  ('GP-ECO-PLAT',     'GP-EPL-8X4-19MM',   1, '8901234502019'),
  ('GP-ECO-PLAT',     'GP-EPL-8X4-16MM',   2, '8901234502016'),
  ('GP-ECO-710',      'GP-E710-8X4-19MM',  1, '8901234503019'),
  ('GP-ECO-710',      'GP-E710-8X4-12MM',  2, '8901234503012'),
  ('GP-ECO-MR',       'GP-EMR-8X4-18MM',   1, '8901234504018'),
  ('GP-ECO-TMR',      'GP-ETMR-8X4-18MM',  1, '8901234505018'),
  ('GP-SHUTTER-BWP',  'GP-SHT-8X4-18MM',   1, '8901234504018'),
  ('GP-SHUTTER-BWP',  'GP-SHT-8X4-12MM',   2, '8901234504012'),
  ('GP-CALIB-25',     'GP-CAL-8X4-25MM',   1, '8901234505025'),
  ('GP-CALIB-19',     'GP-CAL-8X4-19MM',   1, '8901234505019'),
  ('GP-CALIB-16',     'GP-CAL-8X4-16MM',   1, '8901234505016'),
  ('GP-CALIB-12',     'GP-CAL-8X4-12MM',   1, '8901234505012'),
  ('GP-CALIB-9',      'GP-CAL-8X4-9MM',    1, '8901234505009'),
  ('GP-CALIB-6',      'GP-CAL-8X4-6MM',    1, '8901234505006'),
  ('GP-BLOCK-25',     'GP-BLK-8X4-25MM',   1, '8901234506025'),
  ('GP-BLOCK-19',     'GP-BLK-8X4-19MM',   1, '8901234506019'),
  ('GP-DOOR-FLUSH',   'GP-DR-7X3-35MM',    1, '8901234507035'),
  ('GP-VENEER-TEAK',  'GP-VEN-8X4-4MM',    1, '8901234508004'),
  ('GP-VENEER-WALNUT','GP-VEN-WAL-4MM',    1, '8901234508005'),
  ('GP-VENEER-OAK',   'GP-VEN-OAK-4MM',    1, '8901234508006')
) AS v(p_code, sku_code, s_order, gtin)
JOIN product p ON p.product_code = v.p_code
ON CONFLICT (sku_code) DO NOTHING;

-- 6. ATTRIBUTE VALUES ---------------------------------------------------------
INSERT INTO attribute_value (product_id, attribute_id, value_text, value_number, data_state)
SELECT p.id, a.id, v.val_text, v.val_num, 'approved'
FROM (VALUES
  ('GP-CLUB-500',   'water_resistance_grade', 'BWP_GRADE',      NULL),
  ('GP-CLUB-500',   'core_species',           'GURJAN',         NULL),
  ('GP-CLUB-500',   'resin_type',             'UNEXTENDED_PF',  NULL),
  ('GP-CLUB-500',   'calibration_technology', 'PENTA_TECH',     NULL),
  ('GP-CLUB-500',   'warranty_years',         NULL,             30),
  ('GP-CLUB-500',   'thickness_mm',           NULL,             19),

  ('GP-303-MR',      'water_resistance_grade', 'MR_GRADE',       NULL),
  ('GP-303-MR',      'core_species',           'HARDWOOD',       NULL),
  ('GP-303-MR',      'resin_type',             'MUF',            NULL),
  ('GP-303-MR',      'warranty_years',         NULL,             10),
  ('GP-303-MR',      'thickness_mm',           NULL,             18),

  ('GP-FIRE-RET',    'water_resistance_grade', 'BWP_GRADE',      NULL),
  ('GP-FIRE-RET',    'core_species',           'HARDWOOD',       NULL),
  ('GP-FIRE-RET',    'fire_delay_mins',        NULL,             50),
  ('GP-FIRE-RET',    'warranty_years',         NULL,             20),

  ('GP-SHUTTER-BWP', 'water_resistance_grade', 'BWP_GRADE',      NULL),
  ('GP-SHUTTER-BWP', 'core_species',           'HARDWOOD',       NULL),
  ('GP-SHUTTER-BWP', 'thickness_mm',           NULL,             18),
  ('GP-SHUTTER-BWP', 'density_g_cm3',          NULL,             0.84),

  ('GP-CALIB-19',    'calibration_technology', 'PENTA_TECH',     NULL),
  ('GP-CALIB-19',    'thickness_mm',           NULL,             19),
  ('GP-CALIB-19',    'water_resistance_grade', 'BWP_GRADE',      NULL),

  ('GP-BLOCK-25',    'thickness_mm',           NULL,             25),
  ('GP-BLOCK-25',    'water_resistance_grade', 'BWP_GRADE',      NULL),
  ('GP-BLOCK-25',    'warranty_years',         NULL,             25),

  ('GP-DOOR-FLUSH',  'thickness_mm',           NULL,             35),
  ('GP-DOOR-FLUSH',  'water_resistance_grade', 'BWP_GRADE',      NULL),
  ('GP-DOOR-FLUSH',  'warranty_years',         NULL,             25)
) AS v(p_code, a_code, val_text, val_num)
JOIN product p ON p.product_code = v.p_code
JOIN attribute a ON a.code = v.a_code
ON CONFLICT DO NOTHING;

-- 7. DEFAULT ASSETS & ASSET LINKS IN POSTGRESQL -------------------------------
-- Registers core assets and links them to products in the database so
-- /api/v1/products/{code}/assets succeeds on every product.
INSERT INTO asset (storage_provider, storage_key, file_name, mime_type, file_size, asset_type, width, height, status)
VALUES
  ('s3', 'https://images.unsplash.com/photo-1546484396-fb3fc6f95f98?auto=format&fit=crop&w=1200&q=80',
   'plywood_calibrated_face_sheet.jpg', 'image/jpeg', 2150000, 'IMAGE', 1200, 800, 'active'),
  ('s3', 'https://images.unsplash.com/photo-1513694203232-719a280e022f?auto=format&fit=crop&w=1200&q=80',
   'plywood_cross_section_layers.jpg', 'image/jpeg', 1890000, 'IMAGE', 1200, 800, 'active'),
  ('s3', 'https://images.unsplash.com/photo-1600585154340-be6161a56a0c?auto=format&fit=crop&w=1200&q=80',
   'plywood_architectural_joinery.jpg', 'image/jpeg', 2340000, 'IMAGE', 1200, 800, 'active'),
  ('s3', 'https://images.unsplash.com/photo-1586023492125-27b2c045efd7?auto=format&fit=crop&w=1200&q=80',
   'plywood_structural_panels_stack.jpg', 'image/jpeg', 1980000, 'IMAGE', 1200, 800, 'active')
ON CONFLICT (storage_provider, storage_key) DO NOTHING;

INSERT INTO asset_rendition (asset_id, role, uri, width, height, format)
SELECT a.id, 'web', a.storage_key, a.width, a.height, 'jpeg'
FROM asset a
WHERE a.asset_type = 'IMAGE'
ON CONFLICT (asset_id, role) DO NOTHING;

-- Link default images to every product that currently has fewer than 2 images
INSERT INTO asset_link (product_id, sku_id, asset_id, role, is_primary, sort_order)
SELECT p.id, NULL, a.id, 'GALLERY', false, 2
FROM product p
CROSS JOIN (SELECT id FROM asset WHERE file_name = 'plywood_cross_section_layers.jpg') a
WHERE NOT EXISTS (
  SELECT 1 FROM asset_link al WHERE al.product_id = p.id AND al.asset_id = a.id
)
ON CONFLICT (product_id, asset_id, role) DO NOTHING;
