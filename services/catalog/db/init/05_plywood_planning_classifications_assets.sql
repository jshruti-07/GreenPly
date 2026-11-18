-- =============================================================================
-- MODULE · PLYWOOD EXTENDED SEED (Planning & BIM, Classifications, Digital Assets)
--
-- Enriches the Greenply catalogue across:
--   1. Planning & BIM Parameters: MOE, MOR, screw holding load, thermal conductivity,
--      fire delay, standard dimensions, BIM readiness flag.
--   2. Classification Standards: ETIM 9.0 (EC002620 Plywood), UNSPSC (30103603),
--      IFC 4.3 (IfcPlate.SHEET), OmniClass (23-13 23 13), Uniclass (Pr_25_71_92_64),
--      Sales Hierarchy, and BMEcat. Populates 11 ETIM features for 100% mapping.
--   3. Digital Assets: High-res sheet images, cross-section layer diagrams,
--      modular kitchen renders, Technical Datasheets, IS:710 boiling test reports,
--      CARB E0 certificates, joinery guides, and 3D BIM (IFC) / CAD (DWG) files.
-- =============================================================================

SET search_path TO catalog;

-- 1. PLANNING ATTRIBUTES ------------------------------------------------------
INSERT INTO attribute (code, data_type, group_id, unit_id, is_localizable)
SELECT v.code, v.data_type, g.id, u.id, false
FROM (VALUES
  ('mfg_thickness_mm',        'measure', 'planning', 'mm',    false),
  ('modulus_elasticity_mpa',  'measure', 'planning', 'mpa',   false),
  ('modulus_rupture_mpa',     'measure', 'planning', 'mpa',   false),
  ('screw_holding_face_n',    'number',  'planning', 'n',     false),
  ('screw_holding_edge_n',    'number',  'planning', 'n',     false),
  ('thermal_conductivity_w_mk','decimal','planning', NULL,    false)
) AS v(code, data_type, group_code, unit_code, localizable)
JOIN attribute_group g ON g.code = v.group_code
LEFT JOIN unit u ON u.code = v.unit_code
ON CONFLICT (code) DO NOTHING;

INSERT INTO attribute_tr (attribute_id, locale_code, label)
SELECT a.id, v.locale, v.label FROM (VALUES
  ('mfg_thickness_mm',        'en-GB', 'Calibrated Panel Thickness'),
  ('mfg_thickness_mm',        'de-DE', 'Kalibrierte Plattenstärke'),
  ('modulus_elasticity_mpa',  'en-GB', 'Modulus of Elasticity (MOE)'),
  ('modulus_elasticity_mpa',  'de-DE', 'Elastizitätsmodul (MOE)'),
  ('modulus_rupture_mpa',     'en-GB', 'Modulus of Rupture (MOR)'),
  ('modulus_rupture_mpa',     'de-DE', 'Biegefestigkeit (MOR)'),
  ('screw_holding_face_n',    'en-GB', 'Face Screw Holding Load'),
  ('screw_holding_face_n',    'de-DE', 'Schraubenauszugsfestigkeit (Fläche)'),
  ('screw_holding_edge_n',    'en-GB', 'Edge Screw Holding Load'),
  ('screw_holding_edge_n',    'de-DE', 'Schraubenauszugsfestigkeit (Kante)'),
  ('thermal_conductivity_w_mk','en-GB', 'Thermal Conductivity (k-value)'),
  ('thermal_conductivity_w_mk','de-DE', 'Wärmeleitfähigkeit (k-Wert)')
) AS v(code, locale, label)
JOIN attribute a ON a.code = v.code
ON CONFLICT (attribute_id, locale_code) DO NOTHING;

-- Map planning attributes into plywood_core attribute set
INSERT INTO attribute_set_attribute (set_id, attribute_id, is_required, sort_order)
SELECT s.id, a.id, v.required, v.sort_order FROM (VALUES
  ('bim_ready',                 true,  20),
  ('mfg_length_mm',             true,  21),
  ('mfg_width_mm',              true,  22),
  ('mfg_thickness_mm',          true,  23),
  ('modulus_elasticity_mpa',    false, 24),
  ('modulus_rupture_mpa',       false, 25),
  ('screw_holding_face_n',      false, 26),
  ('screw_holding_edge_n',      false, 27),
  ('thermal_conductivity_w_mk', false, 28)
) AS v(code, required, sort_order)
JOIN attribute a ON a.code = v.code
CROSS JOIN (SELECT id FROM attribute_set WHERE code = 'plywood_core') s
ON CONFLICT (set_id, attribute_id) DO NOTHING;

-- Seed Planning Values for Green Club 700
INSERT INTO attribute_value (product_id, attribute_id, value_boolean, data_state)
SELECT p.id, a.id, true, 'approved'
FROM product p CROSS JOIN attribute a
WHERE p.product_code IN ('GP-CLUB-700', 'GP-PLAT-BWP', 'GP-710-MAR') AND a.code = 'bim_ready'
ON CONFLICT DO NOTHING;

INSERT INTO attribute_value (product_id, attribute_id, value_boolean, data_state)
SELECT p.id, a.id, false, 'approved'
FROM product p CROSS JOIN attribute a
WHERE p.product_code = 'GP-GOLD-BWP' AND a.code = 'bim_ready'
ON CONFLICT DO NOTHING;

INSERT INTO attribute_value (product_id, attribute_id, value_number, data_state)
SELECT p.id, a.id, v.val, 'approved'
FROM (VALUES
  ('GP-CLUB-700', 'mfg_length_mm',            2440.0),
  ('GP-CLUB-700', 'mfg_width_mm',             1220.0),
  ('GP-CLUB-700', 'mfg_thickness_mm',         19.0),
  ('GP-CLUB-700', 'modulus_elasticity_mpa',   8500.0),
  ('GP-CLUB-700', 'modulus_rupture_mpa',      65.0),
  ('GP-CLUB-700', 'screw_holding_face_n',     2250.0),
  ('GP-CLUB-700', 'screw_holding_edge_n',     1350.0),
  ('GP-CLUB-700', 'thermal_conductivity_w_mk',0.13),
  ('GP-PLAT-BWP', 'mfg_length_mm',            2440.0),
  ('GP-PLAT-BWP', 'mfg_width_mm',             1220.0),
  ('GP-PLAT-BWP', 'mfg_thickness_mm',         19.0),
  ('GP-PLAT-BWP', 'modulus_elasticity_mpa',   7800.0),
  ('GP-PLAT-BWP', 'modulus_rupture_mpa',      58.0),
  ('GP-PLAT-BWP', 'screw_holding_face_n',     2100.0),
  ('GP-PLAT-BWP', 'screw_holding_edge_n',     1250.0),
  ('GP-PLAT-BWP', 'thermal_conductivity_w_mk',0.14),
  ('GP-710-MAR',  'mfg_length_mm',            2440.0),
  ('GP-710-MAR',  'mfg_width_mm',             1220.0),
  ('GP-710-MAR',  'mfg_thickness_mm',         19.0),
  ('GP-710-MAR',  'modulus_elasticity_mpa',   7200.0),
  ('GP-710-MAR',  'modulus_rupture_mpa',      52.0),
  ('GP-710-MAR',  'screw_holding_face_n',     2000.0),
  ('GP-710-MAR',  'screw_holding_edge_n',     1200.0),
  ('GP-710-MAR',  'thermal_conductivity_w_mk',0.14),
  ('GP-GOLD-BWP', 'mfg_length_mm',            2440.0),
  ('GP-GOLD-BWP', 'mfg_width_mm',             1220.0),
  ('GP-GOLD-BWP', 'mfg_thickness_mm',         19.0),
  ('GP-GOLD-BWP', 'modulus_elasticity_mpa',   6500.0),
  ('GP-GOLD-BWP', 'modulus_rupture_mpa',      48.0),
  ('GP-GOLD-BWP', 'screw_holding_face_n',     1900.0),
  ('GP-GOLD-BWP', 'screw_holding_edge_n',     1100.0),
  ('GP-GOLD-BWP', 'thermal_conductivity_w_mk',0.15)
) AS v(p_code, a_code, val)
JOIN product p ON p.product_code = v.p_code
JOIN attribute a ON a.code = v.a_code
ON CONFLICT DO NOTHING;

-- 2. PLYWOOD CLASSIFICATION CLASSES -------------------------------------------
INSERT INTO classification_class (scheme_id, code, label, path)
SELECT s.id, v.code, v.label, v.path::ltree FROM (VALUES
  ('ETIM',     'EC002620', 'Plywood board / Engineered wood panel', 'etim.ec002620'),
  ('ETIM',     'EC002621', 'Blockboard / Coreboard',                'etim.ec002621'),
  ('ETIM',     'EC000213', 'Flush door shutter',                    'etim.ec000213'),
  ('UNSPSC',   '30103603', 'Plywood',                               'unspsc.n30103603'),
  ('UNSPSC',   '30103600', 'Engineered wood products',              'unspsc.n30103600'),
  ('SALESHIER','WOOD-PLY-STRUCT', 'Wood Panels > Structural E0 Plywood', 'sales.wood.ply.struct'),
  ('SALESHIER','WOOD-PLY-MARINE', 'Wood Panels > Marine BWP Plywood',     'sales.wood.ply.marine'),
  ('SALESHIER','WOOD-PLY-COMM',   'Wood Panels > Commercial MR Plywood',  'sales.wood.ply.comm'),
  ('IFC',      'IfcPlate.SHEET',  'IfcPlate / SHEET (Structural Wood Panels)', 'ifc.plate.sheet'),
  ('OMNICLASS','23-13 23 13',     'Plywood and Composite Wood Panels',         'omniclass.n23_13_23_13'),
  ('UNICLASS', 'Pr_25_71_92_64',  'Plywood sheets and boards',                 'uniclass.pr_25_71_92_64'),
  ('BMECAT',   'T-WOOD-PLY-01',   'Catalog group T-WOOD-PLY-01',               'bmecat.t_wood_ply_01')
) AS v(scheme, code, label, path)
JOIN classification_scheme s ON s.code = v.scheme
ON CONFLICT (scheme_id, code) DO NOTHING;

-- ETIM features for Plywood board (EC002620)
INSERT INTO classification_feature (scheme_id, class_id, code, name, data_type, unit, is_required, sort_order)
SELECT s.id, c.id, v.code, v.name, v.data_type, v.unit, v.req, v.sort_order
FROM classification_scheme s
JOIN classification_class c ON c.scheme_id = s.id AND c.code = 'EC002620'
CROSS JOIN (VALUES
  ('EF_PLY_LEN',  'Sheet length',                'numeric', 'mm',   true,   1),
  ('EF_PLY_WID',  'Sheet width',                 'numeric', 'mm',   true,   2),
  ('EF_PLY_THK',  'Panel thickness',             'numeric', 'mm',   true,   3),
  ('EF_PLY_CORE', 'Core timber species',         'alpha',   NULL,   true,   4),
  ('EF_PLY_EMIS', 'Emission standard',           'alpha',   NULL,   true,   5),
  ('EF_PLY_WRES', 'Water resistance class',      'alpha',   NULL,   true,   6),
  ('EF_PLY_RESN', 'Adhesive / resin system',     'alpha',   NULL,   false,  7),
  ('EF_PLY_DENS', 'Panel density',               'numeric', 'g/cm³',false,  8),
  ('EF_PLY_FIRE', 'Fire retardant treated',      'logical', NULL,   false,  9),
  ('EF_PLY_BORR', 'Borer & termite proof',       'logical', NULL,   true,   10),
  ('EF_PLY_CALI', 'Calibrated panel',            'logical', NULL,   true,   11)
) AS v(code, name, data_type, unit, req, sort_order)
WHERE s.code = 'ETIM'
ON CONFLICT (scheme_id, code) DO NOTHING;

-- Map Greenply products to Plywood Classification Classes
INSERT INTO product_classification (product_id, scheme_id, class_id, is_primary, mapped_status)
SELECT p.id, s.id, c.id, v.is_primary, 'mapped'
FROM (VALUES
  ('GP-CLUB-700', 'ETIM',      'EC002620',       true),
  ('GP-CLUB-700', 'DATANORM',  '100',            false),
  ('GP-CLUB-700', 'BMECAT',    'T-WOOD-PLY-01',  false),
  ('GP-CLUB-700', 'UNSPSC',    '30103603',       false),
  ('GP-CLUB-700', 'SALESHIER', 'WOOD-PLY-STRUCT',false),
  ('GP-CLUB-700', 'IFC',       'IfcPlate.SHEET', false),
  ('GP-CLUB-700', 'OMNICLASS', '23-13 23 13',    false),
  ('GP-CLUB-700', 'UNICLASS',  'Pr_25_71_92_64', false),
  ('GP-PLAT-BWP', 'ETIM',      'EC002620',       true),
  ('GP-PLAT-BWP', 'UNSPSC',    '30103603',       false),
  ('GP-PLAT-BWP', 'SALESHIER', 'WOOD-PLY-STRUCT',false),
  ('GP-PLAT-BWP', 'IFC',       'IfcPlate.SHEET', false),
  ('GP-PLAT-BWP', 'OMNICLASS', '23-13 23 13',    false),
  ('GP-PLAT-BWP', 'UNICLASS',  'Pr_25_71_92_64', false),
  ('GP-710-MAR',  'ETIM',      'EC002620',       true),
  ('GP-710-MAR',  'UNSPSC',    '30103603',       false),
  ('GP-710-MAR',  'SALESHIER', 'WOOD-PLY-MARINE',false),
  ('GP-710-MAR',  'IFC',       'IfcPlate.SHEET', false),
  ('GP-710-MAR',  'OMNICLASS', '23-13 23 13',    false),
  ('GP-710-MAR',  'UNICLASS',  'Pr_25_71_92_64', false),
  ('GP-GOLD-BWP', 'ETIM',      'EC002620',       true),
  ('GP-GOLD-BWP', 'UNSPSC',    '30103603',       false),
  ('GP-GOLD-BWP', 'SALESHIER', 'WOOD-PLY-COMM',  false),
  ('GP-GOLD-BWP', 'IFC',       'IfcPlate.SHEET', false),
  ('GP-GOLD-BWP', 'OMNICLASS', '23-13 23 13',    false),
  ('GP-GOLD-BWP', 'UNICLASS',  'Pr_25_71_92_64', false)
) AS v(p_code, scheme, class_code, is_primary)
JOIN product p ON p.product_code = v.p_code
JOIN classification_scheme s ON s.code = v.scheme
JOIN classification_class c ON c.scheme_id = s.id AND c.code = v.class_code
ON CONFLICT (product_id, scheme_id) DO UPDATE
SET class_id = EXCLUDED.class_id, mapped_status = 'mapped';

-- Populate ETIM 9.0 features for Green Club 700 (11 of 11 filled = 100%)
INSERT INTO product_classification_value (product_id, feature_id, value_text, value_number, value_bool, value_unit)
SELECT p.id, f.id, v.t, v.n, v.b, f.unit
FROM product p
JOIN classification_scheme s ON s.code = 'ETIM'
JOIN classification_feature f ON f.scheme_id = s.id
JOIN (VALUES
  ('EF_PLY_LEN',  NULL,                        2440::numeric, NULL::boolean),
  ('EF_PLY_WID',  NULL,                        1220,          NULL),
  ('EF_PLY_THK',  NULL,                        19,            NULL),
  ('EF_PLY_CORE', '100% Gurjan Hardwood',      NULL,          NULL),
  ('EF_PLY_EMIS', 'E0 Zero Emission',          NULL,          NULL),
  ('EF_PLY_WRES', 'BWP Boiling Water Proof',   NULL,          NULL),
  ('EF_PLY_RESN', 'Unextended Phenolic PF',    NULL,          NULL),
  ('EF_PLY_DENS', NULL,                        0.82,          NULL),
  ('EF_PLY_FIRE', NULL,                        NULL,          true),
  ('EF_PLY_BORR', NULL,                        NULL,          true),
  ('EF_PLY_CALI', NULL,                        NULL,          true)
) AS v(f_code, t, n, b) ON v.f_code = f.code
WHERE p.product_code = 'GP-CLUB-700'
ON CONFLICT (product_id, feature_id) DO UPDATE
SET value_text = EXCLUDED.value_text,
    value_number = EXCLUDED.value_number,
    value_bool = EXCLUDED.value_bool;

-- 3. DIGITAL ASSETS & MEDIA ----------------------------------------------------
-- Assets for Green Club 700 & Green Platinum
INSERT INTO asset (storage_provider, storage_key, file_name, mime_type, file_size, asset_type, width, height, status)
VALUES
  ('s3', 'https://assets.greenply.com/products/green_club_700_sheet_hero.jpg',
   'green_club_700_sheet_hero.jpg', 'image/jpeg', 2450000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/green_club_700_cross_section_layers.jpg',
   'green_club_700_cross_section_layers.jpg', 'image/jpeg', 1890000, 'IMAGE', 1920, 1280, 'active'),
  ('s3', 'https://assets.greenply.com/products/green_club_700_application_kitchen.jpg',
   'green_club_700_application_kitchen.jpg', 'image/jpeg', 2150000, 'IMAGE', 1920, 1280, 'active'),
  ('s3', 'https://www.greenply.com/datasheets/Green_Club_700_Technical_Datasheet_2026.pdf',
   'Green_Club_700_Technical_Datasheet_2026.pdf', 'application/pdf', 1450200, 'DOCUMENT', NULL, NULL, 'active'),
  ('s3', 'https://www.greenply.com/certifications/Greenply_IS710_72hr_Boiling_Water_Test_Report.pdf',
   'Greenply_IS710_72hr_Boiling_Water_Test_Report.pdf', 'application/pdf', 890400, 'DOCUMENT', NULL, NULL, 'active'),
  ('s3', 'https://www.greenply.com/certifications/Greenply_E0_Emission_CARB_P2_Certificate.pdf',
   'Greenply_E0_Emission_CARB_P2_Certificate.pdf', 'application/pdf', 670100, 'DOCUMENT', NULL, NULL, 'active'),
  ('s3', 'https://www.greenply.com/guides/Greenply_Architectural_Joinery_Guide.pdf',
   'Greenply_Architectural_Joinery_Guide.pdf', 'application/pdf', 2150000, 'DOCUMENT', NULL, NULL, 'active'),
  ('s3', 'https://bim.greenply.com/objects/green_club_700_panel_3d.ifc',
   'green_club_700_panel_3d.ifc', 'application/x-step', 4210000, 'BIM', NULL, NULL, 'active'),
  ('s3', 'https://bim.greenply.com/cad/green_club_700_cad_section.dwg',
   'green_club_700_cad_section.dwg', 'application/acad', 1120000, 'CAD', NULL, NULL, 'active'),
  ('s3', 'https://assets.greenply.com/products/green_platinum_sheet_hero.jpg',
   'green_platinum_sheet_hero.jpg', 'image/jpeg', 2280000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://www.greenply.com/datasheets/Green_Platinum_Fire_Retardant_TDS.pdf',
   'Green_Platinum_Fire_Retardant_TDS.pdf', 'application/pdf', 1340000, 'DOCUMENT', NULL, NULL, 'active')
ON CONFLICT (storage_provider, storage_key) DO NOTHING;

-- Asset Translations
INSERT INTO asset_tr (asset_id, locale_code, title, alt_text, caption)
SELECT a.id, v.locale, v.title, v.alt, v.cap
FROM (VALUES
  ('green_club_700_sheet_hero.jpg', 'en-GB', 'Club 700 Master Sheet', 'Calibrated Club 700 Plywood sheet', 'Club 700 structural E0 plywood hero view'),
  ('green_club_700_sheet_hero.jpg', 'de-DE', 'Club 700 Hauptplatte', 'Kalibrierte Club 700 Sperrholzplatte', 'Club 700 E0-Struktursperrholz Hauptansicht'),
  ('green_club_700_cross_section_layers.jpg', 'en-GB', 'Penta-Tech Calibration Layers', 'Cross section showing multi-ply hardwood construction', 'Precision-calibrated hardwood veneers bonded with unextended PF resin'),
  ('green_club_700_cross_section_layers.jpg', 'de-DE', 'Penta-Tech Schichtaufbau', 'Querschnitt des Hartholz-Mehrschichtaufbaus', 'Präzisionskalibrierte Hartholzfurniere mit ungestrecktem Kunstharz'),
  ('green_club_700_application_kitchen.jpg', 'en-GB', 'Modular Kitchen Cabinetry Installation', 'Club 700 used in premium kitchen carcass', 'Waterproof BWP carcass cabinetry fabricated with Club 700'),
  ('Green_Club_700_Technical_Datasheet_2026.pdf', 'en-GB', 'Technical Data Sheet (2026)', 'Club 700 Product TDS PDF', 'Complete engineering, physical, and chemical properties'),
  ('Greenply_IS710_72hr_Boiling_Water_Test_Report.pdf', 'en-GB', 'IS:710 72-Hour Boiling Water Test Report', 'Laboratory test report proving zero delamination', 'Independent testing per Indian Standard IS:710'),
  ('Greenply_E0_Emission_CARB_P2_Certificate.pdf', 'en-GB', 'CARB Phase 2 / E0 Emission Certificate', 'Formaldehyde emission test certificate', 'Zero emission certification (less than 3mg/100g)'),
  ('Greenply_Architectural_Joinery_Guide.pdf', 'en-GB', 'Architectural Joinery & Fabrication Guide', 'Installation manual for carpenters and contractors', 'Screw holding, edge banding, and veneer pressing guidelines'),
  ('green_club_700_panel_3d.ifc', 'en-GB', '3D BIM Object (IFC 4.3)', 'BIM model of Club 700 panel', 'Parametric sheet object for Revit, ArchiCAD, and BIM 360'),
  ('green_club_700_cad_section.dwg', 'en-GB', 'CAD Section & Joinery Detail (DWG)', '2D CAD joinery drawing', 'Detailed edge profiles, tongue-and-groove and joint details')
) AS v(fname, locale, title, alt, cap)
JOIN asset a ON a.file_name = v.fname
ON CONFLICT (asset_id, locale_code) DO NOTHING;

-- Asset Web Renditions (URLs for display)
INSERT INTO asset_rendition (asset_id, role, uri, width, height, format)
SELECT a.id, 'web', a.storage_key, a.width, a.height, 'jpeg'
FROM asset a
WHERE a.asset_type = 'IMAGE'
ON CONFLICT (asset_id, role) DO NOTHING;

-- Link Assets to Products
-- GP-CLUB-700
INSERT INTO asset_link (product_id, sku_id, asset_id, role, is_primary, sort_order)
SELECT p.id, NULL, a.id, v.role, v.is_pri, v.sort_order
FROM (VALUES
  ('GP-CLUB-700', 'green_club_700_sheet_hero.jpg',                          'MAIN',      true,  1),
  ('GP-CLUB-700', 'green_club_700_cross_section_layers.jpg',                'GALLERY',   false, 2),
  ('GP-CLUB-700', 'green_club_700_application_kitchen.jpg',                'GALLERY',   false, 3),
  ('GP-CLUB-700', 'Green_Club_700_Technical_Datasheet_2026.pdf',            'DATASHEET', false, 4),
  ('GP-CLUB-700', 'Greenply_IS710_72hr_Boiling_Water_Test_Report.pdf',      'DATASHEET', false, 5),
  ('GP-CLUB-700', 'Greenply_E0_Emission_CARB_P2_Certificate.pdf',          'DATASHEET', false, 6),
  ('GP-CLUB-700', 'Greenply_Architectural_Joinery_Guide.pdf',              'INSTALL',   false, 7),
  ('GP-CLUB-700', 'green_club_700_panel_3d.ifc',                            'BIM',       false, 8),
  ('GP-CLUB-700', 'green_club_700_cad_section.dwg',                         'DRAWING',   false, 9),
  ('GP-PLAT-BWP', 'green_platinum_sheet_hero.jpg',                          'MAIN',      true,  1),
  ('GP-PLAT-BWP', 'Green_Platinum_Fire_Retardant_TDS.pdf',                  'DATASHEET', false, 2)
) AS v(p_code, fname, role, is_pri, sort_order)
JOIN product p ON p.product_code = v.p_code
JOIN asset a ON a.file_name = v.fname
ON CONFLICT (product_id, asset_id, role) DO NOTHING;
