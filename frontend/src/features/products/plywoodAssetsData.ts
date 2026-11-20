import type { AddAssetRequest, ProductAsset, ProductAssets } from './types';
import { ALL_PLYWOOD_PRODUCTS } from './plywoodProductsData';
import {
  getPlywoodImageForRole,
  PLYWOOD_APPLICATION_KITCHEN_SVG,
  PLYWOOD_CROSS_SECTION_SVG,
  PLYWOOD_FACE_SHEET_SVG,
  PLYWOOD_SHUTTERING_SVG,
  PLYWOOD_STACK_SVG,
  PLYWOOD_TEAK_VENEER_SVG,
} from './plywoodSvgImages';

const APP_BASE_URL = import.meta.env.BASE_URL;

function uploadsPlyUrl(path: string): string {
  return `${APP_BASE_URL.replace(/\/?$/, '/')}${path.replace(/^\/+/, '')}`;
}

/**
 * Authentic product images from uploads_ply directory.
 * Maps product codes to local served image paths.
 */
export const UPLOADS_PLY_MAP: Record<string, string> = {
  'PG-CLUB-700': uploadsPlyUrl('/uploads_ply/ply-club-700.jpeg'),
  'PG-CLUB-500': uploadsPlyUrl('/uploads_ply/CLUB500.jpg'),
  'PG-PLAT-BWP': uploadsPlyUrl('/uploads_ply/Platinum_Fire_retardant_BWP_Grade_Plywood_Thickness_18_mm_Plywood_79ebc4f8-850f-4dfc-a901-e31a17f91707.webp'),
  'PG-FIRE-RET': uploadsPlyUrl('/uploads_ply/fire plywood.jpg'),
  'PG-710-MAR': uploadsPlyUrl('/uploads_ply/marine plywood.jpg'),
  'PG-GOLD-BWP': uploadsPlyUrl('/uploads_ply/Greenply Gold 710 BWP Plywood.png'),
  'PG-303-MR': uploadsPlyUrl('/uploads_ply/images (1) (1).jpg'),
  'PG-FLEX-PLY': uploadsPlyUrl('/uploads_ply/images (2)a.jpg'),
  'PG-ECO-PLAT': uploadsPlyUrl('/uploads_ply/Platinum_Fire_retardant_BWP_Grade_Plywood_Thickness_18_mm_Plywood_79ebc4f8-850f-4dfc-a901-e31a17f91707.webp'),
  'PG-ECO-710': uploadsPlyUrl('/uploads_ply/marine plywood.jpg'),
  'PG-ECO-MR': uploadsPlyUrl('/uploads_ply/images (3).jpg'),
  'PG-ECO-TMR': uploadsPlyUrl('/uploads_ply/images (4).jpg'),
  'PG-OPTIMA-G': uploadsPlyUrl('/uploads_ply/images (3)a.jpg'),
  'PG-BHAROSA': uploadsPlyUrl('/uploads_ply/gold plywood.jpg'),
  'PG-JANSATHI': uploadsPlyUrl('/uploads_ply/fire plywood.jpg'),
  'PG-SHUTTER-BWP': uploadsPlyUrl('/uploads_ply/Film faced plywood shuttering plywood for construction _ TT PLYWOOD.png'),
  'PG-CALIB-25': uploadsPlyUrl('/uploads_ply/Structural Grade Plywood in India_ Prices & Guide.png'),
  'PG-CALIB-19': uploadsPlyUrl('/uploads_ply/Promaxx Plus Ply 19 mm (8x4).png'),
  'PG-CALIB-16': uploadsPlyUrl('/uploads_ply/Frikly Club BWP Red Core Calibrated Plywood _ 6 MM _ 8 x 4 Feet.png'),
  'PG-CALIB-12': uploadsPlyUrl('/uploads_ply/9mm BWP Calibrated Plywood at ₹ 34_sq ft _ Plywood Board in New Delhi _ ID_ 2856762006391.png'),
  'PG-CALIB-9': uploadsPlyUrl('/uploads_ply/9mm BWP Calibrated Plywood at ₹ 34_sq ft _ Plywood Board in New Delhi _ ID_ 2856762006391.png'),
  'PG-CALIB-6': uploadsPlyUrl('/uploads_ply/Frikly Club BWP Red Core Calibrated Plywood _ 6 MM _ 8 x 4 Feet.png'),
  'PG-BLOCK-25': uploadsPlyUrl('/uploads_ply/Buy Online Navkar Premium Blockboard ( Pure Pine ) 25mm.png'),
  'PG-BLOCK-19': uploadsPlyUrl('/uploads_ply/Premium Pinewood Block Board Online _ Best Price Deals.png'),
  'PG-DOOR-FLUSH': uploadsPlyUrl('/uploads_ply/Flush Doors_ Design, Benefits, and Uses.png'),
  'PG-VENEER-TEAK': uploadsPlyUrl('/uploads_ply/What Makes Teak Veneers an Ideal Pick for Your Home – Duroply Latest Blogs & Information.png'),
  'PG-VENEER-WALNUT': uploadsPlyUrl('/uploads_ply/What is Walnut Veneer.png'),
  'PG-VENEER-OAK': uploadsPlyUrl('/uploads_ply/images (2)a.jpg'),
};

/**
 * Returns the matching photo URL from uploads_ply for a given product code.
 */
export function getUploadsPlyImageForProduct(code: string, _role = 'MAIN'): string | null {
  const norm = code.toUpperCase().replace(/^GP-/, 'PG-');
  const direct = UPLOADS_PLY_MAP[norm];
  if (direct) {
    return direct;
  }

  // Keyword-based fallback
  if (norm.includes('700')) return uploadsPlyUrl('/uploads_ply/ply-club-700.jpeg');
  if (norm.includes('500')) return uploadsPlyUrl('/uploads_ply/CLUB500.jpg');
  if (norm.includes('FIRE')) return uploadsPlyUrl('/uploads_ply/fire plywood.jpg');
  if (norm.includes('GOLD')) return uploadsPlyUrl('/uploads_ply/gold plywood.jpg');
  if (norm.includes('MARINE') || norm.includes('710')) return uploadsPlyUrl('/uploads_ply/marine plywood.jpg');
  if (norm.includes('CALIB') && norm.includes('9')) return uploadsPlyUrl('/uploads_ply/9mm BWP Calibrated Plywood at ₹ 34_sq ft _ Plywood Board in New Delhi _ ID_ 2856762006391.png');
  if (norm.includes('CALIB') && norm.includes('6')) return uploadsPlyUrl('/uploads_ply/Frikly Club BWP Red Core Calibrated Plywood _ 6 MM _ 8 x 4 Feet.png');
  if (norm.includes('CALIB') || norm.includes('PROMAXX')) return uploadsPlyUrl('/uploads_ply/Promaxx Plus Ply 19 mm (8x4).png');
  if (norm.includes('SHUTTER') || norm.includes('STRUCTURAL')) return uploadsPlyUrl('/uploads_ply/Structural Grade Plywood in India_ Prices & Guide.png');

  return null;
}

/**
 * Resolves any filename, title, or dead seed URL to a working image URL from uploads_ply or SVG.
 */
export function resolveAssetImageUrl(
  fileName: string | null,
  title: string | null,
  currentUrl: string | null,
): string | null {
  if (currentUrl && (currentUrl.startsWith('/uploads_ply') || currentUrl.startsWith('data:'))) {
    return currentUrl;
  }

  // Preserve user-supplied external images and custom uploads
  if (
    currentUrl &&
    currentUrl.startsWith('http') &&
    !currentUrl.includes('plygallery.com') &&
    !currentUrl.includes('greenply.com') &&
    currentUrl !== 'broken'
  ) {
    return currentUrl;
  }

  const str = `${fileName ?? ''} ${title ?? ''} ${currentUrl ?? ''}`.toLowerCase();

  // Authentic product images from uploads_ply
  if (str.includes('700') || str.includes('club-700')) return '/uploads_ply/ply-club-700.jpeg';
  if (str.includes('500') || str.includes('club-500')) return '/uploads_ply/CLUB500.jpg';
  if (str.includes('platinum')) return '/uploads_ply/Platinum_Fire_retardant_BWP_Grade_Plywood_Thickness_18_mm_Plywood_79ebc4f8-850f-4dfc-a901-e31a17f91707.webp';
  if (str.includes('fire') || str.includes('flammab')) return '/uploads_ply/fire plywood.jpg';
  if (str.includes('710') || str.includes('marine')) return '/uploads_ply/marine plywood.jpg';
  if (str.includes('gold')) return '/uploads_ply/Greenply Gold 710 BWP Plywood.png';
  if (str.includes('303') || str.includes('mr_303') || str.includes('mr 303')) return '/uploads_ply/images (1) (1).jpg';
  if (str.includes('flexi')) return '/uploads_ply/images (2)a.jpg';
  if (str.includes('optima')) return '/uploads_ply/images (3)a.jpg';
  if (str.includes('bharosa')) return '/uploads_ply/gold plywood.jpg';
  if (str.includes('jansathi')) return '/uploads_ply/fire plywood.jpg';
  if (str.includes('shuttering')) return '/uploads_ply/Film faced plywood shuttering plywood for construction _ TT PLYWOOD.png';
  if (str.includes('calibrated') && (str.includes('25') || str.includes('structur'))) return '/uploads_ply/Structural Grade Plywood in India_ Prices & Guide.png';
  if (str.includes('calibrated') && str.includes('19')) return '/uploads_ply/Promaxx Plus Ply 19 mm (8x4).png';
  if (str.includes('calibrated') && str.includes('16')) return '/uploads_ply/Frikly Club BWP Red Core Calibrated Plywood _ 6 MM _ 8 x 4 Feet.png';
  if (str.includes('calibrated') && str.includes('12')) return '/uploads_ply/9mm BWP Calibrated Plywood at ₹ 34_sq ft _ Plywood Board in New Delhi _ ID_ 2856762006391.png';
  if (str.includes('calibrated') && str.includes('9')) return '/uploads_ply/9mm BWP Calibrated Plywood at ₹ 34_sq ft _ Plywood Board in New Delhi _ ID_ 2856762006391.png';
  if (str.includes('calibrated') && str.includes('6')) return '/uploads_ply/Frikly Club BWP Red Core Calibrated Plywood _ 6 MM _ 8 x 4 Feet.png';
  if (str.includes('blockboard') && str.includes('25')) return '/uploads_ply/Buy Online Navkar Premium Blockboard ( Pure Pine ) 25mm.png';
  if (str.includes('blockboard')) return '/uploads_ply/Premium Pinewood Block Board Online _ Best Price Deals.png';
  if (str.includes('flush') || str.includes('door')) return '/uploads_ply/Flush Doors_ Design, Benefits, and Uses.png';
  if (str.includes('teak')) return '/uploads_ply/What Makes Teak Veneers an Ideal Pick for Your Home – Duroply Latest Blogs & Information.png';
  if (str.includes('walnut')) return '/uploads_ply/What is Walnut Veneer.png';
  if (str.includes('oak')) return '/uploads_ply/images (2)a.jpg';
  if (str.includes('truemr') || str.includes('tmr')) return '/uploads_ply/images (4).jpg';
  if (str.includes('cross_section')) return PLYWOOD_CROSS_SECTION_SVG;
  if (str.includes('kitchen') || str.includes('application') || str.includes('joinery')) return PLYWOOD_APPLICATION_KITCHEN_SVG;
  if (str.includes('stack')) return PLYWOOD_STACK_SVG;

  if (currentUrl && !currentUrl.includes('plygallery.com') && !currentUrl.includes('greenply.com') && currentUrl !== 'broken') {
    return currentUrl;
  }
  return null;
}

/**
 * Strips brand name 'Green' from any product or asset string.
 */
export function stripGreen(str: string): string {
  if (!str) return '';
  return str
    .replace(/Green[_-]/gi, '')
    .replace(/\bGreen\s+/gi, '')
    .replace(/\bGreen\b/gi, '')
    .trim();
}

/**
 * In-memory store for client-created or modified assets.
 */
const ASSETS_CACHE = new Map<string, ProductAssets>();

const ASSETS_STORAGE_PREFIX = 'pim.products.assets.v2.';

export function getAssetsStorageKey(productCode: string): string {
  return `${ASSETS_STORAGE_PREFIX}${productCode.toUpperCase()}`;
}

/**
 * Loads persisted assets from browser localStorage if available.
 */
export function loadAssetsFromStorage(productCode: string): ProductAssets | null {
  try {
    if (typeof window === 'undefined' || !window.localStorage) return null;
    const raw = window.localStorage.getItem(getAssetsStorageKey(productCode));
    if (!raw) return null;
    const parsed = JSON.parse(raw) as ProductAssets;
    if (parsed && Array.isArray(parsed.media) && Array.isArray(parsed.documents)) {
      return parsed;
    }
  } catch {
    // Corrupted or inaccessible storage
  }
  return null;
}

/**
 * Saves assets permanently into localStorage and the in-memory cache.
 */
export function saveAssetsToStorage(productCode: string, assets: ProductAssets): void {
  const code = productCode.toUpperCase();
  ASSETS_CACHE.set(code, assets);
  try {
    if (typeof window !== 'undefined' && window.localStorage) {
      window.localStorage.setItem(getAssetsStorageKey(code), JSON.stringify(assets));
    }
  } catch (err) {
    console.warn(`[PIM] Could not persist assets for ${code} to localStorage:`, err);
  }
}

/**
 * Clears custom assets from localStorage and cache, restoring default assets.
 */
export function clearAssetsStorage(productCode: string): void {
  const code = productCode.toUpperCase();
  ASSETS_CACHE.delete(code);
  try {
    if (typeof window !== 'undefined' && window.localStorage) {
      window.localStorage.removeItem(getAssetsStorageKey(code));
    }
  } catch {
    // ignore
  }
}

/**
 * Generates rich, authentic plywood images and documents for any product code.
 */
export function getSyntheticPlywoodAssets(productCode: string): ProductAssets {
  const code = productCode.toUpperCase();

  // 1. Check in-memory cache
  const cached = ASSETS_CACHE.get(code);
  if (cached) {
    return cached;
  }

  // 2. Check persistent localStorage (survives restarts & reloads)
  const stored = loadAssetsFromStorage(code);
  if (stored) {
    ASSETS_CACHE.set(code, stored);
    return stored;
  }

  const normalized = productCode.toUpperCase().replace(/^GP-/, 'PG-');
  const p = ALL_PLYWOOD_PRODUCTS.find(
    (item) => item.code.toUpperCase() === productCode.toUpperCase() || item.code.toUpperCase() === normalized,
  );
  const cleanName = p ? stripGreen(p.name) : stripGreen(productCode);
  const filePrefix = code.toLowerCase().replace(/[^a-z0-9_-]/g, '_');

  const isVeneer = code.includes('VENEER');
  const isShutter = code.includes('SHUTTER');

  // 1. Media: 4 photo views matching the reference layout
  const uploadImg = getUploadsPlyImageForProduct(code, 'MAIN');
  const heroUrl = uploadImg || (isVeneer
    ? PLYWOOD_TEAK_VENEER_SVG
    : isShutter
      ? PLYWOOD_SHUTTERING_SVG
      : PLYWOOD_FACE_SHEET_SVG);

  const media: ProductAsset[] = [
    {
      linkId: 101,
      assetId: 201,
      role: 'MAIN',
      assetType: 'IMAGE',
      storageProvider: 'local',
      fileName: uploadImg ? uploadImg.split('/').pop()! : `${filePrefix}_sheet_hero.jpg`,
      mimeType: 'image/jpeg',
      fileSize: 2450000,
      url: heroUrl,
      title: `${cleanName} Master Sheet`,
      altText: `${cleanName} calibrated face veneer`,
      isPrimary: true,
      sortOrder: 1,
      isExternal: false,
    },
    {
      linkId: 102,
      assetId: 202,
      role: 'GALLERY',
      assetType: 'IMAGE',
      storageProvider: 'local',
      fileName: `${filePrefix}_cross_section_layers.jpg`,
      mimeType: 'image/jpeg',
      fileSize: 1890000,
      url: PLYWOOD_CROSS_SECTION_SVG,
      title: 'Penta-Tech Calibration Layers',
      altText: 'Cross section showing multi-ply hardwood veneer construction',
      isPrimary: false,
      sortOrder: 2,
      isExternal: false,
    },
    {
      linkId: 103,
      assetId: 203,
      role: 'GALLERY',
      assetType: 'IMAGE',
      storageProvider: 'local',
      fileName: `${filePrefix}_application_joinery.jpg`,
      mimeType: 'image/jpeg',
      fileSize: 2150000,
      url: PLYWOOD_APPLICATION_KITCHEN_SVG,
      title: 'Architectural Joinery Installation',
      altText: `${cleanName} installed in custom cabinetry and carcass joinery`,
      isPrimary: false,
      sortOrder: 3,
      isExternal: false,
    },
    {
      linkId: 104,
      assetId: 204,
      role: 'GALLERY',
      assetType: 'IMAGE',
      storageProvider: 'local',
      fileName: `${filePrefix}_panels_stack.jpg`,
      mimeType: 'image/jpeg',
      fileSize: 2310000,
      url: PLYWOOD_STACK_SVG,
      title: 'Calibrated Structural Stack',
      altText: 'Calibrated plywood panels stacked in workshop',
      isPrimary: false,
      sortOrder: 4,
      isExternal: false,
    },
  ];

  // 2. Documents: 4 files matching the reference table
  const documents: ProductAsset[] = [
    {
      linkId: 105,
      assetId: 205,
      role: 'DATASHEET',
      assetType: 'DOCUMENT',
      storageProvider: 'local',
      fileName: `${code}-datasheet.pdf`,
      mimeType: 'application/pdf',
      fileSize: null,
      url: `/documents/${code}-datasheet.pdf`,
      title: 'Technical datasheet',
      altText: null,
      isPrimary: false,
      sortOrder: 1,
      isExternal: false,
    },
    {
      linkId: 106,
      assetId: 206,
      role: 'INSTALL',
      assetType: 'DOCUMENT',
      storageProvider: 'local',
      fileName: `${code}-install.pdf`,
      mimeType: 'application/pdf',
      fileSize: null,
      url: `/documents/${code}-install.pdf`,
      title: 'Installation guide',
      altText: null,
      isPrimary: false,
      sortOrder: 2,
      isExternal: false,
    },
    {
      linkId: 107,
      assetId: 207,
      role: 'BIM',
      assetType: 'BIM',
      storageProvider: 'local',
      fileName: `${code}-bim.ifc`,
      mimeType: 'application/x-step',
      fileSize: null,
      url: `/documents/${code}-bim.ifc`,
      title: 'BIM object',
      altText: null,
      isPrimary: false,
      sortOrder: 3,
      isExternal: false,
    },
    {
      linkId: 108,
      assetId: 208,
      role: 'DRAWING',
      assetType: 'CAD',
      storageProvider: 'local',
      fileName: `${code}-drawing.dxf`,
      mimeType: 'application/acad',
      fileSize: null,
      url: `/documents/${code}-drawing.dxf`,
      title: 'CAD drawing',
      altText: null,
      isPrimary: false,
      sortOrder: 4,
      isExternal: false,
    },
  ];

  const assets: ProductAssets = {
    productCode: code,
    rowVersion: 1,
    media,
    documents,
  };

  ASSETS_CACHE.set(code, assets);
  return assets;
}

/**
 * Sanitizes live API assets to remove 'Green' brand names and ensure all
 * image URLs have guaranteed, working vector/raster visual sources.
 */
export function enhanceLiveAssets(productCode: string, live: ProductAssets): ProductAssets {
  const code = productCode.toUpperCase();
  const synth = getSyntheticPlywoodAssets(code);

  const cleanMedia = (live.media || []).map((m, idx) => {
    const rawName = stripGreen(m.fileName ?? '');
    const cleanTitle = stripGreen(m.title ?? rawName);
    const cleanAlt = stripGreen(m.altText ?? cleanTitle);

    // Only substitute dead sample mock URLs from old demo seeds, never a user-supplied link
    let finalUrl = m.url;
    const isMockSeedUrl =
      !finalUrl ||
      finalUrl.startsWith('https://assets.plygallery.com/products/') ||
      finalUrl.startsWith('https://assets.greenply.com/products/') ||
      finalUrl === 'broken';

    if (isMockSeedUrl) {
      finalUrl = getUploadsPlyImageForProduct(code, m.role) ?? getPlywoodImageForRole(m.role, idx, code);
    }

    const isExternal = isMockSeedUrl
      ? false
      : finalUrl.startsWith('/uploads_ply') || finalUrl.startsWith('data:') || finalUrl.includes('plygallery.com') || finalUrl.includes('greenply.com')
        ? false
        : typeof m.isExternal === 'boolean'
          ? m.isExternal
          : (!finalUrl.startsWith('/') && !finalUrl.startsWith('data:'));

    return {
      ...m,
      fileName: rawName || `${code.toLowerCase()}_asset_${idx + 1}.jpg`,
      title: cleanTitle,
      altText: cleanAlt,
      url: finalUrl,
      storageProvider: !isExternal ? 'local' : (m.storageProvider ?? 'local'),
      isExternal,
    };
  });

  const finalMedia = Array.isArray(live.media) ? cleanMedia : synth.media;

  const cleanDocs = (live.documents || []).map((d) => {
    const rawName = stripGreen(d.fileName ?? '');
    const isSeedDoc =
      !d.url ||
      d.url.includes('plygallery.com') ||
      d.url.includes('greenply.com') ||
      d.url.startsWith('/documents/');

    const finalUrl = isSeedDoc ? `/documents/${rawName || 'datasheet.pdf'}` : d.url;
    const isExternal = isSeedDoc ? false : (typeof d.isExternal === 'boolean' ? d.isExternal : !finalUrl.startsWith('/'));

    return {
      ...d,
      fileName: rawName,
      title: stripGreen(d.title ?? d.fileName ?? 'Document'),
      altText: stripGreen(d.altText ?? ''),
      url: finalUrl,
      storageProvider: !isExternal ? 'local' : (d.storageProvider ?? 'local'),
      isExternal,
    };
  });
  const finalDocuments = Array.isArray(live.documents) ? cleanDocs : synth.documents;

  return {
    productCode: code,
    rowVersion: live.rowVersion || 1,
    media: finalMedia,
    documents: finalDocuments,
  };
}

/**
 * Adds an asset to the cache (supports client-side linking).
 */
export function addSyntheticAsset(productCode: string, req: AddAssetRequest): ProductAssets {
  const current = getSyntheticPlywoodAssets(productCode);
  const code = productCode.toUpperCase();
  const isImage = req.role === 'MAIN' || req.role === 'GALLERY';

  const cleanKey = req.storageKey.trim().replace(/^["']|["']$/g, '');
  let resolvedKey = cleanKey;
  try {
    if (cleanKey.includes('google.') && (cleanKey.includes('/imgres') || cleanKey.includes('/url') || cleanKey.includes('/search'))) {
      const parsed = new URL(cleanKey);
      const direct =
        parsed.searchParams.get('imgurl') ||
        parsed.searchParams.get('url') ||
        parsed.searchParams.get('q');
      if (direct && (direct.startsWith('http://') || direct.startsWith('https://'))) {
        resolvedKey = direct;
      }
    }
  } catch {
    // not a valid URL structure
  }

  let finalFileName = 'image.jpg';
  if (resolvedKey.startsWith('data:')) {
    finalFileName = req.title ? `${req.title.toLowerCase().replace(/[^a-z0-9_-]/g, '_')}.jpg` : 'custom_plywood_image.jpg';
  } else {
    const withoutQuery = resolvedKey.split('?')[0];
    const segment = withoutQuery.split('/').pop() || '';
    if (segment.includes('.')) {
      finalFileName = segment;
    } else {
      finalFileName = isImage ? 'google_image.jpg' : 'document.pdf';
    }
  }

  const newAsset: ProductAsset = {
    linkId: Date.now(),
    assetId: Date.now() + 1,
    role: (req.role as any) || (isImage ? 'GALLERY' : 'DATASHEET'),
    assetType: isImage ? 'IMAGE' : req.role === 'BIM' ? 'BIM' : req.role === 'DRAWING' ? 'CAD' : 'DOCUMENT',
    storageProvider: 's3',
    fileName: finalFileName,
    mimeType: isImage ? 'image/jpeg' : 'application/pdf',
    fileSize: 1500000,
    url: isImage
      ? (resolvedKey.startsWith('http') || resolvedKey.startsWith('data:') || resolvedKey.startsWith('/')
          ? resolvedKey
          : getPlywoodImageForRole(req.role, current.media.length, code))
      : resolvedKey,
    title: req.title || (isImage ? 'Plywood Media Asset' : 'Technical Document'),
    altText: req.altText || null,
    isPrimary: req.role === 'MAIN',
    sortOrder: isImage ? current.media.length + 1 : current.documents.length + 1,
    isExternal: false,
  };

  const updated: ProductAssets = {
    ...current,
    rowVersion: current.rowVersion + 1,
    media: isImage
      ? req.role === 'MAIN'
        ? [newAsset, ...current.media.map((m) => ({ ...m, isPrimary: false }))]
        : [...current.media, newAsset]
      : current.media,
    documents: !isImage ? [...current.documents, newAsset] : current.documents,
  };

  saveAssetsToStorage(code, updated);
  return updated;
}

/**
 * Sets primary image in cache.
 */
export function setPrimarySyntheticAsset(productCode: string, linkId: number): ProductAssets {
  const current = getSyntheticPlywoodAssets(productCode);
  const code = productCode.toUpperCase();

  const updated: ProductAssets = {
    ...current,
    rowVersion: current.rowVersion + 1,
    media: current.media.map((m) => ({
      ...m,
      isPrimary: m.linkId === linkId,
      role: m.linkId === linkId ? 'MAIN' : 'GALLERY',
    })),
  };

  saveAssetsToStorage(code, updated);
  return updated;
}

/**
 * Removes an asset from cache.
 */
export function removeSyntheticAsset(productCode: string, linkId: number): ProductAssets {
  const current = getSyntheticPlywoodAssets(productCode);
  const code = productCode.toUpperCase();

  const updated: ProductAssets = {
    ...current,
    rowVersion: current.rowVersion + 1,
    media: current.media.filter((m) => m.linkId !== linkId),
    documents: current.documents.filter((d) => d.linkId !== linkId),
  };

  // If we unlinked the primary image, make the first remaining image primary
  if (updated.media.length > 0 && !updated.media.some((m) => m.isPrimary)) {
    updated.media[0].isPrimary = true;
    updated.media[0].role = 'MAIN';
  }

  saveAssetsToStorage(code, updated);
  return updated;
}

export interface LinkedProductAssetSummary {
  publicId: string;
  fileName: string;
  title: string;
  assetType: string;
  mimeType: string;
  fileSizeBytes: number;
  width: number;
  height: number;
  storageProvider: string;
  status: string;
  url: string;
  role: string;
  isPrimary: boolean;
  productCode: string;
  productName: string;
  usedBy: Array<{
    productCode: string;
    productName: string;
    productNameLocale: string | null;
    role: string;
    isPrimary: boolean;
  }>;
}

/**
 * Returns all assets that are linked to any product across the catalog.
 * Used by the Digital Assets library to ensure every linked media/document
 * is visible, searchable, and displays all product references.
 */
export function getAllLinkedPlywoodAssets(): LinkedProductAssetSummary[] {
  const mapByUrl = new Map<string, LinkedProductAssetSummary>();

  for (const p of ALL_PLYWOOD_PRODUCTS) {
    const custom = loadAssetsFromStorage(p.code);
    const assets = custom ?? getSyntheticPlywoodAssets(p.code);

    const allItems = [...assets.media, ...assets.documents];
    for (const m of allItems) {
      if (!m.url) continue;

      const usage = {
        productCode: p.code,
        productName: p.name,
        productNameLocale: 'en-GB',
        role: m.role,
        isPrimary: m.isPrimary,
      };

      const existing = mapByUrl.get(m.url);
      if (existing) {
        if (!existing.usedBy.some((u) => u.productCode === p.code && u.role === m.role)) {
          existing.usedBy.push(usage);
        }
      } else {
        const isUploadsPly = m.url.startsWith('/uploads_ply');
        mapByUrl.set(m.url, {
          publicId: `ply-${p.code.toLowerCase()}-${m.linkId}`,
          fileName: m.fileName ?? (isUploadsPly ? m.url.split('/').pop()! : `${p.code.toLowerCase()}_asset.jpg`),
          title: m.title ?? `${p.name} ${m.assetType === 'DOCUMENT' ? 'Document' : 'Photo'}`,
          assetType: m.assetType ?? 'IMAGE',
          mimeType: m.mimeType ?? (m.assetType === 'DOCUMENT' ? 'application/pdf' : 'image/jpeg'),
          fileSizeBytes: m.fileSize ?? 2450000,
          width: 2400,
          height: 1600,
          storageProvider: isUploadsPly ? 'local' : (m.storageProvider ?? 'local'),
          status: 'active',
          url: m.url,
          role: m.role,
          isPrimary: m.isPrimary,
          productCode: p.code,
          productName: p.name,
          usedBy: [usage],
        });
      }
    }
  }

  return Array.from(mapByUrl.values());
}
