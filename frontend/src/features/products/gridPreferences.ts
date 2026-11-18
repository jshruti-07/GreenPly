import type { GridState } from './gridState';

/**
 * Workstation preferences: visible columns and saved views.
 *
 * Stored in localStorage because no identity service exists yet
 * (decision 2026-08-14). When auth ships these move to a per-user table and
 * this module becomes the API client for it — callers already treat it as an
 * async-free repository, so the change is contained here.
 *
 * All reads are defensive: a corrupt or foreign value must never break the
 * grid, so anything unparseable falls back to defaults.
 */

const COLUMNS_KEY = 'pim.products.columns.v1';
const VIEWS_KEY = 'pim.products.views.v1';

export const COLUMN_IDS = [
  'product',
  'category',
  'status',
  'completeness',
  'channels',
  'languages',
  'variants',
  'owner',
  'updated',
] as const;

export type ColumnId = (typeof COLUMN_IDS)[number];

/** Product is intentionally not hideable — a row needs an identity. */
export const REQUIRED_COLUMNS: ReadonlyArray<ColumnId> = ['product'];

export const DEFAULT_VISIBLE_COLUMNS: ColumnId[] = [...COLUMN_IDS];

export interface SavedView {
  id: string;
  name: string;
  state: GridState;
  columns: ColumnId[];
}

function safeParse<T>(raw: string | null): T | null {
  if (!raw) {
    return null;
  }
  try {
    return JSON.parse(raw) as T;
  } catch {
    return null;
  }
}

function safeWrite(key: string, value: unknown): void {
  try {
    localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // Quota exceeded or storage disabled (private mode): preferences are a
    // convenience, never a hard dependency.
  }
}

export function loadVisibleColumns(): ColumnId[] {
  const stored = safeParse<string[]>(localStorage.getItem(COLUMNS_KEY));
  if (!stored) {
    return [...DEFAULT_VISIBLE_COLUMNS];
  }

  const valid = stored.filter((id): id is ColumnId =>
    (COLUMN_IDS as readonly string[]).includes(id),
  );

  // Required columns are re-added even if a stale payload dropped them.
  const withRequired = [...new Set([...REQUIRED_COLUMNS, ...valid])];
  return withRequired.length > 0 ? withRequired : [...DEFAULT_VISIBLE_COLUMNS];
}

export function saveVisibleColumns(columns: ColumnId[]): void {
  safeWrite(COLUMNS_KEY, columns);
}

export function loadSavedViews(): SavedView[] {
  const stored = safeParse<SavedView[]>(localStorage.getItem(VIEWS_KEY));
  if (!Array.isArray(stored)) {
    return [];
  }

  return stored.filter(
    (view): view is SavedView =>
      typeof view?.id === 'string' &&
      typeof view?.name === 'string' &&
      typeof view?.state === 'object' &&
      view.state !== null,
  );
}

export function saveViews(views: SavedView[]): void {
  safeWrite(VIEWS_KEY, views);
}
