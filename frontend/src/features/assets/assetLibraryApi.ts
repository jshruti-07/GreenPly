import { apiFetch } from '../../api/client';

export interface AssetTypeSummary {
  assetType: string;
  count: number;
  totalBytes: number;
}

export interface AssetOmittedPanel {
  panel: string;
  reason: string;
}

export interface AssetLibrarySummary {
  totalAssets: number;
  totalBytes: number;
  byType: AssetTypeSummary[];
  /** Assets linked to no product at all. */
  orphanCount: number;
  productsWithoutMainImage: number;
  rightsExpiringSoon: number;
  rightsExpired: number;
  omittedPanels: AssetOmittedPanel[];
}

export interface AssetListItem {
  publicId: string;
  fileName: string | null;
  title: string | null;
  titleLocale: string | null;
  assetType: string;
  mimeType: string | null;
  fileSizeBytes: number | null;
  width: number | null;
  height: number | null;
  storageProvider: string;
  status: string;
  /** Which rendition `url` points at — 'original' until ingest exists. */
  servedRendition: string;
  url: string;
  linkCount: number;
  usageRights: string | null;
  rightsExpiry: string | null;
}

export interface AssetPage {
  page: number;
  pageSize: number;
  totalCount: number;
  items: AssetListItem[];
}

export interface AssetText {
  localeCode: string;
  localeName: string;
  isBase: boolean;
  title: string | null;
  altText: string | null;
  caption: string | null;
}

export interface AssetRendition {
  role: string;
  uri: string;
  width: number | null;
  height: number | null;
  format: string | null;
}

export interface AssetUsage {
  productCode: string;
  productName: string;
  productNameLocale: string | null;
  role: string;
  isPrimary: boolean;
}

export interface AssetDetail {
  publicId: string;
  fileName: string | null;
  title: string | null;
  titleLocale: string | null;
  assetType: string;
  mimeType: string | null;
  fileSizeBytes: number | null;
  width: number | null;
  height: number | null;
  storageProvider: string;
  storageKey: string;
  checksum: string | null;
  status: string;
  usageRights: string | null;
  rightsExpiry: string | null;
  localeCode: string | null;
  createdAtUtc: string;
  renditions: AssetRendition[];
  texts: AssetText[];
  usedBy: AssetUsage[];
}

export type AssetUsageFilter = 'all' | 'orphan' | 'linked';

/** Bytes to something a human reads. Binary units, stated as such. */
export function formatBytes(bytes: number | null): string {
  if (bytes === null || bytes === 0) return '—';
  const units = ['B', 'KB', 'MB', 'GB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value < 10 && unit > 0 ? value.toFixed(1) : Math.round(value)} ${units[unit]}`;
}

export function fetchAssetSummary(
  locale: string | null,
  signal?: AbortSignal,
): Promise<AssetLibrarySummary> {
  const query = locale ? `?locale=${encodeURIComponent(locale)}` : '';
  return apiFetch<AssetLibrarySummary>(`/api/v1/assets/summary${query}`, { signal });
}

export function fetchAssets(
  params: {
    type: string | null;
    usage: AssetUsageFilter;
    search?: string;
    page: number;
    pageSize: number;
    locale: string | null;
  },
  signal?: AbortSignal,
): Promise<AssetPage> {
  const query = new URLSearchParams({
    usage: params.usage,
    page: String(params.page),
    pageSize: String(params.pageSize),
  });
  if (params.type) query.set('type', params.type);
  if (params.search) query.set('search', params.search);
  if (params.locale) query.set('locale', params.locale);

  return apiFetch<AssetPage>(`/api/v1/assets?${query}`, { signal });
}

export function fetchAssetDetail(
  publicId: string,
  locale: string | null,
  signal?: AbortSignal,
): Promise<AssetDetail> {
  const query = locale ? `?locale=${encodeURIComponent(locale)}` : '';
  return apiFetch<AssetDetail>(`/api/v1/assets/${encodeURIComponent(publicId)}${query}`, { signal });
}

export interface UploadedAsset {
  publicId: string;
  deduplicated: boolean;
}

/**
 * The bytes of one rendition.
 *
 * Always routed through the API rather than through `storageKey`: for locally
 * stored assets the key is a path inside the store and means nothing to a
 * browser, and for imported ones the endpoint redirects. One URL shape either
 * way, and the caller does not have to know which kind it is holding.
 */
export function assetFileUrl(publicId: string, rendition: 'original' | 'thumb' | 'web'): string {
  return `/api/v1/assets/${encodeURIComponent(publicId)}/file?rendition=${rendition}`;
}

export async function uploadAsset(
  file: File,
  options: { assetType?: string; title?: string; altText?: string; locale?: string | null },
): Promise<UploadedAsset> {
  const query = new URLSearchParams();
  if (options.assetType) query.set('assetType', options.assetType);
  if (options.title) query.set('title', options.title);
  if (options.altText) query.set('altText', options.altText);
  if (options.locale) query.set('locale', options.locale);

  const body = new FormData();
  body.append('file', file);

  // Deliberately not apiFetch: that helper sets a JSON content type, and a
  // multipart body must keep the boundary the browser generates.
  const response = await fetch(`/api/v1/assets?${query}`, { method: 'POST', body });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.detail ?? problem?.title ?? `Upload failed (${response.status})`);
  }

  return (await response.json()) as UploadedAsset;
}
