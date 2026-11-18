# Categories Page Migration Plan: Ceramic Tiles to Plywood & Building Boards

> **Document Version**: 1.0  
> **Status**: Ready for Review & Execution  
> **Target Page**: Categories Screen (`/taxonomy`)  
> **Target Systems**: `catalog.taxonomy_node`, `catalog.taxonomy_node_tr`, `catalog.product`, `catalog.attribute_set`, [CategoriesPage.tsx](file:///c:/Users/DELL/Downloads/pim-platform/frontend/src/features/categories/CategoriesPage.tsx)  
> **Reference Model**: [Greenply Industries Merchandising Taxonomy](https://www.greenply.com/products)

---

## 1. Executive Summary & Problem Analysis

The **Categories Page** (`/taxonomy`) serves as the core merchandising taxonomy browser within the PIM platform, rendering:
1. **Left Panel — Taxonomy Tree Hierarchy**: A nested tree with depth toggles, filter search, and direct/subtree product count badges.
2. **Right Panel — Category Detail Panel**:
   - **Header & Metrics**: Level (Brand, Family, Category, Sub-category, Series), Parent node, Brand, Direct products, Subtree products, and Node code.
   - **Translations Table**: Locale labels (`en-GB`, `de-DE`), approval status, and base locale tag.
   - **Attribute Template Card**: Attribute set name, provenance badge (*declared on category* vs *inferred from products*), required attributes count, and attribute-by-attribute coverage across subtree products.

### 1.1. Current State Audit & Tile Contamination
Inspection of the live system reveals that **legacy ceramic tile data dominates the taxonomy view**:

1. **Legacy Root Node `agrob` (`untranslated`, 0 products)**:
   - Contains **9 tile category branches**:
     - `decors` (*Decors*)
     - `floor_tiles` (*Floor tiles*)
     - `mosaic` (*Mosaic*)
     - `patio_tiles` (*Patio tiles*)
     - `pool` (*Pool*)
     - `shower_floors` (*Shower floors*)
     - `skirtings` (*Skirtings*)
     - `stair_tiles` (*Stair tiles*)
     - `wall_tiles` (*Wall tiles*)
   - Contains **70+ ceramic tile series nodes** (e.g., `alcina`, `area_pro`, `aruba`, `ascona`, `basic_stone`, `grooved_tiles`, `pool_edge_system_bamberg`, `keraion_op_special_glazes`, `quarzit`, `streetlife_metallic`, etc.).
   - All 79+ tile nodes display **0 products** and **"No attribute template"** ("Nothing is configured on this category, and there are no products beneath it to infer one from").

2. **Legacy Tile Metadata in Supporting Tables**:
   - `catalog.brand`: Contains `agrob_buchtal` (*Agrob Buchtal*) and `drytile` (*DryTile*).
   - `catalog.attribute_set`: Contains `tile_core` (*Keramik – Kernattribute*) with 24 ceramic attributes (e.g., `slip_resistance`, `rectified`, `glaze`, `frost_resistant`, `joint_width_mm`, `mfg_width_mm`).

3. **Current Plywood Taxonomy Gaps**:
   - The active plywood brand root is `greenply` (*Greenply Collection*, 28 products).
   - Underneath, only 5 broad branches exist (`plywood_blockboard`, `flush_doors`, `decorative_veneers`, `pvc_wpc`, `mdf_boards`).
   - **All 28 plywood products are lumped directly under `plywood_blockboard`** rather than being organized into granular product lines/series.
   - Earlier seed scripts retained the "Green" prefix in node labels (e.g., `Green Club 700 / 500`, `Green Platinum`, `Green 710 Marine`, `Green Gold`), which conflicts with the user's requirement to remove "Green" prefixes from product/series names.
   - Intermediate and leaf categories do not have explicit `attribute_set_id` declarations, relying on fallback inference.

---

## 2. Target Plywood Taxonomy Architecture

The target architecture replaces all ceramic tiles with an authentic, multi-tier **Plywood & Building Boards Merchandising Tree** modeled after Greenply Industries:

```
Plywood & Building Panels Collection (Root: greenply, Level: Brand / Family)
├── Plywood & Blockboards (Code: plywood_blockboard, Level: Category)
│   ├── Club 700 / 500 Plywood (Code: club_plywood_cat, Level: Sub-category) [3 products]
│   ├── Platinum Series BWP (Code: platinum_bwp_cat, Level: Sub-category) [2 products]
│   ├── 710 Marine Series (Code: marine_710_cat, Level: Sub-category) [1 product]
│   ├── Gold & Calibrated (Code: gold_calibrated_cat, Level: Sub-category) [1 product]
│   ├── Commercial MR 303 (Code: commercial_mr_cat, Level: Sub-category) [1 product]
│   ├── Ecotec Plywood Series (Code: ecotec_ply_cat, Level: Sub-category) [4 products]
│   ├── Optima & Utility Boards (Code: optima_utility_cat, Level: Sub-category) [3 products]
│   ├── Calibrated CNC Core Panels (Code: calibrated_core_cat, Level: Sub-category) [6 products]
│   ├── Film-Faced Shuttering Ply (Code: shuttering_ply_cat, Level: Sub-category) [1 product]
│   └── Blockboards & Flush Doors (Code: blockboards_doors_cat, Level: Sub-category) [3 products]
├── Doors & Openings (Code: flush_doors, Level: Category)
├── Natural Decorative Veneers (Code: decorative_veneers, Level: Category) [3 products]
│   └── Architectural Face Veneers (Code: decorative_veneers_cat, Level: Sub-category)
├── PVC & WPC Waterproof Panels (Code: pvc_wpc, Level: Category)
└── MDF & Engineered HDMR Panels (Code: mdf_boards, Level: Category)
```

### 2.1. Complete 28-Product Taxonomy Mapping

Every single one of the 28 plywood products is mapped directly to its specialized category node:

| Product Code | Product Name | Target Category Code | Target Category Name |
| :--- | :--- | :--- | :--- |
| `GP-CLUB-700` | Club 700 Plywood | `club_plywood_cat` | Club 700 / 500 Plywood |
| `GP-CLUB-500` | Club 500 Plywood | `club_plywood_cat` | Club 700 / 500 Plywood |
| `GP-FLEX-PLY` | Club Flexiply | `club_plywood_cat` | Club 700 / 500 Plywood |
| `GP-PLAT-BWP` | Platinum BWP Plywood | `platinum_bwp_cat` | Platinum Series BWP |
| `GP-FIRE-RET` | Fire-Retardant Plywood | `platinum_bwp_cat` | Platinum Series BWP |
| `GP-710-MAR` | 710 Marine Plywood | `marine_710_cat` | 710 Marine Series |
| `GP-GOLD-BWP` | Gold BWP Plywood | `gold_calibrated_cat` | Gold & Calibrated |
| `GP-303-MR` | MR 303 Commercial Plywood | `commercial_mr_cat` | Commercial MR 303 |
| `GP-ECO-PLAT` | Ecotec Platinum BWP Plywood | `ecotec_ply_cat` | Ecotec Plywood Series |
| `GP-ECO-710` | Ecotec 710 Marine Plywood | `ecotec_ply_cat` | Ecotec Plywood Series |
| `GP-ECO-MR` | Ecotec MR Commercial Plywood | `ecotec_ply_cat` | Ecotec Plywood Series |
| `GP-ECO-TMR` | Ecotec True MR Moisture Resistant Ply | `ecotec_ply_cat` | Ecotec Plywood Series |
| `GP-OPTIMA-G` | Optima G Plywood | `optima_utility_cat` | Optima & Utility Boards |
| `GP-BHAROSA` | Bharosa MR Plywood | `optima_utility_cat` | Optima & Utility Boards |
| `GP-JANSATHI` | Jansathi Utility Plywood | `optima_utility_cat` | Optima & Utility Boards |
| `GP-CALIB-25` | Calibrated Core 25mm Panel | `calibrated_core_cat` | Calibrated CNC Core Panels |
| `GP-CALIB-19` | Calibrated Core 19mm Panel | `calibrated_core_cat` | Calibrated CNC Core Panels |
| `GP-CALIB-16` | Calibrated Core 16mm Panel | `calibrated_core_cat` | Calibrated CNC Core Panels |
| `GP-CALIB-12` | Calibrated Core 12mm Panel | `calibrated_core_cat` | Calibrated CNC Core Panels |
| `GP-CALIB-9` | Calibrated Core 9mm Panel | `calibrated_core_cat` | Calibrated CNC Core Panels |
| `GP-CALIB-6` | Calibrated Core 6mm Panel | `calibrated_core_cat` | Calibrated CNC Core Panels |
| `GP-SHUTTER-BWP` | Film-Faced Shuttering Ply 30kg | `shuttering_ply_cat` | Film-Faced Shuttering Ply |
| `GP-BLOCK-25` | Gold BWP Blockboard 25mm | `blockboards_doors_cat` | Blockboards & Flush Doors |
| `GP-BLOCK-19` | Club Pine Blockboard 19mm | `blockboards_doors_cat` | Blockboards & Flush Doors |
| `GP-DOOR-FLUSH` | BWP Solid Core Flush Door 30mm | `blockboards_doors_cat` | Blockboards & Flush Doors |
| `GP-VENEER-TEAK` | Royal Teak Crown Cut Face Veneer | `decorative_veneers_cat` | Natural Decorative Veneers |
| `GP-VENEER-WALNUT`| American Walnut Architectural Veneer| `decorative_veneers_cat` | Natural Decorative Veneers |
| `GP-VENEER-OAK` | Smoked White Oak Quarter Cut Veneer | `decorative_veneers_cat` | Natural Decorative Veneers |

---

## 3. Attribute Template Integration

On the Categories detail screen, clicking any category node renders its associated Attribute Template card.
Under the migration, every plywood node inherits or declares:
- **Attribute Set**: `plywood_core` (*Plywood & Engineered Panels — Core Attributes*)
- **Provenance**: `declared on this category`
- **15 Technical Attributes (7 Required, 100% Coverage)**:
  1. `water_resistance_grade` (BWP / BWR / MR) — *Required*
  2. `emission_standard` (E0 / E1 / CARB P2 / EPA TSCA) — *Required*
  3. `core_species` (Selected Gurjan / Eucalyptus / Pine) — *Required*
  4. `resin_type` (Unextended Phenolic Resin / Melamine Urea MUF) — *Required*
  5. `calibration_technology` (Penta-Tech Calibration / 4-Side Sanded) — *Optional*
  6. `sheet_dimension` (8x4 ft / 2440x1220 mm, 7x4 ft, etc.) — *Required*
  7. `thickness_mm` (4mm – 25mm) — *Required*
  8. `density_g_cm3` (Core density 0.65 – 0.85 g/cm³) — *Optional*
  9. `moisture_content_pct` (Standard 8% – 12%) — *Optional*
  10. `borer_fungus_proof` (Vacuum Pressure Impregnation) — *Required*
  11. `fire_retardant` (IS:5509 Fire Delay formulation) — *Required*
  12. `fire_delay_mins` (Flame penetration delay minutes) — *Optional*
  13. `warranty_years` (10, 15, 20, 25, 30, Lifetime) — *Required*
  14. `money_back_multiple` (2X, 5X, 7X Replacement Guarantee) — *Optional*
  15. `fully_covered_plan` (Comprehensive Restoration Cover) — *Optional*

---

## 4. Database Migration Specification

A dedicated SQL script (`services/catalog/db/init/07_cleanup_tiles_and_rebuild_plywood_taxonomy.sql`) will execute the complete migration in a single transaction:

```sql
-- =============================================================================
-- MODULE 07: PURGE CERAMIC TILES TAXONOMY & REBUILD PLYWOOD TAXONOMY
-- =============================================================================

BEGIN;
SET search_path TO catalog, public;

-- 1. UNLINK ANY REMAINING PRODUCTS FROM TILE NODES
UPDATE product 
SET primary_node_id = (SELECT id FROM taxonomy_node WHERE code = 'plywood_blockboard')
WHERE primary_node_id IN (
    SELECT id FROM taxonomy_node WHERE path <@ 'agrob' OR code = 'agrob'
);

-- 2. PURGE AGROB TILE TAXONOMY TREE
-- Cascade will delete taxonomy_node_tr records automatically
DELETE FROM taxonomy_node WHERE path <@ 'agrob' OR code = 'agrob';

-- 3. PURGE TILE BRANDS & TILE ATTRIBUTE SETS
DELETE FROM brand_tr WHERE brand_id IN (SELECT id FROM brand WHERE code IN ('agrob_buchtal', 'drytile'));
DELETE FROM brand WHERE code IN ('agrob_buchtal', 'drytile');

DELETE FROM attribute_set_attribute WHERE set_id IN (SELECT id FROM attribute_set WHERE code = 'tile_core');
DELETE FROM attribute_set WHERE code = 'tile_core';

-- 4. ENSURE PLYWOOD ROOT & BASE CATEGORIES EXIST
INSERT INTO brand (code, legal_entity, website, country_code, status)
VALUES ('greenply', 'Greenply Industries Limited', 'https://www.greenply.com', 'IN', 'active')
ON CONFLICT (code) DO NOTHING;

INSERT INTO brand_tr (brand_id, locale_code, name, description, tagline)
SELECT b.id, 'en-GB', 'Greenply', 'Premier Plywood and Engineered Wood Manufacturer', 'Zero Emission E0 Plywood'
FROM brand b WHERE b.code = 'greenply'
ON CONFLICT (brand_id, locale_code) DO UPDATE SET name = EXCLUDED.name;

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

-- 5. BUILD CLEAN PLYWOOD SUBCATEGORIES UNDER PLYWOOD_BLOCKBOARD
INSERT INTO taxonomy_node (code, node_type, parent_id, path, depth, brand_id, attribute_set_id)
SELECT v.code, 'SUBCATEGORY', p.id, (p.path::text || '.' || v.code)::ltree, (p.depth + 1)::smallint, p.brand_id, (SELECT id FROM attribute_set WHERE code = 'plywood_core')
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
    attribute_set_id = EXCLUDED.attribute_set_id;

-- Natural Decorative Veneers subcategory under decorative_veneers
INSERT INTO taxonomy_node (code, node_type, parent_id, path, depth, brand_id, attribute_set_id)
SELECT 'decorative_veneers_cat', 'SUBCATEGORY', p.id, (p.path::text || '.decorative_veneers_cat')::ltree, (p.depth + 1)::smallint, p.brand_id, (SELECT id FROM attribute_set WHERE code = 'plywood_core')
FROM taxonomy_node p WHERE p.code = 'decorative_veneers'
ON CONFLICT (code) DO UPDATE
SET parent_id = EXCLUDED.parent_id,
    path = EXCLUDED.path,
    depth = EXCLUDED.depth,
    attribute_set_id = EXCLUDED.attribute_set_id;

-- 6. INSERT APPROVED TRANSLATIONS (WITHOUT "GREEN" PREFIXES)
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

-- 7. RE-ASSIGN ALL 28 PRODUCTS TO THEIR DEDICATED CATEGORY NODES
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

COMMIT;
```

---

## 5. Frontend Refinement & Verification

### 5.1. UI Text & Subtitle Polish in [CategoriesPage.tsx](file:///c:/Users/DELL/Downloads/pim-platform/frontend/src/features/categories/CategoriesPage.tsx)
- Line 440 subtitle:
  *Current*:
  ```tsx
  <p>
    The merchandising taxonomy: Brand → Family → Category → Sub-category → Series. Each node
    can carry an attribute template, and every product hangs from exactly one node.
  </p>
  ```
  *Target*:
  ```tsx
  <p>
    The merchandising taxonomy for Plywood & Engineered Panels: Brand → Family → Category → Sub-category.
    Each node carries an approved technical attribute template, and every product hangs from exactly one node.
  </p>
  ```

### 5.2. Search & Filter Testing
- When searching `"Marine"`:
  - Immediately filters down to `710 Marine Series` (1 product).
- When searching `"Calibrated"`:
  - Highlights `Gold & Calibrated Boards` and `Calibrated CNC Core Panels` (6 products).
- When searching `"Veneer"`:
  - Highlights `Natural Decorative Veneers` (3 products).
- When searching `"Tile"`, `"Fliese"`, `"Keramik"`, `"Agrob"`:
  - Returns `No category matches that`, confirming complete eradication of tile terminology.

---

## 6. Execution Steps & Verification Checklist

1. [ ] **Execute SQL Migration**:
   - Run the script against the `pim` database using the standard postgres command:
     ```bash
     docker exec -i infra-postgres-1 psql -U pim_admin -d pim -f /pim-init/catalog/07_cleanup_tiles_and_rebuild_plywood_taxonomy.sql
     ```
2. [ ] **Verify Database State**:
   - Confirm `SELECT count(*) FROM catalog.taxonomy_node WHERE path <@ 'agrob';` returns `0`.
   - Confirm `SELECT count(*) FROM catalog.taxonomy_node WHERE path <@ 'greenply';` returns `17`.
   - Confirm all 28 products have valid `primary_node_id` matching their designated subcategory.
3. [ ] **Verify Categories Page in Browser**:
   - Navigate to `http://localhost:5173/taxonomy`.
   - Verify tree contains only `Plywood & Building Panels Collection`.
   - Click each of the 11 subcategories and confirm:
     - Direct product count is accurate (3, 2, 1, 1, 1, 4, 3, 6, 1, 3, 3).
     - Subtree count matches.
     - "View N products" links to filtered products grid.
     - Attribute template shows *"Plywood & Engineered Panels — Core Attributes"* with 15 attributes and 100% coverage.
4. [ ] **Confirm Zero Residual Tile Words**:
   - Tree filter for "tile", "decors", "mosaic", "agrob", "ceramic" returns empty.
