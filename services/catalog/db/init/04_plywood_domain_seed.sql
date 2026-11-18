-- =============================================================================
-- MODULE · PLYWOOD DOMAIN SEED (Greenply Industries Model)
--
-- Transitions the PIM catalogue to the Engineered Wood & Plywood domain.
-- Seeds:
--   1. Brand: Greenply Industries
--   2. Units: area (sqft, sqm), density (g/cm³), force (N), pressure (MPa), time (hours)
--   3. Taxonomy: Wood Panels > Plywood & Blockboards, Flush Doors, Veneers, PVC/WPC
--   4. Attribute Groups: Dimensions, Technical, Protection, Health/Air Quality, Commercial
--   5. Typed Attributes & Options: Water resistance (BWP/BWR/MR), Emission (E0/CARB),
--      Core species, Resin, Calibration tech, Fire retardancy, Warranty terms
--   6. Attribute Set: plywood_core
--   7. Applications: Kitchen, Wardrobes, Wall Panelling, Partitions, Marine
--   8. Showcase Products: Green Club 700, Green Platinum, Green 710, Green Gold
--      complete with SKUs, attributes, compliance docs, packaging, and relationships.
-- =============================================================================

SET search_path TO catalog;

-- Ensure compliance_document accepts new plywood compliance standards (ISI, FSC, CARB, EPA, IGBC)
DO $$
BEGIN
  ALTER TABLE catalog.compliance_document DROP CONSTRAINT IF EXISTS compliance_document_doc_type_check;
  ALTER TABLE catalog.compliance_document ADD CONSTRAINT compliance_document_doc_type_check 
    CHECK (doc_type IN ('DOP','CE','EPD','DPP','TEST','UKCA','ISI','FSC','CARB','EPA','IGBC'));
EXCEPTION WHEN OTHERS THEN
  NULL;
END $$;

-- 1. BRAND --------------------------------------------------------------------
INSERT INTO brand (code, legal_entity, website, country_code, status)
VALUES ('greenply', 'Greenply Industries Limited', 'https://www.greenply.com', 'IN', 'active')
ON CONFLICT (code) DO NOTHING;

INSERT INTO brand_tr (brand_id, locale_code, name, description, tagline)
SELECT b.id, v.locale, v.name, v.descr, v.tagline
FROM (VALUES
  ('en-GB', 'Greenply', 'India''s premier manufacturer of zero-emission plywood, blockboards, decorative veneers and PVC boards.', 'Zero Emission (E0) Pioneer'),
  ('de-DE', 'Greenply', 'Führender Hersteller von emissionsfreiem Sperrholz, Tischlerplatten, Furnieren und Verbundwerkstoffen.', 'Pionier für emissionsfreies E0-Sperrholz')
) AS v(locale, name, descr, tagline)
CROSS JOIN (SELECT id FROM brand WHERE code = 'greenply') b
ON CONFLICT (brand_id, locale_code) DO NOTHING;

-- 2. MEASUREMENT UNITS --------------------------------------------------------
INSERT INTO unit (code, symbol, dimension, to_si_factor) VALUES
  ('sqft',  'sq ft', 'area',     0.092903),
  ('sqm',   'm²',    'area',     1.0),
  ('g_cm3', 'g/cm³', 'density',  1000.0),
  ('mpa',   'MPa',   'pressure', 1000000.0),
  ('n',     'N',     'force',    1.0),
  ('hours', 'hrs',   'time',     3600.0)
ON CONFLICT (code) DO NOTHING;

INSERT INTO unit_tr (unit_id, locale_code, name)
SELECT u.id, v.locale, v.name FROM (VALUES
  ('sqft',  'en-GB', 'Square foot'),
  ('sqft',  'de-DE', 'Quadratfuß'),
  ('sqm',   'en-GB', 'Square metre'),
  ('sqm',   'de-DE', 'Quadratmeter'),
  ('g_cm3', 'en-GB', 'Grams per cubic centimetre'),
  ('g_cm3', 'de-DE', 'Gramm pro Kubikzentimeter'),
  ('mpa',   'en-GB', 'Megapascal'),
  ('mpa',   'de-DE', 'Megapascal'),
  ('n',     'en-GB', 'Newton'),
  ('n',     'de-DE', 'Newton'),
  ('hours', 'en-GB', 'Hours'),
  ('hours', 'de-DE', 'Stunden')
) AS v(code, locale, name)
JOIN unit u ON u.code = v.code
ON CONFLICT (unit_id, locale_code) DO NOTHING;

-- 3. TAXONOMY NODES -----------------------------------------------------------
-- Root Family Node
INSERT INTO taxonomy_node (code, node_type, parent_id, path, depth, brand_id)
SELECT 'greenply', 'FAMILY', NULL, 'greenply'::ltree, 0, b.id
FROM brand b WHERE b.code = 'greenply'
ON CONFLICT (code) DO NOTHING;

-- Category Nodes under greenply
INSERT INTO taxonomy_node (code, node_type, parent_id, path, depth, brand_id)
SELECT c.code, 'CATEGORY', r.id, ('greenply.' || c.code)::ltree, 1, r.brand_id
FROM (VALUES
  ('plywood_blockboard'),
  ('flush_doors'),
  ('decorative_veneers'),
  ('pvc_wpc'),
  ('mdf_boards')
) AS c(code)
CROSS JOIN (SELECT id, brand_id FROM taxonomy_node WHERE code = 'greenply') r
ON CONFLICT (code) DO NOTHING;

-- Subcategory / Series under plywood_blockboard
INSERT INTO taxonomy_node (code, node_type, parent_id, path, depth, brand_id)
SELECT s.code, 'SERIES', p.id, ('greenply.plywood_blockboard.' || s.code)::ltree, 2, p.brand_id
FROM (VALUES
  ('green_club'),
  ('green_platinum'),
  ('green_710'),
  ('green_gold'),
  ('green_ecotec')
) AS s(code)
CROSS JOIN (SELECT id, brand_id FROM taxonomy_node WHERE code = 'plywood_blockboard') p
ON CONFLICT (code) DO NOTHING;

-- Taxonomy node translations
INSERT INTO taxonomy_node_tr (node_id, locale_code, name, description, translation_status)
SELECT n.id, v.locale, v.name, v.descr, 'approved'
FROM (VALUES
  ('greenply',           'en-GB', 'Greenply Collection', 'All Greenply product lines and materials'),
  ('greenply',           'de-DE', 'Greenply Kollektion', 'Alle Greenply Produktlinien und Materialien'),
  ('plywood_blockboard', 'en-GB', 'Plywood & Blockboards', 'Structural, Marine, Fire Retardant and Commercial panels'),
  ('plywood_blockboard', 'de-DE', 'Sperrholz & Tischlerplatten', 'Struktur-, Marine- und feuerhemmende Platten'),
  ('flush_doors',        'en-GB', 'Doors', 'High dimensional stability Flush & Decorative Doors'),
  ('flush_doors',        'de-DE', 'Türen', 'Formstabile Glatt- und Designtüren'),
  ('decorative_veneers', 'en-GB', 'Decorative Veneers', 'Natural Wood Crrests and engineered veneers'),
  ('decorative_veneers', 'de-DE', 'Echtholzfurniere', 'Natürliche und veredelte Echtholzfurniere'),
  ('pvc_wpc',            'en-GB', 'PVC & WPC Panels', 'Lead-free 100% waterproof composite boards'),
  ('pvc_wpc',            'de-DE', 'PVC- & WPC-Platten', 'Bleifreie, 100% wasserfeste Verbundplatten'),
  ('mdf_boards',         'en-GB', 'MDF & Engineered Panels', 'HDMR 710 and exterior grade fiberboards'),
  ('mdf_boards',         'de-DE', 'MDF & Faserplatten', 'HDMR 710 und feuchtebeständige Faserplatten'),
  ('green_club',         'en-GB', 'Green Club 700 / 500', 'Flagship structural grade E0 plywood with lifetime warranty'),
  ('green_club',         'de-DE', 'Green Club 700 / 500', 'Premium Struktur-Sperrholz E0 mit lebenslanger Garantie'),
  ('green_platinum',     'en-GB', 'Green Platinum', '2X Fire Retardant 100% BWP Marine grade plywood'),
  ('green_platinum',     'de-DE', 'Green Platinum', '2X feuerhemmendes, 100% kochwasserfestes BWP-Sperrholz'),
  ('green_710',          'en-GB', 'Green 710 Marine', 'Selected hardwood marine grade plywood per IS:710'),
  ('green_710',          'de-DE', 'Green 710 Marine', 'Bootsbau- und Marinesperrholz nach IS:710'),
  ('green_gold',         'en-GB', 'Green Gold / Gold 2.0', 'Commercial calibrated BWP plywood with 11-year restoration cover'),
  ('green_gold',         'de-DE', 'Green Gold / Gold 2.0', 'Kalibriertes BWP-Sperrholz mit 11-Jahre Rundumschutz'),
  ('green_ecotec',       'en-GB', 'Green Ecotec', 'Sustainable plantation timber interior plywood'),
  ('green_ecotec',       'de-DE', 'Green Ecotec', 'Nachhaltiges Plantagenholz-Sperrholz für den Innenausbau')
) AS v(code, locale, name, descr)
JOIN taxonomy_node n ON n.code = v.code
ON CONFLICT (node_id, locale_code) DO NOTHING;

-- 4. ATTRIBUTE GROUPS ---------------------------------------------------------
INSERT INTO attribute_group (code) VALUES
  ('plywood_dimensions'),
  ('plywood_technical'),
  ('plywood_protection'),
  ('plywood_health'),
  ('plywood_commercial')
ON CONFLICT (code) DO NOTHING;

INSERT INTO attribute_group_tr (group_id, locale_code, name)
SELECT g.id, v.locale, v.name FROM (VALUES
  ('plywood_dimensions', 'en-GB', 'Dimensions & Sizing'),
  ('plywood_dimensions', 'de-DE', 'Abmessungen & Formate'),
  ('plywood_technical',  'en-GB', 'Core & Technical Specifications'),
  ('plywood_technical',  'de-DE', 'Technische Daten & Konstruktion'),
  ('plywood_protection', 'en-GB', 'Protection & Durability'),
  ('plywood_protection', 'de-DE', 'Schutz & Widerstandsfähigkeit'),
  ('plywood_health',     'en-GB', 'Health, Emissions & Air Quality'),
  ('plywood_health',     'de-DE', 'Gesundheit, Raumluft & Emissionen'),
  ('plywood_commercial', 'en-GB', 'Commercial & Warranty'),
  ('plywood_commercial', 'de-DE', 'Garantie & Handelskonditionen')
) AS v(code, locale, name)
JOIN attribute_group g ON g.code = v.code
ON CONFLICT (group_id, locale_code) DO NOTHING;

-- 5. ATTRIBUTES ---------------------------------------------------------------
INSERT INTO attribute (code, data_type, group_id, unit_id, is_localizable)
SELECT v.code, v.data_type, g.id, u.id, v.localizable FROM (VALUES
  ('water_resistance_grade', 'list',    'plywood_technical',  NULL,    false),
  ('emission_standard',      'list',    'plywood_health',     NULL,    false),
  ('core_species',           'list',    'plywood_technical',  NULL,    false),
  ('resin_type',             'list',    'plywood_technical',  NULL,    false),
  ('calibration_technology', 'list',    'plywood_technical',  NULL,    false),
  ('sheet_dimension',        'list',    'plywood_dimensions', NULL,    false),
  ('thickness_mm',           'measure', 'plywood_dimensions', 'mm',    false),
  ('density_g_cm3',          'measure', 'plywood_technical',  'g_cm3', false),
  ('moisture_content_pct',   'decimal', 'plywood_technical',  NULL,    false),
  ('borer_fungus_proof',     'boolean', 'plywood_protection', NULL,    false),
  ('fire_retardant',         'boolean', 'plywood_protection', NULL,    false),
  ('fire_delay_mins',        'number',  'plywood_protection', NULL,    false),
  ('warranty_years',         'number',  'plywood_commercial', NULL,    false),
  ('money_back_multiple',    'list',    'plywood_commercial', NULL,    false),
  ('fully_covered_plan',     'boolean', 'plywood_commercial', NULL,    false)
) AS v(code, data_type, group_code, unit_code, localizable)
JOIN attribute_group g ON g.code = v.group_code
LEFT JOIN unit u ON u.code = v.unit_code
ON CONFLICT (code) DO NOTHING;

INSERT INTO attribute_tr (attribute_id, locale_code, label)
SELECT a.id, v.locale, v.label FROM (VALUES
  ('water_resistance_grade', 'en-GB', 'Water Resistance Grade'),
  ('water_resistance_grade', 'de-DE', 'Wasserbeständigkeitsklasse'),
  ('emission_standard',      'en-GB', 'Formaldehyde Emission Grade'),
  ('emission_standard',      'de-DE', 'Formaldehyd-Emissionsklasse'),
  ('core_species',           'en-GB', 'Core Timber Species'),
  ('core_species',           'de-DE', 'Kernholz-Holzart'),
  ('resin_type',             'en-GB', 'Adhesive / Resin System'),
  ('resin_type',             'de-DE', 'Klebstoff- / Harzsystem'),
  ('calibration_technology', 'en-GB', 'Calibration Technology'),
  ('calibration_technology', 'de-DE', 'Kalibriertechnologie'),
  ('sheet_dimension',        'en-GB', 'Standard Sheet Size'),
  ('sheet_dimension',        'de-DE', 'Standard-Plattenmaß'),
  ('thickness_mm',           'en-GB', 'Calibrated Thickness'),
  ('thickness_mm',           'de-DE', 'Kalibrierte Materialstärke'),
  ('density_g_cm3',          'en-GB', 'Panel Density'),
  ('density_g_cm3',          'de-DE', 'Plattendichte'),
  ('moisture_content_pct',   'en-GB', 'Moisture Content (%)'),
  ('moisture_content_pct',   'de-DE', 'Feuchtigkeitsgehalt (%)'),
  ('borer_fungus_proof',     'en-GB', 'Borer & Termite Proof'),
  ('borer_fungus_proof',     'de-DE', 'Bohrkäfer- & Termitenschutz'),
  ('fire_retardant',         'en-GB', 'Fire Retardant Treated'),
  ('fire_retardant',         'de-DE', 'Feuerhemmend behandelt'),
  ('fire_delay_mins',        'en-GB', 'Fire Penetration Delay (Minutes)'),
  ('fire_delay_mins',        'de-DE', 'Feuerdurchtrittsverzögerung (Minuten)'),
  ('warranty_years',         'en-GB', 'Warranty Duration (Years)'),
  ('warranty_years',         'de-DE', 'Garantiedauer (Jahre)'),
  ('money_back_multiple',    'en-GB', 'Money-Back Guarantee'),
  ('money_back_multiple',    'de-DE', 'Geld-zurück-Garantiefaktor'),
  ('fully_covered_plan',     'en-GB', '11-Year Fully Covered+ Restoration Plan'),
  ('fully_covered_plan',     'de-DE', '11-Jahre Rundumschutz-Sanierungsplan')
) AS v(code, locale, label)
JOIN attribute a ON a.code = v.code
ON CONFLICT (attribute_id, locale_code) DO NOTHING;

-- 6. ATTRIBUTE OPTIONS --------------------------------------------------------
-- water_resistance_grade
INSERT INTO attribute_option (attribute_id, code, sort_order)
SELECT a.id, v.code, v.sort_order FROM (VALUES
  ('BWP', 1),
  ('BWR', 2),
  ('MR', 3),
  ('STRUCTURAL', 4)
) AS v(code, sort_order)
CROSS JOIN (SELECT id FROM attribute WHERE code = 'water_resistance_grade') a
ON CONFLICT (attribute_id, code) DO NOTHING;

INSERT INTO attribute_option_tr (option_id, locale_code, label)
SELECT o.id, v.locale, v.label FROM (VALUES
  ('BWP', 'en-GB', 'BWP — Boiling Water Proof (Marine Grade, 72h boil test)'),
  ('BWP', 'de-DE', 'BWP — Kochwasserfest (Marinequalität, 72h Kochtest)'),
  ('BWR', 'en-GB', 'BWR — Boiling Water Resistant (Exterior Grade)'),
  ('BWR', 'de-DE', 'BWR — Kochwasserbeständig (Außenbereich)'),
  ('MR',  'en-GB', 'MR — Moisture Resistant (Commercial Interior)'),
  ('MR',  'de-DE', 'MR — Feuchtigkeitsbeständig (Innenausbau)'),
  ('STRUCTURAL', 'en-GB', 'Structural Grade (IS:10701 Heavy Load)'),
  ('STRUCTURAL', 'de-DE', 'Konstruktionsklasse (IS:10701 Hochlast)')
) AS v(code, locale, label)
JOIN attribute_option o ON o.code = v.code
JOIN attribute a ON a.id = o.attribute_id AND a.code = 'water_resistance_grade'
ON CONFLICT (option_id, locale_code) DO NOTHING;

-- emission_standard
INSERT INTO attribute_option (attribute_id, code, sort_order)
SELECT a.id, v.code, v.sort_order FROM (VALUES
  ('E0', 1),
  ('E1', 2),
  ('CARB_P2', 3),
  ('EPA_TSCA', 4)
) AS v(code, sort_order)
CROSS JOIN (SELECT id FROM attribute WHERE code = 'emission_standard') a
ON CONFLICT (attribute_id, code) DO NOTHING;

INSERT INTO attribute_option_tr (option_id, locale_code, label)
SELECT o.id, v.locale, v.label FROM (VALUES
  ('E0', 'en-GB', 'E0 — Zero Emission (≤ 3 mg/100g formalin)'),
  ('E0', 'de-DE', 'E0 — Emissionsfrei (≤ 3 mg/100g Formalin)'),
  ('E1', 'en-GB', 'E1 — Low Emission (≤ 8 mg/100g formalin)'),
  ('E1', 'de-DE', 'E1 — Emissionsarm (≤ 8 mg/100g Formalin)'),
  ('CARB_P2', 'en-GB', 'CARB Phase 2 Certified (California Standards)'),
  ('CARB_P2', 'de-DE', 'CARB Phase 2 zertifiziert (Kalifornien-Standard)'),
  ('EPA_TSCA', 'en-GB', 'EPA TSCA Title VI Compliant'),
  ('EPA_TSCA', 'de-DE', 'EPA TSCA Title VI konform')
) AS v(code, locale, label)
JOIN attribute_option o ON o.code = v.code
JOIN attribute a ON a.id = o.attribute_id AND a.code = 'emission_standard'
ON CONFLICT (option_id, locale_code) DO NOTHING;

-- core_species
INSERT INTO attribute_option (attribute_id, code, sort_order)
SELECT a.id, v.code, v.sort_order FROM (VALUES
  ('GURJAN',     1),
  ('EUCALYPTUS', 2),
  ('HARDWOOD',   3),
  ('POPLAR',     4),
  ('COMBI',      5)
) AS v(code, sort_order)
CROSS JOIN (SELECT id FROM attribute WHERE code = 'core_species') a
ON CONFLICT (attribute_id, code) DO NOTHING;

INSERT INTO attribute_option_tr (option_id, locale_code, label)
SELECT o.id, v.locale, v.label FROM (VALUES
  ('GURJAN',     'en-GB', '100% Gurjan Hardwood Timber'),
  ('GURJAN',     'de-DE', '100% Gurjan-Edelholz'),
  ('EUCALYPTUS', 'en-GB', 'Plantation Eucalyptus Core'),
  ('EUCALYPTUS', 'de-DE', 'Plantagen-Eukalyptus-Kern'),
  ('HARDWOOD',   'en-GB', 'Selected High-Density Hardwood Timber'),
  ('HARDWOOD',   'de-DE', 'Ausgewähltes hochdichtes Hartholz'),
  ('POPLAR',     'en-GB', 'Selected Poplar Timber'),
  ('POPLAR',     'de-DE', 'Ausgewähltes Pappelholz'),
  ('COMBI',      'en-GB', 'Combi Hardwood / Plantation Core'),
  ('COMBI',      'de-DE', 'Kombikern Hartholz / Plantagenholz')
) AS v(code, locale, label)
JOIN attribute_option o ON o.code = v.code
JOIN attribute a ON a.id = o.attribute_id AND a.code = 'core_species'
ON CONFLICT (option_id, locale_code) DO NOTHING;

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
SELECT o.id, v.locale, v.label FROM (VALUES
  ('UNEXTENDED_PF', 'en-GB', 'Unextended Phenol Formaldehyde (PF) Resin'),
  ('UNEXTENDED_PF', 'de-DE', 'Ungestrecktes Phenol-Formaldehyd-Harz (PF)'),
  ('MUF',           'en-GB', 'Melamine Urea Formaldehyde (MUF) Resin'),
  ('MUF',           'de-DE', 'Melamin-Harnstoff-Formaldehyd-Harz (MUF)'),
  ('UF',            'en-GB', 'Urea Formaldehyde (UF) Resin'),
  ('UF',            'de-DE', 'Harnstoff-Formaldehyd-Harz (UF)')
) AS v(code, locale, label)
JOIN attribute_option o ON o.code = v.code
JOIN attribute a ON a.id = o.attribute_id AND a.code = 'resin_type'
ON CONFLICT (option_id, locale_code) DO NOTHING;

-- calibration_technology
INSERT INTO attribute_option (attribute_id, code, sort_order)
SELECT a.id, v.code, v.sort_order FROM (VALUES
  ('PENTA_TECH',   1),
  ('FOUR_PRESS',   2),
  ('DOUBLE_PRESS', 3)
) AS v(code, sort_order)
CROSS JOIN (SELECT id FROM attribute WHERE code = 'calibration_technology') a
ON CONFLICT (attribute_id, code) DO NOTHING;

INSERT INTO attribute_option_tr (option_id, locale_code, label)
SELECT o.id, v.locale, v.label FROM (VALUES
  ('PENTA_TECH',   'en-GB', 'Penta (5) Tech — Multi-stage Calibration'),
  ('PENTA_TECH',   'de-DE', 'Penta (5) Tech — 5-Stufen-Kalibrierung'),
  ('FOUR_PRESS',   'en-GB', '4-Press Quad Calibration'),
  ('FOUR_PRESS',   'de-DE', '4-Press Vierfach-Kalibrierung'),
  ('DOUBLE_PRESS', 'en-GB', 'Standard Double Hot Press'),
  ('DOUBLE_PRESS', 'de-DE', 'Standard-Doppelheißpresse')
) AS v(code, locale, label)
JOIN attribute_option o ON o.code = v.code
JOIN attribute a ON a.id = o.attribute_id AND a.code = 'calibration_technology'
ON CONFLICT (option_id, locale_code) DO NOTHING;

-- sheet_dimension
INSERT INTO attribute_option (attribute_id, code, sort_order)
SELECT a.id, v.code, v.sort_order FROM (VALUES
  ('2440X1220', 1),
  ('2440X915',  2),
  ('2135X1220', 3),
  ('2135X915',  4),
  ('1830X1220', 5),
  ('1830X915',  6)
) AS v(code, sort_order)
CROSS JOIN (SELECT id FROM attribute WHERE code = 'sheet_dimension') a
ON CONFLICT (attribute_id, code) DO NOTHING;

INSERT INTO attribute_option_tr (option_id, locale_code, label)
SELECT o.id, v.locale, v.label FROM (VALUES
  ('2440X1220', 'en-GB', '8 ft × 4 ft (2440 × 1220 mm) — Standard'),
  ('2440X1220', 'de-DE', '8 Fuß × 4 Fuß (2440 × 1220 mm) — Standardmaß'),
  ('2440X915',  'en-GB', '8 ft × 3 ft (2440 × 915 mm)'),
  ('2440X915',  'de-DE', '8 Fuß × 3 Fuß (2440 × 915 mm)'),
  ('2135X1220', 'en-GB', '7 ft × 4 ft (2135 × 1220 mm)'),
  ('2135X1220', 'de-DE', '7 Fuß × 4 Fuß (2135 × 1220 mm)'),
  ('2135X915',  'en-GB', '7 ft × 3 ft (2135 × 915 mm)'),
  ('2135X915',  'de-DE', '7 Fuß × 3 Fuß (2135 × 915 mm)'),
  ('1830X1220', 'en-GB', '6 ft × 4 ft (1830 × 1220 mm)'),
  ('1830X1220', 'de-DE', '6 Fuß × 4 Fuß (1830 × 1220 mm)'),
  ('1830X915',  'en-GB', '6 ft × 3 ft (1830 × 915 mm)'),
  ('1830X915',  'de-DE', '6 Fuß × 3 Fuß (1830 × 915 mm)')
) AS v(code, locale, label)
JOIN attribute_option o ON o.code = v.code
JOIN attribute a ON a.id = o.attribute_id AND a.code = 'sheet_dimension'
ON CONFLICT (option_id, locale_code) DO NOTHING;

-- money_back_multiple
INSERT INTO attribute_option (attribute_id, code, sort_order)
SELECT a.id, v.code, v.sort_order FROM (VALUES
  ('NONE', 1),
  ('2X',   2),
  ('7X',   3)
) AS v(code, sort_order)
CROSS JOIN (SELECT id FROM attribute WHERE code = 'money_back_multiple') a
ON CONFLICT (attribute_id, code) DO NOTHING;

INSERT INTO attribute_option_tr (option_id, locale_code, label)
SELECT o.id, v.locale, v.label FROM (VALUES
  ('NONE', 'en-GB', 'Standard Product Replacement'),
  ('NONE', 'de-DE', 'Standard-Ersatzgarantie'),
  ('2X',   'en-GB', '2X Money-Back Guarantee'),
  ('2X',   'de-DE', '2-fache Geld-zurück-Garantie'),
  ('7X',   'en-GB', '7X Money-Back Guarantee (Lifetime Coverage)'),
  ('7X',   'de-DE', '7-fache Geld-zurück-Garantie (Lebenslang)')
) AS v(code, locale, label)
JOIN attribute_option o ON o.code = v.code
JOIN attribute a ON a.id = o.attribute_id AND a.code = 'money_back_multiple'
ON CONFLICT (option_id, locale_code) DO NOTHING;

-- 7. ATTRIBUTE SET (plywood_core) --------------------------------------------
INSERT INTO attribute_set (code, name) VALUES
  ('plywood_core', 'Plywood & Engineered Panels — Core Attributes')
ON CONFLICT (code) DO NOTHING;

INSERT INTO attribute_set_attribute (set_id, attribute_id, is_required, sort_order)
SELECT s.id, a.id, v.required, v.sort_order FROM (VALUES
  ('water_resistance_grade', true,  1),
  ('emission_standard',      true,  2),
  ('core_species',           true,  3),
  ('resin_type',             true,  4),
  ('calibration_technology', false, 5),
  ('sheet_dimension',        true,  6),
  ('thickness_mm',           true,  7),
  ('density_g_cm3',          false, 8),
  ('moisture_content_pct',   false, 9),
  ('borer_fungus_proof',     true,  10),
  ('fire_retardant',         true,  11),
  ('fire_delay_mins',        false, 12),
  ('warranty_years',         true,  13),
  ('money_back_multiple',    false, 14),
  ('fully_covered_plan',     false, 15)
) AS v(code, required, sort_order)
JOIN attribute a ON a.code = v.code
CROSS JOIN (SELECT id FROM attribute_set WHERE code = 'plywood_core') s
ON CONFLICT (set_id, attribute_id) DO NOTHING;

-- Bind the attribute set to the plywood taxonomy subtree
UPDATE taxonomy_node
SET attribute_set_id = (SELECT id FROM attribute_set WHERE code = 'plywood_core')
WHERE path <@ 'greenply.plywood_blockboard';

-- 8. APPLICATIONS -------------------------------------------------------------
INSERT INTO application (group_code, code, sort_order) VALUES
  ('USE_CASE', 'modular_kitchen',       1),
  ('USE_CASE', 'wardrobe_cabinetry',    2),
  ('USE_CASE', 'bed_structural',        3),
  ('USE_CASE', 'wall_panelling',        4),
  ('USE_CASE', 'false_ceiling',         5),
  ('USE_CASE', 'partitions_doors',      6),
  ('USE_CASE', 'marine_boat_building',  7),
  ('AREA',     'wet_moisture_prone',    1),
  ('AREA',     'dry_interior',          2),
  ('AREA',     'fire_exit_corridors',   3),
  ('AREA',     'exterior_semi_covered', 4)
ON CONFLICT (group_code, code) DO NOTHING;

INSERT INTO application_tr (application_id, locale_code, label)
SELECT a.id, v.locale, v.label FROM (VALUES
  ('USE_CASE', 'modular_kitchen',      'en-GB', 'Modular Kitchen Cabinets & Shutters'),
  ('USE_CASE', 'modular_kitchen',      'de-DE', 'Modulare Küchenkorpusse & Fronten'),
  ('USE_CASE', 'wardrobe_cabinetry',   'en-GB', 'Wardrobes & Built-in Closets'),
  ('USE_CASE', 'wardrobe_cabinetry',   'de-DE', 'Einbauschränke & Garderoben'),
  ('USE_CASE', 'bed_structural',       'en-GB', 'Beds & Structural Load-Bearing Furniture'),
  ('USE_CASE', 'bed_structural',       'de-DE', 'Bettkonstruktionen & tragende Möbel'),
  ('USE_CASE', 'wall_panelling',       'en-GB', 'Wall Cladding & Acoustic Panelling'),
  ('USE_CASE', 'wall_panelling',       'de-DE', 'Wandverkleidung & Akustikpaneele'),
  ('USE_CASE', 'false_ceiling',        'en-GB', 'False Ceilings & Roof Frameworks'),
  ('USE_CASE', 'false_ceiling',        'de-DE', 'Abgehängte Decken & Unterkonstruktionen'),
  ('USE_CASE', 'partitions_doors',     'en-GB', 'Office Partitions & Core Frames'),
  ('USE_CASE', 'partitions_doors',     'de-DE', 'Bürotrennwände & Rahmen'),
  ('USE_CASE', 'marine_boat_building', 'en-GB', 'Marine Vessels & Boat Interiors'),
  ('USE_CASE', 'marine_boat_building', 'de-DE', 'Bootsbau & Schiffsausstattung'),
  ('AREA',     'wet_moisture_prone',   'en-GB', 'High-Moisture & Water-Exposed Areas'),
  ('AREA',     'wet_moisture_prone',   'de-DE', 'Feuchträume & Wasser-exponierte Bereiche'),
  ('AREA',     'dry_interior',         'en-GB', 'Dry Indoor Living Areas'),
  ('AREA',     'dry_interior',         'de-DE', 'Trockene Wohninnenräume'),
  ('AREA',     'fire_exit_corridors',  'en-GB', 'Public Corridors & Fire-Escape Passages'),
  ('AREA',     'fire_exit_corridors',  'de-DE', 'Fluchtwege & Brandschutzkorridore'),
  ('AREA',     'exterior_semi_covered','en-GB', 'Semi-Covered Exterior Areas'),
  ('AREA',     'exterior_semi_covered','de-DE', 'Halbüberdachte Außenbereiche')
) AS v(gcode, code, locale, label)
JOIN application a ON a.group_code = v.gcode AND a.code = v.code
ON CONFLICT (application_id, locale_code) DO NOTHING;

-- 9. SHOWCASE PRODUCTS (GREENPLY FLAGSHIP SUITE) -----------------------------
-- Product 1: Green Club 700 (Structural E0 Plywood)
INSERT INTO product (product_code, primary_node_id, brand_id, attribute_set_id, base_locale,
                     lifecycle_state, workflow_state, owner_display_name, gtin, sap_material_no, row_version)
SELECT 'GP-CLUB-700', n.id, b.id, s.id, 'en-GB', 'ACTIVE', 'Published', 'Markus Klein', '8901234500700', 'SAP-GP-700', 1
FROM taxonomy_node n
CROSS JOIN brand b
CROSS JOIN attribute_set s
WHERE n.code = 'green_club' AND b.code = 'greenply' AND s.code = 'plywood_core'
ON CONFLICT (product_code) DO NOTHING;

INSERT INTO product_tr (product_id, locale_code, name, short_desc, long_desc, marketing, slug, translation_status)
SELECT p.id, v.locale, v.name, v.short_desc, v.long_desc, v.marketing, v.slug, 'approved'
FROM (VALUES
  ('en-GB', 'Green Club 700 Plywood',
   'Flagship structural E0 zero-emission plywood with 2X fire resistance and lifetime warranty.',
   'Green Club 700 is crafted from 100% selected hardwood timber with unextended phenolic resin. Certified E0 emission for healthy indoor air, 2X fire retardant per IS:5509, and lifetime warranty with 7X money-back guarantee.',
   'Zero Emission E0 • 2X Fire Retardant • Lifetime Warranty • Penta 5 Tech',
   'green-club-700-plywood'),
  ('de-DE', 'Green Club 700 Sperrholz',
   'Flaggschiff-Struktursperrholz E0 mit 2-fachem Brandschutz und lebenslanger Garantie.',
   'Green Club 700 wird aus 100% ausgewähltem Hartholz mit ungestrecktem Kunstharz gefertigt. Emissionsklasse E0 für gesunde Innenräume, 2-facher Brandschutz und lebenslange Garantie mit 7-facher Geld-zurück-Garantie.',
   'Emissionsfrei E0 • 2X Brandschutz • Lebenslange Garantie • Penta-5-Technologie',
   'green-club-700-sperrholz')
) AS v(locale, name, short_desc, long_desc, marketing, slug)
CROSS JOIN (SELECT id FROM product WHERE product_code = 'GP-CLUB-700') p
ON CONFLICT (product_id, locale_code) DO NOTHING;

-- Product 2: Green Platinum (2X Fire Retardant BWP)
INSERT INTO product (product_code, primary_node_id, brand_id, attribute_set_id, base_locale,
                     lifecycle_state, workflow_state, owner_display_name, gtin, sap_material_no, row_version)
SELECT 'GP-PLAT-BWP', n.id, b.id, s.id, 'en-GB', 'ACTIVE', 'Approval', 'Anna Becker', '8901234500701', 'SAP-GP-PLAT', 1
FROM taxonomy_node n
CROSS JOIN brand b
CROSS JOIN attribute_set s
WHERE n.code = 'green_platinum' AND b.code = 'greenply' AND s.code = 'plywood_core'
ON CONFLICT (product_code) DO NOTHING;

INSERT INTO product_tr (product_id, locale_code, name, short_desc, long_desc, marketing, slug, translation_status)
SELECT p.id, v.locale, v.name, v.short_desc, v.long_desc, v.marketing, v.slug, 'approved'
FROM (VALUES
  ('en-GB', 'Green Platinum BWP Plywood',
   'Heavy-duty 100% boiling water proof plywood with 2X fire delay and 30-year warranty.',
   'Green Platinum is manufactured using unextended phenolic resin and vacuum-pressure chemical treatment. Fully resistant to boiling water, borer, and termite attacks, carrying a 30-year warranty with 2X money back.',
   '100% BWP Marine Proof • 2X Fire Retardant • 30 Years Warranty (2X Money Back) • E0 Emission',
   'green-platinum-bwp-plywood'),
  ('de-DE', 'Green Platinum BWP Sperrholz',
   'Hochbelastbares, 100% kochwasserfestes BWP-Sperrholz mit Brandschutz und 30 Jahren Garantie.',
   'Green Platinum bietet extreme Feuchtigkeitsbeständigkeit für Küchen und Nassräume sowie 30 Jahre Garantie mit 2-fachem Geld-zurück-Schutz.',
   '100% BWP Kochwasserfest • 30 Jahre Garantie • E0 Emissionsarm',
   'green-platinum-bwp-sperrholz')
) AS v(locale, name, short_desc, long_desc, marketing, slug)
CROSS JOIN (SELECT id FROM product WHERE product_code = 'GP-PLAT-BWP') p
ON CONFLICT (product_id, locale_code) DO NOTHING;

-- Product 3: Green 710 Marine
INSERT INTO product (product_code, primary_node_id, brand_id, attribute_set_id, base_locale,
                     lifecycle_state, workflow_state, owner_display_name, gtin, sap_material_no, row_version)
SELECT 'GP-710-MAR', n.id, b.id, s.id, 'en-GB', 'ACTIVE', 'Review', 'Lena Vogt', '8901234500710', 'SAP-GP-710', 1
FROM taxonomy_node n
CROSS JOIN brand b
CROSS JOIN attribute_set s
WHERE n.code = 'green_710' AND b.code = 'greenply' AND s.code = 'plywood_core'
ON CONFLICT (product_code) DO NOTHING;

INSERT INTO product_tr (product_id, locale_code, name, short_desc, long_desc, marketing, slug, translation_status)
SELECT p.id, v.locale, v.name, v.short_desc, v.long_desc, v.marketing, v.slug, 'approved'
FROM (VALUES
  ('en-GB', 'Green 710 Marine Plywood',
   'Certified IS:710 marine plywood designed for high-humidity and marine environments.',
   'Green 710 Marine is engineered with selected hardwood timber and multi-stage preservative treatment, withstands rigorous 72-hour boiling water testing without delamination.',
   'Certified IS:710 Marine • 25 Years Warranty • Hardwood Core • Termite and Borer Proof',
   'green-710-marine-plywood'),
  ('de-DE', 'Green 710 Marine Sperrholz',
   'Zertifiziertes IS:710 Marine-Sperrholz für Feuchträume und Bootsbau.',
   'Green 710 Marine widersteht 72-stündigem Kochen ohne Delaminierung und bietet 25 Jahre Garantie.',
   'IS:710 Marinesperrholz • 25 Jahre Garantie • 100% Hartholz',
   'green-710-marine-sperrholz')
) AS v(locale, name, short_desc, long_desc, marketing, slug)
CROSS JOIN (SELECT id FROM product WHERE product_code = 'GP-710-MAR') p
ON CONFLICT (product_id, locale_code) DO NOTHING;

-- Product 4: Green Gold (Commercial BWP)
INSERT INTO product (product_code, primary_node_id, brand_id, attribute_set_id, base_locale,
                     lifecycle_state, workflow_state, owner_display_name, gtin, sap_material_no, row_version)
SELECT 'GP-GOLD-BWP', n.id, b.id, s.id, 'en-GB', 'ACTIVE', 'Draft', 'Sophie Adler', '8901234500720', 'SAP-GP-GOLD', 1
FROM taxonomy_node n
CROSS JOIN brand b
CROSS JOIN attribute_set s
WHERE n.code = 'green_gold' AND b.code = 'greenply' AND s.code = 'plywood_core'
ON CONFLICT (product_code) DO NOTHING;

INSERT INTO product_tr (product_id, locale_code, name, short_desc, long_desc, marketing, slug, translation_status)
SELECT p.id, v.locale, v.name, v.short_desc, v.long_desc, v.marketing, v.slug, 'approved'
FROM (VALUES
  ('en-GB', 'Green Gold BWP Plywood',
   'Precision-calibrated commercial BWP plywood with 11-Year Fully Covered+ restoration guarantee.',
   'Green Gold is a high-grade moisture-proof plywood featuring 4-press quad calibration for super-flat surface finish and superior screw-holding strength.',
   '30 Years Warranty • 11-Year Fully Covered+ Plan • 4-Press Calibration • Borer Proof',
   'green-gold-bwp-plywood'),
  ('de-DE', 'Green Gold BWP Sperrholz',
   'Präzisionskalibriertes BWP-Sperrholz mit 11-Jahre Rundumschutz.',
   'Green Gold bietet 4-Stufen-Kalibrierung und Rundumschutz für anspruchsvollen Möbelbau.',
   '30 Jahre Garantie • 11 Jahre Rundumschutz • 4-Stufen Kalibriert',
   'green-gold-bwp-sperrholz')
) AS v(locale, name, short_desc, long_desc, marketing, slug)
CROSS JOIN (SELECT id FROM product WHERE product_code = 'GP-GOLD-BWP') p
ON CONFLICT (product_id, locale_code) DO NOTHING;

-- 10. PRODUCT SKUs (VARIANTS) -------------------------------------------------
INSERT INTO sku (product_id, sku_code, gtin, erp_material_no, status, sort_order)
SELECT p.id, v.sku_code, v.gtin, v.erp, 'active', v.sort_order
FROM (VALUES
  ('GP-CLUB-700', 'GP-C700-8X4-19MM', '8901234519700', 'ERP-GC700-19', 1),
  ('GP-CLUB-700', 'GP-C700-8X4-16MM', '8901234516700', 'ERP-GC700-16', 2),
  ('GP-CLUB-700', 'GP-C700-8X4-12MM', '8901234512700', 'ERP-GC700-12', 3),
  ('GP-CLUB-700', 'GP-C700-8X4-6MM',  '8901234506700', 'ERP-GC700-06', 4),
  ('GP-PLAT-BWP', 'GP-PLAT-8X4-19MM', '8901234519701', 'ERP-PLAT-19',  1),
  ('GP-PLAT-BWP', 'GP-PLAT-8X4-16MM', '8901234516701', 'ERP-PLAT-16',  2),
  ('GP-PLAT-BWP', 'GP-PLAT-8X4-12MM', '8901234512701', 'ERP-PLAT-12',  3),
  ('GP-710-MAR',  'GP-710-8X4-19MM',  '8901234519710', 'ERP-M710-19',  1),
  ('GP-710-MAR',  'GP-710-8X4-12MM',  '8901234512710', 'ERP-M710-12',  2),
  ('GP-GOLD-BWP', 'GP-GOLD-8X4-19MM', '8901234519720', 'ERP-GOLD-19',  1),
  ('GP-GOLD-BWP', 'GP-GOLD-8X4-16MM', '8901234516720', 'ERP-GOLD-16',  2)
) AS v(p_code, sku_code, gtin, erp, sort_order)
JOIN product p ON p.product_code = v.p_code
ON CONFLICT (sku_code) DO NOTHING;

-- Normalize any casing in sku status
UPDATE sku SET status = 'active' WHERE status = 'Active';

-- 11. ATTRIBUTE VALUES (GREEN CLUB 700) ----------------------------------------
-- Populate 100% completeness for Green Club 700
INSERT INTO attribute_value (product_id, attribute_id, value_option_id, data_state)
SELECT p.id, a.id, o.id, 'approved'
FROM (VALUES
  ('GP-CLUB-700', 'water_resistance_grade', 'STRUCTURAL'),
  ('GP-CLUB-700', 'emission_standard',      'E0'),
  ('GP-CLUB-700', 'core_species',           'GURJAN'),
  ('GP-CLUB-700', 'resin_type',             'UNEXTENDED_PF'),
  ('GP-CLUB-700', 'calibration_technology', 'PENTA_TECH'),
  ('GP-CLUB-700', 'sheet_dimension',        '2440X1220'),
  ('GP-CLUB-700', 'money_back_multiple',    '7X')
) AS v(p_code, a_code, o_code)
JOIN product p ON p.product_code = v.p_code
JOIN attribute a ON a.code = v.a_code
JOIN attribute_option o ON o.attribute_id = a.id AND o.code = v.o_code
ON CONFLICT DO NOTHING;

INSERT INTO attribute_value (product_id, attribute_id, value_boolean, data_state)
SELECT p.id, a.id, v.val, 'approved'
FROM (VALUES
  ('GP-CLUB-700', 'borer_fungus_proof', true),
  ('GP-CLUB-700', 'fire_retardant',     true),
  ('GP-CLUB-700', 'fully_covered_plan', false)
) AS v(p_code, a_code, val)
JOIN product p ON p.product_code = v.p_code
JOIN attribute a ON a.code = v.a_code
ON CONFLICT DO NOTHING;

INSERT INTO attribute_value (product_id, attribute_id, value_number, data_state)
SELECT p.id, a.id, v.val, 'approved'
FROM (VALUES
  ('GP-CLUB-700', 'thickness_mm',         19.0),
  ('GP-CLUB-700', 'density_g_cm3',        0.82),
  ('GP-CLUB-700', 'moisture_content_pct', 9.5),
  ('GP-CLUB-700', 'fire_delay_mins',      45),
  ('GP-CLUB-700', 'warranty_years',       99)
) AS v(p_code, a_code, val)
JOIN product p ON p.product_code = v.p_code
JOIN attribute a ON a.code = v.a_code
ON CONFLICT DO NOTHING;

-- Populate Green Platinum values
INSERT INTO attribute_value (product_id, attribute_id, value_option_id, data_state)
SELECT p.id, a.id, o.id, 'approved'
FROM (VALUES
  ('GP-PLAT-BWP', 'water_resistance_grade', 'BWP'),
  ('GP-PLAT-BWP', 'emission_standard',      'E0'),
  ('GP-PLAT-BWP', 'core_species',           'HARDWOOD'),
  ('GP-PLAT-BWP', 'resin_type',             'UNEXTENDED_PF'),
  ('GP-PLAT-BWP', 'calibration_technology', 'FOUR_PRESS'),
  ('GP-PLAT-BWP', 'sheet_dimension',        '2440X1220'),
  ('GP-PLAT-BWP', 'money_back_multiple',    '2X')
) AS v(p_code, a_code, o_code)
JOIN product p ON p.product_code = v.p_code
JOIN attribute a ON a.code = v.a_code
JOIN attribute_option o ON o.attribute_id = a.id AND o.code = v.o_code
ON CONFLICT DO NOTHING;

INSERT INTO attribute_value (product_id, attribute_id, value_boolean, data_state)
SELECT p.id, a.id, v.val, 'approved'
FROM (VALUES
  ('GP-PLAT-BWP', 'borer_fungus_proof', true),
  ('GP-PLAT-BWP', 'fire_retardant',     true),
  ('GP-PLAT-BWP', 'fully_covered_plan', false)
) AS v(p_code, a_code, val)
JOIN product p ON p.product_code = v.p_code
JOIN attribute a ON a.code = v.a_code
ON CONFLICT DO NOTHING;

INSERT INTO attribute_value (product_id, attribute_id, value_number, data_state)
SELECT p.id, a.id, v.val, 'approved'
FROM (VALUES
  ('GP-PLAT-BWP', 'thickness_mm',   19.0),
  ('GP-PLAT-BWP', 'warranty_years', 30)
) AS v(p_code, a_code, val)
JOIN product p ON p.product_code = v.p_code
JOIN attribute a ON a.code = v.a_code
ON CONFLICT DO NOTHING;

-- 12. PACKAGING HIERARCHY -----------------------------------------------------
-- EACH (1 Sheet: 2440x1220x19 mm, ~38 kg) -> CARTON (Banded crate: 25 sheets, ~950 kg)
INSERT INTO packaging_unit (sku_id, parent_id, level, contains_qty, gtin, gross_weight, net_weight, length_mm, width_mm, height_mm, is_base_unit)
SELECT s.id, NULL, 'EACH', 1, s.gtin, 38.5, 37.8, 2440, 1220, 19, true
FROM sku s
WHERE s.sku_code = 'GP-C700-8X4-19MM'
ON CONFLICT (sku_id, level) DO NOTHING;

INSERT INTO packaging_unit (sku_id, parent_id, level, contains_qty, gtin, gross_weight, net_weight, length_mm, width_mm, height_mm, is_base_unit)
SELECT s.id, p_each.id, 'CARTON', 25, '8901234599700', 980.0, 945.0, 2460, 1240, 520, false
FROM sku s
JOIN packaging_unit p_each ON p_each.sku_id = s.id AND p_each.level = 'EACH'
WHERE s.sku_code = 'GP-C700-8X4-19MM'
ON CONFLICT (sku_id, level) DO NOTHING;

-- 13. COMPLIANCE DOCUMENTS ----------------------------------------------------
INSERT INTO compliance_document (product_id, doc_type, reference, standard, authority, valid_from, valid_to, status)
SELECT p.id, v.doc_type, v.ref, v.std, v.auth, v.v_from::date, v.v_to::date, 'valid'
FROM (VALUES
  ('GP-CLUB-700', 'ISI',  'BIS-CM/L-10701-700', 'IS:10701', 'Bureau of Indian Standards', '2025-01-01', '2030-12-31'),
  ('GP-CLUB-700', 'ISI',  'BIS-CM/L-0710-700',  'IS:710',   'Bureau of Indian Standards', '2025-01-01', '2030-12-31'),
  ('GP-CLUB-700', 'CARB', 'CARB-E0-GP-700',     'CARB P2',  'California Air Resources',   '2024-06-01', '2029-05-31'),
  ('GP-CLUB-700', 'FSC',  'FSC-C123456-GP',     'FSC-STD',  'Forest Stewardship Council', '2023-01-01', '2028-12-31'),
  ('GP-PLAT-BWP', 'ISI',  'BIS-CM/L-5509-PLAT', 'IS:5509',  'Bureau of Indian Standards', '2025-01-01', '2030-12-31'),
  ('GP-PLAT-BWP', 'ISI',  'BIS-CM/L-0710-PLAT', 'IS:710',   'Bureau of Indian Standards', '2025-01-01', '2030-12-31'),
  ('GP-710-MAR',  'ISI',  'BIS-CM/L-0710-M710', 'IS:710',   'Bureau of Indian Standards', '2024-01-01', '2029-12-31')
) AS v(p_code, doc_type, ref, std, auth, v_from, v_to)
JOIN product p ON p.product_code = v.p_code
WHERE NOT EXISTS (
  SELECT 1 FROM compliance_document cd WHERE cd.product_id = p.id AND cd.reference = v.ref
);

-- 14. PRODUCT APPLICATIONS ----------------------------------------------------
INSERT INTO product_application (product_id, application_id)
SELECT p.id, a.id
FROM (VALUES
  ('GP-CLUB-700', 'modular_kitchen'),
  ('GP-CLUB-700', 'wardrobe_cabinetry'),
  ('GP-CLUB-700', 'bed_structural'),
  ('GP-CLUB-700', 'wet_moisture_prone'),
  ('GP-CLUB-700', 'fire_exit_corridors'),
  ('GP-PLAT-BWP', 'modular_kitchen'),
  ('GP-PLAT-BWP', 'wet_moisture_prone'),
  ('GP-PLAT-BWP', 'fire_exit_corridors'),
  ('GP-710-MAR',  'marine_boat_building'),
  ('GP-710-MAR',  'wet_moisture_prone'),
  ('GP-GOLD-BWP', 'wardrobe_cabinetry'),
  ('GP-GOLD-BWP', 'dry_interior')
) AS v(p_code, app_code)
JOIN product p ON p.product_code = v.p_code
JOIN application a ON a.code = v.app_code
ON CONFLICT DO NOTHING;

-- 15. RELATIONSHIPS -----------------------------------------------------------
-- UP_SELL: Green 710 -> Green Platinum -> Green Club 700
INSERT INTO product_relationship (from_product_id, to_product_id, relation_type, priority)
SELECT p_from.id, p_to.id, v.rel_type, v.priority
FROM (VALUES
  ('GP-GOLD-BWP', 'GP-PLAT-BWP', 'UP_SELL', 1),
  ('GP-PLAT-BWP', 'GP-CLUB-700', 'UP_SELL', 1),
  ('GP-710-MAR',  'GP-CLUB-700', 'UP_SELL', 2),
  ('GP-CLUB-700', 'GP-PLAT-BWP', 'COMPATIBLE', 1)
) AS v(from_code, to_code, rel_type, priority)
JOIN product p_from ON p_from.product_code = v.from_code
JOIN product p_to   ON p_to.product_code = v.to_code
ON CONFLICT DO NOTHING;

-- Ensure full-text search document is freshly generated for new products
UPDATE catalog.product
SET search_vector = catalog.product_search_document(id)
WHERE product_code LIKE 'GP-%';
