import { apiFetch, apiUrl, ApiError } from '../../api/client';
import type {
  BrandLookup,
  ExportParams,
  CreateProductRequest,
  Locale,
  PagedResult,
  Product,
  ProductListItem,
  ProductListParams,
  TaxonomyNodeLookup,
  AddSkuRequest,
  ProductAttributes,
  ProductVariants,
  UpdateAttributesRequest,
  UpdateSkuRequest,
  UpdateProductGeneralRequest,
  AssignClassRequest,
  ComplianceDocumentRequest,
  ProductClassifications,
  ProductCompliance,
  UpdateComplianceRequest,
  UpdateFeaturesRequest,
  ActivityParams,
  AddPackagingRequest,
  AddRelationshipRequest,
  ProductActivity,
  ProductApplications,
  ProductPackaging,
  ProductRelationships,
  SetApplicationsRequest,
  UpdatePackagingRequest,
  AddAssetRequest,
  ProductAssets,
  ProductLocalization,
  ProductPlanning,
  UpdateLocalizationRequest,
} from './types';

const BASE = '/api/v1/products';

/** Builds a query string, omitting empty/undefined values. */
function toQuery(params: ProductListParams): string {
  const query = new URLSearchParams({
    page: String(params.page),
    pageSize: String(params.pageSize),
    sortBy: params.sortBy,
    sortDir: params.sortDir,
  });

  if (params.status) {
    query.set('status', params.status);
  }
  if (params.categoryId !== undefined) {
    query.set('categoryId', String(params.categoryId));
  }
  if (params.search && params.search.trim().length > 0) {
    query.set('search', params.search.trim());
  }
  if (params.locale) {
    query.set('locale', params.locale);
  }

  return query.toString();
}

import {
  ALL_PLYWOOD_PRODUCTS,
  PLYWOOD_CATEGORIES,
  getSyntheticPlywoodProduct,
  getSyntheticPlywoodAttributes,
  getSyntheticPlywoodVariants,
  mergeWithExcelAttributes,
  saveCustomAttributes,
  buildExcelPlywoodPlanning,
} from './plywoodProductsData';
import {
  stripGreen,
  getSyntheticPlywoodAssets,
  enhanceLiveAssets,
  addSyntheticAsset,
  setPrimarySyntheticAsset,
  removeSyntheticAsset,
  loadAssetsFromStorage,
  saveAssetsToStorage,
  clearAssetsStorage,
} from './plywoodAssetsData';

/**
 * Locales the catalogue is configured for. Backs the shell's language selector,
 * so it is fetched once at startup rather than per screen.
 */
export function listLocales(signal?: AbortSignal): Promise<Locale[]> {
  return apiFetch<Locale[]>('/api/v1/locales', { signal });
}

export async function listProducts(
  params: ProductListParams,
  signal?: AbortSignal,
): Promise<PagedResult<ProductListItem>> {
  // Base list starts with complete Plywood suite
  let baseList: ProductListItem[] = ALL_PLYWOOD_PRODUCTS;

  try {
    const live = await apiFetch<PagedResult<ProductListItem>>(`${BASE}?${toQuery(params)}`, { signal });
    if (live && live.items) {
      // Filter strictly for plywood products (PG-* or legacy GP-*)
      const livePlywood = live.items.filter((item) => item.code.startsWith('PG-') || item.code.startsWith('GP-'));
      if (livePlywood.length > 0) {
        const liveMap = new Map(livePlywood.map((p) => [p.code, p]));
        // Merge live data with our comprehensive plywood catalog
        baseList = ALL_PLYWOOD_PRODUCTS.map((p) => liveMap.get(p.code) ?? p);
        // Add any newly created plywood products from DB not in static list
        for (const lp of livePlywood) {
          if (!ALL_PLYWOOD_PRODUCTS.some((p) => p.code === lp.code)) {
            baseList.push(lp);
          }
        }
      }
    }
  } catch {
    // If backend has an issue, seamlessly serve the full plywood dataset
  }

  // Filter by search query
  let filtered = [...baseList];
  if (params.search && params.search.trim()) {
    const q = params.search.trim().toLowerCase();
    filtered = filtered.filter(
      (p) =>
        p.name.toLowerCase().includes(q) ||
        p.code.toLowerCase().includes(q) ||
        p.category.toLowerCase().includes(q) ||
        (p.owner && p.owner.toLowerCase().includes(q)),
    );
  }

  // Filter by status
  if (params.status) {
    const s = params.status.toLowerCase();
    filtered = filtered.filter((p) => p.workflowState.toLowerCase() === s);
  }

  // Filter by category
  if (params.categoryId !== undefined) {
    const cat = PLYWOOD_CATEGORIES.find((c) => c.id === params.categoryId);
    if (cat) {
      filtered = filtered.filter(
        (p) =>
          p.category.toLowerCase() === cat.name.toLowerCase() ||
          p.category.toLowerCase().includes(cat.name.toLowerCase()) ||
          p.category.toLowerCase().includes(cat.code.toLowerCase()),
      );
    }
  }

  // Sort
  const dir = params.sortDir === 'desc' ? -1 : 1;
  filtered.sort((a, b) => {
    switch (params.sortBy) {
      case 'code':
        return a.code.localeCompare(b.code) * dir;
      case 'category':
        return a.category.localeCompare(b.category) * dir;
      case 'status':
        return a.workflowState.localeCompare(b.workflowState) * dir;
      case 'quality':
        return (a.completenessPct - b.completenessPct) * dir;
      case 'owner':
        return (a.owner ?? '').localeCompare(b.owner ?? '') * dir;
      case 'updated':
        return (new Date(a.updatedAtUtc).getTime() - new Date(b.updatedAtUtc).getTime()) * dir;
      default:
        return a.code.localeCompare(b.code) * dir;
    }
  });

  // Pagination
  const page = Math.max(1, params.page || 1);
  const pageSize = params.pageSize || 25;
  const start = (page - 1) * pageSize;
  const items = filtered.slice(start, start + pageSize);

  return {
    items,
    totalCount: filtered.length,
    page,
    pageSize,
  };
}

export async function getProductByCode(code: string): Promise<Product> {
  try {
    const p = await apiFetch<Product>(`${BASE}/${encodeURIComponent(code)}`);
    return {
      ...p,
      name: p.name ? stripGreen(p.name) : null,
    };
  } catch {
    const synthetic = getSyntheticPlywoodProduct(code);
    if (synthetic) {
      return {
        ...synthetic,
        name: synthetic.name ? stripGreen(synthetic.name) : null,
      };
    }
    throw new Error(`Product ${code} not found.`);
  }
}

export function createProduct(request: CreateProductRequest): Promise<Product> {
  return apiFetch<Product>(BASE, {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

/**
 * Returns the CSV as a Blob.
 */
export async function exportProductsCsv(params: ExportParams): Promise<Blob> {
  const query = new URLSearchParams({ sortBy: params.sortBy, sortDir: params.sortDir });

  if (params.status) {
    query.set('status', params.status);
  }
  if (params.categoryId !== undefined) {
    query.set('categoryId', String(params.categoryId));
  }
  if (params.search && params.search.trim().length > 0) {
    query.set('search', params.search.trim());
  }
  if (params.codes && params.codes.length > 0) {
    query.set('codes', params.codes.join(','));
  }
  if (params.locale) {
    query.set('locale', params.locale);
  }

  try {
    const response = await fetch(apiUrl(`${BASE}/export.csv?${query.toString()}`));
    if (response.ok) {
      return await response.blob();
    }
  } catch {
    // fallback
  }

  // Generate plywood CSV export client-side if API call has issue
  const headers = ['Product Code', 'Name', 'Category', 'Status', 'Completeness %', 'Owner', 'Updated At'];
  const rows = ALL_PLYWOOD_PRODUCTS.map((p) =>
    [p.code, `"${p.name}"`, `"${p.category}"`, p.workflowState, p.completenessPct, `"${p.owner ?? ''}"`, p.updatedAtUtc].join(','),
  );
  const csvContent = [headers.join(','), ...rows].join('\n');
  return new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
}

export function updateProductGeneral(
  code: string,
  request: UpdateProductGeneralRequest,
): Promise<Product> {
  return apiFetch<Product>(`${BASE}/${encodeURIComponent(code)}`, {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}

export async function getProductAttributes(code: string): Promise<ProductAttributes> {
  try {
    const live = await apiFetch<ProductAttributes>(`${BASE}/${encodeURIComponent(code)}/attributes`);
    return mergeWithExcelAttributes(code, live);
  } catch {
    return getSyntheticPlywoodAttributes(code);
  }
}

export async function updateProductAttributes(
  code: string,
  request: UpdateAttributesRequest,
): Promise<ProductAttributes> {
  saveCustomAttributes(code, request.values);
  try {
    const live = await apiFetch<ProductAttributes>(`${BASE}/${encodeURIComponent(code)}/attributes`, {
      method: 'PUT',
      body: JSON.stringify(request),
    });
    return mergeWithExcelAttributes(code, live);
  } catch {
    return getSyntheticPlywoodAttributes(code);
  }
}

export async function getProductVariants(code: string): Promise<ProductVariants> {
  try {
    return await apiFetch<ProductVariants>(`${BASE}/${encodeURIComponent(code)}/variants`);
  } catch {
    return getSyntheticPlywoodVariants(code);
  }
}

export function addSku(code: string, request: AddSkuRequest): Promise<ProductVariants> {
  return apiFetch<ProductVariants>(`${BASE}/${encodeURIComponent(code)}/variants`, {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function updateSku(
  code: string,
  skuCode: string,
  request: UpdateSkuRequest,
): Promise<ProductVariants> {
  return apiFetch<ProductVariants>(
    `${BASE}/${encodeURIComponent(code)}/variants/${encodeURIComponent(skuCode)}`,
    { method: 'PUT', body: JSON.stringify(request) },
  );
}

export function removeSku(
  code: string,
  skuCode: string,
  rowVersion: number,
): Promise<ProductVariants> {
  return apiFetch<ProductVariants>(
    `${BASE}/${encodeURIComponent(code)}/variants/${encodeURIComponent(skuCode)}?rowVersion=${rowVersion}`,
    { method: 'DELETE' },
  );
}

export async function listBrands(): Promise<BrandLookup[]> {
  try {
    const brands = await apiFetch<BrandLookup[]>('/api/v1/brands');
    return (brands || []).map((b) => ({
      ...b,
      code: b.code.toLowerCase() === 'greenply' ? 'plygallery' : b.code,
      name: b.name.replace(/Greenply/gi, 'Ply Gallery'),
    }));
  } catch {
    return [{ id: 1, code: 'plygallery', name: 'Ply Gallery' }];
  }
}

export async function listTaxonomyNodes(nodeType?: string): Promise<TaxonomyNodeLookup[]> {
  const suffix = nodeType ? `?nodeType=${encodeURIComponent(nodeType)}` : '';
  try {
    const nodes = await apiFetch<TaxonomyNodeLookup[]>(`/api/v1/taxonomy-nodes${suffix}`);
    if (nodes && nodes.length > 0) {
      return nodes.map((n) => ({
        ...n,
        code: n.code.toLowerCase() === 'greenply' ? 'plygallery' : n.code,
        name: n.name.replace(/Greenply/gi, 'Ply Gallery'),
      }));
    }
  } catch {
    // fallback to static plywood categories when offline
  }
  return nodeType === 'CATEGORY' ? PLYWOOD_CATEGORIES : [];
}

// --- Classifications (Sprint 8) --------------------------------------------

export function getProductClassifications(code: string): Promise<ProductClassifications> {
  return apiFetch<ProductClassifications>(
    `${BASE}/${encodeURIComponent(code)}/classifications`,
  );
}

export function assignClassification(
  code: string,
  schemeCode: string,
  request: AssignClassRequest,
): Promise<ProductClassifications> {
  return apiFetch<ProductClassifications>(
    `${BASE}/${encodeURIComponent(code)}/classifications/${encodeURIComponent(schemeCode)}`,
    { method: 'PUT', body: JSON.stringify(request) },
  );
}

export function updateClassificationFeatures(
  code: string,
  schemeCode: string,
  request: UpdateFeaturesRequest,
): Promise<ProductClassifications> {
  return apiFetch<ProductClassifications>(
    `${BASE}/${encodeURIComponent(code)}/classifications/${encodeURIComponent(schemeCode)}/features`,
    { method: 'PUT', body: JSON.stringify(request) },
  );
}

// --- Compliance (Sprint 8) --------------------------------------------------

export function getProductCompliance(code: string): Promise<ProductCompliance> {
  return apiFetch<ProductCompliance>(`${BASE}/${encodeURIComponent(code)}/compliance`);
}

export function addComplianceDocument(
  code: string,
  request: ComplianceDocumentRequest,
): Promise<ProductCompliance> {
  return apiFetch<ProductCompliance>(`${BASE}/${encodeURIComponent(code)}/compliance`, {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function updateComplianceDocument(
  code: string,
  documentId: number,
  request: UpdateComplianceRequest,
): Promise<ProductCompliance> {
  return apiFetch<ProductCompliance>(
    `${BASE}/${encodeURIComponent(code)}/compliance/${documentId}`,
    { method: 'PUT', body: JSON.stringify(request) },
  );
}

export function removeComplianceDocument(
  code: string,
  documentId: number,
  rowVersion: number,
): Promise<ProductCompliance> {
  return apiFetch<ProductCompliance>(
    `${BASE}/${encodeURIComponent(code)}/compliance/${documentId}?rowVersion=${rowVersion}`,
    { method: 'DELETE' },
  );
}

// --- Packaging, relationships, applications (Sprint 9) ----------------------

export function getProductPackaging(code: string): Promise<ProductPackaging> {
  return apiFetch<ProductPackaging>(`${BASE}/${encodeURIComponent(code)}/packaging`);
}

export function addPackagingUnit(
  code: string,
  request: AddPackagingRequest,
): Promise<ProductPackaging> {
  return apiFetch<ProductPackaging>(`${BASE}/${encodeURIComponent(code)}/packaging`, {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function updatePackagingUnit(
  code: string,
  unitId: number,
  request: UpdatePackagingRequest,
): Promise<ProductPackaging> {
  return apiFetch<ProductPackaging>(`${BASE}/${encodeURIComponent(code)}/packaging/${unitId}`, {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}

export function removePackagingUnit(
  code: string,
  unitId: number,
  rowVersion: number,
): Promise<ProductPackaging> {
  return apiFetch<ProductPackaging>(
    `${BASE}/${encodeURIComponent(code)}/packaging/${unitId}?rowVersion=${rowVersion}`,
    { method: 'DELETE' },
  );
}

export function getProductRelationships(code: string): Promise<ProductRelationships> {
  return apiFetch<ProductRelationships>(`${BASE}/${encodeURIComponent(code)}/relationships`);
}

export function addRelationship(
  code: string,
  request: AddRelationshipRequest,
): Promise<ProductRelationships> {
  return apiFetch<ProductRelationships>(`${BASE}/${encodeURIComponent(code)}/relationships`, {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function removeRelationship(
  code: string,
  relationType: string,
  targetCode: string,
  rowVersion: number,
): Promise<ProductRelationships> {
  return apiFetch<ProductRelationships>(
    `${BASE}/${encodeURIComponent(code)}/relationships/${encodeURIComponent(relationType)}/${encodeURIComponent(targetCode)}?rowVersion=${rowVersion}`,
    { method: 'DELETE' },
  );
}

export function getProductApplications(code: string): Promise<ProductApplications> {
  return apiFetch<ProductApplications>(`${BASE}/${encodeURIComponent(code)}/applications`);
}

export function setProductApplications(
  code: string,
  request: SetApplicationsRequest,
): Promise<ProductApplications> {
  return apiFetch<ProductApplications>(`${BASE}/${encodeURIComponent(code)}/applications`, {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}

// --- Activity (Sprint 10) ---------------------------------------------------

export function getProductActivity(
  code: string,
  params: ActivityParams,
): Promise<ProductActivity> {
  const query = new URLSearchParams({
    page: String(params.page),
    pageSize: String(params.pageSize),
  });

  if (params.action) {
    query.set('action', params.action);
  }

  if (params.search) {
    query.set('search', params.search);
  }

  return apiFetch<ProductActivity>(
    `${BASE}/${encodeURIComponent(code)}/activity?${query.toString()}`,
  );
}

// --- Digital assets and documents (Sprint 12) -------------------------------

export async function getProductAssets(code: string): Promise<ProductAssets> {
  // 1. Live backend API is the single source of truth across all devices
  try {
    const live = await apiFetch<ProductAssets>(`${BASE}/${encodeURIComponent(code)}/assets`);
    if (live) {
      return enhanceLiveAssets(code, live);
    }
  } catch (error: unknown) {
    // If the server rejected or errored (e.g., 500), surface it so the UI can display an alert & retry
    if (error instanceof ApiError && error.status !== 404) {
      throw error;
    }
  }

  // 2. Fall back to persistent localStorage only when backend is offline or for synthetic dev
  const local = loadAssetsFromStorage(code);
  if (local) {
    return local;
  }

  // 3. Fall back to defaults
  return getSyntheticPlywoodAssets(code);
}

export async function addProductAsset(
  code: string,
  request: AddAssetRequest,
): Promise<ProductAssets> {
  try {
    const live = await apiFetch<ProductAssets>(`${BASE}/${encodeURIComponent(code)}/assets`, {
      method: 'POST',
      body: JSON.stringify(request),
    });
    const enhanced = enhanceLiveAssets(code, live);
    saveAssetsToStorage(code, enhanced);
    return enhanced;
  } catch (error: unknown) {
    if (error instanceof ApiError) {
      throw error;
    }
    return addSyntheticAsset(code, request);
  }
}

export async function setPrimaryAsset(
  code: string,
  linkId: number,
  rowVersion: number,
): Promise<ProductAssets> {
  try {
    const live = await apiFetch<ProductAssets>(
      `${BASE}/${encodeURIComponent(code)}/assets/${linkId}/primary`,
      { method: 'POST', body: JSON.stringify({ rowVersion }) },
    );
    const enhanced = enhanceLiveAssets(code, live);
    saveAssetsToStorage(code, enhanced);
    return enhanced;
  } catch (error: unknown) {
    if (error instanceof ApiError) {
      throw error;
    }
    return setPrimarySyntheticAsset(code, linkId);
  }
}

export async function removeProductAsset(
  code: string,
  linkId: number,
  rowVersion: number,
): Promise<ProductAssets> {
  try {
    const live = await apiFetch<ProductAssets>(
      `${BASE}/${encodeURIComponent(code)}/assets/${linkId}?rowVersion=${rowVersion}`,
      { method: 'DELETE' },
    );
    const enhanced = enhanceLiveAssets(code, live);
    saveAssetsToStorage(code, enhanced);
    return enhanced;
  } catch (error: unknown) {
    if (error instanceof ApiError) {
      throw error;
    }
    return removeSyntheticAsset(code, linkId);
  }
}

/**
 * Resets a product's media and documents back to pristine default plywood assets.
 */
export function resetProductAssets(code: string): ProductAssets {
  clearAssetsStorage(code);
  return getSyntheticPlywoodAssets(code);
}

// --- Planning & BIM and Localization (Sprint 13) -----------------------------

export async function getProductPlanning(code: string): Promise<ProductPlanning> {
  try {
    const live = await apiFetch<ProductPlanning>(`${BASE}/${encodeURIComponent(code)}/planning`);
    return buildExcelPlywoodPlanning(code, live);
  } catch {
    return buildExcelPlywoodPlanning(code);
  }
}

export function getProductLocalization(code: string): Promise<ProductLocalization> {
  return apiFetch<ProductLocalization>(`${BASE}/${encodeURIComponent(code)}/localization`);
}

export function saveProductLocalization(
  code: string,
  locale: string,
  request: UpdateLocalizationRequest,
): Promise<ProductLocalization> {
  return apiFetch<ProductLocalization>(
    `${BASE}/${encodeURIComponent(code)}/localization/${encodeURIComponent(locale)}`,
    { method: 'PUT', body: JSON.stringify(request) },
  );
}
