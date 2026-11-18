import {
  DEFAULT_VISIBLE_COLUMNS,
  loadSavedViews,
  loadVisibleColumns,
  saveViews,
  saveVisibleColumns,
  type SavedView,
} from './gridPreferences';
import { DEFAULT_GRID_STATE } from './gridState';

describe('column preferences', () => {
  beforeEach(() => localStorage.clear());

  it('defaults to every column', () => {
    expect(loadVisibleColumns()).toEqual(DEFAULT_VISIBLE_COLUMNS);
  });

  it('round-trips a selection', () => {
    saveVisibleColumns(['product', 'status']);
    expect(loadVisibleColumns()).toEqual(['product', 'status']);
  });

  it('re-adds required columns dropped by a stale payload', () => {
    saveVisibleColumns(['status']);
    expect(loadVisibleColumns()).toContain('product');
  });

  it('discards unknown column ids', () => {
    localStorage.setItem('pim.products.columns.v1', JSON.stringify(['product', 'not_a_column']));
    expect(loadVisibleColumns()).toEqual(['product']);
  });

  it('falls back to defaults on corrupt JSON', () => {
    localStorage.setItem('pim.products.columns.v1', '{not json');
    expect(loadVisibleColumns()).toEqual(DEFAULT_VISIBLE_COLUMNS);
  });
});

describe('saved views', () => {
  beforeEach(() => localStorage.clear());

  const view: SavedView = {
    id: '1',
    name: 'Drafts',
    state: { ...DEFAULT_GRID_STATE, status: 'Draft' },
    columns: ['product', 'status'],
  };

  it('starts empty', () => {
    expect(loadSavedViews()).toEqual([]);
  });

  it('round-trips views', () => {
    saveViews([view]);
    expect(loadSavedViews()).toEqual([view]);
  });

  it('ignores corrupt storage', () => {
    localStorage.setItem('pim.products.views.v1', '[[[');
    expect(loadSavedViews()).toEqual([]);
  });

  it('filters out malformed entries', () => {
    localStorage.setItem('pim.products.views.v1', JSON.stringify([view, { id: 5 }, null]));
    expect(loadSavedViews()).toEqual([view]);
  });
});
