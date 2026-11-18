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

/**
 * Strips brand name 'Green' from any product or asset string.
 */
export function stripGreen(str: string): string {
  if (!str) return '';
  return str
    .replace(/Greenply[_-]?/gi, '')
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

  const p = ALL_PLYWOOD_PRODUCTS.find(
    (item) => item.code.toUpperCase() === productCode.toUpperCase(),
  );
  const cleanName = p ? stripGreen(p.name) : stripGreen(productCode);
  const filePrefix = code.toLowerCase().replace(/[^a-z0-9_-]/g, '_');

  const isVeneer = code.includes('VENEER');
  const isShutter = code.includes('SHUTTER');

  // 1. Media: 4 photo views matching the reference layout
  const heroUrl = isVeneer
    ? PLYWOOD_TEAK_VENEER_SVG
    : isShutter
      ? PLYWOOD_SHUTTERING_SVG
      : PLYWOOD_FACE_SHEET_SVG;

  const media: ProductAsset[] = [
    {
      linkId: 101,
      assetId: 201,
      role: 'MAIN',
      assetType: 'IMAGE',
      storageProvider: 's3',
      fileName: `${filePrefix}_sheet_hero.jpg`,
      mimeType: 'image/jpeg',
      fileSize: 2450000,
      url: heroUrl,
      title: `${cleanName} Master Sheet`,
      altText: `${cleanName} calibrated face veneer`,
      isPrimary: true,
      sortOrder: 1,
      isExternal: true,
    },
    {
      linkId: 102,
      assetId: 202,
      role: 'GALLERY',
      assetType: 'IMAGE',
      storageProvider: 's3',
      fileName: `${filePrefix}_cross_section_layers.jpg`,
      mimeType: 'image/jpeg',
      fileSize: 1890000,
      url: PLYWOOD_CROSS_SECTION_SVG,
      title: 'Penta-Tech Calibration Layers',
      altText: 'Cross section showing multi-ply hardwood veneer construction',
      isPrimary: false,
      sortOrder: 2,
      isExternal: true,
    },
    {
      linkId: 103,
      assetId: 203,
      role: 'GALLERY',
      assetType: 'IMAGE',
      storageProvider: 's3',
      fileName: `${filePrefix}_application_joinery.jpg`,
      mimeType: 'image/jpeg',
      fileSize: 2150000,
      url: PLYWOOD_APPLICATION_KITCHEN_SVG,
      title: 'Architectural Joinery Installation',
      altText: `${cleanName} installed in custom cabinetry and carcass joinery`,
      isPrimary: false,
      sortOrder: 3,
      isExternal: true,
    },
    {
      linkId: 104,
      assetId: 204,
      role: 'GALLERY',
      assetType: 'IMAGE',
      storageProvider: 's3',
      fileName: `${filePrefix}_panels_stack.jpg`,
      mimeType: 'image/jpeg',
      fileSize: 2310000,
      url: PLYWOOD_STACK_SVG,
      title: 'Calibrated Structural Stack',
      altText: 'Calibrated plywood panels stacked in workshop',
      isPrimary: false,
      sortOrder: 4,
      isExternal: true,
    },
  ];

  // 2. Documents: 4 files matching the reference table
  const documents: ProductAsset[] = [
    {
      linkId: 105,
      assetId: 205,
      role: 'DATASHEET',
      assetType: 'DOCUMENT',
      storageProvider: 's3',
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
      storageProvider: 's3',
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
      storageProvider: 's3',
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
      storageProvider: 's3',
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
    if (!finalUrl || finalUrl.startsWith('https://assets.greenply.com/products/green_') || finalUrl === 'broken') {
      finalUrl = getPlywoodImageForRole(m.role, idx, code);
    }

    return {
      ...m,
      fileName: rawName || `${code.toLowerCase()}_asset_${idx + 1}.jpg`,
      title: cleanTitle,
      altText: cleanAlt,
      url: finalUrl,
      isExternal: !finalUrl.startsWith('data:'),
    };
  });

  const finalMedia = cleanMedia.length > 0 ? cleanMedia : synth.media;

  const cleanDocs = (live.documents || []).map((d) => ({
    ...d,
    fileName: stripGreen(d.fileName ?? ''),
    title: stripGreen(d.title ?? d.fileName ?? 'Document'),
    altText: stripGreen(d.altText ?? ''),
  }));
  const finalDocuments = cleanDocs.length > 0 ? cleanDocs : synth.documents;

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

  const newAsset: ProductAsset = {
    linkId: Date.now(),
    assetId: Date.now() + 1,
    role: (req.role as any) || (isImage ? 'GALLERY' : 'DATASHEET'),
    assetType: isImage ? 'IMAGE' : req.role === 'BIM' ? 'BIM' : req.role === 'DRAWING' ? 'CAD' : 'DOCUMENT',
    storageProvider: 's3',
    fileName: req.storageKey.startsWith('data:')
      ? (req.title ? `${req.title.toLowerCase().replace(/[^a-z0-9_-]/g, '_')}.jpg` : 'custom_plywood_image.jpg')
      : req.storageKey.split('/').pop() || (isImage ? 'new_plywood_image.jpg' : 'new_document.pdf'),
    mimeType: isImage ? 'image/jpeg' : 'application/pdf',
    fileSize: 1500000,
    url: isImage
      ? (req.storageKey.startsWith('http') || req.storageKey.startsWith('data:') || req.storageKey.startsWith('/')
          ? req.storageKey
          : getPlywoodImageForRole(req.role, current.media.length, code))
      : req.storageKey,
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
