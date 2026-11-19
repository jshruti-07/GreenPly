-- =============================================================================
-- MODULE 09: EXCEL PRODUCT ATTRIBUTES MIGRATION
-- Enriches all plywood products with the 24 technical & commercial attributes
-- structured across 7 groups from the master specification sheet.
-- =============================================================================

SET search_path TO catalog, public;

-- 1. ATTRIBUTE GROUPS ---------------------------------------------------------
INSERT INTO attribute_group (code, sort_order) VALUES
  ('plywood_commercial', 1),
  ('plywood_dimensions', 2),
  ('plywood_health',     3),
  ('plywood_protection', 4),
  ('plywood_technical',  5),
  ('planning_bim',       6),
  ('plywood_mechanical', 7)
ON CONFLICT (code) DO UPDATE SET sort_order = EXCLUDED.sort_order;

INSERT INTO attribute_group_tr (group_id, locale_code, name)
SELECT g.id, v.locale, v.name FROM (VALUES
  ('plywood_commercial', 'en-GB', 'Commercial & Warranty'),
  ('plywood_commercial', 'de-DE', 'Garantie & Handelskonditionen'),
  ('plywood_dimensions', 'en-GB', 'Dimensions & Sizing'),
  ('plywood_dimensions', 'de-DE', 'Abmessungen & Formate'),
  ('plywood_health',     'en-GB', 'Health, Emissions & Air Quality'),
  ('plywood_health',     'de-DE', 'Gesundheit, Raumluft & Emissionen'),
  ('plywood_protection', 'en-GB', 'Protection & Durability'),
  ('plywood_protection', 'de-DE', 'Schutz & Widerstandsfähigkeit'),
  ('plywood_technical',  'en-GB', 'Core & Technical Specifications'),
  ('plywood_technical',  'de-DE', 'Technische Daten & Konstruktion'),
  ('planning_bim',       'en-GB', 'Planning & BIM'),
  ('planning_bim',       'de-DE', 'Planung & BIM-Parameter'),
  ('plywood_mechanical', 'en-GB', 'Mechanical & Physical Properties'),
  ('plywood_mechanical', 'de-DE', 'Mechanische & physikalische Eigenschaften')
) AS v(code, locale, name)
JOIN attribute_group g ON g.code = v.code
ON CONFLICT (group_id, locale_code) DO UPDATE SET name = EXCLUDED.name;

-- 2. UNITS --------------------------------------------------------------------
INSERT INTO unit (code, symbol) VALUES
  ('years', 'years'),
  ('mm',    'mm'),
  ('g_cm3', 'g/cm³'),
  ('mins',  'mins'),
  ('mpa',   'MPa'),
  ('n',     'N'),
  ('w_mk',  'W/m·K')
ON CONFLICT (code) DO NOTHING;

-- 3. ATTRIBUTES ---------------------------------------------------------------
INSERT INTO attribute (code, data_type, group_id, unit_id, is_localizable)
SELECT v.code, v.data_type, g.id, u.id, false FROM (VALUES
  ('warranty_years',          'number',  'plywood_commercial', 'years'),
  ('money_back_multiple',     'list',    'plywood_commercial', NULL),
  ('fully_covered_plan',      'boolean', 'plywood_commercial', NULL),
  ('sheet_dimension',         'list',    'plywood_dimensions', NULL),
  ('thickness_mm',            'measure', 'plywood_dimensions', 'mm'),
  ('mfg_length_mm',           'measure', 'plywood_dimensions', 'mm'),
  ('mfg_width_mm',            'measure', 'plywood_dimensions', 'mm'),
  ('mfg_thickness_mm',        'measure', 'plywood_dimensions', 'mm'),
  ('emission_standard',       'list',    'plywood_health',     NULL),
  ('borer_fungus_proof',      'boolean', 'plywood_protection', NULL),
  ('fire_retardant',          'boolean', 'plywood_protection', NULL),
  ('fire_delay_mins',         'number',  'plywood_protection', 'mins'),
  ('water_resistance_grade',  'list',    'plywood_technical',  NULL),
  ('core_species',            'list',    'plywood_technical',  NULL),
  ('resin_type',              'list',    'plywood_technical',  NULL),
  ('calibration_technology',  'list',    'plywood_technical',  NULL),
  ('density_g_cm3',           'measure', 'plywood_technical',  'g_cm3'),
  ('moisture_content_pct',    'decimal', 'plywood_technical',  NULL),
  ('bim_ready',               'boolean', 'planning_bim',       NULL),
  ('modulus_elasticity_mpa',   'measure', 'plywood_mechanical', 'mpa'),
  ('modulus_rupture_mpa',      'measure', 'plywood_mechanical', 'mpa'),
  ('screw_holding_face_n',     'number',  'plywood_mechanical', 'n'),
  ('screw_holding_edge_n',     'number',  'plywood_mechanical', 'n'),
  ('thermal_conductivity_w_mk','decimal','plywood_mechanical', 'w_mk')
) AS v(code, data_type, group_code, unit_code)
JOIN attribute_group g ON g.code = v.group_code
LEFT JOIN unit u ON u.code = v.unit_code
ON CONFLICT (code) DO UPDATE
SET group_id = EXCLUDED.group_id, unit_id = EXCLUDED.unit_id;

INSERT INTO attribute_tr (attribute_id, locale_code, label)
SELECT a.id, v.locale, v.label FROM (VALUES
  ('warranty_years',          'en-GB', 'Warranty Duration (Years)'),
  ('money_back_multiple',     'en-GB', 'Money-Back Guarantee'),
  ('fully_covered_plan',      'en-GB', '11-Year Fully Covered+ Restoration Plan'),
  ('sheet_dimension',         'en-GB', 'Standard Sheet Size'),
  ('thickness_mm',            'en-GB', 'Material thickness (mm)'),
  ('mfg_length_mm',           'en-GB', 'Manufacturing length (mm)'),
  ('mfg_width_mm',            'en-GB', 'Manufacturing width (mm)'),
  ('mfg_thickness_mm',        'en-GB', 'Calibrated Panel Thickness (mm)'),
  ('emission_standard',       'en-GB', 'Formaldehyde Emission Grade'),
  ('borer_fungus_proof',      'en-GB', 'Borer & Termite Proof'),
  ('fire_retardant',          'en-GB', 'Fire Retardant Treated'),
  ('fire_delay_mins',         'en-GB', 'Fire Penetration Delay (Minutes)'),
  ('water_resistance_grade',  'en-GB', 'Water Resistance Grade'),
  ('core_species',            'en-GB', 'Core Timber Species'),
  ('resin_type',              'en-GB', 'Adhesive / Resin System'),
  ('calibration_technology',  'en-GB', 'Calibration Technology'),
  ('density_g_cm3',           'en-GB', 'Panel Density (g/cm³)'),
  ('moisture_content_pct',    'en-GB', 'Moisture Content (%)'),
  ('bim_ready',               'en-GB', 'BIM object available'),
  ('modulus_elasticity_mpa',   'en-GB', 'Modulus of Elasticity (MOE) (MPa)'),
  ('modulus_rupture_mpa',      'en-GB', 'Modulus of Rupture (MOR) (MPa)'),
  ('screw_holding_face_n',     'en-GB', 'Face Screw Holding Load (N)'),
  ('screw_holding_edge_n',     'en-GB', 'Edge Screw Holding Load (N)'),
  ('thermal_conductivity_w_mk','en-GB', 'Thermal Conductivity (k-value)')
) AS v(code, locale, label)
JOIN attribute a ON a.code = v.code
ON CONFLICT (attribute_id, locale_code) DO UPDATE SET label = EXCLUDED.label;

-- 4. ATTRIBUTE OPTIONS --------------------------------------------------------
-- money_back_multiple
INSERT INTO attribute_option (attribute_id, code, sort_order)
SELECT a.id, v.code, v.sort_order FROM (VALUES
  ('7X',       1),
  ('5X',       2),
  ('2X',       3),
  ('STANDARD', 4)
) AS v(code, sort_order)
CROSS JOIN (SELECT id FROM attribute WHERE code = 'money_back_multiple') a
ON CONFLICT (attribute_id, code) DO NOTHING;

INSERT INTO attribute_option_tr (option_id, locale_code, label)
SELECT o.id, 'en-GB', v.label FROM (VALUES
  ('7X',       '7X Money-Back Guarantee (Lifetime Coverage)'),
  ('5X',       '500% (5X) Replacement Warranty'),
  ('2X',       '2X Money-Back Guarantee'),
  ('STANDARD', 'Standard Product Replacement')
) AS v(code, label)
JOIN attribute_option o ON o.code = v.code AND o.attribute_id = (SELECT id FROM attribute WHERE code = 'money_back_multiple')
ON CONFLICT (option_id, locale_code) DO UPDATE SET label = EXCLUDED.label;

-- sheet_dimension
INSERT INTO attribute_option (attribute_id, code, sort_order)
SELECT a.id, v.code, v.sort_order FROM (VALUES
  ('8X4', 1),
  ('7X4', 2),
  ('7X3', 3),
  ('6X4', 4),
  ('6X3', 5)
) AS v(code, sort_order)
CROSS JOIN (SELECT id FROM attribute WHERE code = 'sheet_dimension') a
ON CONFLICT (attribute_id, code) DO NOTHING;

INSERT INTO attribute_option_tr (option_id, locale_code, label)
SELECT o.id, 'en-GB', v.label FROM (VALUES
  ('8X4', '8 ft × 4 ft (2440 × 1220 mm) — Standard'),
  ('7X4', '7 ft × 4 ft (2135 × 1220 mm)'),
  ('7X3', '7 ft × 3 ft (2135 × 915 mm)'),
  ('6X4', '6 ft × 4 ft (1830 × 1220 mm)'),
  ('6X3', '6 ft × 3 ft (1830 × 915 mm)')
) AS v(code, label)
JOIN attribute_option o ON o.code = v.code AND o.attribute_id = (SELECT id FROM attribute WHERE code = 'sheet_dimension')
ON CONFLICT (option_id, locale_code) DO UPDATE SET label = EXCLUDED.label;

-- emission_standard
INSERT INTO attribute_option (attribute_id, code, sort_order)
SELECT a.id, v.code, v.sort_order FROM (VALUES
  ('E0', 1),
  ('E1', 2)
) AS v(code, sort_order)
CROSS JOIN (SELECT id FROM attribute WHERE code = 'emission_standard') a
ON CONFLICT (attribute_id, code) DO NOTHING;

INSERT INTO attribute_option_tr (option_id, locale_code, label)
SELECT o.id, 'en-GB', v.label FROM (VALUES
  ('E0', 'E0 — Zero Emission (≤ 3 mg/100g formalin)'),
  ('E1', 'E1 — Low Emission (≤ 8 mg/100g formalin)')
) AS v(code, label)
JOIN attribute_option o ON o.code = v.code AND o.attribute_id = (SELECT id FROM attribute WHERE code = 'emission_standard')
ON CONFLICT (option_id, locale_code) DO UPDATE SET label = EXCLUDED.label;

-- water_resistance_grade
INSERT INTO attribute_option (attribute_id, code, sort_order)
SELECT a.id, v.code, v.sort_order FROM (VALUES
  ('STRUCTURAL', 1),
  ('BWP',        2),
  ('BWR',        3),
  ('MR',         4)
) AS v(code, sort_order)
CROSS JOIN (SELECT id FROM attribute WHERE code = 'water_resistance_grade') a
ON CONFLICT (attribute_id, code) DO NOTHING;

INSERT INTO attribute_option_tr (option_id, locale_code, label)
SELECT o.id, 'en-GB', v.label FROM (VALUES
  ('STRUCTURAL', 'Structural Grade (IS:10701 Heavy Load)'),
  ('BWP',        'BWP — Boiling Water Proof (Marine Grade, 72h boil test)'),
  ('BWR',        'BWR — Boiling Water Resistant (Exterior Grade)'),
  ('MR',         'MR — Moisture Resistant (Commercial Interior)')
) AS v(code, label)
JOIN attribute_option o ON o.code = v.code AND o.attribute_id = (SELECT id FROM attribute WHERE code = 'water_resistance_grade')
ON CONFLICT (option_id, locale_code) DO UPDATE SET label = EXCLUDED.label;

-- core_species
INSERT INTO attribute_option (attribute_id, code, sort_order)
SELECT a.id, v.code, v.sort_order FROM (VALUES
  ('HARDWOOD',   1),
  ('GURJAN',     2),
  ('POPLAR',     3),
  ('EUCALYPTUS', 4),
  ('COMBI',      5)
) AS v(code, sort_order)
CROSS JOIN (SELECT id FROM attribute WHERE code = 'core_species') a
ON CONFLICT (attribute_id, code) DO NOTHING;

INSERT INTO attribute_option_tr (option_id, locale_code, label)
SELECT o.id, 'en-GB', v.label FROM (VALUES
  ('HARDWOOD',   'Selected High-Density Hardwood Timber'),
  ('GURJAN',     '100% Gurjan Hardwood Timber'),
  ('POPLAR',     'Selected Poplar Timber'),
  ('EUCALYPTUS', 'Plantation Eucalyptus Core'),
  ('COMBI',      'Combi Hardwood / Plantation Core')
) AS v(code, label)
JOIN attribute_option o ON o.code = v.code AND o.attribute_id = (SELECT id FROM attribute WHERE code = 'core_species')
ON CONFLICT (option_id, locale_code) DO UPDATE SET label = EXCLUDED.label;

-- resin_type
INSERT INTO attribute_option (attribute_id, code, sort_order)
SELECT a.id, v.code, v.sort_order FROM (VALUES
  ('UNEXTENDED_PF', 1),
  ('MUF',           2),
  ('UF',            3)
) AS v(code, sort_order)
CROSS JOIN (SELECT id FROM attribute WHERE code = 'resin_type') a
ON CONFLICT (attribute_id, code) DO NOTHING;

INSERT INTO attribute_option_tr (option_id, locale_code, label)
SELECT o.id, 'en-GB', v.label FROM (VALUES
  ('UNEXTENDED_PF', 'Unextended Phenol Formaldehyde (PF) Resin'),
  ('MUF',           'Melamine Urea Formaldehyde (MUF) Resin'),
  ('UF',            'Urea Formaldehyde (UF) Resin')
) AS v(code, label)
JOIN attribute_option o ON o.code = v.code AND o.attribute_id = (SELECT id FROM attribute WHERE code = 'resin_type')
ON CONFLICT (option_id, locale_code) DO UPDATE SET label = EXCLUDED.label;

-- calibration_technology
INSERT INTO attribute_option (attribute_id, code, sort_order)
SELECT a.id, v.code, v.sort_order FROM (VALUES
  ('PENTA_TECH',       1),
  ('FOUR_PRESS',       2),
  ('DOUBLE_HOT_PRESS', 3)
) AS v(code, sort_order)
CROSS JOIN (SELECT id FROM attribute WHERE code = 'calibration_technology') a
ON CONFLICT (attribute_id, code) DO NOTHING;

INSERT INTO attribute_option_tr (option_id, locale_code, label)
SELECT o.id, 'en-GB', v.label FROM (VALUES
  ('PENTA_TECH',       'Penta (5) Tech — Multi-stage Calibration'),
  ('FOUR_PRESS',       '4-Press Quad Calibration'),
  ('DOUBLE_HOT_PRESS', 'Standard Double Hot Press')
) AS v(code, label)
JOIN attribute_option o ON o.code = v.code AND o.attribute_id = (SELECT id FROM attribute WHERE code = 'calibration_technology')
ON CONFLICT (option_id, locale_code) DO UPDATE SET label = EXCLUDED.label;

-- 5. LINK ATTRIBUTES TO PLYWOOD_CORE SET ---------------------------------------
INSERT INTO attribute_set_attribute (set_id, attribute_id, is_required, sort_order)
SELECT s.id, a.id, v.required, v.sort_order FROM (VALUES
  ('warranty_years',          true,  1),
  ('money_back_multiple',     false, 2),
  ('fully_covered_plan',      false, 3),
  ('sheet_dimension',         true,  4),
  ('thickness_mm',            true,  5),
  ('mfg_length_mm',           true,  6),
  ('mfg_width_mm',            true,  7),
  ('mfg_thickness_mm',        false, 8),
  ('emission_standard',       true,  9),
  ('borer_fungus_proof',      true,  10),
  ('fire_retardant',          false, 11),
  ('fire_delay_mins',         false, 12),
  ('water_resistance_grade',  true,  13),
  ('core_species',            true,  14),
  ('resin_type',              true,  15),
  ('calibration_technology',  true,  16),
  ('density_g_cm3',           false, 17),
  ('moisture_content_pct',    false, 18),
  ('bim_ready',               false, 19),
  ('modulus_elasticity_mpa',   false, 20),
  ('modulus_rupture_mpa',      false, 21),
  ('screw_holding_face_n',     false, 22),
  ('screw_holding_edge_n',     false, 23),
  ('thermal_conductivity_w_mk', false, 24)
) AS v(code, required, sort_order)
JOIN attribute a ON a.code = v.code
CROSS JOIN (SELECT id FROM attribute_set WHERE code = 'plywood_core') s
ON CONFLICT (set_id, attribute_id) DO UPDATE
SET is_required = EXCLUDED.is_required, sort_order = EXCLUDED.sort_order;

-- 6. SEED ATTRIBUTE VALUES FOR ALL PRODUCTS -----------------------------------
CREATE TEMP TABLE temp_product_attribute_values (
  p_code text,
  a_code text,
  val_num numeric,
  val_bool boolean,
  opt_code text
);

INSERT INTO temp_product_attribute_values (p_code, a_code, val_num, val_bool, opt_code) VALUES
-- PG-CALIB-25
('PG-CALIB-25', 'warranty_years', 25, NULL, NULL),
('PG-CALIB-25', 'money_back_multiple', NULL, NULL, '7X'),
('PG-CALIB-25', 'fully_covered_plan', NULL, true, NULL),
('PG-CALIB-25', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-CALIB-25', 'thickness_mm', 25, NULL, NULL),
('PG-CALIB-25', 'mfg_length_mm', 2440, NULL, NULL),
('PG-CALIB-25', 'mfg_width_mm', 1220, NULL, NULL),
('PG-CALIB-25', 'mfg_thickness_mm', 24.8, NULL, NULL),
('PG-CALIB-25', 'emission_standard', NULL, NULL, 'E0'),
('PG-CALIB-25', 'borer_fungus_proof', NULL, true, NULL),
('PG-CALIB-25', 'fire_retardant', NULL, true, NULL),
('PG-CALIB-25', 'fire_delay_mins', 45, NULL, NULL),
('PG-CALIB-25', 'water_resistance_grade', NULL, NULL, 'STRUCTURAL'),
('PG-CALIB-25', 'core_species', NULL, NULL, 'HARDWOOD'),
('PG-CALIB-25', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('PG-CALIB-25', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('PG-CALIB-25', 'density_g_cm3', 0.79, NULL, NULL),
('PG-CALIB-25', 'moisture_content_pct', 8.1, NULL, NULL),
('PG-CALIB-25', 'bim_ready', NULL, true, NULL),
('PG-CALIB-25', 'modulus_elasticity_mpa', 7040, NULL, NULL),
('PG-CALIB-25', 'modulus_rupture_mpa', 59.4, NULL, NULL),
('PG-CALIB-25', 'screw_holding_face_n', 1930, NULL, NULL),
('PG-CALIB-25', 'screw_holding_edge_n', 1230, NULL, NULL),
('PG-CALIB-25', 'thermal_conductivity_w_mk', 0.165, NULL, NULL),

-- PG-JANSATHI
('PG-JANSATHI', 'warranty_years', 10, NULL, NULL),
('PG-JANSATHI', 'money_back_multiple', NULL, NULL, 'STANDARD'),
('PG-JANSATHI', 'fully_covered_plan', NULL, false, NULL),
('PG-JANSATHI', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-JANSATHI', 'thickness_mm', 12, NULL, NULL),
('PG-JANSATHI', 'mfg_length_mm', 2440, NULL, NULL),
('PG-JANSATHI', 'mfg_width_mm', 1220, NULL, NULL),
('PG-JANSATHI', 'mfg_thickness_mm', 11.8, NULL, NULL),
('PG-JANSATHI', 'emission_standard', NULL, NULL, 'E1'),
('PG-JANSATHI', 'borer_fungus_proof', NULL, true, NULL),
('PG-JANSATHI', 'fire_retardant', NULL, false, NULL),
('PG-JANSATHI', 'fire_delay_mins', 0, NULL, NULL),
('PG-JANSATHI', 'water_resistance_grade', NULL, NULL, 'MR'),
('PG-JANSATHI', 'core_species', NULL, NULL, 'POPLAR'),
('PG-JANSATHI', 'resin_type', NULL, NULL, 'UF'),
('PG-JANSATHI', 'calibration_technology', NULL, NULL, 'DOUBLE_HOT_PRESS'),
('PG-JANSATHI', 'density_g_cm3', 0.62, NULL, NULL),
('PG-JANSATHI', 'moisture_content_pct', 8.1, NULL, NULL),
('PG-JANSATHI', 'bim_ready', NULL, true, NULL),
('PG-JANSATHI', 'modulus_elasticity_mpa', 4600, NULL, NULL),
('PG-JANSATHI', 'modulus_rupture_mpa', 32.4, NULL, NULL),
('PG-JANSATHI', 'screw_holding_face_n', 1440, NULL, NULL),
('PG-JANSATHI', 'screw_holding_edge_n', 940, NULL, NULL),
('PG-JANSATHI', 'thermal_conductivity_w_mk', 0.152, NULL, NULL),

-- PG-SHUTTER-BWP
('PG-SHUTTER-BWP', 'warranty_years', 15, NULL, NULL),
('PG-SHUTTER-BWP', 'money_back_multiple', NULL, NULL, '2X'),
('PG-SHUTTER-BWP', 'fully_covered_plan', NULL, false, NULL),
('PG-SHUTTER-BWP', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-SHUTTER-BWP', 'thickness_mm', 18, NULL, NULL),
('PG-SHUTTER-BWP', 'mfg_length_mm', 2440, NULL, NULL),
('PG-SHUTTER-BWP', 'mfg_width_mm', 1220, NULL, NULL),
('PG-SHUTTER-BWP', 'mfg_thickness_mm', 17.8, NULL, NULL),
('PG-SHUTTER-BWP', 'emission_standard', NULL, NULL, 'E1'),
('PG-SHUTTER-BWP', 'borer_fungus_proof', NULL, true, NULL),
('PG-SHUTTER-BWP', 'fire_retardant', NULL, false, NULL),
('PG-SHUTTER-BWP', 'fire_delay_mins', 0, NULL, NULL),
('PG-SHUTTER-BWP', 'water_resistance_grade', NULL, NULL, 'BWP'),
('PG-SHUTTER-BWP', 'core_species', NULL, NULL, 'GURJAN'),
('PG-SHUTTER-BWP', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('PG-SHUTTER-BWP', 'calibration_technology', NULL, NULL, 'FOUR_PRESS'),
('PG-SHUTTER-BWP', 'density_g_cm3', 0.73, NULL, NULL),
('PG-SHUTTER-BWP', 'moisture_content_pct', 10.1, NULL, NULL),
('PG-SHUTTER-BWP', 'bim_ready', NULL, true, NULL),
('PG-SHUTTER-BWP', 'modulus_elasticity_mpa', 7010, NULL, NULL),
('PG-SHUTTER-BWP', 'modulus_rupture_mpa', 54.1, NULL, NULL),
('PG-SHUTTER-BWP', 'screw_holding_face_n', 1690, NULL, NULL),
('PG-SHUTTER-BWP', 'screw_holding_edge_n', 1150, NULL, NULL),
('PG-SHUTTER-BWP', 'thermal_conductivity_w_mk', 0.161, NULL, NULL),

-- PG-BHAROSA
('PG-BHAROSA', 'warranty_years', 10, NULL, NULL),
('PG-BHAROSA', 'money_back_multiple', NULL, NULL, 'STANDARD'),
('PG-BHAROSA', 'fully_covered_plan', NULL, false, NULL),
('PG-BHAROSA', 'sheet_dimension', NULL, NULL, '7X3'),
('PG-BHAROSA', 'thickness_mm', 12, NULL, NULL),
('PG-BHAROSA', 'mfg_length_mm', 2135, NULL, NULL),
('PG-BHAROSA', 'mfg_width_mm', 915, NULL, NULL),
('PG-BHAROSA', 'mfg_thickness_mm', 11.6, NULL, NULL),
('PG-BHAROSA', 'emission_standard', NULL, NULL, 'E1'),
('PG-BHAROSA', 'borer_fungus_proof', NULL, true, NULL),
('PG-BHAROSA', 'fire_retardant', NULL, false, NULL),
('PG-BHAROSA', 'fire_delay_mins', 0, NULL, NULL),
('PG-BHAROSA', 'water_resistance_grade', NULL, NULL, 'MR'),
('PG-BHAROSA', 'core_species', NULL, NULL, 'COMBI'),
('PG-BHAROSA', 'resin_type', NULL, NULL, 'UF'),
('PG-BHAROSA', 'calibration_technology', NULL, NULL, 'DOUBLE_HOT_PRESS'),
('PG-BHAROSA', 'density_g_cm3', 0.62, NULL, NULL),
('PG-BHAROSA', 'moisture_content_pct', 8.3, NULL, NULL),
('PG-BHAROSA', 'bim_ready', NULL, true, NULL),
('PG-BHAROSA', 'modulus_elasticity_mpa', 4810, NULL, NULL),
('PG-BHAROSA', 'modulus_rupture_mpa', 34.4, NULL, NULL),
('PG-BHAROSA', 'screw_holding_face_n', 1480, NULL, NULL),
('PG-BHAROSA', 'screw_holding_edge_n', 900, NULL, NULL),
('PG-BHAROSA', 'thermal_conductivity_w_mk', 0.156, NULL, NULL),

-- PG-OPTIMA-G
('PG-OPTIMA-G', 'warranty_years', 15, NULL, NULL),
('PG-OPTIMA-G', 'money_back_multiple', NULL, NULL, '2X'),
('PG-OPTIMA-G', 'fully_covered_plan', NULL, false, NULL),
('PG-OPTIMA-G', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-OPTIMA-G', 'thickness_mm', 19, NULL, NULL),
('PG-OPTIMA-G', 'mfg_length_mm', 2440, NULL, NULL),
('PG-OPTIMA-G', 'mfg_width_mm', 1220, NULL, NULL),
('PG-OPTIMA-G', 'mfg_thickness_mm', 18.6, NULL, NULL),
('PG-OPTIMA-G', 'emission_standard', NULL, NULL, 'E0'),
('PG-OPTIMA-G', 'borer_fungus_proof', NULL, true, NULL),
('PG-OPTIMA-G', 'fire_retardant', NULL, false, NULL),
('PG-OPTIMA-G', 'fire_delay_mins', 0, NULL, NULL),
('PG-OPTIMA-G', 'water_resistance_grade', NULL, NULL, 'MR'),
('PG-OPTIMA-G', 'core_species', NULL, NULL, 'EUCALYPTUS'),
('PG-OPTIMA-G', 'resin_type', NULL, NULL, 'MUF'),
('PG-OPTIMA-G', 'calibration_technology', NULL, NULL, 'FOUR_PRESS'),
('PG-OPTIMA-G', 'density_g_cm3', 0.64, NULL, NULL),
('PG-OPTIMA-G', 'moisture_content_pct', 9, NULL, NULL),
('PG-OPTIMA-G', 'bim_ready', NULL, false, NULL),
('PG-OPTIMA-G', 'modulus_elasticity_mpa', 5570, NULL, NULL),
('PG-OPTIMA-G', 'modulus_rupture_mpa', 39.4, NULL, NULL),
('PG-OPTIMA-G', 'screw_holding_face_n', 1490, NULL, NULL),
('PG-OPTIMA-G', 'screw_holding_edge_n', 920, NULL, NULL),
('PG-OPTIMA-G', 'thermal_conductivity_w_mk', 0.155, NULL, NULL),

-- PG-ECO-MR
('PG-ECO-MR', 'warranty_years', 15, NULL, NULL),
('PG-ECO-MR', 'money_back_multiple', NULL, NULL, '2X'),
('PG-ECO-MR', 'fully_covered_plan', NULL, true, NULL),
('PG-ECO-MR', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-ECO-MR', 'thickness_mm', 19, NULL, NULL),
('PG-ECO-MR', 'mfg_length_mm', 2440, NULL, NULL),
('PG-ECO-MR', 'mfg_width_mm', 1220, NULL, NULL),
('PG-ECO-MR', 'mfg_thickness_mm', 18.8, NULL, NULL),
('PG-ECO-MR', 'emission_standard', NULL, NULL, 'E0'),
('PG-ECO-MR', 'borer_fungus_proof', NULL, true, NULL),
('PG-ECO-MR', 'fire_retardant', NULL, false, NULL),
('PG-ECO-MR', 'fire_delay_mins', 0, NULL, NULL),
('PG-ECO-MR', 'water_resistance_grade', NULL, NULL, 'MR'),
('PG-ECO-MR', 'core_species', NULL, NULL, 'EUCALYPTUS'),
('PG-ECO-MR', 'resin_type', NULL, NULL, 'MUF'),
('PG-ECO-MR', 'calibration_technology', NULL, NULL, 'FOUR_PRESS'),
('PG-ECO-MR', 'density_g_cm3', 0.66, NULL, NULL),
('PG-ECO-MR', 'moisture_content_pct', 11.4, NULL, NULL),
('PG-ECO-MR', 'bim_ready', NULL, true, NULL),
('PG-ECO-MR', 'modulus_elasticity_mpa', 5250, NULL, NULL),
('PG-ECO-MR', 'modulus_rupture_mpa', 38.8, NULL, NULL),
('PG-ECO-MR', 'screw_holding_face_n', 1640, NULL, NULL),
('PG-ECO-MR', 'screw_holding_edge_n', 960, NULL, NULL),
('PG-ECO-MR', 'thermal_conductivity_w_mk', 0.155, NULL, NULL),

-- PG-ECO-710
('PG-ECO-710', 'warranty_years', 25, NULL, NULL),
('PG-ECO-710', 'money_back_multiple', NULL, NULL, '7X'),
('PG-ECO-710', 'fully_covered_plan', NULL, true, NULL),
('PG-ECO-710', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-ECO-710', 'thickness_mm', 19, NULL, NULL),
('PG-ECO-710', 'mfg_length_mm', 2440, NULL, NULL),
('PG-ECO-710', 'mfg_width_mm', 1220, NULL, NULL),
('PG-ECO-710', 'mfg_thickness_mm', 18.6, NULL, NULL),
('PG-ECO-710', 'emission_standard', NULL, NULL, 'E0'),
('PG-ECO-710', 'borer_fungus_proof', NULL, true, NULL),
('PG-ECO-710', 'fire_retardant', NULL, true, NULL),
('PG-ECO-710', 'fire_delay_mins', 30, NULL, NULL),
('PG-ECO-710', 'water_resistance_grade', NULL, NULL, 'BWP'),
('PG-ECO-710', 'core_species', NULL, NULL, 'GURJAN'),
('PG-ECO-710', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('PG-ECO-710', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('PG-ECO-710', 'density_g_cm3', 0.77, NULL, NULL),
('PG-ECO-710', 'moisture_content_pct', 10.5, NULL, NULL),
('PG-ECO-710', 'bim_ready', NULL, false, NULL),
('PG-ECO-710', 'modulus_elasticity_mpa', 6760, NULL, NULL),
('PG-ECO-710', 'modulus_rupture_mpa', 57.8, NULL, NULL),
('PG-ECO-710', 'screw_holding_face_n', 1850, NULL, NULL),
('PG-ECO-710', 'screw_holding_edge_n', 1130, NULL, NULL),
('PG-ECO-710', 'thermal_conductivity_w_mk', 0.167, NULL, NULL),

-- PG-ECO-PLAT
('PG-ECO-PLAT', 'warranty_years', 25, NULL, NULL),
('PG-ECO-PLAT', 'money_back_multiple', NULL, NULL, '7X'),
('PG-ECO-PLAT', 'fully_covered_plan', NULL, true, NULL),
('PG-ECO-PLAT', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-ECO-PLAT', 'thickness_mm', 19, NULL, NULL),
('PG-ECO-PLAT', 'mfg_length_mm', 2440, NULL, NULL),
('PG-ECO-PLAT', 'mfg_width_mm', 1220, NULL, NULL),
('PG-ECO-PLAT', 'mfg_thickness_mm', 18.6, NULL, NULL),
('PG-ECO-PLAT', 'emission_standard', NULL, NULL, 'E0'),
('PG-ECO-PLAT', 'borer_fungus_proof', NULL, true, NULL),
('PG-ECO-PLAT', 'fire_retardant', NULL, true, NULL),
('PG-ECO-PLAT', 'fire_delay_mins', 60, NULL, NULL),
('PG-ECO-PLAT', 'water_resistance_grade', NULL, NULL, 'BWP'),
('PG-ECO-PLAT', 'core_species', NULL, NULL, 'HARDWOOD'),
('PG-ECO-PLAT', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('PG-ECO-PLAT', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('PG-ECO-PLAT', 'density_g_cm3', 0.76, NULL, NULL),
('PG-ECO-PLAT', 'moisture_content_pct', 11.2, NULL, NULL),
('PG-ECO-PLAT', 'bim_ready', NULL, false, NULL),
('PG-ECO-PLAT', 'modulus_elasticity_mpa', 7230, NULL, NULL),
('PG-ECO-PLAT', 'modulus_rupture_mpa', 58.9, NULL, NULL),
('PG-ECO-PLAT', 'screw_holding_face_n', 1870, NULL, NULL),
('PG-ECO-PLAT', 'screw_holding_edge_n', 1090, NULL, NULL),
('PG-ECO-PLAT', 'thermal_conductivity_w_mk', 0.169, NULL, NULL),

-- PG-FLEX-PLY
('PG-FLEX-PLY', 'warranty_years', 15, NULL, NULL),
('PG-FLEX-PLY', 'money_back_multiple', NULL, NULL, '2X'),
('PG-FLEX-PLY', 'fully_covered_plan', NULL, false, NULL),
('PG-FLEX-PLY', 'sheet_dimension', NULL, NULL, '6X3'),
('PG-FLEX-PLY', 'thickness_mm', 6, NULL, NULL),
('PG-FLEX-PLY', 'mfg_length_mm', 1830, NULL, NULL),
('PG-FLEX-PLY', 'mfg_width_mm', 915, NULL, NULL),
('PG-FLEX-PLY', 'mfg_thickness_mm', 5.8, NULL, NULL),
('PG-FLEX-PLY', 'emission_standard', NULL, NULL, 'E0'),
('PG-FLEX-PLY', 'borer_fungus_proof', NULL, true, NULL),
('PG-FLEX-PLY', 'fire_retardant', NULL, false, NULL),
('PG-FLEX-PLY', 'fire_delay_mins', 0, NULL, NULL),
('PG-FLEX-PLY', 'water_resistance_grade', NULL, NULL, 'BWR'),
('PG-FLEX-PLY', 'core_species', NULL, NULL, 'POPLAR'),
('PG-FLEX-PLY', 'resin_type', NULL, NULL, 'MUF'),
('PG-FLEX-PLY', 'calibration_technology', NULL, NULL, 'FOUR_PRESS'),
('PG-FLEX-PLY', 'density_g_cm3', 0.71, NULL, NULL),
('PG-FLEX-PLY', 'moisture_content_pct', 9.4, NULL, NULL),
('PG-FLEX-PLY', 'bim_ready', NULL, true, NULL),
('PG-FLEX-PLY', 'modulus_elasticity_mpa', 5990, NULL, NULL),
('PG-FLEX-PLY', 'modulus_rupture_mpa', 44.3, NULL, NULL),
('PG-FLEX-PLY', 'screw_holding_face_n', 1740, NULL, NULL),
('PG-FLEX-PLY', 'screw_holding_edge_n', 1030, NULL, NULL),
('PG-FLEX-PLY', 'thermal_conductivity_w_mk', 0.161, NULL, NULL),

-- PG-303-MR
('PG-303-MR', 'warranty_years', 10, NULL, NULL),
('PG-303-MR', 'money_back_multiple', NULL, NULL, 'STANDARD'),
('PG-303-MR', 'fully_covered_plan', NULL, false, NULL),
('PG-303-MR', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-303-MR', 'thickness_mm', 19, NULL, NULL),
('PG-303-MR', 'mfg_length_mm', 2440, NULL, NULL),
('PG-303-MR', 'mfg_width_mm', 1220, NULL, NULL),
('PG-303-MR', 'mfg_thickness_mm', 18.7, NULL, NULL),
('PG-303-MR', 'emission_standard', NULL, NULL, 'E1'),
('PG-303-MR', 'borer_fungus_proof', NULL, true, NULL),
('PG-303-MR', 'fire_retardant', NULL, false, NULL),
('PG-303-MR', 'fire_delay_mins', 0, NULL, NULL),
('PG-303-MR', 'water_resistance_grade', NULL, NULL, 'MR'),
('PG-303-MR', 'core_species', NULL, NULL, 'COMBI'),
('PG-303-MR', 'resin_type', NULL, NULL, 'UF'),
('PG-303-MR', 'calibration_technology', NULL, NULL, 'DOUBLE_HOT_PRESS'),
('PG-303-MR', 'density_g_cm3', 0.68, NULL, NULL),
('PG-303-MR', 'moisture_content_pct', 10.2, NULL, NULL),
('PG-303-MR', 'bim_ready', NULL, true, NULL),
('PG-303-MR', 'modulus_elasticity_mpa', 5050, NULL, NULL),
('PG-303-MR', 'modulus_rupture_mpa', 38.9, NULL, NULL),
('PG-303-MR', 'screw_holding_face_n', 1640, NULL, NULL),
('PG-303-MR', 'screw_holding_edge_n', 1050, NULL, NULL),
('PG-303-MR', 'thermal_conductivity_w_mk', 0.157, NULL, NULL),

-- PG-ECO-TMR
('PG-ECO-TMR', 'warranty_years', 10, NULL, NULL),
('PG-ECO-TMR', 'money_back_multiple', NULL, NULL, 'STANDARD'),
('PG-ECO-TMR', 'fully_covered_plan', NULL, false, NULL),
('PG-ECO-TMR', 'sheet_dimension', NULL, NULL, '7X4'),
('PG-ECO-TMR', 'thickness_mm', 12, NULL, NULL),
('PG-ECO-TMR', 'mfg_length_mm', 2135, NULL, NULL),
('PG-ECO-TMR', 'mfg_width_mm', 1220, NULL, NULL),
('PG-ECO-TMR', 'mfg_thickness_mm', 11.6, NULL, NULL),
('PG-ECO-TMR', 'emission_standard', NULL, NULL, 'E1'),
('PG-ECO-TMR', 'borer_fungus_proof', NULL, true, NULL),
('PG-ECO-TMR', 'fire_retardant', NULL, false, NULL),
('PG-ECO-TMR', 'fire_delay_mins', 0, NULL, NULL),
('PG-ECO-TMR', 'water_resistance_grade', NULL, NULL, 'MR'),
('PG-ECO-TMR', 'core_species', NULL, NULL, 'POPLAR'),
('PG-ECO-TMR', 'resin_type', NULL, NULL, 'UF'),
('PG-ECO-TMR', 'calibration_technology', NULL, NULL, 'DOUBLE_HOT_PRESS'),
('PG-ECO-TMR', 'density_g_cm3', 0.6, NULL, NULL),
('PG-ECO-TMR', 'moisture_content_pct', 9.3, NULL, NULL),
('PG-ECO-TMR', 'bim_ready', NULL, true, NULL),
('PG-ECO-TMR', 'modulus_elasticity_mpa', 4400, NULL, NULL),
('PG-ECO-TMR', 'modulus_rupture_mpa', 32.8, NULL, NULL),
('PG-ECO-TMR', 'screw_holding_face_n', 1490, NULL, NULL),
('PG-ECO-TMR', 'screw_holding_edge_n', 870, NULL, NULL),
('PG-ECO-TMR', 'thermal_conductivity_w_mk', 0.147, NULL, NULL),

-- PG-FIRE-RET
('PG-FIRE-RET', 'warranty_years', 20, NULL, NULL),
('PG-FIRE-RET', 'money_back_multiple', NULL, NULL, '2X'),
('PG-FIRE-RET', 'fully_covered_plan', NULL, true, NULL),
('PG-FIRE-RET', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-FIRE-RET', 'thickness_mm', 19, NULL, NULL),
('PG-FIRE-RET', 'mfg_length_mm', 2440, NULL, NULL),
('PG-FIRE-RET', 'mfg_width_mm', 1220, NULL, NULL),
('PG-FIRE-RET', 'mfg_thickness_mm', 18.7, NULL, NULL),
('PG-FIRE-RET', 'emission_standard', NULL, NULL, 'E0'),
('PG-FIRE-RET', 'borer_fungus_proof', NULL, true, NULL),
('PG-FIRE-RET', 'fire_retardant', NULL, true, NULL),
('PG-FIRE-RET', 'fire_delay_mins', 90, NULL, NULL),
('PG-FIRE-RET', 'water_resistance_grade', NULL, NULL, 'BWR'),
('PG-FIRE-RET', 'core_species', NULL, NULL, 'HARDWOOD'),
('PG-FIRE-RET', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('PG-FIRE-RET', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('PG-FIRE-RET', 'density_g_cm3', 0.76, NULL, NULL),
('PG-FIRE-RET', 'moisture_content_pct', 10.1, NULL, NULL),
('PG-FIRE-RET', 'bim_ready', NULL, true, NULL),
('PG-FIRE-RET', 'modulus_elasticity_mpa', 6850, NULL, NULL),
('PG-FIRE-RET', 'modulus_rupture_mpa', 51.8, NULL, NULL),
('PG-FIRE-RET', 'screw_holding_face_n', 1780, NULL, NULL),
('PG-FIRE-RET', 'screw_holding_edge_n', 1150, NULL, NULL),
('PG-FIRE-RET', 'thermal_conductivity_w_mk', 0.17, NULL, NULL),

-- PG-CLUB-500
('PG-CLUB-500', 'warranty_years', 20, NULL, NULL),
('PG-CLUB-500', 'money_back_multiple', NULL, NULL, '2X'),
('PG-CLUB-500', 'fully_covered_plan', NULL, true, NULL),
('PG-CLUB-500', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-CLUB-500', 'thickness_mm', 19, NULL, NULL),
('PG-CLUB-500', 'mfg_length_mm', 2440, NULL, NULL),
('PG-CLUB-500', 'mfg_width_mm', 1220, NULL, NULL),
('PG-CLUB-500', 'mfg_thickness_mm', 18.7, NULL, NULL),
('PG-CLUB-500', 'emission_standard', NULL, NULL, 'E0'),
('PG-CLUB-500', 'borer_fungus_proof', NULL, true, NULL),
('PG-CLUB-500', 'fire_retardant', NULL, true, NULL),
('PG-CLUB-500', 'fire_delay_mins', 25, NULL, NULL),
('PG-CLUB-500', 'water_resistance_grade', NULL, NULL, 'BWR'),
('PG-CLUB-500', 'core_species', NULL, NULL, 'HARDWOOD'),
('PG-CLUB-500', 'resin_type', NULL, NULL, 'MUF'),
('PG-CLUB-500', 'calibration_technology', NULL, NULL, 'FOUR_PRESS'),
('PG-CLUB-500', 'density_g_cm3', 0.73, NULL, NULL),
('PG-CLUB-500', 'moisture_content_pct', 8.4, NULL, NULL),
('PG-CLUB-500', 'bim_ready', NULL, false, NULL),
('PG-CLUB-500', 'modulus_elasticity_mpa', 6330, NULL, NULL),
('PG-CLUB-500', 'modulus_rupture_mpa', 47.2, NULL, NULL),
('PG-CLUB-500', 'screw_holding_face_n', 1720, NULL, NULL),
('PG-CLUB-500', 'screw_holding_edge_n', 1060, NULL, NULL),
('PG-CLUB-500', 'thermal_conductivity_w_mk', 0.158, NULL, NULL),

-- PG-CALIB-19
('PG-CALIB-19', 'warranty_years', 25, NULL, NULL),
('PG-CALIB-19', 'money_back_multiple', NULL, NULL, '7X'),
('PG-CALIB-19', 'fully_covered_plan', NULL, true, NULL),
('PG-CALIB-19', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-CALIB-19', 'thickness_mm', 19, NULL, NULL),
('PG-CALIB-19', 'mfg_length_mm', 2440, NULL, NULL),
('PG-CALIB-19', 'mfg_width_mm', 1220, NULL, NULL),
('PG-CALIB-19', 'mfg_thickness_mm', 18.8, NULL, NULL),
('PG-CALIB-19', 'emission_standard', NULL, NULL, 'E0'),
('PG-CALIB-19', 'borer_fungus_proof', NULL, true, NULL),
('PG-CALIB-19', 'fire_retardant', NULL, true, NULL),
('PG-CALIB-19', 'fire_delay_mins', 45, NULL, NULL),
('PG-CALIB-19', 'water_resistance_grade', NULL, NULL, 'STRUCTURAL'),
('PG-CALIB-19', 'core_species', NULL, NULL, 'HARDWOOD'),
('PG-CALIB-19', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('PG-CALIB-19', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('PG-CALIB-19', 'density_g_cm3', 0.78, NULL, NULL),
('PG-CALIB-19', 'moisture_content_pct', 10.5, NULL, NULL),
('PG-CALIB-19', 'bim_ready', NULL, false, NULL),
('PG-CALIB-19', 'modulus_elasticity_mpa', 7100, NULL, NULL),
('PG-CALIB-19', 'modulus_rupture_mpa', 54.8, NULL, NULL),
('PG-CALIB-19', 'screw_holding_face_n', 1900, NULL, NULL),
('PG-CALIB-19', 'screw_holding_edge_n', 1210, NULL, NULL),
('PG-CALIB-19', 'thermal_conductivity_w_mk', 0.168, NULL, NULL),

-- PG-CALIB-16
('PG-CALIB-16', 'warranty_years', 25, NULL, NULL),
('PG-CALIB-16', 'money_back_multiple', NULL, NULL, '7X'),
('PG-CALIB-16', 'fully_covered_plan', NULL, true, NULL),
('PG-CALIB-16', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-CALIB-16', 'thickness_mm', 16, NULL, NULL),
('PG-CALIB-16', 'mfg_length_mm', 2440, NULL, NULL),
('PG-CALIB-16', 'mfg_width_mm', 1220, NULL, NULL),
('PG-CALIB-16', 'mfg_thickness_mm', 15.6, NULL, NULL),
('PG-CALIB-16', 'emission_standard', NULL, NULL, 'E0'),
('PG-CALIB-16', 'borer_fungus_proof', NULL, true, NULL),
('PG-CALIB-16', 'fire_retardant', NULL, true, NULL),
('PG-CALIB-16', 'fire_delay_mins', 40, NULL, NULL),
('PG-CALIB-16', 'water_resistance_grade', NULL, NULL, 'STRUCTURAL'),
('PG-CALIB-16', 'core_species', NULL, NULL, 'HARDWOOD'),
('PG-CALIB-16', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('PG-CALIB-16', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('PG-CALIB-16', 'density_g_cm3', 0.76, NULL, NULL),
('PG-CALIB-16', 'moisture_content_pct', 9.3, NULL, NULL),
('PG-CALIB-16', 'bim_ready', NULL, true, NULL),
('PG-CALIB-16', 'modulus_elasticity_mpa', 7360, NULL, NULL),
('PG-CALIB-16', 'modulus_rupture_mpa', 59.4, NULL, NULL),
('PG-CALIB-16', 'screw_holding_face_n', 1900, NULL, NULL),
('PG-CALIB-16', 'screw_holding_edge_n', 1150, NULL, NULL),
('PG-CALIB-16', 'thermal_conductivity_w_mk', 0.166, NULL, NULL),

-- PG-CALIB-12
('PG-CALIB-12', 'warranty_years', 25, NULL, NULL),
('PG-CALIB-12', 'money_back_multiple', NULL, NULL, '7X'),
('PG-CALIB-12', 'fully_covered_plan', NULL, true, NULL),
('PG-CALIB-12', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-CALIB-12', 'thickness_mm', 12, NULL, NULL),
('PG-CALIB-12', 'mfg_length_mm', 2440, NULL, NULL),
('PG-CALIB-12', 'mfg_width_mm', 1220, NULL, NULL),
('PG-CALIB-12', 'mfg_thickness_mm', 11.8, NULL, NULL),
('PG-CALIB-12', 'emission_standard', NULL, NULL, 'E0'),
('PG-CALIB-12', 'borer_fungus_proof', NULL, true, NULL),
('PG-CALIB-12', 'fire_retardant', NULL, true, NULL),
('PG-CALIB-12', 'fire_delay_mins', 35, NULL, NULL),
('PG-CALIB-12', 'water_resistance_grade', NULL, NULL, 'STRUCTURAL'),
('PG-CALIB-12', 'core_species', NULL, NULL, 'HARDWOOD'),
('PG-CALIB-12', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('PG-CALIB-12', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('PG-CALIB-12', 'density_g_cm3', 0.76, NULL, NULL),
('PG-CALIB-12', 'moisture_content_pct', 9.3, NULL, NULL),
('PG-CALIB-12', 'bim_ready', NULL, true, NULL),
('PG-CALIB-12', 'modulus_elasticity_mpa', 6950, NULL, NULL),
('PG-CALIB-12', 'modulus_rupture_mpa', 60.2, NULL, NULL),
('PG-CALIB-12', 'screw_holding_face_n', 1910, NULL, NULL),
('PG-CALIB-12', 'screw_holding_edge_n', 1090, NULL, NULL),
('PG-CALIB-12', 'thermal_conductivity_w_mk', 0.166, NULL, NULL),

-- PG-CALIB-9
('PG-CALIB-9', 'warranty_years', 25, NULL, NULL),
('PG-CALIB-9', 'money_back_multiple', NULL, NULL, '7X'),
('PG-CALIB-9', 'fully_covered_plan', NULL, false, NULL),
('PG-CALIB-9', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-CALIB-9', 'thickness_mm', 9, NULL, NULL),
('PG-CALIB-9', 'mfg_length_mm', 2440, NULL, NULL),
('PG-CALIB-9', 'mfg_width_mm', 1220, NULL, NULL),
('PG-CALIB-9', 'mfg_thickness_mm', 8.8, NULL, NULL),
('PG-CALIB-9', 'emission_standard', NULL, NULL, 'E0'),
('PG-CALIB-9', 'borer_fungus_proof', NULL, true, NULL),
('PG-CALIB-9', 'fire_retardant', NULL, true, NULL),
('PG-CALIB-9', 'fire_delay_mins', 30, NULL, NULL),
('PG-CALIB-9', 'water_resistance_grade', NULL, NULL, 'STRUCTURAL'),
('PG-CALIB-9', 'core_species', NULL, NULL, 'HARDWOOD'),
('PG-CALIB-9', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('PG-CALIB-9', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('PG-CALIB-9', 'density_g_cm3', 0.76, NULL, NULL),
('PG-CALIB-9', 'moisture_content_pct', 10.7, NULL, NULL),
('PG-CALIB-9', 'bim_ready', NULL, true, NULL),
('PG-CALIB-9', 'modulus_elasticity_mpa', 7390, NULL, NULL),
('PG-CALIB-9', 'modulus_rupture_mpa', 61.6, NULL, NULL),
('PG-CALIB-9', 'screw_holding_face_n', 1760, NULL, NULL),
('PG-CALIB-9', 'screw_holding_edge_n', 1140, NULL, NULL),
('PG-CALIB-9', 'thermal_conductivity_w_mk', 0.167, NULL, NULL),

-- PG-CALIB-6
('PG-CALIB-6', 'warranty_years', 25, NULL, NULL),
('PG-CALIB-6', 'money_back_multiple', NULL, NULL, '7X'),
('PG-CALIB-6', 'fully_covered_plan', NULL, false, NULL),
('PG-CALIB-6', 'sheet_dimension', NULL, NULL, '6X4'),
('PG-CALIB-6', 'thickness_mm', 6, NULL, NULL),
('PG-CALIB-6', 'mfg_length_mm', 1830, NULL, NULL),
('PG-CALIB-6', 'mfg_width_mm', 1220, NULL, NULL),
('PG-CALIB-6', 'mfg_thickness_mm', 5.6, NULL, NULL),
('PG-CALIB-6', 'emission_standard', NULL, NULL, 'E0'),
('PG-CALIB-6', 'borer_fungus_proof', NULL, true, NULL),
('PG-CALIB-6', 'fire_retardant', NULL, true, NULL),
('PG-CALIB-6', 'fire_delay_mins', 25, NULL, NULL),
('PG-CALIB-6', 'water_resistance_grade', NULL, NULL, 'STRUCTURAL'),
('PG-CALIB-6', 'core_species', NULL, NULL, 'HARDWOOD'),
('PG-CALIB-6', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('PG-CALIB-6', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('PG-CALIB-6', 'density_g_cm3', 0.81, NULL, NULL),
('PG-CALIB-6', 'moisture_content_pct', 11.3, NULL, NULL),
('PG-CALIB-6', 'bim_ready', NULL, false, NULL),
('PG-CALIB-6', 'modulus_elasticity_mpa', 7220, NULL, NULL),
('PG-CALIB-6', 'modulus_rupture_mpa', 58.9, NULL, NULL),
('PG-CALIB-6', 'screw_holding_face_n', 2040, NULL, NULL),
('PG-CALIB-6', 'screw_holding_edge_n', 1230, NULL, NULL),
('PG-CALIB-6', 'thermal_conductivity_w_mk', 0.17, NULL, NULL),

-- PG-BLOCK-25
('PG-BLOCK-25', 'warranty_years', 25, NULL, NULL),
('PG-BLOCK-25', 'money_back_multiple', NULL, NULL, '7X'),
('PG-BLOCK-25', 'fully_covered_plan', NULL, true, NULL),
('PG-BLOCK-25', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-BLOCK-25', 'thickness_mm', 25, NULL, NULL),
('PG-BLOCK-25', 'mfg_length_mm', 2440, NULL, NULL),
('PG-BLOCK-25', 'mfg_width_mm', 1220, NULL, NULL),
('PG-BLOCK-25', 'mfg_thickness_mm', 24.8, NULL, NULL),
('PG-BLOCK-25', 'emission_standard', NULL, NULL, 'E0'),
('PG-BLOCK-25', 'borer_fungus_proof', NULL, true, NULL),
('PG-BLOCK-25', 'fire_retardant', NULL, true, NULL),
('PG-BLOCK-25', 'fire_delay_mins', 40, NULL, NULL),
('PG-BLOCK-25', 'water_resistance_grade', NULL, NULL, 'BWP'),
('PG-BLOCK-25', 'core_species', NULL, NULL, 'GURJAN'),
('PG-BLOCK-25', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('PG-BLOCK-25', 'calibration_technology', NULL, NULL, 'FOUR_PRESS'),
('PG-BLOCK-25', 'density_g_cm3', 0.6, NULL, NULL),
('PG-BLOCK-25', 'moisture_content_pct', 8.4, NULL, NULL),
('PG-BLOCK-25', 'bim_ready', NULL, true, NULL),
('PG-BLOCK-25', 'modulus_elasticity_mpa', 3750, NULL, NULL),
('PG-BLOCK-25', 'modulus_rupture_mpa', 30.3, NULL, NULL),
('PG-BLOCK-25', 'screw_holding_face_n', 1400, NULL, NULL),
('PG-BLOCK-25', 'screw_holding_edge_n', 870, NULL, NULL),
('PG-BLOCK-25', 'thermal_conductivity_w_mk', 0.146, NULL, NULL),

-- PG-BLOCK-19
('PG-BLOCK-19', 'warranty_years', 15, NULL, NULL),
('PG-BLOCK-19', 'money_back_multiple', NULL, NULL, '2X'),
('PG-BLOCK-19', 'fully_covered_plan', NULL, false, NULL),
('PG-BLOCK-19', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-BLOCK-19', 'thickness_mm', 19, NULL, NULL),
('PG-BLOCK-19', 'mfg_length_mm', 2440, NULL, NULL),
('PG-BLOCK-19', 'mfg_width_mm', 1220, NULL, NULL),
('PG-BLOCK-19', 'mfg_thickness_mm', 18.7, NULL, NULL),
('PG-BLOCK-19', 'emission_standard', NULL, NULL, 'E0'),
('PG-BLOCK-19', 'borer_fungus_proof', NULL, true, NULL),
('PG-BLOCK-19', 'fire_retardant', NULL, false, NULL),
('PG-BLOCK-19', 'fire_delay_mins', 0, NULL, NULL),
('PG-BLOCK-19', 'water_resistance_grade', NULL, NULL, 'MR'),
('PG-BLOCK-19', 'core_species', NULL, NULL, 'HARDWOOD'),
('PG-BLOCK-19', 'resin_type', NULL, NULL, 'MUF'),
('PG-BLOCK-19', 'calibration_technology', NULL, NULL, 'FOUR_PRESS'),
('PG-BLOCK-19', 'density_g_cm3', 0.58, NULL, NULL),
('PG-BLOCK-19', 'moisture_content_pct', 8.8, NULL, NULL),
('PG-BLOCK-19', 'bim_ready', NULL, true, NULL),
('PG-BLOCK-19', 'modulus_elasticity_mpa', 3380, NULL, NULL),
('PG-BLOCK-19', 'modulus_rupture_mpa', 26.3, NULL, NULL),
('PG-BLOCK-19', 'screw_holding_face_n', 1420, NULL, NULL),
('PG-BLOCK-19', 'screw_holding_edge_n', 850, NULL, NULL),
('PG-BLOCK-19', 'thermal_conductivity_w_mk', 0.145, NULL, NULL),

-- PG-DOOR-FLUSH
('PG-DOOR-FLUSH', 'warranty_years', 15, NULL, NULL),
('PG-DOOR-FLUSH', 'money_back_multiple', NULL, NULL, '2X'),
('PG-DOOR-FLUSH', 'fully_covered_plan', NULL, false, NULL),
('PG-DOOR-FLUSH', 'sheet_dimension', NULL, NULL, '7X3'),
('PG-DOOR-FLUSH', 'thickness_mm', 35, NULL, NULL),
('PG-DOOR-FLUSH', 'mfg_length_mm', 2135, NULL, NULL),
('PG-DOOR-FLUSH', 'mfg_width_mm', 915, NULL, NULL),
('PG-DOOR-FLUSH', 'mfg_thickness_mm', 34.8, NULL, NULL),
('PG-DOOR-FLUSH', 'emission_standard', NULL, NULL, 'E0'),
('PG-DOOR-FLUSH', 'borer_fungus_proof', NULL, true, NULL),
('PG-DOOR-FLUSH', 'fire_retardant', NULL, true, NULL),
('PG-DOOR-FLUSH', 'fire_delay_mins', 30, NULL, NULL),
('PG-DOOR-FLUSH', 'water_resistance_grade', NULL, NULL, 'MR'),
('PG-DOOR-FLUSH', 'core_species', NULL, NULL, 'COMBI'),
('PG-DOOR-FLUSH', 'resin_type', NULL, NULL, 'MUF'),
('PG-DOOR-FLUSH', 'calibration_technology', NULL, NULL, 'DOUBLE_HOT_PRESS'),
('PG-DOOR-FLUSH', 'density_g_cm3', 0.56, NULL, NULL),
('PG-DOOR-FLUSH', 'moisture_content_pct', 10, NULL, NULL),
('PG-DOOR-FLUSH', 'bim_ready', NULL, true, NULL),
('PG-DOOR-FLUSH', 'modulus_elasticity_mpa', 3270, NULL, NULL),
('PG-DOOR-FLUSH', 'modulus_rupture_mpa', 24.7, NULL, NULL),
('PG-DOOR-FLUSH', 'screw_holding_face_n', 1300, NULL, NULL),
('PG-DOOR-FLUSH', 'screw_holding_edge_n', 810, NULL, NULL),
('PG-DOOR-FLUSH', 'thermal_conductivity_w_mk', 0.145, NULL, NULL),

-- PG-VENEER-TEAK
('PG-VENEER-TEAK', 'warranty_years', 10, NULL, NULL),
('PG-VENEER-TEAK', 'money_back_multiple', NULL, NULL, 'STANDARD'),
('PG-VENEER-TEAK', 'fully_covered_plan', NULL, false, NULL),
('PG-VENEER-TEAK', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-VENEER-TEAK', 'thickness_mm', 19, NULL, NULL),
('PG-VENEER-TEAK', 'mfg_length_mm', 2440, NULL, NULL),
('PG-VENEER-TEAK', 'mfg_width_mm', 1220, NULL, NULL),
('PG-VENEER-TEAK', 'mfg_thickness_mm', 18.8, NULL, NULL),
('PG-VENEER-TEAK', 'emission_standard', NULL, NULL, 'E0'),
('PG-VENEER-TEAK', 'borer_fungus_proof', NULL, true, NULL),
('PG-VENEER-TEAK', 'fire_retardant', NULL, false, NULL),
('PG-VENEER-TEAK', 'fire_delay_mins', 0, NULL, NULL),
('PG-VENEER-TEAK', 'water_resistance_grade', NULL, NULL, 'MR'),
('PG-VENEER-TEAK', 'core_species', NULL, NULL, 'COMBI'),
('PG-VENEER-TEAK', 'resin_type', NULL, NULL, 'MUF'),
('PG-VENEER-TEAK', 'calibration_technology', NULL, NULL, 'DOUBLE_HOT_PRESS'),
('PG-VENEER-TEAK', 'density_g_cm3', 0.68, NULL, NULL),
('PG-VENEER-TEAK', 'moisture_content_pct', 9.6, NULL, NULL),
('PG-VENEER-TEAK', 'bim_ready', NULL, false, NULL),
('PG-VENEER-TEAK', 'modulus_elasticity_mpa', 5070, NULL, NULL),
('PG-VENEER-TEAK', 'modulus_rupture_mpa', 39.8, NULL, NULL),
('PG-VENEER-TEAK', 'screw_holding_face_n', 1570, NULL, NULL),
('PG-VENEER-TEAK', 'screw_holding_edge_n', 1010, NULL, NULL),
('PG-VENEER-TEAK', 'thermal_conductivity_w_mk', 0.156, NULL, NULL),

-- PG-VENEER-WALNUT
('PG-VENEER-WALNUT', 'warranty_years', 10, NULL, NULL),
('PG-VENEER-WALNUT', 'money_back_multiple', NULL, NULL, 'STANDARD'),
('PG-VENEER-WALNUT', 'fully_covered_plan', NULL, false, NULL),
('PG-VENEER-WALNUT', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-VENEER-WALNUT', 'thickness_mm', 19, NULL, NULL),
('PG-VENEER-WALNUT', 'mfg_length_mm', 2440, NULL, NULL),
('PG-VENEER-WALNUT', 'mfg_width_mm', 1220, NULL, NULL),
('PG-VENEER-WALNUT', 'mfg_thickness_mm', 18.6, NULL, NULL),
('PG-VENEER-WALNUT', 'emission_standard', NULL, NULL, 'E0'),
('PG-VENEER-WALNUT', 'borer_fungus_proof', NULL, true, NULL),
('PG-VENEER-WALNUT', 'fire_retardant', NULL, false, NULL),
('PG-VENEER-WALNUT', 'fire_delay_mins', 0, NULL, NULL),
('PG-VENEER-WALNUT', 'water_resistance_grade', NULL, NULL, 'MR'),
('PG-VENEER-WALNUT', 'core_species', NULL, NULL, 'COMBI'),
('PG-VENEER-WALNUT', 'resin_type', NULL, NULL, 'MUF'),
('PG-VENEER-WALNUT', 'calibration_technology', NULL, NULL, 'DOUBLE_HOT_PRESS'),
('PG-VENEER-WALNUT', 'density_g_cm3', 0.7, NULL, NULL),
('PG-VENEER-WALNUT', 'moisture_content_pct', 8.9, NULL, NULL),
('PG-VENEER-WALNUT', 'bim_ready', NULL, true, NULL),
('PG-VENEER-WALNUT', 'modulus_elasticity_mpa', 5080, NULL, NULL),
('PG-VENEER-WALNUT', 'modulus_rupture_mpa', 38.7, NULL, NULL),
('PG-VENEER-WALNUT', 'screw_holding_face_n', 1640, NULL, NULL),
('PG-VENEER-WALNUT', 'screw_holding_edge_n', 1020, NULL, NULL),
('PG-VENEER-WALNUT', 'thermal_conductivity_w_mk', 0.164, NULL, NULL),

-- PG-VENEER-OAK
('PG-VENEER-OAK', 'warranty_years', 10, NULL, NULL),
('PG-VENEER-OAK', 'money_back_multiple', NULL, NULL, 'STANDARD'),
('PG-VENEER-OAK', 'fully_covered_plan', NULL, false, NULL),
('PG-VENEER-OAK', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-VENEER-OAK', 'thickness_mm', 19, NULL, NULL),
('PG-VENEER-OAK', 'mfg_length_mm', 2440, NULL, NULL),
('PG-VENEER-OAK', 'mfg_width_mm', 1220, NULL, NULL),
('PG-VENEER-OAK', 'mfg_thickness_mm', 18.8, NULL, NULL),
('PG-VENEER-OAK', 'emission_standard', NULL, NULL, 'E0'),
('PG-VENEER-OAK', 'borer_fungus_proof', NULL, true, NULL),
('PG-VENEER-OAK', 'fire_retardant', NULL, false, NULL),
('PG-VENEER-OAK', 'fire_delay_mins', 0, NULL, NULL),
('PG-VENEER-OAK', 'water_resistance_grade', NULL, NULL, 'MR'),
('PG-VENEER-OAK', 'core_species', NULL, NULL, 'COMBI'),
('PG-VENEER-OAK', 'resin_type', NULL, NULL, 'MUF'),
('PG-VENEER-OAK', 'calibration_technology', NULL, NULL, 'DOUBLE_HOT_PRESS'),
('PG-VENEER-OAK', 'density_g_cm3', 0.7, NULL, NULL),
('PG-VENEER-OAK', 'moisture_content_pct', 11, NULL, NULL),
('PG-VENEER-OAK', 'bim_ready', NULL, false, NULL),
('PG-VENEER-OAK', 'modulus_elasticity_mpa', 5010, NULL, NULL),
('PG-VENEER-OAK', 'modulus_rupture_mpa', 42, NULL, NULL),
('PG-VENEER-OAK', 'screw_holding_face_n', 1740, NULL, NULL),
('PG-VENEER-OAK', 'screw_holding_edge_n', 1100, NULL, NULL),
('PG-VENEER-OAK', 'thermal_conductivity_w_mk', 0.164, NULL, NULL),

-- PG-CLUB-700
('PG-CLUB-700', 'warranty_years', 25, NULL, NULL),
('PG-CLUB-700', 'money_back_multiple', NULL, NULL, '7X'),
('PG-CLUB-700', 'fully_covered_plan', NULL, true, NULL),
('PG-CLUB-700', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-CLUB-700', 'thickness_mm', 19, NULL, NULL),
('PG-CLUB-700', 'mfg_length_mm', 2440, NULL, NULL),
('PG-CLUB-700', 'mfg_width_mm', 1220, NULL, NULL),
('PG-CLUB-700', 'mfg_thickness_mm', 18.7, NULL, NULL),
('PG-CLUB-700', 'emission_standard', NULL, NULL, 'E0'),
('PG-CLUB-700', 'borer_fungus_proof', NULL, true, NULL),
('PG-CLUB-700', 'fire_retardant', NULL, true, NULL),
('PG-CLUB-700', 'fire_delay_mins', 50, NULL, NULL),
('PG-CLUB-700', 'water_resistance_grade', NULL, NULL, 'BWP'),
('PG-CLUB-700', 'core_species', NULL, NULL, 'GURJAN'),
('PG-CLUB-700', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('PG-CLUB-700', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('PG-CLUB-700', 'density_g_cm3', 0.78, NULL, NULL),
('PG-CLUB-700', 'moisture_content_pct', 8.6, NULL, NULL),
('PG-CLUB-700', 'bim_ready', NULL, true, NULL),
('PG-CLUB-700', 'modulus_elasticity_mpa', 6840, NULL, NULL),
('PG-CLUB-700', 'modulus_rupture_mpa', 55.4, NULL, NULL),
('PG-CLUB-700', 'screw_holding_face_n', 1790, NULL, NULL),
('PG-CLUB-700', 'screw_holding_edge_n', 1160, NULL, NULL),
('PG-CLUB-700', 'thermal_conductivity_w_mk', 0.173, NULL, NULL),

-- PG-PLAT-BWP
('PG-PLAT-BWP', 'warranty_years', 25, NULL, NULL),
('PG-PLAT-BWP', 'money_back_multiple', NULL, NULL, '7X'),
('PG-PLAT-BWP', 'fully_covered_plan', NULL, true, NULL),
('PG-PLAT-BWP', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-PLAT-BWP', 'thickness_mm', 19, NULL, NULL),
('PG-PLAT-BWP', 'mfg_length_mm', 2440, NULL, NULL),
('PG-PLAT-BWP', 'mfg_width_mm', 1220, NULL, NULL),
('PG-PLAT-BWP', 'mfg_thickness_mm', 18.7, NULL, NULL),
('PG-PLAT-BWP', 'emission_standard', NULL, NULL, 'E0'),
('PG-PLAT-BWP', 'borer_fungus_proof', NULL, true, NULL),
('PG-PLAT-BWP', 'fire_retardant', NULL, true, NULL),
('PG-PLAT-BWP', 'fire_delay_mins', 60, NULL, NULL),
('PG-PLAT-BWP', 'water_resistance_grade', NULL, NULL, 'BWP'),
('PG-PLAT-BWP', 'core_species', NULL, NULL, 'HARDWOOD'),
('PG-PLAT-BWP', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('PG-PLAT-BWP', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('PG-PLAT-BWP', 'density_g_cm3', 0.77, NULL, NULL),
('PG-PLAT-BWP', 'moisture_content_pct', 9.8, NULL, NULL),
('PG-PLAT-BWP', 'bim_ready', NULL, true, NULL),
('PG-PLAT-BWP', 'modulus_elasticity_mpa', 7150, NULL, NULL),
('PG-PLAT-BWP', 'modulus_rupture_mpa', 56.5, NULL, NULL),
('PG-PLAT-BWP', 'screw_holding_face_n', 1860, NULL, NULL),
('PG-PLAT-BWP', 'screw_holding_edge_n', 1180, NULL, NULL),
('PG-PLAT-BWP', 'thermal_conductivity_w_mk', 0.170, NULL, NULL),

-- PG-710-MAR
('PG-710-MAR', 'warranty_years', 25, NULL, NULL),
('PG-710-MAR', 'money_back_multiple', NULL, NULL, '7X'),
('PG-710-MAR', 'fully_covered_plan', NULL, true, NULL),
('PG-710-MAR', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-710-MAR', 'thickness_mm', 19, NULL, NULL),
('PG-710-MAR', 'mfg_length_mm', 2440, NULL, NULL),
('PG-710-MAR', 'mfg_width_mm', 1220, NULL, NULL),
('PG-710-MAR', 'mfg_thickness_mm', 18.7, NULL, NULL),
('PG-710-MAR', 'emission_standard', NULL, NULL, 'E0'),
('PG-710-MAR', 'borer_fungus_proof', NULL, true, NULL),
('PG-710-MAR', 'fire_retardant', NULL, true, NULL),
('PG-710-MAR', 'fire_delay_mins', 30, NULL, NULL),
('PG-710-MAR', 'water_resistance_grade', NULL, NULL, 'BWP'),
('PG-710-MAR', 'core_species', NULL, NULL, 'GURJAN'),
('PG-710-MAR', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('PG-710-MAR', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('PG-710-MAR', 'density_g_cm3', 0.78, NULL, NULL),
('PG-710-MAR', 'moisture_content_pct', 10.2, NULL, NULL),
('PG-710-MAR', 'bim_ready', NULL, true, NULL),
('PG-710-MAR', 'modulus_elasticity_mpa', 6900, NULL, NULL),
('PG-710-MAR', 'modulus_rupture_mpa', 56.0, NULL, NULL),
('PG-710-MAR', 'screw_holding_face_n', 1810, NULL, NULL),
('PG-710-MAR', 'screw_holding_edge_n', 1140, NULL, NULL),
('PG-710-MAR', 'thermal_conductivity_w_mk', 0.168, NULL, NULL),

-- PG-GOLD-BWP
('PG-GOLD-BWP', 'warranty_years', 20, NULL, NULL),
('PG-GOLD-BWP', 'money_back_multiple', NULL, NULL, '2X'),
('PG-GOLD-BWP', 'fully_covered_plan', NULL, true, NULL),
('PG-GOLD-BWP', 'sheet_dimension', NULL, NULL, '8X4'),
('PG-GOLD-BWP', 'thickness_mm', 19, NULL, NULL),
('PG-GOLD-BWP', 'mfg_length_mm', 2440, NULL, NULL),
('PG-GOLD-BWP', 'mfg_width_mm', 1220, NULL, NULL),
('PG-GOLD-BWP', 'mfg_thickness_mm', 18.7, NULL, NULL),
('PG-GOLD-BWP', 'emission_standard', NULL, NULL, 'E0'),
('PG-GOLD-BWP', 'borer_fungus_proof', NULL, true, NULL),
('PG-GOLD-BWP', 'fire_retardant', NULL, true, NULL),
('PG-GOLD-BWP', 'fire_delay_mins', 20, NULL, NULL),
('PG-GOLD-BWP', 'water_resistance_grade', NULL, NULL, 'BWP'),
('PG-GOLD-BWP', 'core_species', NULL, NULL, 'HARDWOOD'),
('PG-GOLD-BWP', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('PG-GOLD-BWP', 'calibration_technology', NULL, NULL, 'FOUR_PRESS'),
('PG-GOLD-BWP', 'density_g_cm3', 0.75, NULL, NULL),
('PG-GOLD-BWP', 'moisture_content_pct', 9.2, NULL, NULL),
('PG-GOLD-BWP', 'bim_ready', NULL, true, NULL),
('PG-GOLD-BWP', 'modulus_elasticity_mpa', 6500, NULL, NULL),
('PG-GOLD-BWP', 'modulus_rupture_mpa', 50.0, NULL, NULL),
('PG-GOLD-BWP', 'screw_holding_face_n', 1750, NULL, NULL),
('PG-GOLD-BWP', 'screw_holding_edge_n', 1100, NULL, NULL),
('PG-GOLD-BWP', 'thermal_conductivity_w_mk', 0.162, NULL, NULL);

-- Clean existing values for these attributes across the products being updated
DELETE FROM catalog.attribute_value av
USING catalog.product p, catalog.attribute a, temp_product_attribute_values t
WHERE av.product_id = p.id AND av.attribute_id = a.id
  AND p.product_code = t.p_code AND a.code = t.a_code;

-- Insert new authoritative values
INSERT INTO catalog.attribute_value
  (product_id, attribute_id, value_number, value_boolean, value_option_id, data_state, source)
SELECT
  p.id,
  a.id,
  t.val_num,
  t.val_bool,
  ao.id,
  'approved',
  'manual'
FROM temp_product_attribute_values t
JOIN catalog.product p ON p.product_code = t.p_code
JOIN catalog.attribute a ON a.code = t.a_code
LEFT JOIN catalog.attribute_option ao ON ao.attribute_id = a.id AND ao.code = t.opt_code;

DROP TABLE temp_product_attribute_values;
