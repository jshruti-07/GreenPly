import type { ProductSortKey, SortDirection } from './types';

/**
 * Grid state lives in the URL so a filtered view is shareable, bookmarkable
 * and survives reload/back-forward. Column visibility and saved views live in
 * localStorage instead: they are workstation preferences, not something you
 * send to a colleague.
 */
export interface GridState {
  page: number;
  pageSize: number;
  sortBy: ProductSortKey;
  sortDir: SortDirection;
  status: string;
  categoryId?: number;
  search: string;
  compact: boolean;
}

export const SORT_KEYS: readonly ProductSortKey[] = [
  'code',
  'name',
  'category',
  'status',
  'quality',
  'owner',
  'updated',
];

export const PAGE_SIZES = [25, 50, 100] as const;

export const DEFAULT_GRID_STATE: GridState = {
  page: 1,
  pageSize: 25,
  sortBy: 'updated',
  sortDir: 'desc',
  status: '',
  categoryId: undefined,
  search: '',
  compact: false,
};

function parsePositiveInt(raw: string | null, fallback: number): number {
  const value = Number(raw);
  return Number.isInteger(value) && value >= 1 ? value : fallback;
}

/**
 * Tolerant of hand-edited URLs: anything invalid silently falls back to the
 * default rather than sending a request the API would reject with a 400.
 */
export function readGridState(params: URLSearchParams): GridState {
  const sortByRaw = params.get('sortBy');
  const sortDirRaw = params.get('sortDir');
  const pageSize = parsePositiveInt(params.get('pageSize'), DEFAULT_GRID_STATE.pageSize);
  const categoryRaw = params.get('categoryId');
  const categoryId = categoryRaw !== null ? Number(categoryRaw) : Number.NaN;

  return {
    page: parsePositiveInt(params.get('page'), DEFAULT_GRID_STATE.page),
    pageSize: (PAGE_SIZES as readonly number[]).includes(pageSize)
      ? pageSize
      : DEFAULT_GRID_STATE.pageSize,
    sortBy: SORT_KEYS.includes(sortByRaw as ProductSortKey)
      ? (sortByRaw as ProductSortKey)
      : DEFAULT_GRID_STATE.sortBy,
    sortDir: sortDirRaw === 'asc' || sortDirRaw === 'desc' ? sortDirRaw : DEFAULT_GRID_STATE.sortDir,
    status: params.get('status') ?? DEFAULT_GRID_STATE.status,
    categoryId: Number.isInteger(categoryId) && categoryId >= 1 ? categoryId : undefined,
    search: params.get('search') ?? DEFAULT_GRID_STATE.search,
    compact: params.get('density') === 'compact',
  };
}

/** Only non-default values are written, keeping shared URLs readable. */
export function writeGridState(state: GridState): URLSearchParams {
  const params = new URLSearchParams();

  if (state.page !== DEFAULT_GRID_STATE.page) {
    params.set('page', String(state.page));
  }
  if (state.pageSize !== DEFAULT_GRID_STATE.pageSize) {
    params.set('pageSize', String(state.pageSize));
  }
  if (state.sortBy !== DEFAULT_GRID_STATE.sortBy) {
    params.set('sortBy', state.sortBy);
  }
  if (state.sortDir !== DEFAULT_GRID_STATE.sortDir) {
    params.set('sortDir', state.sortDir);
  }
  if (state.status) {
    params.set('status', state.status);
  }
  if (state.categoryId !== undefined) {
    params.set('categoryId', String(state.categoryId));
  }
  if (state.search) {
    params.set('search', state.search);
  }
  if (state.compact) {
    params.set('density', 'compact');
  }

  return params;
}

export function hasActiveFilters(state: GridState): boolean {
  return Boolean(state.status || state.categoryId !== undefined || state.search);
}
