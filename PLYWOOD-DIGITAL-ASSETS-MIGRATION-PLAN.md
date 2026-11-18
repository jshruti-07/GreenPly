# Digital Assets Page Migration Plan: Ceramic Tiles to Plywood & Building Boards

> **Document Version**: 1.0  
> **Status**: Ready for Review & Execution  
> **Target Page**: Digital Assets Screen (`/assets`) — Focus on **"All" Tab**  
> **Target Systems**: `catalog.asset`, `catalog.asset_tr`, `catalog.asset_rendition`, `catalog.asset_link`, [AssetLibraryPage.tsx](file:///c:/Users/DELL/Downloads/pim-platform/frontend/src/features/assets/AssetLibraryPage.tsx), [AssetLibraryStore.cs](file:///c:/Users/DELL/Downloads/pim-platform/services/catalog/src/Pim.Catalog.Infrastructure/ReadModels/AssetLibraryStore.cs)  
> **Reference Model**: [Greenply Industries Media & Digital Assets Catalog](https://www.greenply.com/)

---

## 1. Executive Summary & Page Architecture

The **Digital Assets Page** (`/assets`) provides an enterprise Digital Asset Management (DAM) interface for all marketing imagery, technical data sheets, laboratory compliance test certificates, CAD joinery drawings, and 3D BIM objects across the PIM platform.

### 1.1. Key UI Components of the Page
1. **Header & KPI Bar**:
   - `Total assets` (count and storage footprint).
   - `Orphan assets` (assets not linked to any product).
   - `Products without main image` (coverage gap indicator).
   - `Rights expiring` (licence compliance within 90 days).
2. **Filter & Search Toolbar**:
   - **Asset Type Segment Tabs**: **`All`**, `IMAGE`, `DOCUMENT`, `CAD`, `BIM`, `VIDEO`, `SPIN_360`.
   - **Usage Filter**: `All assets`, `Not linked to any product`, `Linked to a product`.
   - **Search Input**: Live keyword search matching filename and multilingual titles.
3. **Asset Cards Grid (`AssetCard`)**:
   - Visual preview (thumbnail or format glyph `▣`, `▤`, `◈`, `⬢`).
   - Type chip and Orphan warning chip.
   - **Asset Title** (from `asset_tr.title`, fallback to filename).
   - Metadata line: file size, pixel dimensions, and **`{linkCount} product(s)`**.
4. **Asset Detail Drawer (`DetailPanel`)**:
   - Opens on card click to show metadata facts, storage provider, renditions table, and multilingual text.
   - **"Used by ({count})" Section**: Lists every linked product with **Product Name** (hyperlinked to `/products/{productCode}?tab=assets`), link role (`MAIN`, `GALLERY`, `DATASHEET`, etc.), and primary status flag.

---

## 2. Audit of Current State & Tile Data Contamination in "All" Tab

A thorough audit of the database and the `/assets` screen reveals that the "All" tab is almost entirely contaminated with legacy architectural ceramic tile assets:

### 2.1. Legacy Tile Product Names in Asset Titles (`catalog.asset_tr`)
Over 1,900 assets feature tile-specific product names, formats, and German translations:
- *"Craft Extended Cover plate drip nose 31 x 30 cm"* (`Craft Extended Abdeckplatte Tropfkante`)
- *"Profiled edging External corner for skirting radius 30 mm"* (`Radialleistensystem Aussenecke`)
- *"Skirtings External corner for skirting and edging radius 60 mm"* (`Hohlkehlensystem Aussenecke`)
- *"Pool edging tiles External corner to pool edging tile"* (`Beckenrandsteine Aussenecke`)
- *"Pool edge system Finnland with integrated grating support 140 mm"* (`Beckenkopfsystem Finnland`)
- *"Alcina floor tile 60 x 60 cm Glazed"* (`Alcina Bodenfliesen 60x60`)
- *"Amano Mosaic Glossy 30 x 30 cm"* (`Amano Mosaik Glänzend`)
- *"Savona Non-slip Patio tile R11 60 x 120 cm"* (`Savona Terrassenplatten`)
- *"Grooved tiles Natural Ceramics Stair tile"* (`Rillenstufenplatte Naturkeramik`)

### 2.2. Legacy Tile Product Links in "Used by" Section (`catalog.asset_link`)
- When tile products were purged (`DELETE FROM product WHERE product_code NOT LIKE 'GP-%'`), foreign key cascades deleted the corresponding `asset_link` records.
- As a direct consequence, **nearly all 1,900+ tile assets in the library now display as `orphan` ("Not linked to any product")**.
- Any remaining links reference legacy tile product names rather than the 28 authentic Plywood products.

### 2.3. Obsolete Storage Providers & URLs
- The majority of assets point to `https://magento.agrob-buchtal.de/media/mam/source_images/...`, which is an external German ceramics MAM system completely unrelated to Plywood and Building Boards.

---

## 3. Target Plywood Digital Asset Architecture

In the target architecture, all tile assets and titles are replaced with a dedicated library of **Plywood & Building Panels Digital Assets** linked directly to the 28 plywood products:

### 3.1. Asset Categories by Media Type
1. **Product Images (`IMAGE`)**:
   - **Master Sheet Hero Views (`MAIN`)**: Full 8x4 ft panel faces showing wood grain, face veneer quality, and brand stamp.
   - **Calibrated Cross-Sections (`GALLERY`)**: Multi-ply high-density hardwood core veneer layers bonded with unextended phenolic resin.
   - **Architectural & Joinery Applications (`GALLERY`)**: Modular kitchens, heavy-duty wardrobe carcasses, acoustic panelling, and boat decks.
   - **Quality & Stress Testing (`GALLERY`)**: 72-hour boiling water immersion, Penta-Tech calibration gauge, and fire resistance demonstrations.
2. **Technical & Compliance Documents (`DOCUMENT`)**:
   - **Product Technical Data Sheets (`DATASHEET`)**: Detailed engineering specs, density, moisture %, screw-holding strength, and modulus of rupture (MOR/MOE).
   - **IS Certification Reports (`DATASHEET`)**: Official Bureau of Indian Standards test certificates (`IS:710`, `IS:303`, `IS:10701`, `IS:5509`).
   - **Environmental & Health Certificates (`DATASHEET`)**: CARB Phase 2 & EPA TSCA Title VI (< 0.05 ppm E0 formaldehyde emission) and FSC sustainable forestry certificates.
   - **Architectural Joinery Guides (`INSTALL`)**: Step-by-step fabrication, screw pilot-hole sizing, and edge-banding guidelines.
3. **BIM & CAD Engineering Assets (`BIM`, `CAD`)**:
   - Parametric 3D sheet objects in IFC 4.3 and Revit formats (`.ifc`, `.rvt`).
   - 2D CAD cross-section joinery profiles and edge details in DWG format (`.dwg`).

---

## 4. Complete Mapping: 28 Plywood Products to Digital Assets

Every product is allocated a complete digital asset bundle with authentic plywood naming:

| Product Code | Plywood Product Name | Asset Title (English / German) | Type | Role |
| :--- | :--- | :--- | :---: | :---: |
| `GP-CLUB-700` | **Club 700 Plywood** | Club 700 Master Sheet Face / Club 700 Hauptplatte | `IMAGE` | `MAIN` |
| `GP-CLUB-700` | **Club 700 Plywood** | Penta-Tech Calibrated Core Layers / Penta-Tech Schichtaufbau | `IMAGE` | `GALLERY` |
| `GP-CLUB-700` | **Club 700 Plywood** | Modular Kitchen BWP Carcass Installation / Küchenkorpus BWP | `IMAGE` | `GALLERY` |
| `GP-CLUB-700` | **Club 700 Plywood** | Club 700 Technical Data Sheet (2026) / Technisches Datenblatt | `DOCUMENT` | `DATASHEET` |
| `GP-CLUB-700` | **Club 700 Plywood** | IS:710 72h Boiling Water Immersion Report / IS:710 Kochwassertest | `DOCUMENT` | `DATASHEET` |
| `GP-CLUB-700` | **Club 700 Plywood** | CARB Phase 2 / E0 Zero Emission Certificate / E0 Zertifikat | `DOCUMENT` | `DATASHEET` |
| `GP-CLUB-700` | **Club 700 Plywood** | Club 700 3D BIM Parametric Panel Object / 3D-BIM-Objekt (IFC) | `BIM` | `BIM` |
| `GP-CLUB-700` | **Club 700 Plywood** | Club 700 Joinery CAD Detail Section / CAD-Schnittzeichnung | `CAD` | `DRAWING` |
| `GP-CLUB-500` | **Club 500 Plywood** | Club 500 Structural BWP Hardwood Sheet / Club 500 BWP Platte | `IMAGE` | `MAIN` |
| `GP-CLUB-500` | **Club 500 Plywood** | Virashield Antimicrobial Protection Test / Virashield Prüfbericht | `DOCUMENT` | `DATASHEET` |
| `GP-FLEX-PLY` | **Club Flexiply** | Cold-Press Contour Flexible Plywood Sheet / Flexiply Biegesperrholz | `IMAGE` | `MAIN` |
| `GP-FLEX-PLY` | **Club Flexiply** | Curved Column & Serpentine Joinery Guide / Verarbeitungsanleitung | `DOCUMENT` | `INSTALL` |
| `GP-PLAT-BWP` | **Platinum BWP Plywood** | Platinum BWP Marine Hardwood Panel / Platinum BWP Marineplatte | `IMAGE` | `MAIN` |
| `GP-PLAT-BWP` | **Platinum BWP Plywood** | Multi-Layer Unextended Resin Cross Section / Schichtaufbau Harz | `IMAGE` | `GALLERY` |
| `GP-FIRE-RET` | **Fire-Retardant Plywood** | Fire-Retardant Pressure Impregnated Sheet / Brandschutzplatte | `IMAGE` | `MAIN` |
| `GP-FIRE-RET` | **Fire-Retardant Plywood** | IS:5509 50+ Min Flame Penetration Delay Report / Brandprüfbericht | `DOCUMENT` | `DATASHEET` |
| `GP-710-MAR` | **710 Marine Plywood** | 710 Marine Selected Hardwood Face Sheet / 710 Marinesperrholz | `IMAGE` | `MAIN` |
| `GP-710-MAR` | **710 Marine Plywood** | Boat Building & High Moisture Exposure TDS / Bootsbau TDS | `DOCUMENT` | `DATASHEET` |
| `GP-GOLD-BWP` | **Gold BWP Plywood** | Gold BWP Structural Calibrated Sheet / Gold BWP Platte | `IMAGE` | `MAIN` |
| `GP-303-MR` | **MR 303 Commercial Plywood** | Commercial MR 303 Interior Joinery Sheet / MR 303 Innenausbau | `IMAGE` | `MAIN` |
| `GP-303-MR` | **MR 303 Commercial Plywood** | IS:303 Moisture Resistant Melamine Urea Report / IS:303 Prüfbericht | `DOCUMENT` | `DATASHEET` |
| `GP-ECO-PLAT` | **Ecotec Platinum BWP Plywood**| Ecotec Platinum BWP Calibrated Board / Ecotec Platinum BWP | `IMAGE` | `MAIN` |
| `GP-ECO-710` | **Ecotec 710 Marine Plywood** | Ecotec 710 Marine Grade Timber Sheet / Ecotec 710 Marine | `IMAGE` | `MAIN` |
| `GP-ECO-MR` | **Ecotec MR Commercial Plywood**| Ecotec MR Plantation Timber Panel / Ecotec MR Platte | `IMAGE` | `MAIN` |
| `GP-ECO-TMR` | **Ecotec True MR Moisture Ply**| Ecotec True MR Interior Partition Board / Ecotec True MR | `IMAGE` | `MAIN` |
| `GP-OPTIMA-G` | **Optima G Plywood** | Optima G Selected Core Utility Sheet / Optima G Bauplatte | `IMAGE` | `MAIN` |
| `GP-BHAROSA` | **Bharosa MR Plywood** | Bharosa High Density Commercial Board / Bharosa Trägerplatte | `IMAGE` | `MAIN` |
| `GP-JANSATHI` | **Jansathi Utility Plywood** | Jansathi Interior Carpentry Grade Sheet / Jansathi Ausbauholz | `IMAGE` | `MAIN` |
| `GP-CALIB-25` | **Calibrated Core 25mm Panel** | CNC Four-Side Calibrated 25mm Heavy Panel / 25mm CNC Trägerplatte | `IMAGE` | `MAIN` |
| `GP-CALIB-19` | **Calibrated Core 19mm Panel** | Precision Calibrated 19mm Core Panel / 19mm Präzisionsplatte | `IMAGE` | `MAIN` |
| `GP-CALIB-16` | **Calibrated Core 16mm Panel** | Precision Calibrated 16mm Core Panel / 16mm Präzisionsplatte | `IMAGE` | `MAIN` |
| `GP-CALIB-12` | **Calibrated Core 12mm Panel** | Precision Calibrated 12mm Core Panel / 12mm Präzisionsplatte | `IMAGE` | `MAIN` |
| `GP-CALIB-9` | **Calibrated Core 9mm Panel** | Precision Calibrated 9mm Core Panel / 9mm Präzisionsplatte | `IMAGE` | `MAIN` |
| `GP-CALIB-6` | **Calibrated Core 6mm Panel** | Precision Calibrated 6mm Core Panel / 6mm Präzisionsplatte | `IMAGE` | `MAIN` |
| `GP-SHUTTER-BWP`| **Film-Faced Shuttering Ply** | Mirror Finish 30kg Concrete Shuttering Board / Schalholz 30kg | `IMAGE` | `MAIN` |
| `GP-SHUTTER-BWP`| **Film-Faced Shuttering Ply** | Repeated Concrete Pour Repetition Data Sheet / Schalungs-TDS | `DOCUMENT` | `DATASHEET` |
| `GP-BLOCK-25` | **Gold BWP Blockboard 25mm** | Gold BWP Solid Pine Core 25mm Blockboard / 25mm Tischlerplatte | `IMAGE` | `MAIN` |
| `GP-BLOCK-19` | **Club Pine Blockboard 19mm** | Club High Stability 19mm Pine Blockboard / 19mm Kiefernplatte | `IMAGE` | `MAIN` |
| `GP-DOOR-FLUSH` | **BWP Solid Core Flush Door** | BWP Solid Core 30mm Flush Door Shutter / 30mm Vollkerntür | `IMAGE` | `MAIN` |
| `GP-VENEER-TEAK`| **Royal Teak Crown Cut Veneer** | Royal Teak Architectural Face Veneer / Teak Echtholzfurnier | `IMAGE` | `MAIN` |
| `GP-VENEER-WALNUT`|**American Walnut Face Veneer**| Natural American Walnut Quarter Cut Veneer / Nussbaumfurnier | `IMAGE` | `MAIN` |
| `GP-VENEER-OAK` | **Smoked White Oak Veneer** | Smoked White Oak Crown Cut Architectural Veneer / Eichenfurnier | `IMAGE` | `MAIN` |

---

## 5. Database Migration Specification

A dedicated SQL script (`services/catalog/db/init/08_replace_tile_assets_with_plywood_assets.sql`) will execute the complete digital assets transformation in a single transaction:

```sql
-- =============================================================================
-- MODULE 08: PURGE CERAMIC TILE DIGITAL ASSETS & SEED PLYWOOD ASSET SUITE
-- =============================================================================

BEGIN;
SET search_path TO catalog, public;

-- 1. PURGE OBSOLETE CERAMIC TILE ASSETS & LINKS
-- Cascade deletes asset_tr, asset_rendition, and asset_link records
DELETE FROM asset 
WHERE storage_provider = 'client-magento'
   OR storage_key LIKE '%agrob-buchtal%'
   OR storage_key LIKE '%drytile%'
   OR file_name LIKE '%.rvt'
   OR file_name LIKE '%tile%';

-- 2. INSERT HIGH-RESOLUTION PLYWOOD ASSET CATALOGUE
INSERT INTO asset (storage_provider, storage_key, file_name, mime_type, file_size, asset_type, width, height, status)
VALUES
  -- Club 700 / 500 Assets
  ('s3', 'https://assets.greenply.com/products/club_700_sheet_hero.jpg',
   'club_700_sheet_hero.jpg', 'image/jpeg', 2450000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/club_700_cross_section_layers.jpg',
   'club_700_cross_section_layers.jpg', 'image/jpeg', 1890000, 'IMAGE', 1920, 1280, 'active'),
  ('s3', 'https://assets.greenply.com/products/club_700_application_kitchen.jpg',
   'club_700_application_kitchen.jpg', 'image/jpeg', 2150000, 'IMAGE', 1920, 1280, 'active'),
  ('s3', 'https://www.greenply.com/datasheets/Club_700_Technical_Datasheet_2026.pdf',
   'Club_700_Technical_Datasheet_2026.pdf', 'application/pdf', 1450200, 'DOCUMENT', NULL, NULL, 'active'),
  ('s3', 'https://www.greenply.com/certifications/IS710_72hr_Boiling_Water_Test_Report.pdf',
   'IS710_72hr_Boiling_Water_Test_Report.pdf', 'application/pdf', 890400, 'DOCUMENT', NULL, NULL, 'active'),
  ('s3', 'https://www.greenply.com/certifications/E0_Emission_CARB_P2_Certificate.pdf',
   'E0_Emission_CARB_P2_Certificate.pdf', 'application/pdf', 670100, 'DOCUMENT', NULL, NULL, 'active'),
  ('s3', 'https://bim.greenply.com/objects/club_700_panel_3d.ifc',
   'club_700_panel_3d.ifc', 'application/x-step', 4210000, 'BIM', NULL, NULL, 'active'),
  ('s3', 'https://bim.greenply.com/cad/club_700_cad_section.dwg',
   'club_700_cad_section.dwg', 'application/acad', 1120000, 'CAD', NULL, NULL, 'active'),
  ('s3', 'https://assets.greenply.com/products/club_500_sheet_hero.jpg',
   'club_500_sheet_hero.jpg', 'image/jpeg', 2240000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/club_flexiply_sheet_hero.jpg',
   'club_flexiply_sheet_hero.jpg', 'image/jpeg', 2180000, 'IMAGE', 2400, 1600, 'active'),

  -- Platinum Series Assets
  ('s3', 'https://assets.greenply.com/products/platinum_bwp_sheet_hero.jpg',
   'platinum_bwp_sheet_hero.jpg', 'image/jpeg', 2310000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/fire_retardant_sheet_hero.jpg',
   'fire_retardant_sheet_hero.jpg', 'image/jpeg', 2420000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://www.greenply.com/datasheets/IS5509_Fire_Retardant_Test_Report.pdf',
   'IS5509_Fire_Retardant_Test_Report.pdf', 'application/pdf', 1340000, 'DOCUMENT', NULL, NULL, 'active'),

  -- 710 Marine & Gold Assets
  ('s3', 'https://assets.greenply.com/products/710_marine_sheet_hero.jpg',
   '710_marine_sheet_hero.jpg', 'image/jpeg', 2290000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/gold_bwp_sheet_hero.jpg',
   'gold_bwp_sheet_hero.jpg', 'image/jpeg', 2190000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/mr_303_sheet_hero.jpg',
   'mr_303_sheet_hero.jpg', 'image/jpeg', 2140000, 'IMAGE', 2400, 1600, 'active'),

  -- Ecotec Plywood Line Assets
  ('s3', 'https://assets.greenply.com/products/ecotec_platinum_sheet_hero.jpg',
   'ecotec_platinum_sheet_hero.jpg', 'image/jpeg', 2180000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/ecotec_710_sheet_hero.jpg',
   'ecotec_710_sheet_hero.jpg', 'image/jpeg', 2150000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/ecotec_mr_sheet_hero.jpg',
   'ecotec_mr_sheet_hero.jpg', 'image/jpeg', 2120000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/ecotec_truemr_sheet_hero.jpg',
   'ecotec_truemr_sheet_hero.jpg', 'image/jpeg', 2160000, 'IMAGE', 2400, 1600, 'active'),

  -- Optima & Utility Assets
  ('s3', 'https://assets.greenply.com/products/optima_g_sheet_hero.jpg',
   'optima_g_sheet_hero.jpg', 'image/jpeg', 2050000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/bharosa_mr_sheet_hero.jpg',
   'bharosa_mr_sheet_hero.jpg', 'image/jpeg', 2020000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/jansathi_utility_sheet_hero.jpg',
   'jansathi_utility_sheet_hero.jpg', 'image/jpeg', 1980000, 'IMAGE', 2400, 1600, 'active'),

  -- Calibrated Core Panels Assets
  ('s3', 'https://assets.greenply.com/products/calibrated_25mm_sheet_hero.jpg',
   'calibrated_25mm_sheet_hero.jpg', 'image/jpeg', 2350000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/calibrated_19mm_sheet_hero.jpg',
   'calibrated_19mm_sheet_hero.jpg', 'image/jpeg', 2310000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/calibrated_16mm_sheet_hero.jpg',
   'calibrated_16mm_sheet_hero.jpg', 'image/jpeg', 2280000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/calibrated_12mm_sheet_hero.jpg',
   'calibrated_12mm_sheet_hero.jpg', 'image/jpeg', 2220000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/calibrated_9mm_sheet_hero.jpg',
   'calibrated_9mm_sheet_hero.jpg', 'image/jpeg', 2160000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/calibrated_6mm_sheet_hero.jpg',
   'calibrated_6mm_sheet_hero.jpg', 'image/jpeg', 2090000, 'IMAGE', 2400, 1600, 'active'),

  -- Shuttering & Doors Assets
  ('s3', 'https://assets.greenply.com/products/shuttering_30kg_sheet_hero.jpg',
   'shuttering_30kg_sheet_hero.jpg', 'image/jpeg', 2480000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/blockboard_25mm_sheet_hero.jpg',
   'blockboard_25mm_sheet_hero.jpg', 'image/jpeg', 2290000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/blockboard_19mm_sheet_hero.jpg',
   'blockboard_19mm_sheet_hero.jpg', 'image/jpeg', 2250000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/flush_door_30mm_hero.jpg',
   'flush_door_30mm_hero.jpg', 'image/jpeg', 2340000, 'IMAGE', 2400, 1600, 'active'),

  -- Architectural Decorative Veneers Assets
  ('s3', 'https://assets.greenply.com/products/veneer_royal_teak_hero.jpg',
   'veneer_royal_teak_hero.jpg', 'image/jpeg', 2520000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/veneer_american_walnut_hero.jpg',
   'veneer_american_walnut_hero.jpg', 'image/jpeg', 2480000, 'IMAGE', 2400, 1600, 'active'),
  ('s3', 'https://assets.greenply.com/products/veneer_smoked_oak_hero.jpg',
   'veneer_smoked_oak_hero.jpg', 'image/jpeg', 2460000, 'IMAGE', 2400, 1600, 'active')
ON CONFLICT (storage_provider, storage_key) DO UPDATE
SET file_name = EXCLUDED.file_name,
    status = 'active';

-- 3. ENSURE WEB RENDITIONS FOR ALL IMAGES
INSERT INTO asset_rendition (asset_id, role, uri, width, height, format)
SELECT a.id, 'web', a.storage_key, a.width, a.height, 'jpeg'
FROM asset a
WHERE a.asset_type = 'IMAGE'
ON CONFLICT (asset_id, role) DO UPDATE
SET uri = EXCLUDED.uri;

-- 4. INSERT MULTILINGUAL ASSET TITLES & ALT TEXTS (NO "GREEN" PREFIXES)
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

-- 5. LINK ASSETS TO PRODUCTS (POPULATING "USED BY" IN DETAIL DRAWER)
DELETE FROM asset_link; -- Reset links to connect cleanly to all 28 products

INSERT INTO asset_link (product_id, asset_id, role, is_primary, sort_order)
SELECT p.id, a.id, v.role, v.is_pri, v.sort_order
FROM (VALUES
  ('GP-CLUB-700',     'club_700_sheet_hero.jpg',                     'MAIN',      true,  1),
  ('GP-CLUB-700',     'club_700_cross_section_layers.jpg',           'GALLERY',   false, 2),
  ('GP-CLUB-700',     'club_700_application_kitchen.jpg',           'GALLERY',   false, 3),
  ('GP-CLUB-700',     'Club_700_Technical_Datasheet_2026.pdf',       'DATASHEET', false, 4),
  ('GP-CLUB-700',     'IS710_72hr_Boiling_Water_Test_Report.pdf', 'DATASHEET', false, 5),
  ('GP-CLUB-700',     'E0_Emission_CARB_P2_Certificate.pdf',         'DATASHEET', false, 6),
  ('GP-CLUB-700',     'club_700_panel_3d.ifc',                       'BIM',       false, 7),
  ('GP-CLUB-700',     'club_700_cad_section.dwg',                    'DRAWING',   false, 8),
  ('GP-CLUB-500',     'club_500_sheet_hero.jpg',                     'MAIN',      true,  1),
  ('GP-FLEX-PLY',     'club_flexiply_sheet_hero.jpg',                'MAIN',      true,  1),
  ('GP-PLAT-BWP',     'platinum_bwp_sheet_hero.jpg',                 'MAIN',      true,  1),
  ('GP-FIRE-RET',     'fire_retardant_sheet_hero.jpg',               'MAIN',      true,  1),
  ('GP-FIRE-RET',     'IS5509_Fire_Retardant_Test_Report.pdf',       'DATASHEET', false, 2),
  ('GP-710-MAR',      '710_marine_sheet_hero.jpg',                   'MAIN',      true,  1),
  ('GP-GOLD-BWP',     'gold_bwp_sheet_hero.jpg',                     'MAIN',      true,  1),
  ('GP-303-MR',       'mr_303_sheet_hero.jpg',                       'MAIN',      true,  1),
  ('GP-ECO-PLAT',     'ecotec_platinum_sheet_hero.jpg',              'MAIN',      true,  1),
  ('GP-ECO-710',      'ecotec_710_sheet_hero.jpg',                   'MAIN',      true,  1),
  ('GP-ECO-MR',       'ecotec_mr_sheet_hero.jpg',                    'MAIN',      true,  1),
  ('GP-ECO-TMR',      'ecotec_truemr_sheet_hero.jpg',                'MAIN',      true,  1),
  ('GP-OPTIMA-G',     'optima_g_sheet_hero.jpg',                     'MAIN',      true,  1),
  ('GP-BHAROSA',      'bharosa_mr_sheet_hero.jpg',                   'MAIN',      true,  1),
  ('GP-JANSATHI',     'jansathi_utility_sheet_hero.jpg',             'MAIN',      true,  1),
  ('GP-CALIB-25',     'calibrated_25mm_sheet_hero.jpg',              'MAIN',      true,  1),
  ('GP-CALIB-19',     'calibrated_19mm_sheet_hero.jpg',              'MAIN',      true,  1),
  ('GP-CALIB-16',     'calibrated_16mm_sheet_hero.jpg',              'MAIN',      true,  1),
  ('GP-CALIB-12',     'calibrated_12mm_sheet_hero.jpg',              'MAIN',      true,  1),
  ('GP-CALIB-9',      'calibrated_9mm_sheet_hero.jpg',               'MAIN',      true,  1),
  ('GP-CALIB-6',      'calibrated_6mm_sheet_hero.jpg',               'MAIN',      true,  1),
  ('GP-SHUTTER-BWP',  'shuttering_30kg_sheet_hero.jpg',              'MAIN',      true,  1),
  ('GP-BLOCK-25',     'blockboard_25mm_sheet_hero.jpg',              'MAIN',      true,  1),
  ('GP-BLOCK-19',     'blockboard_19mm_sheet_hero.jpg',              'MAIN',      true,  1),
  ('GP-DOOR-FLUSH',   'flush_door_30mm_hero.jpg',                    'MAIN',      true,  1),
  ('GP-VENEER-TEAK',  'veneer_royal_teak_hero.jpg',                  'MAIN',      true,  1),
  ('GP-VENEER-WALNUT','veneer_american_walnut_hero.jpg',             'MAIN',      true,  1),
  ('GP-VENEER-OAK',   'veneer_smoked_oak_hero.jpg',                  'MAIN',      true,  1)
) AS v(p_code, fname, role, is_pri, sort_order)
JOIN product p ON p.product_code = v.p_code
JOIN asset a ON a.file_name = v.fname
ON CONFLICT (product_id, asset_id, role) DO UPDATE
SET is_primary = EXCLUDED.is_primary,
    sort_order = EXCLUDED.sort_order;

COMMIT;
```

---

## 6. Frontend Display & Verification in "All" Tab

When this migration is applied, the **"All" tab** on `http://localhost:5173/assets` will transform:

### 6.1. Grid Cards (`AssetCard`)
- **Asset Titles**: Display authentic plywood product labels:
  - *"Club 700 Plywood Master Sheet"*
  - *"Platinum Series BWP Marine Panel"*
  - *"710 Marine Series Plywood Sheet"*
  - *"Gold BWP Structural Calibrated Board"*
  - *"Calibrated CNC Core 19mm Panel"*
  - *"Film-Faced Shuttering Ply 30kg Mirror Finish"*
- **Badges**:
  - `orphan` warning badge count drops to `0`.
  - Product count badge displays `1 product` (or `N products`).

### 6.2. Detail Drawer (`DetailPanel`)
When any asset is clicked:
- **Title & Header**: Clean plywood description without "Green" prefixes.
- **"Used by" Section**:
  - **Product Column**: Displays the clean plywood product name:
    - `Club 700 Plywood` (linking to `/products/GP-CLUB-700?tab=assets`)
    - `Platinum BWP Plywood` (`/products/GP-PLAT-BWP?tab=assets`)
    - `710 Marine Plywood` (`/products/GP-710-MAR?tab=assets`)
    - `Calibrated Core 19mm Panel` (`/products/GP-CALIB-19?tab=assets`)
  - **Role Column**: `MAIN`, `GALLERY`, `DATASHEET`, `BIM`, or `DRAWING`.
  - **Primary Column**: Shows `Yes` for main product hero images.

### 6.3. Search Verification
- Searching `"Tile"`, `"Fliese"`, `"Keramik"`, `"Alcina"` returns `0` results.
- Searching `"Marine"`, `"BWP"`, `"Calibrated"`, `"Datasheet"`, `"Veneer"` returns instant, targeted asset cards.

---

## 7. Execution Steps

1. Execute the migration script in PowerShell:
   ```powershell
   docker exec -i infra-postgres-1 psql -U pim_admin -d pim -f /pim-init/catalog/08_replace_tile_assets_with_plywood_assets.sql
   ```
2. Navigate to `http://localhost:5173/assets` with tab set to **"All"**.
3. Verify all asset cards, titles, and "Used by" product links reflect Plywood and Building Boards.
