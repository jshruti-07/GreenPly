-- =============================================================================
-- French (fr-FR) language pack for the development catalogue.
--
-- WHY THIS IS A SEPARATE FILE
-- ---------------------------
-- 02_dev_seed.sql is generated from the client's own export and is governed by
-- decision D-2026-08-16-a: it is derived, never invented. The client's export
-- carries German and English only -- every translated column in it is [DE] or
-- [EN] -- so there is no French to derive. fr-FR was nevertheless configured in
-- catalog.locale from the beginning, which left the third language selectable
-- and completely empty: choosing French showed nothing but fallbacks, so the
-- feature could not be exercised.
--
-- This file supplies that French. Keeping it separate means the provenance of
-- every row is unambiguous -- anything in 02 came from the client, anything
-- here did not -- and deleting this one file removes it all.
--
-- WHERE THE FRENCH COMES FROM
-- ---------------------------
-- Two different mechanisms, because two different kinds of text:
--
-- 1. REFERENCE VOCABULARY (attribute labels, groups, applications, taxonomy
--    categories, non-colour options) is translated term by term below. It is a
--    bounded, closed set -- 8 groups, 32 attributes, 18 applications, ~20
--    translatable categories, 18 options -- and every one is standard ceramic
--    and construction vocabulary with a settled French equivalent.
--
-- 2. PRODUCT NAMES AND COLOURS are DERIVED, not written out. Both are composed
--    from a small technical vocabulary -- the whole 667-product catalogue uses
--    about sixty distinct words -- so this file translates that vocabulary and
--    applies it to the English text at apply time.
--
--    That is deliberate and it matters: because the French is computed from
--    whatever English rows exist rather than pasted in as literals, a refreshed
--    client export regenerates 02_dev_seed.sql and this file still produces
--    correct French for it, with no second regeneration step and no stale rows
--    left pointing at products that no longer exist.
--
-- WHAT IS DELIBERATELY LEFT UNTRANSLATED
-- --------------------------------------
-- Series names (Alcina, Plural, Craft, Wiesbaden) are proper nouns and stay as
-- they are; that is what a French catalogue would print too.
--
-- One product in five is left with NO French row at all. That is not an
-- omission -- it is the point. A catalogue mid-translation is the normal state
-- of a PIM, and it is the only way to see the fallback marker doing its job:
-- the grid in French shows real French names beside German ones flagged as
-- fallbacks. A fully translated catalogue would hide that behaviour entirely.
--
-- Colour names that are marketing coinages rather than colours ("shabby chic
-- brown", "illusion dark") pass through untranslated, which is what happens to
-- them in real catalogues.
-- =============================================================================

\set ON_ERROR_STOP on

-- Each file in db/init is applied by its own psql invocation, so the
-- search_path set by 01 and 02 does not carry over to this one. Setting it
-- here is what lets the unqualified table names below resolve at all.
SET search_path TO catalog;

-- -----------------------------------------------------------------------------
-- The phrase translator.
--
-- Ordered longest-first, because replacement is greedy and "Pool edging tile"
-- must be consumed before "tile" can match inside it. Getting this order wrong
-- does not fail loudly -- it produces half-French text -- so the coverage check
-- at the bottom of this file exists to catch exactly that.
--
-- Dropped again at the end: this is a seed-time tool, not part of the schema.
-- -----------------------------------------------------------------------------
CREATE FUNCTION catalog.i18n_fr(src text) RETURNS text AS $fn$
DECLARE
    result text := src;
    pair   text[];
    pairs  text[][] := ARRAY[
        -- ---- the most specific phrases in the catalogue. These sit first
        --      because each one contains a shorter phrase further down the
        --      list, and being consumed early is the only thing stopping the
        --      shorter rule from mangling them.
        ARRAY['External angle to pool edging tile', 'Angle extérieur vers margelle de bassin'],
        ARRAY['Inclined grooved tile',       'Dalle rainurée inclinée'],
        ARRAY['(half-size)',                 '(demi-longueur)'],
        ARRAY['(half size)',                 '(demi-longueur)'],
        ARRAY['pool edge system',            'système de margelle'],
        ARRAY['pool edging tile',            'margelle de bassin'],
        ARRAY['edging tile',                 'margelle'],
        ARRAY['Overflow channel',            'Caniveau de débordement'],
        ARRAY['Guide tile - knob structure', 'Dalle de guidage - structure à plots'],
        ARRAY['Guide tile',                  'Dalle de guidage'],
        ARRAY['Safety strip tile',           'Carreau de bande de sécurité'],
        ARRAY['Ripple tile',                 'Carreau ondulé'],
        ARRAY['Special tiles',               'Pièces spéciales'],
        ARRAY['Special tile',                'Carreau spécial'],
        ARRAY[' sides',                      ' côtés'],
        ARRAY['Corner piece',                'Pièce d''angle'],
        ARRAY['first piece',                 'première pièce'],
        ARRAY['short side rounded',          'côté court arrondi'],
        ARRAY['profiled edgings',            'profilés radiaux'],
        ARRAY['connection of',               'raccordement de'],
        ARRAY['- decor',                     '- décor'],

        -- ---- pool systems: the longest phrases, so they come first ----------
        ARRAY['Terminal inlay for overflow channel', 'Insert d''extrémité pour caniveau de débordement'],
        ARRAY['Pool edge system',            'Système de margelle'],
        ARRAY['edge system',                 'système de margelle'],
        ARRAY['elevated water level',        'niveau d''eau haut'],
        ARRAY['decreased water level',       'niveau d''eau bas'],
        ARRAY['with integrated grating support', 'avec appui de grille intégré'],
        ARRAY['with grating support',        'avec appui de grille'],
        ARRAY['Terminal piece left for handhold-piece',  'Pièce d''extrémité gauche pour pierre de préhension'],
        ARRAY['Terminal piece right for handhold-piece', 'Pièce d''extrémité droite pour pierre de préhension'],
        ARRAY['Terminal piece',              'Pièce d''extrémité'],
        ARRAY['terminal piece',              'pièce d''extrémité'],
        ARRAY['Handhold-piece',              'Pierre de préhension'],
        ARRAY['handhold-piece',              'pierre de préhension'],
        ARRAY['Pool edging tiles',           'Margelles de bassin'],
        ARRAY['Pool edging tile',            'Margelle de bassin'],
        ARRAY['Edging tile',                 'Margelle'],
        ARRAY['Pool edging',                 'Margelle de bassin'],
        ARRAY['overflow channel',            'caniveau de débordement'],

        -- ---- product families. Singulars listed alongside plurals: the
        --      catalogue uses both, and a missing singular leaves English in
        --      the middle of an otherwise French name.
        ARRAY['Floor/wall tiles',            'Carreau de sol/mur'],
        ARRAY['Floor/wall tile',             'Carreau de sol/mur'],
        ARRAY['Wall/floor tile',             'Carreau mur/sol'],
        ARRAY['Floor tile extra thick',      'Carreau de sol extra épais'],
        ARRAY['Floor tiles',                 'Carreau de sol'],
        ARRAY['Floor tile',                  'Carreau de sol'],
        ARRAY['Wall tiles',                  'Carreau mural'],
        ARRAY['Wall tile',                   'Carreau mural'],
        ARRAY['Patio tiles',                 'Dalle de terrasse'],
        ARRAY['Patio tile',                  'Dalle de terrasse'],
        ARRAY['Stair thread tile',           'Nez de marche'],
        ARRAY['Stair safety border',         'Bordure de sécurité d''escalier'],
        ARRAY['Stair tiles',                 'Carreau d''escalier'],
        ARRAY['Stair tile',                  'Carreau d''escalier'],
        ARRAY['Nosing tile',                 'Nez de marche'],
        ARRAY['Shower tub system',           'Système de receveur de douche'],
        ARRAY['Shower floors',               'Receveur de douche'],
        ARRAY['Special tiles',               'Pièce spéciale'],
        ARRAY['Grooved tiles',               'Dalle rainurée'],
        ARRAY['Grooved tile',                'Dalle rainurée'],
        ARRAY['Gutter tile',                 'Dalle de caniveau'],
        ARRAY['Grid profile tile',           'Profilé de grille'],
        ARRAY['Decorative tile',             'Carreau décoratif'],
        ARRAY['Double-tip tile',             'Carreau à double pointe'],
        ARRAY['Strip tile',                  'Carreau en bande'],
        ARRAY['Letter tile',                 'Carreau lettre'],
        ARRAY['Profiled edging',             'Profilé radial'],
        ARRAY['profiled edging',             'profilé radial'],
        ARRAY['End-skirting',                'Plinthe d''extrémité'],
        ARRAY['Cove skirtings',              'Gorges'],
        ARRAY['Cove skirting',               'Gorge'],
        ARRAY['cove skirtings',              'gorges'],
        ARRAY['cove skirting',               'gorge'],
        ARRAY['Skirtings',                   'Plinthes'],
        ARRAY['Skirting',                    'Plinthe'],
        ARRAY['skirtings',                   'plinthes'],
        ARRAY['skirting',                    'plinthe'],
        ARRAY['Shaped piece',                'Pièce de forme'],
        ARRAY['External angle',              'Angle extérieur'],
        ARRAY['External corner',             'Angle extérieur'],
        ARRAY['Internal corner',             'Angle intérieur'],
        ARRAY['external corner',             'angle extérieur'],
        ARRAY['internal corner',             'angle intérieur'],
        ARRAY['rounded corner',              'angle arrondi'],
        ARRAY['Border - decor',              'Frise - décor'],
        ARRAY['Border',                      'Frise'],
        ARRAY['Orientation aids for the blind', 'Guidage podotactile'],
        ARRAY['Outlets + cover gratings',    'Siphons + grilles de recouvrement'],
        ARRAY['Natural Ceramics',            'Céramique naturelle'],
        ARRAY['Mosaic',                      'Mosaïque'],
        ARRAY['mosaic',                      'mosaïque'],
        ARRAY['Decors',                      'Décors'],
        ARRAY['Pool',                        'Bassin'],

        -- ---- qualifiers. "long side rounded" and "mm long" are listed before
        --      the bare words they contain, because " long" on its own would
        --      otherwise turn "long side" into "de long side".
        ARRAY['long side rounded',           'côté long arrondi'],
        ARRAY['for the connection of',       'pour le raccordement de'],
        ARRAY['connection on',               'raccordement sur'],
        ARRAY['v-pointed profile symetric',  'profil en V symétrique'],
        ARRAY['v-pointed profile asymetric', 'profil en V asymétrique'],
        ARRAY['wave profile',                'profil ondulé'],
        ARRAY['with bevel and grooving',     'avec chanfrein et rainurage'],
        ARRAY['extra thick',                 'extra épais'],
        ARRAY['with outlet',                 'avec évacuation'],
        ARRAY['"low-noise"',                 'silencieux'],
        ARRAY['"half-size"',                 'demi-longueur'],
        ARRAY['"half size"',                 'demi-longueur'],
        ARRAY['(t-pieces)',                  '(pièces en T)'],
        ARRAY['non-slip-safety',             'antidérapant'],
        ARRAY['safety marking',              'marquage de sécurité'],
        ARRAY['overglazed',                  'surémaillé'],
        ARRAY['grooving',                    'rainurage'],
        ARRAY['bevel',                       'chanfrein'],
        ARRAY['outlet',                      'évacuation'],
        ARRAY['corner',                      'angle'],
        ARRAY['recess',                      'réservation'],
        ARRAY['strip',                       'bande'],
        ARRAY['glued',                       'collé'],
        ARRAY['marking',                     'de repérage'],
        ARRAY['rounded',                     'arrondi'],
        ARRAY['radius',                      'rayon'],
        ARRAY['mm long',                     'mm de long'],
        ARRAY['cm long',                     'cm de long'],
        ARRAY['mm wide',                     'mm de large'],
        ARRAY['cm wide',                     'cm de large'],
        ARRAY[' left',                       ' gauche'],
        ARRAY[' right',                      ' droite'],
        ARRAY[' large ',                     ' grand '],
        ARRAY[' small ',                     ' petit '],
        ARRAY[' channel',                    ' caniveau'],
        ARRAY[' for ',                       ' pour '],
        ARRAY[' with ',                      ' avec '],
        ARRAY[' and ',                       ' et ']
    ];
BEGIN
    FOREACH pair SLICE 1 IN ARRAY pairs LOOP
        result := replace(result, pair[1], pair[2]);
    END LOOP;
    RETURN result;
END;
$fn$ LANGUAGE plpgsql IMMUTABLE;

-- -----------------------------------------------------------------------------
-- Colours.
--
-- WHOLE-LABEL LOOKUP, not word substitution, and that is the important part.
--
-- The first attempt here did substitute word by word, and it produced French
-- that was wrong in a way nobody would notice from a row count: "stone-grey"
-- came out as "pierre-gris" and "chalk white" as "chalk blanc", because French
-- puts the adjective after the noun and English does not. Every one of those
-- rows would have satisfied a "labels differ between locales" assertion while
-- being unreadable to an actual French speaker.
--
-- So this maps complete labels only. A colour either has a correct French name
-- here or keeps its English one -- which is also what real catalogues do with
-- marketing coinages like "shabby chic brown" or "Calacatta gold". Partial
-- coverage that is right beats full coverage that is wrong, and the coverage
-- number is reported at the end of this file rather than assumed.
-- -----------------------------------------------------------------------------
CREATE FUNCTION catalog.i18n_fr_colour(src text) RETURNS text AS $fn$
DECLARE
    key text := lower(btrim(src));
BEGIN
    RETURN COALESCE((SELECT fr FROM (VALUES
        ('white',                     'blanc'),
        ('black',                     'noir'),
        ('grey',                      'gris'),
        ('gray',                      'gris'),
        ('anthracite',                'anthracite'),
        ('deep anthracite',           'anthracite profond'),
        ('basalt',                    'basalte'),
        ('basalt-grey',               'gris basalte'),
        ('graphite',                  'graphite'),
        ('pearl',                     'perle'),
        ('silver',                    'argent'),
        ('quartz',                    'quartz'),
        ('lime',                      'chaux'),
        ('clay',                      'argile'),
        ('marble',                    'marbre'),
        ('chalk',                     'craie'),
        ('cream',                     'crème'),
        ('ivory',                     'ivoire'),
        ('ivory white',               'blanc ivoire'),
        ('natural-white',             'blanc naturel'),
        ('natural white',             'blanc naturel'),
        ('sand white',                'blanc sable'),
        ('sandwhite',                 'blanc sable'),
        ('arctic white',              'blanc arctique'),
        ('carrara white',             'blanc carrare'),
        ('italian white',             'blanc italien'),
        ('sunny white',               'blanc ensoleillé'),
        ('off white',                 'blanc cassé'),
        ('chalk white',               'blanc craie'),
        ('active white',              'blanc actif'),
        ('sand-beige',                'beige sable'),
        ('sand beige',                'beige sable'),
        ('almond beige',              'beige amande'),
        ('sunset beige',              'beige crépuscule'),
        ('light beige',               'beige clair'),
        ('dark beige',                'beige foncé'),
        ('grey-beige',                'gris-beige'),
        ('cream-beige multicolored',  'beige crème multicolore'),
        ('light grey',                'gris clair'),
        ('dark grey',                 'gris foncé'),
        ('medium grey',               'gris moyen'),
        ('mid-grey',                  'gris moyen'),
        ('stone grey',                'gris pierre'),
        ('stone-grey',                'gris pierre'),
        ('sand grey',                 'gris sable'),
        ('sand-grey',                 'gris sable'),
        ('sandgrey',                  'gris sable'),
        ('light grey-mix',            'gris clair chiné'),
        ('active stone grey',         'gris pierre actif'),
        ('dark blue',                 'bleu foncé'),
        ('light blue',                'bleu clair'),
        ('intense blue',              'bleu intense'),
        ('aqua blue',                 'bleu aqua'),
        ('silk blue',                 'bleu soie'),
        ('active azure',              'azur actif'),
        ('medium azure',              'azur moyen'),
        ('azure',                     'azur'),
        ('olive-green flamed',        'vert olive flammé'),
        ('red green flamed',          'rouge vert flammé'),
        ('green',                     'vert'),
        ('red',                       'rouge'),
        ('contrasting red',           'rouge contrasté'),
        ('burgundy red',              'rouge bordeaux'),
        ('rust',                      'rouille'),
        ('brown',                     'brun'),
        ('medium brown',              'brun moyen'),
        ('dark brown',                'brun foncé'),
        ('light brown',               'brun clair'),
        ('dark taupe',                'taupe foncé'),
        ('taupe',                     'taupe'),
        ('autumn leaves',             'feuilles d''automne'),
        ('crema',                     'crema'),
        ('titanit',                   'titanit'),
        ('terra',                     'terre'),
        ('sepia',                     'sépia'),
        ('volcano',                   'volcan'),
        ('oak',                       'chêne'),
        ('anthracite oak',            'chêne anthracite'),
        ('pitch black',               'noir profond'),
        ('deep black',                'noir profond'),
        ('yellow',                    'jaune'),
        ('pink',                      'rose'),
        ('gold',                      'or'),
        ('neutral 10 (active white)',         'neutre 10 (blanc actif)'),
        ('neutral 10 (active white glossy)',  'neutre 10 (blanc actif brillant)'),
        ('grey-beige / neutral 1 (deep black)',      'gris-beige / neutre 1 (noir profond)'),
        ('grey-beige / neutral 10 (active white)',   'gris-beige / neutre 10 (blanc actif)')
    ) AS t(en, fr) WHERE t.en = key), src);
END;
$fn$ LANGUAGE plpgsql IMMUTABLE;

-- =============================================================================
-- 1. Reference vocabulary -- translated term by term.
-- =============================================================================

INSERT INTO attribute_group_tr (group_id, locale_code, name)
SELECT g.id, 'fr-FR', v.name FROM (VALUES
    ('appearance',  'Aspect'),
    ('commercial',  'Commercial'),
    ('dimensions',  'Dimensions'),
    ('logistics',   'Logistique'),
    ('planning',    'Conception & BIM'),
    ('surface',     'Surface'),
    ('technical',   'Données techniques'),
    ('usage',       'Utilisation')
) AS v(code, name) JOIN attribute_group g ON g.code = v.code
ON CONFLICT DO NOTHING;

INSERT INTO attribute_tr (attribute_id, locale_code, label)
SELECT a.id, 'fr-FR', v.label FROM (VALUES
    ('available_from',    'Disponible à partir du'),
    ('bim_ready',         'Objet BIM disponible'),
    ('colour',            'Couleur'),
    ('colour_variation',  'Variation de nuance'),
    ('finishing',         'Traitement de surface'),
    ('frost_resistant',   'Résistant au gel'),
    ('glaze',             'Émaillage'),
    ('joint_width_mm',    'Largeur de joint'),
    ('load_group',        'Groupe de charge'),
    ('lrv',               'Facteur de réflexion lumineuse Y'),
    ('material_type',     'Type de matériau'),
    ('mfg_length_mm',     'Longueur de fabrication'),
    ('mfg_width_mm',      'Largeur de fabrication'),
    ('ncs_number',        'Numéro NCS'),
    ('nominal_size',      'Format nominal'),
    ('optics',            'Aspect'),
    ('pieces_per_m2',     'Pièces par m²'),
    ('price_group',       'Groupe de prix'),
    ('product_standard',  'Norme produit'),
    ('ral_number',        'Numéro RAL'),
    ('rectified',         'Rectifié'),
    ('slip_barefoot',     'Résistance au glissement pieds nus (A/B/C)'),
    ('slip_commercial',   'Résistance au glissement en usage professionnel (R)'),
    ('slip_displacement', 'Volume de déplacement (V)'),
    ('slip_resistance',   'Résistance au glissement'),
    ('surface_gloss',     'Degré de brillance'),
    ('surface_structure', 'Structure'),
    ('thickness_mm',      'Épaisseur du matériau'),
    ('use_indoor',        'Intérieur/extérieur'),
    ('use_wall_floor',    'Utilisation mur/sol'),
    ('wear_group',        'Groupe d''usure'),
    ('weight_per_m2',     'Poids par m²')
) AS v(code, label) JOIN attribute a ON a.code = v.code
ON CONFLICT DO NOTHING;

INSERT INTO application_tr (application_id, locale_code, label)
SELECT a.id, 'fr-FR', v.label FROM (VALUES
    ('circulation',     'Circulation'),
    ('commercial',      'Commerce'),
    ('education',       'Enseignement'),
    ('facade',          'Façade'),
    ('floor_covering',  'Revêtement de sol'),
    ('healthcare',      'Santé'),
    ('hospital',        'Hôpital'),
    ('hospitality',     'Hôtellerie'),
    ('hotel',           'Hôtel'),
    ('hygiene_surface', 'Surface hygiénique'),
    ('kitchen',         'Cuisine'),
    ('office',          'Bureau'),
    ('pool_lining',     'Revêtement de bassin'),
    ('residential',     'Résidentiel'),
    ('school',          'École'),
    ('swimming_pool',   'Piscine'),
    ('wall_cladding',   'Revêtement mural'),
    ('wet_room',        'Local humide')
) AS v(code, label) JOIN application a ON a.code = v.code
ON CONFLICT DO NOTHING;

-- Non-colour options. Colours are handled by the derivation below.
INSERT INTO attribute_option_tr (option_id, locale_code, label)
SELECT o.id, 'fr-FR', v.label
FROM (VALUES
    ('finishing',         'hytect',        'Hytect'),
    ('finishing',         'protecta',      'Protecta'),
    ('finishing',         'without',       'sans'),
    ('glaze',             'glazed',        'émaillé'),
    ('glaze',             'unglazed',      'non émaillé'),
    ('material_type',     'conductive',    'Dalles céramiques conductrices'),
    ('material_type',     'earthenware',   'Carreaux en faïence'),
    ('material_type',     'porcelain',     'Grès cérame fin'),
    ('material_type',     'stoneware',     'Dalles en grès'),
    ('surface_gloss',     'glossy',        'brillant'),
    ('surface_gloss',     'matt',          'mat'),
    ('surface_gloss',     'polished',      'poli'),
    ('surface_gloss',     'semi_polished', 'semi-poli'),
    ('surface_gloss',     'silky_matt',    'satiné'),
    ('surface_gloss',     'stone_matt',    'mat pierre'),
    ('surface_structure', 'even',          'lisse'),
    ('surface_structure', 'relief',        'relief'),
    ('surface_structure', 'structured',    'structuré')
) AS v(attr, code, label)
JOIN attribute a ON a.code = v.attr
JOIN attribute_option o ON o.attribute_id = a.id AND o.code = v.code
ON CONFLICT DO NOTHING;

-- Taxonomy: only the genuine category names. Every other node is a series name
-- -- Alcina, Plural, Craft -- and a proper noun stays a proper noun in French.
INSERT INTO taxonomy_node_tr (node_id, locale_code, name, translation_status)
SELECT n.id, 'fr-FR', v.name, 'approved' FROM (VALUES
    ('floor_tiles',                  'Carreaux de sol'),
    ('wall_tiles',                   'Carreaux muraux'),
    ('patio_tiles',                  'Dalles de terrasse'),
    ('stair_tiles',                  'Carreaux d''escalier'),
    ('shower_floors',                'Receveurs de douche'),
    ('special_tiles',                'Pièces spéciales'),
    ('grooved_tiles',                'Dalles rainurées'),
    ('mosaic',                       'Mosaïque'),
    ('decors',                       'Décors'),
    ('pool',                         'Piscine'),
    ('skirtings',                    'Plinthes'),
    ('skirtings_2',                  'Système de gorge'),
    ('pool_edging_tiles',            'Margelles de bassin'),
    ('profiled_edging',              'Système de profilés radiaux'),
    ('natural_ceramics',             'Céramique naturelle'),
    ('orientation_aids_for_the_bli', 'Guidage podotactile'),
    ('outlets_cover_gratings',       'Siphons et grilles de recouvrement'),
    ('pool_edge_system_bamberg',     'Système de margelle Bamberg'),
    ('pool_edge_system_finnland',    'Système de margelle Finnland'),
    ('pool_edge_system_finnland_ii', 'Système de margelle Finnland II'),
    ('pool_edge_system_wiesbaden_s', 'Système de margelle Wiesbaden Style'),
    ('pool_edge_system_wiesbaden_d', 'Système de margelle Wiesbaden - niveau d''eau bas'),
    ('pool_edge_system_wiesbaden_e', 'Système de margelle Wiesbaden - niveau d''eau haut')
) AS v(code, name) JOIN taxonomy_node n ON n.code = v.code
ON CONFLICT DO NOTHING;

-- Series nodes: the French row carries the same proper noun, so the Categories
-- filter is populated in French rather than falling back for two thirds of the
-- tree. Marked 'approved' because "Alcina" genuinely is the French for Alcina.
INSERT INTO taxonomy_node_tr (node_id, locale_code, name, translation_status)
SELECT n.id, 'fr-FR', en.name, 'approved'
FROM taxonomy_node n
JOIN taxonomy_node_tr en ON en.node_id = n.id AND en.locale_code = 'en-GB'
WHERE NOT EXISTS (
    SELECT 1 FROM taxonomy_node_tr fr
    WHERE fr.node_id = n.id AND fr.locale_code = 'fr-FR')
ON CONFLICT DO NOTHING;

INSERT INTO brand_tr (brand_id, locale_code, name)
SELECT b.id, 'fr-FR', en.name
FROM brand b JOIN brand_tr en ON en.brand_id = b.id AND en.locale_code = 'en-GB'
ON CONFLICT DO NOTHING;

-- =============================================================================
-- 2. Derived text -- colours and product names.
-- =============================================================================

INSERT INTO attribute_option_tr (option_id, locale_code, label)
SELECT o.id, 'fr-FR', catalog.i18n_fr_colour(en.label)
FROM attribute_option o
JOIN attribute a ON a.id = o.attribute_id AND a.code = 'colour'
JOIN attribute_option_tr en ON en.option_id = o.id AND en.locale_code = 'en-GB'
ON CONFLICT DO NOTHING;

-- Four products in five. The fifth is left with no French row on purpose: the
-- grid then shows real French names beside German fallbacks, which is both what
-- a half-translated catalogue looks like and the only way to see the fallback
-- marker work. hashtext is used rather than random() so the same products are
-- chosen on every reset -- a seed that shuffled itself would make every
-- assertion downstream flaky.
INSERT INTO product_tr (product_id, locale_code, name, short_desc, translation_status)
SELECT p.id,
       'fr-FR',
       catalog.i18n_fr(en.name),
       catalog.i18n_fr(en.short_desc),
       'approved'
FROM product p
JOIN product_tr en ON en.product_id = p.id AND en.locale_code = 'en-GB'
WHERE abs(hashtext(p.product_code)) % 5 <> 0
ON CONFLICT DO NOTHING;

-- =============================================================================
-- 3. Coverage check.
--
-- A seed that silently produced English-looking "French" would be worse than no
-- French at all: every locale assertion downstream would pass while the feature
-- was broken. R-10 is the standing lesson here -- a check that cannot fail gets
-- counted as evidence. So this one fails the load.
-- =============================================================================
DO $check$
DECLARE
    fr_products   int;
    en_products   int;
    untranslated  int;
    leaked        int;
    colours_fr    int;
    colours_used  int;
BEGIN
    SELECT count(*) INTO fr_products FROM product_tr WHERE locale_code = 'fr-FR';
    SELECT count(*) INTO en_products FROM product_tr WHERE locale_code = 'en-GB';
    untranslated := en_products - fr_products;

    IF fr_products = 0 THEN
        RAISE EXCEPTION 'French pack produced no product rows at all.';
    END IF;

    -- The deliberate gap has to be a gap, not an accident in either direction.
    IF untranslated = 0 THEN
        RAISE EXCEPTION
            'Every product got a French name; the fallback path is now untestable.';
    END IF;

    IF untranslated > en_products / 2 THEN
        RAISE EXCEPTION
            'Only % of % products were translated -- the selection rule is wrong.',
            fr_products, en_products;
    END IF;

    -- The phrase table is order-sensitive and fails quietly when it is wrong.
    -- These words appear in most product names, so a French row still carrying
    -- one means a longer phrase failed to match ahead of it.
    SELECT count(*) INTO leaked
    FROM product_tr
    WHERE locale_code = 'fr-FR'
      AND name ~* ('\y(' || array_to_string(ARRAY[
            'tiles?','pieces?','edging','skirtings?','water level','channels?',
            'corner','overflow','outlet','stair','grooved','cove','recess',
            'strip','gutter','connection','bevel','border','safety','patio',
            'mosaic','wall','floor','shower','wide','long side','with','and',
            'for','left','right','half.size','rounded','radius','decor',
            'thick','glued','grooving','marking'
          ], '|') || ')\y');

    IF leaked > 0 THEN
        RAISE EXCEPTION
            '% French product names still contain untranslated English terms.', leaked;
    END IF;

    -- Colour coverage is measured against colours a product actually uses, not
    -- against the whole 265-row option list. Most of that list is never
    -- referenced by anything, so a percentage over all of it would understate
    -- what a user sees on screen and would drift every time the client's export
    -- adds a shade nobody stocks.
    SELECT count(DISTINCT o.id) FILTER (WHERE fr.label <> en.label),
           count(DISTINCT o.id)
      INTO colours_fr, colours_used
    FROM attribute_option o
    JOIN attribute a           ON a.id = o.attribute_id AND a.code = 'colour'
    JOIN attribute_option_tr en ON en.option_id = o.id AND en.locale_code = 'en-GB'
    JOIN attribute_option_tr fr ON fr.option_id = o.id AND fr.locale_code = 'fr-FR'
    JOIN attribute_value av    ON av.value_option_id = o.id;

    IF colours_used > 0 AND colours_fr * 2 < colours_used THEN
        RAISE EXCEPTION
            'Only % of % colours in use have French names; the lookup has been gutted.',
            colours_fr, colours_used;
    END IF;

    RAISE NOTICE 'fr-FR pack: % of % products translated, % left as fallbacks.',
        fr_products, en_products, untranslated;
    RAISE NOTICE 'fr-FR pack: % of % colours in use have French names.',
        colours_fr, colours_used;
END
$check$;

DROP FUNCTION catalog.i18n_fr(text);
DROP FUNCTION catalog.i18n_fr_colour(text);
