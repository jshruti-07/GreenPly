import {
  DEFAULT_GRID_STATE,
  hasActiveFilters,
  readGridState,
  writeGridState,
  type GridState,
} from './gridState';

describe('readGridState', () => {
  it('returns defaults for an empty URL', () => {
    expect(readGridState(new URLSearchParams())).toEqual(DEFAULT_GRID_STATE);
  });

  it('reads every supported parameter', () => {
    const state = readGridState(
      new URLSearchParams(
        'page=3&pageSize=50&sortBy=quality&sortDir=asc&status=Draft&categoryId=2&search=door&density=compact',
      ),
    );

    expect(state).toEqual({
      page: 3,
      pageSize: 50,
      sortBy: 'quality',
      sortDir: 'asc',
      status: 'Draft',
      categoryId: 2,
      search: 'door',
      compact: true,
    });
  });

  // A hand-edited URL must never produce a request the API would 400 on.
  it.each([
    ['sortBy=updated_at', 'sortBy', DEFAULT_GRID_STATE.sortBy],
    ['sortDir=sideways', 'sortDir', DEFAULT_GRID_STATE.sortDir],
    ['pageSize=999', 'pageSize', DEFAULT_GRID_STATE.pageSize],
    ['page=0', 'page', DEFAULT_GRID_STATE.page],
    ['page=-4', 'page', DEFAULT_GRID_STATE.page],
    ['page=abc', 'page', DEFAULT_GRID_STATE.page],
  ])('falls back to the default when %s is invalid', (query, key, expected) => {
    const state = readGridState(new URLSearchParams(query)) as unknown as Record<string, unknown>;
    expect(state[key]).toBe(expected);
  });

  it('ignores a non-numeric categoryId', () => {
    expect(readGridState(new URLSearchParams('categoryId=abc')).categoryId).toBeUndefined();
  });
});

describe('writeGridState', () => {
  it('omits defaults so shared URLs stay short', () => {
    expect(writeGridState(DEFAULT_GRID_STATE).toString()).toBe('');
  });

  it('writes only what differs from the defaults', () => {
    const params = writeGridState({ ...DEFAULT_GRID_STATE, status: 'Draft', page: 2 });

    expect(params.get('status')).toBe('Draft');
    expect(params.get('page')).toBe('2');
    expect(params.get('sortBy')).toBeNull();
  });

  it('round-trips a fully specified state', () => {
    const state: GridState = {
      page: 4,
      pageSize: 100,
      sortBy: 'owner',
      sortDir: 'asc',
      status: 'Review',
      categoryId: 7,
      search: 'fire door',
      compact: true,
    };

    expect(readGridState(writeGridState(state))).toEqual(state);
  });
});

describe('hasActiveFilters', () => {
  it('is false for defaults', () => {
    expect(hasActiveFilters(DEFAULT_GRID_STATE)).toBe(false);
  });

  it.each([
    ['status', { status: 'Draft' }],
    ['categoryId', { categoryId: 2 }],
    ['search', { search: 'x' }],
  ])('is true when %s is set', (_label, patch) => {
    expect(hasActiveFilters({ ...DEFAULT_GRID_STATE, ...patch })).toBe(true);
  });

  it('ignores sorting and paging', () => {
    expect(hasActiveFilters({ ...DEFAULT_GRID_STATE, page: 5, sortBy: 'code' })).toBe(false);
  });
});
