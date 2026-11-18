import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { ApiError } from '../../api/client';
import { Button } from '../../components/Button';
import { ChannelDots } from '../../components/ChannelDots';
import { Checkbox } from '../../components/Checkbox';
import { StatusBadge } from '../../components/Chip';
import { EmptyState } from '../../components/EmptyState';
import { SearchInput } from '../../components/Field';
import { QualityBar } from '../../components/QualityBar';
import { ProductCell, Table, Td, Th } from '../../components/Table';
import { useToast } from '../../components/Toast';
import { CreateProductDrawer } from './CreateProductDrawer';
import { ColumnChooser, SavedViewsMenu } from './GridToolbarMenus';
import {
  loadSavedViews,
  loadVisibleColumns,
  saveViews,
  saveVisibleColumns,
  type ColumnId,
  type SavedView,
} from './gridPreferences';
import {
  DEFAULT_GRID_STATE,
  PAGE_SIZES,
  hasActiveFilters,
  readGridState,
  writeGridState,
  type GridState,
} from './gridState';
import { useLocale } from '../../app/LocaleContext';
import { FallbackMark } from './FallbackMark';
import { exportProductsCsv, listProducts, listTaxonomyNodes } from './productsApi';
import type { PagedResult, ProductListItem, ProductSortKey, TaxonomyNodeLookup } from './types';
import { CHANNELS, WORKFLOW_STATES } from './types';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'loaded'; page: PagedResult<ProductListItem> };

const SEARCH_DEBOUNCE_MS = 300;

interface ColumnDef {
  id: ColumnId;
  label: string;
  sortKey: ProductSortKey | null;
  /** Header suffix marking data that is not authoritative. */
  note?: string;
}

const COLUMNS: readonly ColumnDef[] = [
  { id: 'product', label: 'Product', sortKey: 'code' },
  { id: 'category', label: 'Category', sortKey: 'category' },
  { id: 'status', label: 'Status', sortKey: 'status' },
  { id: 'completeness', label: 'Completeness', sortKey: 'quality' },
  { id: 'channels', label: 'Channels', sortKey: null, note: 'derived' },
  { id: 'languages', label: 'Languages', sortKey: null },
  { id: 'variants', label: 'Variants', sortKey: null },
  { id: 'owner', label: 'Owner', sortKey: 'owner' },
  { id: 'updated', label: 'Updated', sortKey: 'updated' },
];

function formatDate(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime())
    ? '—'
    : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
}

function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}

export function ProductsGrid() {
  const navigate = useNavigate();
  const { showToast } = useToast();
  const [searchParams, setSearchParams] = useSearchParams();

  // The URL is the single source of truth for filter/sort/page state.
  const state = useMemo(() => readGridState(searchParams), [searchParams]);

  const [data, setData] = useState<LoadState>({ kind: 'loading' });
  const [searchInput, setSearchInput] = useState(state.search);
  const [categories, setCategories] = useState<TaxonomyNodeLookup[]>([]);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [columns, setColumns] = useState<ColumnId[]>(() => loadVisibleColumns());
  const [views, setViews] = useState<SavedView[]>(() => loadSavedViews());
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [reloadToken, setReloadToken] = useState(0);
  const { locale } = useLocale();

  const update = useCallback(
    (patch: Partial<GridState>, options?: { resetPage?: boolean }) => {
      const next: GridState = {
        ...state,
        ...patch,
        page: options?.resetPage ? 1 : (patch.page ?? state.page),
      };
      setSearchParams(writeGridState(next), { replace: true });
    },
    [state, setSearchParams],
  );

  // Debounce typing, then push into the URL.
  useEffect(() => {
    if (searchInput === state.search) {
      return;
    }
    const timer = window.setTimeout(() => update({ search: searchInput }, { resetPage: true }), SEARCH_DEBOUNCE_MS);
    return () => window.clearTimeout(timer);
  }, [searchInput, state.search, update]);

  useEffect(() => {
    let active = true;
    listTaxonomyNodes('CATEGORY')
      .then((nodes) => active && setCategories(nodes))
      .catch(() => active && setCategories([]));
    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    setData({ kind: 'loading' });

    listProducts(
      {
        page: state.page,
        pageSize: state.pageSize,
        sortBy: state.sortBy,
        sortDir: state.sortDir,
        status: state.status || undefined,
        categoryId: state.categoryId,
        search: state.search,
        locale,
      },
      controller.signal,
    )
      .then((result) => setData({ kind: 'loaded', page: result }))
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }
        setData({
          kind: 'error',
          message:
            error instanceof ApiError
              ? error.message
              : 'Could not load products. Check that the catalog API is running.',
        });
      });

    // `locale` is a dependency: changing the language must refetch, because the
    // text lives on the server side of this boundary. Re-rendering with the old
    // rows would show the previous language under the new selection.
    return () => controller.abort();
  }, [state, reloadToken, locale]);

  function toggleSort(key: ProductSortKey) {
    update(
      key === state.sortBy
        ? { sortDir: state.sortDir === 'asc' ? 'desc' : 'asc' }
        : { sortBy: key, sortDir: 'asc' },
      { resetPage: true },
    );
  }

  const rows = data.kind === 'loaded' ? data.page.items : [];
  const totalCount = data.kind === 'loaded' ? data.page.totalCount : 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / state.pageSize));
  const visibleColumns = COLUMNS.filter((column) => columns.includes(column.id));

  const pageCodes = rows.map((row) => row.code);
  const selectedOnPage = pageCodes.filter((code) => selected.has(code));
  const allOnPageSelected = pageCodes.length > 0 && selectedOnPage.length === pageCodes.length;

  function toggleSelectAll(checked: boolean) {
    const next = new Set(selected);
    for (const code of pageCodes) {
      if (checked) {
        next.add(code);
      } else {
        next.delete(code);
      }
    }
    setSelected(next);
  }

  function toggleRow(code: string, checked: boolean) {
    const next = new Set(selected);
    if (checked) {
      next.add(code);
    } else {
      next.delete(code);
    }
    setSelected(next);
  }

  async function exportCsv(codes?: string[]) {
    setExporting(true);
    try {
      const blob = await exportProductsCsv({
        sortBy: state.sortBy,
        sortDir: state.sortDir,
        status: state.status || undefined,
        categoryId: state.categoryId,
        search: state.search,
        codes,
        // The file must match the screen it came from; a CSV in a different
        // language from the grid is drift nobody notices until it is elsewhere.
        locale,
      });
      downloadBlob(blob, `products-${new Date().toISOString().slice(0, 10)}.csv`);
      showToast(codes ? `Exported ${codes.length} selected products` : 'Export complete');
    } catch (error: unknown) {
      showToast(
        error instanceof ApiError ? error.message : 'Export failed. Please try again.',
        'error',
      );
    } finally {
      setExporting(false);
    }
  }

  function applyView(view: SavedView) {
    setSearchParams(writeGridState(view.state), { replace: true });
    setSearchInput(view.state.search);
    setColumns(view.columns);
    saveVisibleColumns(view.columns);
  }

  function saveCurrentView(name: string) {
    const view: SavedView = {
      id: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
      name,
      state,
      columns,
    };
    const next = [...views, view];
    setViews(next);
    saveViews(next);
    showToast(`View “${name}” saved`);
  }

  function deleteView(id: string) {
    const next = views.filter((view) => view.id !== id);
    setViews(next);
    saveViews(next);
  }

  function changeColumns(next: ColumnId[]) {
    setColumns(next);
    saveVisibleColumns(next);
  }

  function clearFilters() {
    setSearchInput('');
    setSearchParams(writeGridState({ ...DEFAULT_GRID_STATE, compact: state.compact }), {
      replace: true,
    });
  }

  return (
    <>
      <div className="phead">
        <div>
          <h1>Products</h1>
          <p>
            {data.kind === 'loaded'
              ? `${totalCount.toLocaleString()} products in the catalogue`
              : 'Loading catalogue…'}
          </p>
        </div>
        <div className="sp">
          <Button onClick={() => exportCsv()} disabled={exporting || totalCount === 0}>
            {exporting ? 'Exporting…' : 'Export CSV'}
          </Button>
          <Button variant="primary" onClick={() => setDrawerOpen(true)}>
            New Product
          </Button>
        </div>
      </div>

      <div className="card">
        <div className="card-h">
          <div className="toolbar" style={{ width: '100%' }}>
            <SearchInput
              label="Search products"
              placeholder="Search code or name…"
              value={searchInput}
              onChange={(event) => setSearchInput(event.target.value)}
              style={{ minWidth: 220 }}
            />

            <select
              className="sel"
              aria-label="Filter by status"
              value={state.status}
              onChange={(event) => update({ status: event.target.value }, { resetPage: true })}
            >
              <option value="">All statuses</option>
              {WORKFLOW_STATES.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>

            <select
              className="sel"
              aria-label="Filter by category"
              value={state.categoryId ?? ''}
              onChange={(event) =>
                update(
                  { categoryId: event.target.value ? Number(event.target.value) : undefined },
                  { resetPage: true },
                )
              }
            >
              <option value="">All categories</option>
              {categories.map((node) => (
                <option key={node.id} value={node.id}>
                  {node.name}
                </option>
              ))}
            </select>

            {hasActiveFilters(state) && (
              <Button size="sm" variant="ghost" onClick={clearFilters}>
                Clear filters
              </Button>
            )}

            <div style={{ marginLeft: 'auto', display: 'flex', gap: 8, alignItems: 'center' }}>
              <SavedViewsMenu
                views={views}
                onApply={applyView}
                onSave={saveCurrentView}
                onDelete={deleteView}
              />
              <ColumnChooser visible={columns} onChange={changeColumns} />
              <div className="seg" role="group" aria-label="Row density">
                <button
                  type="button"
                  className={state.compact ? '' : 'on'}
                  aria-pressed={!state.compact}
                  onClick={() => update({ compact: false })}
                >
                  Comfortable
                </button>
                <button
                  type="button"
                  className={state.compact ? 'on' : ''}
                  aria-pressed={state.compact}
                  onClick={() => update({ compact: true })}
                >
                  Compact
                </button>
              </div>
            </div>
          </div>
        </div>

        <div className="card-b" style={{ padding: 0 }}>
          {data.kind === 'loading' && (
            <p className="muted" style={{ padding: 24, margin: 0 }} role="status">
              Loading products…
            </p>
          )}

          {data.kind === 'error' && (
            <div style={{ padding: 16 }}>
              <div className="alert err" role="alert">
                {data.message}
              </div>
              <Button style={{ marginTop: 12 }} onClick={() => setReloadToken((t) => t + 1)}>
                Retry
              </Button>
            </div>
          )}

          {data.kind === 'loaded' && rows.length === 0 && (
            <div style={{ padding: 16 }}>
              <EmptyState
                title={
                  hasActiveFilters(state) ? 'No products match these filters' : 'No products yet'
                }
                description={
                  hasActiveFilters(state)
                    ? 'Try clearing the search or filters.'
                    : 'Create your first product to get started.'
                }
                action={
                  hasActiveFilters(state) ? (
                    <Button onClick={clearFilters}>Clear filters</Button>
                  ) : (
                    <Button variant="primary" onClick={() => setDrawerOpen(true)}>
                      New Product
                    </Button>
                  )
                }
              />
            </div>
          )}

          {data.kind === 'loaded' && rows.length > 0 && (
            <Table compact={state.compact}>
              <thead>
                <tr>
                  <Th className="ck">
                    <Checkbox
                      label="Select all products on this page"
                      checked={allOnPageSelected}
                      indeterminate={selectedOnPage.length > 0 && !allOnPageSelected}
                      onChange={toggleSelectAll}
                    />
                  </Th>
                  {visibleColumns.map((column) => (
                    <Th
                      key={column.id}
                      sorted={column.sortKey && state.sortBy === column.sortKey ? state.sortDir : undefined}
                      onSort={column.sortKey ? () => toggleSort(column.sortKey as ProductSortKey) : undefined}
                    >
                      {column.label}
                      {column.note && (
                        <span className="muted" style={{ fontWeight: 500 }}>
                          {' '}
                          ({column.note})
                        </span>
                      )}
                    </Th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map((item) => (
                  <tr
                    key={item.publicId}
                    className={selected.has(item.code) ? 'sel' : undefined}
                    onClick={() => navigate(`/products/${encodeURIComponent(item.code)}`)}
                  >
                    <Td className="ck" onClick={(event) => event.stopPropagation()}>
                      <Checkbox
                        label={`Select ${item.code}`}
                        checked={selected.has(item.code)}
                        onChange={(checked) => toggleRow(item.code, checked)}
                      />
                    </Td>
                    {visibleColumns.map((column) => {
                      switch (column.id) {
                        case 'product':
                          return (
                            <Td key={column.id}>
                              <ProductCell name={item.name} sku={item.code} />
                              <FallbackMark actual={item.nameLocale} requested={locale} />
                            </Td>
                          );
                        case 'category':
                          return (
                            <Td key={column.id}>
                              {item.category}
                              <FallbackMark actual={item.categoryLocale} requested={locale} />
                            </Td>
                          );
                        case 'status':
                          return (
                            <Td key={column.id}>
                              <StatusBadge status={item.workflowState} />
                            </Td>
                          );
                        case 'completeness':
                          return (
                            <Td key={column.id} style={{ minWidth: 140 }}>
                              <QualityBar value={item.completenessPct} />
                            </Td>
                          );
                        case 'channels':
                          return (
                            <Td key={column.id}>
                              <ChannelDots levels={item.channelReadiness} channels={CHANNELS} />
                            </Td>
                          );
                        case 'languages':
                          return (
                            <Td key={column.id}>
                              <span className="muted">{item.languages.join(', ') || '—'}</span>
                            </Td>
                          );
                        case 'variants':
                          return (
                            <Td key={column.id} className="num">
                              {item.variantCount}
                            </Td>
                          );
                        case 'owner':
                          return (
                            <Td key={column.id}>
                              {item.owner ?? <span className="muted">Unassigned</span>}
                            </Td>
                          );
                        case 'updated':
                          return (
                            <Td key={column.id} className="muted">
                              {formatDate(item.updatedAtUtc)}
                            </Td>
                          );
                        default:
                          return null;
                      }
                    })}
                  </tr>
                ))}
              </tbody>
            </Table>
          )}
        </div>

        {data.kind === 'loaded' && totalCount > 0 && (
          <div className="card-h" style={{ borderTop: '1px solid var(--line)', borderBottom: 'none' }}>
            <span className="muted">
              Page {state.page} of {totalPages} · {totalCount.toLocaleString()} products
            </span>
            <div className="sp" style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
              <select
                className="sel"
                aria-label="Rows per page"
                value={state.pageSize}
                onChange={(event) =>
                  update({ pageSize: Number(event.target.value) }, { resetPage: true })
                }
              >
                {PAGE_SIZES.map((size) => (
                  <option key={size} value={size}>
                    {size} per page
                  </option>
                ))}
              </select>
              <Button
                size="sm"
                disabled={state.page <= 1}
                onClick={() => update({ page: state.page - 1 })}
              >
                Previous
              </Button>
              <Button
                size="sm"
                disabled={state.page >= totalPages}
                onClick={() => update({ page: state.page + 1 })}
              >
                Next
              </Button>
            </div>
          </div>
        )}
      </div>

      {selected.size > 0 && (
        <div className="bulk" role="region" aria-label="Bulk actions">
          <b>{selected.size}</b> selected
          <Button size="sm" onClick={() => exportCsv([...selected])} disabled={exporting}>
            Export selected
          </Button>
          <Button size="sm" onClick={() => setSelected(new Set())}>
            Clear
          </Button>
        </div>
      )}

      <CreateProductDrawer
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        onCreated={(product) => {
          setDrawerOpen(false);
          showToast(`Product ${product.code} created`);
          setSearchInput('');
          setSearchParams(writeGridState({ ...DEFAULT_GRID_STATE, compact: state.compact }), {
            replace: true,
          });
          setReloadToken((t) => t + 1);
        }}
      />
    </>
  );
}
