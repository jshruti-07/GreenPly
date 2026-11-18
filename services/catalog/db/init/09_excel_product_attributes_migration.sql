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
-- GP-CALIB-25
('GP-CALIB-25', 'warranty_years', 25, NULL, NULL),
('GP-CALIB-25', 'money_back_multiple', NULL, NULL, '7X'),
('GP-CALIB-25', 'fully_covered_plan', NULL, true, NULL),
('GP-CALIB-25', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-CALIB-25', 'thickness_mm', 25, NULL, NULL),
('GP-CALIB-25', 'mfg_length_mm', 2440, NULL, NULL),
('GP-CALIB-25', 'mfg_width_mm', 1220, NULL, NULL),
('GP-CALIB-25', 'mfg_thickness_mm', 24.8, NULL, NULL),
('GP-CALIB-25', 'emission_standard', NULL, NULL, 'E0'),
('GP-CALIB-25', 'borer_fungus_proof', NULL, true, NULL),
('GP-CALIB-25', 'fire_retardant', NULL, true, NULL),
('GP-CALIB-25', 'fire_delay_mins', 45, NULL, NULL),
('GP-CALIB-25', 'water_resistance_grade', NULL, NULL, 'STRUCTURAL'),
('GP-CALIB-25', 'core_species', NULL, NULL, 'HARDWOOD'),
('GP-CALIB-25', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('GP-CALIB-25', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('GP-CALIB-25', 'density_g_cm3', 0.79, NULL, NULL),
('GP-CALIB-25', 'moisture_content_pct', 8.1, NULL, NULL),
('GP-CALIB-25', 'bim_ready', NULL, true, NULL),
('GP-CALIB-25', 'modulus_elasticity_mpa', 7040, NULL, NULL),
('GP-CALIB-25', 'modulus_rupture_mpa', 59.4, NULL, NULL),
('GP-CALIB-25', 'screw_holding_face_n', 1930, NULL, NULL),
('GP-CALIB-25', 'screw_holding_edge_n', 1230, NULL, NULL),
('GP-CALIB-25', 'thermal_conductivity_w_mk', 0.165, NULL, NULL),

-- GP-JANSATHI
('GP-JANSATHI', 'warranty_years', 10, NULL, NULL),
('GP-JANSATHI', 'money_back_multiple', NULL, NULL, 'STANDARD'),
('GP-JANSATHI', 'fully_covered_plan', NULL, false, NULL),
('GP-JANSATHI', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-JANSATHI', 'thickness_mm', 12, NULL, NULL),
('GP-JANSATHI', 'mfg_length_mm', 2440, NULL, NULL),
('GP-JANSATHI', 'mfg_width_mm', 1220, NULL, NULL),
('GP-JANSATHI', 'mfg_thickness_mm', 11.8, NULL, NULL),
('GP-JANSATHI', 'emission_standard', NULL, NULL, 'E1'),
('GP-JANSATHI', 'borer_fungus_proof', NULL, true, NULL),
('GP-JANSATHI', 'fire_retardant', NULL, false, NULL),
('GP-JANSATHI', 'fire_delay_mins', 0, NULL, NULL),
('GP-JANSATHI', 'water_resistance_grade', NULL, NULL, 'MR'),
('GP-JANSATHI', 'core_species', NULL, NULL, 'POPLAR'),
('GP-JANSATHI', 'resin_type', NULL, NULL, 'UF'),
('GP-JANSATHI', 'calibration_technology', NULL, NULL, 'DOUBLE_HOT_PRESS'),
('GP-JANSATHI', 'density_g_cm3', 0.62, NULL, NULL),
('GP-JANSATHI', 'moisture_content_pct', 8.1, NULL, NULL),
('GP-JANSATHI', 'bim_ready', NULL, true, NULL),
('GP-JANSATHI', 'modulus_elasticity_mpa', 4600, NULL, NULL),
('GP-JANSATHI', 'modulus_rupture_mpa', 32.4, NULL, NULL),
('GP-JANSATHI', 'screw_holding_face_n', 1440, NULL, NULL),
('GP-JANSATHI', 'screw_holding_edge_n', 940, NULL, NULL),
('GP-JANSATHI', 'thermal_conductivity_w_mk', 0.152, NULL, NULL),

-- GP-SHUTTER-BWP
('GP-SHUTTER-BWP', 'warranty_years', 15, NULL, NULL),
('GP-SHUTTER-BWP', 'money_back_multiple', NULL, NULL, '2X'),
('GP-SHUTTER-BWP', 'fully_covered_plan', NULL, false, NULL),
('GP-SHUTTER-BWP', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-SHUTTER-BWP', 'thickness_mm', 18, NULL, NULL),
('GP-SHUTTER-BWP', 'mfg_length_mm', 2440, NULL, NULL),
('GP-SHUTTER-BWP', 'mfg_width_mm', 1220, NULL, NULL),
('GP-SHUTTER-BWP', 'mfg_thickness_mm', 17.8, NULL, NULL),
('GP-SHUTTER-BWP', 'emission_standard', NULL, NULL, 'E1'),
('GP-SHUTTER-BWP', 'borer_fungus_proof', NULL, true, NULL),
('GP-SHUTTER-BWP', 'fire_retardant', NULL, false, NULL),
('GP-SHUTTER-BWP', 'fire_delay_mins', 0, NULL, NULL),
('GP-SHUTTER-BWP', 'water_resistance_grade', NULL, NULL, 'BWP'),
('GP-SHUTTER-BWP', 'core_species', NULL, NULL, 'GURJAN'),
('GP-SHUTTER-BWP', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('GP-SHUTTER-BWP', 'calibration_technology', NULL, NULL, 'FOUR_PRESS'),
('GP-SHUTTER-BWP', 'density_g_cm3', 0.73, NULL, NULL),
('GP-SHUTTER-BWP', 'moisture_content_pct', 10.1, NULL, NULL),
('GP-SHUTTER-BWP', 'bim_ready', NULL, true, NULL),
('GP-SHUTTER-BWP', 'modulus_elasticity_mpa', 7010, NULL, NULL),
('GP-SHUTTER-BWP', 'modulus_rupture_mpa', 54.1, NULL, NULL),
('GP-SHUTTER-BWP', 'screw_holding_face_n', 1690, NULL, NULL),
('GP-SHUTTER-BWP', 'screw_holding_edge_n', 1150, NULL, NULL),
('GP-SHUTTER-BWP', 'thermal_conductivity_w_mk', 0.161, NULL, NULL),

-- GP-BHAROSA
('GP-BHAROSA', 'warranty_years', 10, NULL, NULL),
('GP-BHAROSA', 'money_back_multiple', NULL, NULL, 'STANDARD'),
('GP-BHAROSA', 'fully_covered_plan', NULL, false, NULL),
('GP-BHAROSA', 'sheet_dimension', NULL, NULL, '7X3'),
('GP-BHAROSA', 'thickness_mm', 12, NULL, NULL),
('GP-BHAROSA', 'mfg_length_mm', 2135, NULL, NULL),
('GP-BHAROSA', 'mfg_width_mm', 915, NULL, NULL),
('GP-BHAROSA', 'mfg_thickness_mm', 11.6, NULL, NULL),
('GP-BHAROSA', 'emission_standard', NULL, NULL, 'E1'),
('GP-BHAROSA', 'borer_fungus_proof', NULL, true, NULL),
('GP-BHAROSA', 'fire_retardant', NULL, false, NULL),
('GP-BHAROSA', 'fire_delay_mins', 0, NULL, NULL),
('GP-BHAROSA', 'water_resistance_grade', NULL, NULL, 'MR'),
('GP-BHAROSA', 'core_species', NULL, NULL, 'COMBI'),
('GP-BHAROSA', 'resin_type', NULL, NULL, 'UF'),
('GP-BHAROSA', 'calibration_technology', NULL, NULL, 'DOUBLE_HOT_PRESS'),
('GP-BHAROSA', 'density_g_cm3', 0.62, NULL, NULL),
('GP-BHAROSA', 'moisture_content_pct', 8.3, NULL, NULL),
('GP-BHAROSA', 'bim_ready', NULL, true, NULL),
('GP-BHAROSA', 'modulus_elasticity_mpa', 4810, NULL, NULL),
('GP-BHAROSA', 'modulus_rupture_mpa', 34.4, NULL, NULL),
('GP-BHAROSA', 'screw_holding_face_n', 1480, NULL, NULL),
('GP-BHAROSA', 'screw_holding_edge_n', 900, NULL, NULL),
('GP-BHAROSA', 'thermal_conductivity_w_mk', 0.156, NULL, NULL),

-- GP-OPTIMA-G
('GP-OPTIMA-G', 'warranty_years', 15, NULL, NULL),
('GP-OPTIMA-G', 'money_back_multiple', NULL, NULL, '2X'),
('GP-OPTIMA-G', 'fully_covered_plan', NULL, false, NULL),
('GP-OPTIMA-G', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-OPTIMA-G', 'thickness_mm', 19, NULL, NULL),
('GP-OPTIMA-G', 'mfg_length_mm', 2440, NULL, NULL),
('GP-OPTIMA-G', 'mfg_width_mm', 1220, NULL, NULL),
('GP-OPTIMA-G', 'mfg_thickness_mm', 18.6, NULL, NULL),
('GP-OPTIMA-G', 'emission_standard', NULL, NULL, 'E0'),
('GP-OPTIMA-G', 'borer_fungus_proof', NULL, true, NULL),
('GP-OPTIMA-G', 'fire_retardant', NULL, false, NULL),
('GP-OPTIMA-G', 'fire_delay_mins', 0, NULL, NULL),
('GP-OPTIMA-G', 'water_resistance_grade', NULL, NULL, 'MR'),
('GP-OPTIMA-G', 'core_species', NULL, NULL, 'EUCALYPTUS'),
('GP-OPTIMA-G', 'resin_type', NULL, NULL, 'MUF'),
('GP-OPTIMA-G', 'calibration_technology', NULL, NULL, 'FOUR_PRESS'),
('GP-OPTIMA-G', 'density_g_cm3', 0.64, NULL, NULL),
('GP-OPTIMA-G', 'moisture_content_pct', 9, NULL, NULL),
('GP-OPTIMA-G', 'bim_ready', NULL, false, NULL),
('GP-OPTIMA-G', 'modulus_elasticity_mpa', 5570, NULL, NULL),
('GP-OPTIMA-G', 'modulus_rupture_mpa', 39.4, NULL, NULL),
('GP-OPTIMA-G', 'screw_holding_face_n', 1490, NULL, NULL),
('GP-OPTIMA-G', 'screw_holding_edge_n', 920, NULL, NULL),
('GP-OPTIMA-G', 'thermal_conductivity_w_mk', 0.155, NULL, NULL),

-- GP-ECO-MR
('GP-ECO-MR', 'warranty_years', 15, NULL, NULL),
('GP-ECO-MR', 'money_back_multiple', NULL, NULL, '2X'),
('GP-ECO-MR', 'fully_covered_plan', NULL, true, NULL),
('GP-ECO-MR', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-ECO-MR', 'thickness_mm', 19, NULL, NULL),
('GP-ECO-MR', 'mfg_length_mm', 2440, NULL, NULL),
('GP-ECO-MR', 'mfg_width_mm', 1220, NULL, NULL),
('GP-ECO-MR', 'mfg_thickness_mm', 18.8, NULL, NULL),
('GP-ECO-MR', 'emission_standard', NULL, NULL, 'E0'),
('GP-ECO-MR', 'borer_fungus_proof', NULL, true, NULL),
('GP-ECO-MR', 'fire_retardant', NULL, false, NULL),
('GP-ECO-MR', 'fire_delay_mins', 0, NULL, NULL),
('GP-ECO-MR', 'water_resistance_grade', NULL, NULL, 'MR'),
('GP-ECO-MR', 'core_species', NULL, NULL, 'EUCALYPTUS'),
('GP-ECO-MR', 'resin_type', NULL, NULL, 'MUF'),
('GP-ECO-MR', 'calibration_technology', NULL, NULL, 'FOUR_PRESS'),
('GP-ECO-MR', 'density_g_cm3', 0.66, NULL, NULL),
('GP-ECO-MR', 'moisture_content_pct', 11.4, NULL, NULL),
('GP-ECO-MR', 'bim_ready', NULL, true, NULL),
('GP-ECO-MR', 'modulus_elasticity_mpa', 5250, NULL, NULL),
('GP-ECO-MR', 'modulus_rupture_mpa', 38.8, NULL, NULL),
('GP-ECO-MR', 'screw_holding_face_n', 1640, NULL, NULL),
('GP-ECO-MR', 'screw_holding_edge_n', 960, NULL, NULL),
('GP-ECO-MR', 'thermal_conductivity_w_mk', 0.155, NULL, NULL),

-- GP-ECO-710
('GP-ECO-710', 'warranty_years', 25, NULL, NULL),
('GP-ECO-710', 'money_back_multiple', NULL, NULL, '7X'),
('GP-ECO-710', 'fully_covered_plan', NULL, true, NULL),
('GP-ECO-710', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-ECO-710', 'thickness_mm', 19, NULL, NULL),
('GP-ECO-710', 'mfg_length_mm', 2440, NULL, NULL),
('GP-ECO-710', 'mfg_width_mm', 1220, NULL, NULL),
('GP-ECO-710', 'mfg_thickness_mm', 18.6, NULL, NULL),
('GP-ECO-710', 'emission_standard', NULL, NULL, 'E0'),
('GP-ECO-710', 'borer_fungus_proof', NULL, true, NULL),
('GP-ECO-710', 'fire_retardant', NULL, true, NULL),
('GP-ECO-710', 'fire_delay_mins', 30, NULL, NULL),
('GP-ECO-710', 'water_resistance_grade', NULL, NULL, 'BWP'),
('GP-ECO-710', 'core_species', NULL, NULL, 'GURJAN'),
('GP-ECO-710', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('GP-ECO-710', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('GP-ECO-710', 'density_g_cm3', 0.77, NULL, NULL),
('GP-ECO-710', 'moisture_content_pct', 10.5, NULL, NULL),
('GP-ECO-710', 'bim_ready', NULL, false, NULL),
('GP-ECO-710', 'modulus_elasticity_mpa', 6760, NULL, NULL),
('GP-ECO-710', 'modulus_rupture_mpa', 57.8, NULL, NULL),
('GP-ECO-710', 'screw_holding_face_n', 1850, NULL, NULL),
('GP-ECO-710', 'screw_holding_edge_n', 1130, NULL, NULL),
('GP-ECO-710', 'thermal_conductivity_w_mk', 0.167, NULL, NULL),

-- GP-ECO-PLAT
('GP-ECO-PLAT', 'warranty_years', 25, NULL, NULL),
('GP-ECO-PLAT', 'money_back_multiple', NULL, NULL, '7X'),
('GP-ECO-PLAT', 'fully_covered_plan', NULL, true, NULL),
('GP-ECO-PLAT', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-ECO-PLAT', 'thickness_mm', 19, NULL, NULL),
('GP-ECO-PLAT', 'mfg_length_mm', 2440, NULL, NULL),
('GP-ECO-PLAT', 'mfg_width_mm', 1220, NULL, NULL),
('GP-ECO-PLAT', 'mfg_thickness_mm', 18.6, NULL, NULL),
('GP-ECO-PLAT', 'emission_standard', NULL, NULL, 'E0'),
('GP-ECO-PLAT', 'borer_fungus_proof', NULL, true, NULL),
('GP-ECO-PLAT', 'fire_retardant', NULL, true, NULL),
('GP-ECO-PLAT', 'fire_delay_mins', 60, NULL, NULL),
('GP-ECO-PLAT', 'water_resistance_grade', NULL, NULL, 'BWP'),
('GP-ECO-PLAT', 'core_species', NULL, NULL, 'HARDWOOD'),
('GP-ECO-PLAT', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('GP-ECO-PLAT', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('GP-ECO-PLAT', 'density_g_cm3', 0.76, NULL, NULL),
('GP-ECO-PLAT', 'moisture_content_pct', 11.2, NULL, NULL),
('GP-ECO-PLAT', 'bim_ready', NULL, false, NULL),
('GP-ECO-PLAT', 'modulus_elasticity_mpa', 7230, NULL, NULL),
('GP-ECO-PLAT', 'modulus_rupture_mpa', 58.9, NULL, NULL),
('GP-ECO-PLAT', 'screw_holding_face_n', 1870, NULL, NULL),
('GP-ECO-PLAT', 'screw_holding_edge_n', 1090, NULL, NULL),
('GP-ECO-PLAT', 'thermal_conductivity_w_mk', 0.169, NULL, NULL),

-- GP-FLEX-PLY
('GP-FLEX-PLY', 'warranty_years', 15, NULL, NULL),
('GP-FLEX-PLY', 'money_back_multiple', NULL, NULL, '2X'),
('GP-FLEX-PLY', 'fully_covered_plan', NULL, false, NULL),
('GP-FLEX-PLY', 'sheet_dimension', NULL, NULL, '6X3'),
('GP-FLEX-PLY', 'thickness_mm', 6, NULL, NULL),
('GP-FLEX-PLY', 'mfg_length_mm', 1830, NULL, NULL),
('GP-FLEX-PLY', 'mfg_width_mm', 915, NULL, NULL),
('GP-FLEX-PLY', 'mfg_thickness_mm', 5.8, NULL, NULL),
('GP-FLEX-PLY', 'emission_standard', NULL, NULL, 'E0'),
('GP-FLEX-PLY', 'borer_fungus_proof', NULL, true, NULL),
('GP-FLEX-PLY', 'fire_retardant', NULL, false, NULL),
('GP-FLEX-PLY', 'fire_delay_mins', 0, NULL, NULL),
('GP-FLEX-PLY', 'water_resistance_grade', NULL, NULL, 'BWR'),
('GP-FLEX-PLY', 'core_species', NULL, NULL, 'POPLAR'),
('GP-FLEX-PLY', 'resin_type', NULL, NULL, 'MUF'),
('GP-FLEX-PLY', 'calibration_technology', NULL, NULL, 'FOUR_PRESS'),
('GP-FLEX-PLY', 'density_g_cm3', 0.71, NULL, NULL),
('GP-FLEX-PLY', 'moisture_content_pct', 9.4, NULL, NULL),
('GP-FLEX-PLY', 'bim_ready', NULL, true, NULL),
('GP-FLEX-PLY', 'modulus_elasticity_mpa', 5990, NULL, NULL),
('GP-FLEX-PLY', 'modulus_rupture_mpa', 44.3, NULL, NULL),
('GP-FLEX-PLY', 'screw_holding_face_n', 1740, NULL, NULL),
('GP-FLEX-PLY', 'screw_holding_edge_n', 1030, NULL, NULL),
('GP-FLEX-PLY', 'thermal_conductivity_w_mk', 0.161, NULL, NULL),

-- GP-303-MR
('GP-303-MR', 'warranty_years', 10, NULL, NULL),
('GP-303-MR', 'money_back_multiple', NULL, NULL, 'STANDARD'),
('GP-303-MR', 'fully_covered_plan', NULL, false, NULL),
('GP-303-MR', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-303-MR', 'thickness_mm', 19, NULL, NULL),
('GP-303-MR', 'mfg_length_mm', 2440, NULL, NULL),
('GP-303-MR', 'mfg_width_mm', 1220, NULL, NULL),
('GP-303-MR', 'mfg_thickness_mm', 18.7, NULL, NULL),
('GP-303-MR', 'emission_standard', NULL, NULL, 'E1'),
('GP-303-MR', 'borer_fungus_proof', NULL, true, NULL),
('GP-303-MR', 'fire_retardant', NULL, false, NULL),
('GP-303-MR', 'fire_delay_mins', 0, NULL, NULL),
('GP-303-MR', 'water_resistance_grade', NULL, NULL, 'MR'),
('GP-303-MR', 'core_species', NULL, NULL, 'COMBI'),
('GP-303-MR', 'resin_type', NULL, NULL, 'UF'),
('GP-303-MR', 'calibration_technology', NULL, NULL, 'DOUBLE_HOT_PRESS'),
('GP-303-MR', 'density_g_cm3', 0.68, NULL, NULL),
('GP-303-MR', 'moisture_content_pct', 10.2, NULL, NULL),
('GP-303-MR', 'bim_ready', NULL, true, NULL),
('GP-303-MR', 'modulus_elasticity_mpa', 5050, NULL, NULL),
('GP-303-MR', 'modulus_rupture_mpa', 38.9, NULL, NULL),
('GP-303-MR', 'screw_holding_face_n', 1640, NULL, NULL),
('GP-303-MR', 'screw_holding_edge_n', 1050, NULL, NULL),
('GP-303-MR', 'thermal_conductivity_w_mk', 0.157, NULL, NULL),

-- GP-ECO-TMR
('GP-ECO-TMR', 'warranty_years', 10, NULL, NULL),
('GP-ECO-TMR', 'money_back_multiple', NULL, NULL, 'STANDARD'),
('GP-ECO-TMR', 'fully_covered_plan', NULL, false, NULL),
('GP-ECO-TMR', 'sheet_dimension', NULL, NULL, '7X4'),
('GP-ECO-TMR', 'thickness_mm', 12, NULL, NULL),
('GP-ECO-TMR', 'mfg_length_mm', 2135, NULL, NULL),
('GP-ECO-TMR', 'mfg_width_mm', 1220, NULL, NULL),
('GP-ECO-TMR', 'mfg_thickness_mm', 11.6, NULL, NULL),
('GP-ECO-TMR', 'emission_standard', NULL, NULL, 'E1'),
('GP-ECO-TMR', 'borer_fungus_proof', NULL, true, NULL),
('GP-ECO-TMR', 'fire_retardant', NULL, false, NULL),
('GP-ECO-TMR', 'fire_delay_mins', 0, NULL, NULL),
('GP-ECO-TMR', 'water_resistance_grade', NULL, NULL, 'MR'),
('GP-ECO-TMR', 'core_species', NULL, NULL, 'POPLAR'),
('GP-ECO-TMR', 'resin_type', NULL, NULL, 'UF'),
('GP-ECO-TMR', 'calibration_technology', NULL, NULL, 'DOUBLE_HOT_PRESS'),
('GP-ECO-TMR', 'density_g_cm3', 0.6, NULL, NULL),
('GP-ECO-TMR', 'moisture_content_pct', 9.3, NULL, NULL),
('GP-ECO-TMR', 'bim_ready', NULL, true, NULL),
('GP-ECO-TMR', 'modulus_elasticity_mpa', 4400, NULL, NULL),
('GP-ECO-TMR', 'modulus_rupture_mpa', 32.8, NULL, NULL),
('GP-ECO-TMR', 'screw_holding_face_n', 1490, NULL, NULL),
('GP-ECO-TMR', 'screw_holding_edge_n', 870, NULL, NULL),
('GP-ECO-TMR', 'thermal_conductivity_w_mk', 0.147, NULL, NULL),

-- GP-FIRE-RET
('GP-FIRE-RET', 'warranty_years', 20, NULL, NULL),
('GP-FIRE-RET', 'money_back_multiple', NULL, NULL, '2X'),
('GP-FIRE-RET', 'fully_covered_plan', NULL, true, NULL),
('GP-FIRE-RET', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-FIRE-RET', 'thickness_mm', 19, NULL, NULL),
('GP-FIRE-RET', 'mfg_length_mm', 2440, NULL, NULL),
('GP-FIRE-RET', 'mfg_width_mm', 1220, NULL, NULL),
('GP-FIRE-RET', 'mfg_thickness_mm', 18.7, NULL, NULL),
('GP-FIRE-RET', 'emission_standard', NULL, NULL, 'E0'),
('GP-FIRE-RET', 'borer_fungus_proof', NULL, true, NULL),
('GP-FIRE-RET', 'fire_retardant', NULL, true, NULL),
('GP-FIRE-RET', 'fire_delay_mins', 90, NULL, NULL),
('GP-FIRE-RET', 'water_resistance_grade', NULL, NULL, 'BWR'),
('GP-FIRE-RET', 'core_species', NULL, NULL, 'HARDWOOD'),
('GP-FIRE-RET', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('GP-FIRE-RET', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('GP-FIRE-RET', 'density_g_cm3', 0.76, NULL, NULL),
('GP-FIRE-RET', 'moisture_content_pct', 10.1, NULL, NULL),
('GP-FIRE-RET', 'bim_ready', NULL, true, NULL),
('GP-FIRE-RET', 'modulus_elasticity_mpa', 6850, NULL, NULL),
('GP-FIRE-RET', 'modulus_rupture_mpa', 51.8, NULL, NULL),
('GP-FIRE-RET', 'screw_holding_face_n', 1780, NULL, NULL),
('GP-FIRE-RET', 'screw_holding_edge_n', 1150, NULL, NULL),
('GP-FIRE-RET', 'thermal_conductivity_w_mk', 0.17, NULL, NULL),

-- GP-CLUB-500
('GP-CLUB-500', 'warranty_years', 20, NULL, NULL),
('GP-CLUB-500', 'money_back_multiple', NULL, NULL, '2X'),
('GP-CLUB-500', 'fully_covered_plan', NULL, true, NULL),
('GP-CLUB-500', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-CLUB-500', 'thickness_mm', 19, NULL, NULL),
('GP-CLUB-500', 'mfg_length_mm', 2440, NULL, NULL),
('GP-CLUB-500', 'mfg_width_mm', 1220, NULL, NULL),
('GP-CLUB-500', 'mfg_thickness_mm', 18.7, NULL, NULL),
('GP-CLUB-500', 'emission_standard', NULL, NULL, 'E0'),
('GP-CLUB-500', 'borer_fungus_proof', NULL, true, NULL),
('GP-CLUB-500', 'fire_retardant', NULL, true, NULL),
('GP-CLUB-500', 'fire_delay_mins', 25, NULL, NULL),
('GP-CLUB-500', 'water_resistance_grade', NULL, NULL, 'BWR'),
('GP-CLUB-500', 'core_species', NULL, NULL, 'HARDWOOD'),
('GP-CLUB-500', 'resin_type', NULL, NULL, 'MUF'),
('GP-CLUB-500', 'calibration_technology', NULL, NULL, 'FOUR_PRESS'),
('GP-CLUB-500', 'density_g_cm3', 0.73, NULL, NULL),
('GP-CLUB-500', 'moisture_content_pct', 8.4, NULL, NULL),
('GP-CLUB-500', 'bim_ready', NULL, false, NULL),
('GP-CLUB-500', 'modulus_elasticity_mpa', 6330, NULL, NULL),
('GP-CLUB-500', 'modulus_rupture_mpa', 47.2, NULL, NULL),
('GP-CLUB-500', 'screw_holding_face_n', 1720, NULL, NULL),
('GP-CLUB-500', 'screw_holding_edge_n', 1060, NULL, NULL),
('GP-CLUB-500', 'thermal_conductivity_w_mk', 0.158, NULL, NULL),

-- GP-CALIB-19
('GP-CALIB-19', 'warranty_years', 25, NULL, NULL),
('GP-CALIB-19', 'money_back_multiple', NULL, NULL, '7X'),
('GP-CALIB-19', 'fully_covered_plan', NULL, true, NULL),
('GP-CALIB-19', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-CALIB-19', 'thickness_mm', 19, NULL, NULL),
('GP-CALIB-19', 'mfg_length_mm', 2440, NULL, NULL),
('GP-CALIB-19', 'mfg_width_mm', 1220, NULL, NULL),
('GP-CALIB-19', 'mfg_thickness_mm', 18.8, NULL, NULL),
('GP-CALIB-19', 'emission_standard', NULL, NULL, 'E0'),
('GP-CALIB-19', 'borer_fungus_proof', NULL, true, NULL),
('GP-CALIB-19', 'fire_retardant', NULL, true, NULL),
('GP-CALIB-19', 'fire_delay_mins', 45, NULL, NULL),
('GP-CALIB-19', 'water_resistance_grade', NULL, NULL, 'STRUCTURAL'),
('GP-CALIB-19', 'core_species', NULL, NULL, 'HARDWOOD'),
('GP-CALIB-19', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('GP-CALIB-19', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('GP-CALIB-19', 'density_g_cm3', 0.78, NULL, NULL),
('GP-CALIB-19', 'moisture_content_pct', 10.5, NULL, NULL),
('GP-CALIB-19', 'bim_ready', NULL, false, NULL),
('GP-CALIB-19', 'modulus_elasticity_mpa', 7100, NULL, NULL),
('GP-CALIB-19', 'modulus_rupture_mpa', 54.8, NULL, NULL),
('GP-CALIB-19', 'screw_holding_face_n', 1900, NULL, NULL),
('GP-CALIB-19', 'screw_holding_edge_n', 1210, NULL, NULL),
('GP-CALIB-19', 'thermal_conductivity_w_mk', 0.168, NULL, NULL),

-- GP-CALIB-16
('GP-CALIB-16', 'warranty_years', 25, NULL, NULL),
('GP-CALIB-16', 'money_back_multiple', NULL, NULL, '7X'),
('GP-CALIB-16', 'fully_covered_plan', NULL, true, NULL),
('GP-CALIB-16', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-CALIB-16', 'thickness_mm', 16, NULL, NULL),
('GP-CALIB-16', 'mfg_length_mm', 2440, NULL, NULL),
('GP-CALIB-16', 'mfg_width_mm', 1220, NULL, NULL),
('GP-CALIB-16', 'mfg_thickness_mm', 15.6, NULL, NULL),
('GP-CALIB-16', 'emission_standard', NULL, NULL, 'E0'),
('GP-CALIB-16', 'borer_fungus_proof', NULL, true, NULL),
('GP-CALIB-16', 'fire_retardant', NULL, true, NULL),
('GP-CALIB-16', 'fire_delay_mins', 40, NULL, NULL),
('GP-CALIB-16', 'water_resistance_grade', NULL, NULL, 'STRUCTURAL'),
('GP-CALIB-16', 'core_species', NULL, NULL, 'HARDWOOD'),
('GP-CALIB-16', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('GP-CALIB-16', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('GP-CALIB-16', 'density_g_cm3', 0.76, NULL, NULL),
('GP-CALIB-16', 'moisture_content_pct', 9.3, NULL, NULL),
('GP-CALIB-16', 'bim_ready', NULL, true, NULL),
('GP-CALIB-16', 'modulus_elasticity_mpa', 7360, NULL, NULL),
('GP-CALIB-16', 'modulus_rupture_mpa', 59.4, NULL, NULL),
('GP-CALIB-16', 'screw_holding_face_n', 1900, NULL, NULL),
('GP-CALIB-16', 'screw_holding_edge_n', 1150, NULL, NULL),
('GP-CALIB-16', 'thermal_conductivity_w_mk', 0.166, NULL, NULL),

-- GP-CALIB-12
('GP-CALIB-12', 'warranty_years', 25, NULL, NULL),
('GP-CALIB-12', 'money_back_multiple', NULL, NULL, '7X'),
('GP-CALIB-12', 'fully_covered_plan', NULL, true, NULL),
('GP-CALIB-12', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-CALIB-12', 'thickness_mm', 12, NULL, NULL),
('GP-CALIB-12', 'mfg_length_mm', 2440, NULL, NULL),
('GP-CALIB-12', 'mfg_width_mm', 1220, NULL, NULL),
('GP-CALIB-12', 'mfg_thickness_mm', 11.8, NULL, NULL),
('GP-CALIB-12', 'emission_standard', NULL, NULL, 'E0'),
('GP-CALIB-12', 'borer_fungus_proof', NULL, true, NULL),
('GP-CALIB-12', 'fire_retardant', NULL, true, NULL),
('GP-CALIB-12', 'fire_delay_mins', 35, NULL, NULL),
('GP-CALIB-12', 'water_resistance_grade', NULL, NULL, 'STRUCTURAL'),
('GP-CALIB-12', 'core_species', NULL, NULL, 'HARDWOOD'),
('GP-CALIB-12', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('GP-CALIB-12', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('GP-CALIB-12', 'density_g_cm3', 0.76, NULL, NULL),
('GP-CALIB-12', 'moisture_content_pct', 9.3, NULL, NULL),
('GP-CALIB-12', 'bim_ready', NULL, true, NULL),
('GP-CALIB-12', 'modulus_elasticity_mpa', 6950, NULL, NULL),
('GP-CALIB-12', 'modulus_rupture_mpa', 60.2, NULL, NULL),
('GP-CALIB-12', 'screw_holding_face_n', 1910, NULL, NULL),
('GP-CALIB-12', 'screw_holding_edge_n', 1090, NULL, NULL),
('GP-CALIB-12', 'thermal_conductivity_w_mk', 0.166, NULL, NULL),

-- GP-CALIB-9
('GP-CALIB-9', 'warranty_years', 25, NULL, NULL),
('GP-CALIB-9', 'money_back_multiple', NULL, NULL, '7X'),
('GP-CALIB-9', 'fully_covered_plan', NULL, false, NULL),
('GP-CALIB-9', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-CALIB-9', 'thickness_mm', 9, NULL, NULL),
('GP-CALIB-9', 'mfg_length_mm', 2440, NULL, NULL),
('GP-CALIB-9', 'mfg_width_mm', 1220, NULL, NULL),
('GP-CALIB-9', 'mfg_thickness_mm', 8.8, NULL, NULL),
('GP-CALIB-9', 'emission_standard', NULL, NULL, 'E0'),
('GP-CALIB-9', 'borer_fungus_proof', NULL, true, NULL),
('GP-CALIB-9', 'fire_retardant', NULL, true, NULL),
('GP-CALIB-9', 'fire_delay_mins', 30, NULL, NULL),
('GP-CALIB-9', 'water_resistance_grade', NULL, NULL, 'STRUCTURAL'),
('GP-CALIB-9', 'core_species', NULL, NULL, 'HARDWOOD'),
('GP-CALIB-9', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('GP-CALIB-9', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('GP-CALIB-9', 'density_g_cm3', 0.76, NULL, NULL),
('GP-CALIB-9', 'moisture_content_pct', 10.7, NULL, NULL),
('GP-CALIB-9', 'bim_ready', NULL, true, NULL),
('GP-CALIB-9', 'modulus_elasticity_mpa', 7390, NULL, NULL),
('GP-CALIB-9', 'modulus_rupture_mpa', 61.6, NULL, NULL),
('GP-CALIB-9', 'screw_holding_face_n', 1760, NULL, NULL),
('GP-CALIB-9', 'screw_holding_edge_n', 1140, NULL, NULL),
('GP-CALIB-9', 'thermal_conductivity_w_mk', 0.167, NULL, NULL),

-- GP-CALIB-6
('GP-CALIB-6', 'warranty_years', 25, NULL, NULL),
('GP-CALIB-6', 'money_back_multiple', NULL, NULL, '7X'),
('GP-CALIB-6', 'fully_covered_plan', NULL, false, NULL),
('GP-CALIB-6', 'sheet_dimension', NULL, NULL, '6X4'),
('GP-CALIB-6', 'thickness_mm', 6, NULL, NULL),
('GP-CALIB-6', 'mfg_length_mm', 1830, NULL, NULL),
('GP-CALIB-6', 'mfg_width_mm', 1220, NULL, NULL),
('GP-CALIB-6', 'mfg_thickness_mm', 5.6, NULL, NULL),
('GP-CALIB-6', 'emission_standard', NULL, NULL, 'E0'),
('GP-CALIB-6', 'borer_fungus_proof', NULL, true, NULL),
('GP-CALIB-6', 'fire_retardant', NULL, true, NULL),
('GP-CALIB-6', 'fire_delay_mins', 25, NULL, NULL),
('GP-CALIB-6', 'water_resistance_grade', NULL, NULL, 'STRUCTURAL'),
('GP-CALIB-6', 'core_species', NULL, NULL, 'HARDWOOD'),
('GP-CALIB-6', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('GP-CALIB-6', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('GP-CALIB-6', 'density_g_cm3', 0.81, NULL, NULL),
('GP-CALIB-6', 'moisture_content_pct', 11.3, NULL, NULL),
('GP-CALIB-6', 'bim_ready', NULL, false, NULL),
('GP-CALIB-6', 'modulus_elasticity_mpa', 7220, NULL, NULL),
('GP-CALIB-6', 'modulus_rupture_mpa', 58.9, NULL, NULL),
('GP-CALIB-6', 'screw_holding_face_n', 2040, NULL, NULL),
('GP-CALIB-6', 'screw_holding_edge_n', 1230, NULL, NULL),
('GP-CALIB-6', 'thermal_conductivity_w_mk', 0.17, NULL, NULL),

-- GP-BLOCK-25
('GP-BLOCK-25', 'warranty_years', 25, NULL, NULL),
('GP-BLOCK-25', 'money_back_multiple', NULL, NULL, '7X'),
('GP-BLOCK-25', 'fully_covered_plan', NULL, true, NULL),
('GP-BLOCK-25', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-BLOCK-25', 'thickness_mm', 25, NULL, NULL),
('GP-BLOCK-25', 'mfg_length_mm', 2440, NULL, NULL),
('GP-BLOCK-25', 'mfg_width_mm', 1220, NULL, NULL),
('GP-BLOCK-25', 'mfg_thickness_mm', 24.8, NULL, NULL),
('GP-BLOCK-25', 'emission_standard', NULL, NULL, 'E0'),
('GP-BLOCK-25', 'borer_fungus_proof', NULL, true, NULL),
('GP-BLOCK-25', 'fire_retardant', NULL, true, NULL),
('GP-BLOCK-25', 'fire_delay_mins', 40, NULL, NULL),
('GP-BLOCK-25', 'water_resistance_grade', NULL, NULL, 'BWP'),
('GP-BLOCK-25', 'core_species', NULL, NULL, 'GURJAN'),
('GP-BLOCK-25', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('GP-BLOCK-25', 'calibration_technology', NULL, NULL, 'FOUR_PRESS'),
('GP-BLOCK-25', 'density_g_cm3', 0.6, NULL, NULL),
('GP-BLOCK-25', 'moisture_content_pct', 8.4, NULL, NULL),
('GP-BLOCK-25', 'bim_ready', NULL, true, NULL),
('GP-BLOCK-25', 'modulus_elasticity_mpa', 3750, NULL, NULL),
('GP-BLOCK-25', 'modulus_rupture_mpa', 30.3, NULL, NULL),
('GP-BLOCK-25', 'screw_holding_face_n', 1400, NULL, NULL),
('GP-BLOCK-25', 'screw_holding_edge_n', 870, NULL, NULL),
('GP-BLOCK-25', 'thermal_conductivity_w_mk', 0.146, NULL, NULL),

-- GP-BLOCK-19
('GP-BLOCK-19', 'warranty_years', 15, NULL, NULL),
('GP-BLOCK-19', 'money_back_multiple', NULL, NULL, '2X'),
('GP-BLOCK-19', 'fully_covered_plan', NULL, false, NULL),
('GP-BLOCK-19', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-BLOCK-19', 'thickness_mm', 19, NULL, NULL),
('GP-BLOCK-19', 'mfg_length_mm', 2440, NULL, NULL),
('GP-BLOCK-19', 'mfg_width_mm', 1220, NULL, NULL),
('GP-BLOCK-19', 'mfg_thickness_mm', 18.7, NULL, NULL),
('GP-BLOCK-19', 'emission_standard', NULL, NULL, 'E0'),
('GP-BLOCK-19', 'borer_fungus_proof', NULL, true, NULL),
('GP-BLOCK-19', 'fire_retardant', NULL, false, NULL),
('GP-BLOCK-19', 'fire_delay_mins', 0, NULL, NULL),
('GP-BLOCK-19', 'water_resistance_grade', NULL, NULL, 'MR'),
('GP-BLOCK-19', 'core_species', NULL, NULL, 'HARDWOOD'),
('GP-BLOCK-19', 'resin_type', NULL, NULL, 'MUF'),
('GP-BLOCK-19', 'calibration_technology', NULL, NULL, 'FOUR_PRESS'),
('GP-BLOCK-19', 'density_g_cm3', 0.58, NULL, NULL),
('GP-BLOCK-19', 'moisture_content_pct', 8.8, NULL, NULL),
('GP-BLOCK-19', 'bim_ready', NULL, true, NULL),
('GP-BLOCK-19', 'modulus_elasticity_mpa', 3380, NULL, NULL),
('GP-BLOCK-19', 'modulus_rupture_mpa', 26.3, NULL, NULL),
('GP-BLOCK-19', 'screw_holding_face_n', 1420, NULL, NULL),
('GP-BLOCK-19', 'screw_holding_edge_n', 850, NULL, NULL),
('GP-BLOCK-19', 'thermal_conductivity_w_mk', 0.145, NULL, NULL),

-- GP-DOOR-FLUSH
('GP-DOOR-FLUSH', 'warranty_years', 15, NULL, NULL),
('GP-DOOR-FLUSH', 'money_back_multiple', NULL, NULL, '2X'),
('GP-DOOR-FLUSH', 'fully_covered_plan', NULL, false, NULL),
('GP-DOOR-FLUSH', 'sheet_dimension', NULL, NULL, '7X3'),
('GP-DOOR-FLUSH', 'thickness_mm', 35, NULL, NULL),
('GP-DOOR-FLUSH', 'mfg_length_mm', 2135, NULL, NULL),
('GP-DOOR-FLUSH', 'mfg_width_mm', 915, NULL, NULL),
('GP-DOOR-FLUSH', 'mfg_thickness_mm', 34.8, NULL, NULL),
('GP-DOOR-FLUSH', 'emission_standard', NULL, NULL, 'E0'),
('GP-DOOR-FLUSH', 'borer_fungus_proof', NULL, true, NULL),
('GP-DOOR-FLUSH', 'fire_retardant', NULL, true, NULL),
('GP-DOOR-FLUSH', 'fire_delay_mins', 30, NULL, NULL),
('GP-DOOR-FLUSH', 'water_resistance_grade', NULL, NULL, 'MR'),
('GP-DOOR-FLUSH', 'core_species', NULL, NULL, 'COMBI'),
('GP-DOOR-FLUSH', 'resin_type', NULL, NULL, 'MUF'),
('GP-DOOR-FLUSH', 'calibration_technology', NULL, NULL, 'DOUBLE_HOT_PRESS'),
('GP-DOOR-FLUSH', 'density_g_cm3', 0.56, NULL, NULL),
('GP-DOOR-FLUSH', 'moisture_content_pct', 10, NULL, NULL),
('GP-DOOR-FLUSH', 'bim_ready', NULL, true, NULL),
('GP-DOOR-FLUSH', 'modulus_elasticity_mpa', 3270, NULL, NULL),
('GP-DOOR-FLUSH', 'modulus_rupture_mpa', 24.7, NULL, NULL),
('GP-DOOR-FLUSH', 'screw_holding_face_n', 1300, NULL, NULL),
('GP-DOOR-FLUSH', 'screw_holding_edge_n', 810, NULL, NULL),
('GP-DOOR-FLUSH', 'thermal_conductivity_w_mk', 0.145, NULL, NULL),

-- GP-VENEER-TEAK
('GP-VENEER-TEAK', 'warranty_years', 10, NULL, NULL),
('GP-VENEER-TEAK', 'money_back_multiple', NULL, NULL, 'STANDARD'),
('GP-VENEER-TEAK', 'fully_covered_plan', NULL, false, NULL),
('GP-VENEER-TEAK', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-VENEER-TEAK', 'thickness_mm', 19, NULL, NULL),
('GP-VENEER-TEAK', 'mfg_length_mm', 2440, NULL, NULL),
('GP-VENEER-TEAK', 'mfg_width_mm', 1220, NULL, NULL),
('GP-VENEER-TEAK', 'mfg_thickness_mm', 18.8, NULL, NULL),
('GP-VENEER-TEAK', 'emission_standard', NULL, NULL, 'E0'),
('GP-VENEER-TEAK', 'borer_fungus_proof', NULL, true, NULL),
('GP-VENEER-TEAK', 'fire_retardant', NULL, false, NULL),
('GP-VENEER-TEAK', 'fire_delay_mins', 0, NULL, NULL),
('GP-VENEER-TEAK', 'water_resistance_grade', NULL, NULL, 'MR'),
('GP-VENEER-TEAK', 'core_species', NULL, NULL, 'COMBI'),
('GP-VENEER-TEAK', 'resin_type', NULL, NULL, 'MUF'),
('GP-VENEER-TEAK', 'calibration_technology', NULL, NULL, 'DOUBLE_HOT_PRESS'),
('GP-VENEER-TEAK', 'density_g_cm3', 0.68, NULL, NULL),
('GP-VENEER-TEAK', 'moisture_content_pct', 9.6, NULL, NULL),
('GP-VENEER-TEAK', 'bim_ready', NULL, false, NULL),
('GP-VENEER-TEAK', 'modulus_elasticity_mpa', 5070, NULL, NULL),
('GP-VENEER-TEAK', 'modulus_rupture_mpa', 39.8, NULL, NULL),
('GP-VENEER-TEAK', 'screw_holding_face_n', 1570, NULL, NULL),
('GP-VENEER-TEAK', 'screw_holding_edge_n', 1010, NULL, NULL),
('GP-VENEER-TEAK', 'thermal_conductivity_w_mk', 0.156, NULL, NULL),

-- GP-VENEER-WALNUT
('GP-VENEER-WALNUT', 'warranty_years', 10, NULL, NULL),
('GP-VENEER-WALNUT', 'money_back_multiple', NULL, NULL, 'STANDARD'),
('GP-VENEER-WALNUT', 'fully_covered_plan', NULL, false, NULL),
('GP-VENEER-WALNUT', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-VENEER-WALNUT', 'thickness_mm', 19, NULL, NULL),
('GP-VENEER-WALNUT', 'mfg_length_mm', 2440, NULL, NULL),
('GP-VENEER-WALNUT', 'mfg_width_mm', 1220, NULL, NULL),
('GP-VENEER-WALNUT', 'mfg_thickness_mm', 18.6, NULL, NULL),
('GP-VENEER-WALNUT', 'emission_standard', NULL, NULL, 'E0'),
('GP-VENEER-WALNUT', 'borer_fungus_proof', NULL, true, NULL),
('GP-VENEER-WALNUT', 'fire_retardant', NULL, false, NULL),
('GP-VENEER-WALNUT', 'fire_delay_mins', 0, NULL, NULL),
('GP-VENEER-WALNUT', 'water_resistance_grade', NULL, NULL, 'MR'),
('GP-VENEER-WALNUT', 'core_species', NULL, NULL, 'COMBI'),
('GP-VENEER-WALNUT', 'resin_type', NULL, NULL, 'MUF'),
('GP-VENEER-WALNUT', 'calibration_technology', NULL, NULL, 'DOUBLE_HOT_PRESS'),
('GP-VENEER-WALNUT', 'density_g_cm3', 0.7, NULL, NULL),
('GP-VENEER-WALNUT', 'moisture_content_pct', 8.9, NULL, NULL),
('GP-VENEER-WALNUT', 'bim_ready', NULL, true, NULL),
('GP-VENEER-WALNUT', 'modulus_elasticity_mpa', 5080, NULL, NULL),
('GP-VENEER-WALNUT', 'modulus_rupture_mpa', 38.7, NULL, NULL),
('GP-VENEER-WALNUT', 'screw_holding_face_n', 1640, NULL, NULL),
('GP-VENEER-WALNUT', 'screw_holding_edge_n', 1020, NULL, NULL),
('GP-VENEER-WALNUT', 'thermal_conductivity_w_mk', 0.164, NULL, NULL),

-- GP-VENEER-OAK
('GP-VENEER-OAK', 'warranty_years', 10, NULL, NULL),
('GP-VENEER-OAK', 'money_back_multiple', NULL, NULL, 'STANDARD'),
('GP-VENEER-OAK', 'fully_covered_plan', NULL, false, NULL),
('GP-VENEER-OAK', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-VENEER-OAK', 'thickness_mm', 19, NULL, NULL),
('GP-VENEER-OAK', 'mfg_length_mm', 2440, NULL, NULL),
('GP-VENEER-OAK', 'mfg_width_mm', 1220, NULL, NULL),
('GP-VENEER-OAK', 'mfg_thickness_mm', 18.8, NULL, NULL),
('GP-VENEER-OAK', 'emission_standard', NULL, NULL, 'E0'),
('GP-VENEER-OAK', 'borer_fungus_proof', NULL, true, NULL),
('GP-VENEER-OAK', 'fire_retardant', NULL, false, NULL),
('GP-VENEER-OAK', 'fire_delay_mins', 0, NULL, NULL),
('GP-VENEER-OAK', 'water_resistance_grade', NULL, NULL, 'MR'),
('GP-VENEER-OAK', 'core_species', NULL, NULL, 'COMBI'),
('GP-VENEER-OAK', 'resin_type', NULL, NULL, 'MUF'),
('GP-VENEER-OAK', 'calibration_technology', NULL, NULL, 'DOUBLE_HOT_PRESS'),
('GP-VENEER-OAK', 'density_g_cm3', 0.7, NULL, NULL),
('GP-VENEER-OAK', 'moisture_content_pct', 11, NULL, NULL),
('GP-VENEER-OAK', 'bim_ready', NULL, false, NULL),
('GP-VENEER-OAK', 'modulus_elasticity_mpa', 5010, NULL, NULL),
('GP-VENEER-OAK', 'modulus_rupture_mpa', 42, NULL, NULL),
('GP-VENEER-OAK', 'screw_holding_face_n', 1740, NULL, NULL),
('GP-VENEER-OAK', 'screw_holding_edge_n', 1100, NULL, NULL),
('GP-VENEER-OAK', 'thermal_conductivity_w_mk', 0.164, NULL, NULL),

-- GP-CLUB-700
('GP-CLUB-700', 'warranty_years', 25, NULL, NULL),
('GP-CLUB-700', 'money_back_multiple', NULL, NULL, '7X'),
('GP-CLUB-700', 'fully_covered_plan', NULL, true, NULL),
('GP-CLUB-700', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-CLUB-700', 'thickness_mm', 19, NULL, NULL),
('GP-CLUB-700', 'mfg_length_mm', 2440, NULL, NULL),
('GP-CLUB-700', 'mfg_width_mm', 1220, NULL, NULL),
('GP-CLUB-700', 'mfg_thickness_mm', 18.7, NULL, NULL),
('GP-CLUB-700', 'emission_standard', NULL, NULL, 'E0'),
('GP-CLUB-700', 'borer_fungus_proof', NULL, true, NULL),
('GP-CLUB-700', 'fire_retardant', NULL, true, NULL),
('GP-CLUB-700', 'fire_delay_mins', 50, NULL, NULL),
('GP-CLUB-700', 'water_resistance_grade', NULL, NULL, 'BWP'),
('GP-CLUB-700', 'core_species', NULL, NULL, 'GURJAN'),
('GP-CLUB-700', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('GP-CLUB-700', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('GP-CLUB-700', 'density_g_cm3', 0.78, NULL, NULL),
('GP-CLUB-700', 'moisture_content_pct', 8.6, NULL, NULL),
('GP-CLUB-700', 'bim_ready', NULL, true, NULL),
('GP-CLUB-700', 'modulus_elasticity_mpa', 6840, NULL, NULL),
('GP-CLUB-700', 'modulus_rupture_mpa', 55.4, NULL, NULL),
('GP-CLUB-700', 'screw_holding_face_n', 1790, NULL, NULL),
('GP-CLUB-700', 'screw_holding_edge_n', 1160, NULL, NULL),
('GP-CLUB-700', 'thermal_conductivity_w_mk', 0.173, NULL, NULL),

-- GP-PLAT-BWP
('GP-PLAT-BWP', 'warranty_years', 25, NULL, NULL),
('GP-PLAT-BWP', 'money_back_multiple', NULL, NULL, '7X'),
('GP-PLAT-BWP', 'fully_covered_plan', NULL, true, NULL),
('GP-PLAT-BWP', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-PLAT-BWP', 'thickness_mm', 19, NULL, NULL),
('GP-PLAT-BWP', 'mfg_length_mm', 2440, NULL, NULL),
('GP-PLAT-BWP', 'mfg_width_mm', 1220, NULL, NULL),
('GP-PLAT-BWP', 'mfg_thickness_mm', 18.7, NULL, NULL),
('GP-PLAT-BWP', 'emission_standard', NULL, NULL, 'E0'),
('GP-PLAT-BWP', 'borer_fungus_proof', NULL, true, NULL),
('GP-PLAT-BWP', 'fire_retardant', NULL, true, NULL),
('GP-PLAT-BWP', 'fire_delay_mins', 60, NULL, NULL),
('GP-PLAT-BWP', 'water_resistance_grade', NULL, NULL, 'BWP'),
('GP-PLAT-BWP', 'core_species', NULL, NULL, 'HARDWOOD'),
('GP-PLAT-BWP', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('GP-PLAT-BWP', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('GP-PLAT-BWP', 'density_g_cm3', 0.77, NULL, NULL),
('GP-PLAT-BWP', 'moisture_content_pct', 9.8, NULL, NULL),
('GP-PLAT-BWP', 'bim_ready', NULL, true, NULL),
('GP-PLAT-BWP', 'modulus_elasticity_mpa', 7150, NULL, NULL),
('GP-PLAT-BWP', 'modulus_rupture_mpa', 56.5, NULL, NULL),
('GP-PLAT-BWP', 'screw_holding_face_n', 1860, NULL, NULL),
('GP-PLAT-BWP', 'screw_holding_edge_n', 1180, NULL, NULL),
('GP-PLAT-BWP', 'thermal_conductivity_w_mk', 0.170, NULL, NULL),

-- GP-710-MAR
('GP-710-MAR', 'warranty_years', 25, NULL, NULL),
('GP-710-MAR', 'money_back_multiple', NULL, NULL, '7X'),
('GP-710-MAR', 'fully_covered_plan', NULL, true, NULL),
('GP-710-MAR', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-710-MAR', 'thickness_mm', 19, NULL, NULL),
('GP-710-MAR', 'mfg_length_mm', 2440, NULL, NULL),
('GP-710-MAR', 'mfg_width_mm', 1220, NULL, NULL),
('GP-710-MAR', 'mfg_thickness_mm', 18.7, NULL, NULL),
('GP-710-MAR', 'emission_standard', NULL, NULL, 'E0'),
('GP-710-MAR', 'borer_fungus_proof', NULL, true, NULL),
('GP-710-MAR', 'fire_retardant', NULL, true, NULL),
('GP-710-MAR', 'fire_delay_mins', 30, NULL, NULL),
('GP-710-MAR', 'water_resistance_grade', NULL, NULL, 'BWP'),
('GP-710-MAR', 'core_species', NULL, NULL, 'GURJAN'),
('GP-710-MAR', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('GP-710-MAR', 'calibration_technology', NULL, NULL, 'PENTA_TECH'),
('GP-710-MAR', 'density_g_cm3', 0.78, NULL, NULL),
('GP-710-MAR', 'moisture_content_pct', 10.2, NULL, NULL),
('GP-710-MAR', 'bim_ready', NULL, true, NULL),
('GP-710-MAR', 'modulus_elasticity_mpa', 6900, NULL, NULL),
('GP-710-MAR', 'modulus_rupture_mpa', 56.0, NULL, NULL),
('GP-710-MAR', 'screw_holding_face_n', 1810, NULL, NULL),
('GP-710-MAR', 'screw_holding_edge_n', 1140, NULL, NULL),
('GP-710-MAR', 'thermal_conductivity_w_mk', 0.168, NULL, NULL),

-- GP-GOLD-BWP
('GP-GOLD-BWP', 'warranty_years', 20, NULL, NULL),
('GP-GOLD-BWP', 'money_back_multiple', NULL, NULL, '2X'),
('GP-GOLD-BWP', 'fully_covered_plan', NULL, true, NULL),
('GP-GOLD-BWP', 'sheet_dimension', NULL, NULL, '8X4'),
('GP-GOLD-BWP', 'thickness_mm', 19, NULL, NULL),
('GP-GOLD-BWP', 'mfg_length_mm', 2440, NULL, NULL),
('GP-GOLD-BWP', 'mfg_width_mm', 1220, NULL, NULL),
('GP-GOLD-BWP', 'mfg_thickness_mm', 18.7, NULL, NULL),
('GP-GOLD-BWP', 'emission_standard', NULL, NULL, 'E0'),
('GP-GOLD-BWP', 'borer_fungus_proof', NULL, true, NULL),
('GP-GOLD-BWP', 'fire_retardant', NULL, true, NULL),
('GP-GOLD-BWP', 'fire_delay_mins', 20, NULL, NULL),
('GP-GOLD-BWP', 'water_resistance_grade', NULL, NULL, 'BWP'),
('GP-GOLD-BWP', 'core_species', NULL, NULL, 'HARDWOOD'),
('GP-GOLD-BWP', 'resin_type', NULL, NULL, 'UNEXTENDED_PF'),
('GP-GOLD-BWP', 'calibration_technology', NULL, NULL, 'FOUR_PRESS'),
('GP-GOLD-BWP', 'density_g_cm3', 0.75, NULL, NULL),
('GP-GOLD-BWP', 'moisture_content_pct', 9.2, NULL, NULL),
('GP-GOLD-BWP', 'bim_ready', NULL, true, NULL),
('GP-GOLD-BWP', 'modulus_elasticity_mpa', 6500, NULL, NULL),
('GP-GOLD-BWP', 'modulus_rupture_mpa', 50.0, NULL, NULL),
('GP-GOLD-BWP', 'screw_holding_face_n', 1750, NULL, NULL),
('GP-GOLD-BWP', 'screw_holding_edge_n', 1100, NULL, NULL),
('GP-GOLD-BWP', 'thermal_conductivity_w_mk', 0.162, NULL, NULL);

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
