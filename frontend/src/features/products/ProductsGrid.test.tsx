import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { LocaleProvider } from '../../app/LocaleContext';
import { ToastProvider } from '../../components/Toast';
import { ProductsGrid } from './ProductsGrid';
import { installFakeApi, makeListItem, makePage, type FakeApi } from './testApi';

function renderGrid(initialUrl = '/products') {
  return render(
    <MemoryRouter initialEntries={[initialUrl]}>
      <LocaleProvider>
        <ToastProvider>
          <Routes>
            <Route path="/products" element={<ProductsGrid />} />
            <Route path="/products/:code" element={<div>detail page</div>} />
          </Routes>
        </ToastProvider>
      </LocaleProvider>
    </MemoryRouter>,
  );
}

/** Last list request the grid made — the contract under test. */
function lastQuery(api: FakeApi): URLSearchParams {
  return api.listCalls[api.listCalls.length - 1];
}

describe('ProductsGrid', () => {
  let api: FakeApi;

  afterEach(() => api?.restore());

  it('renders rows with the v4.2 columns', async () => {
    api = installFakeApi({
      page: makePage([makeListItem({ code: 'FD-90-GS-1000', name: 'Fire Door 90 Min' })]),
    });
    renderGrid();

    expect(await screen.findByText('Fire Door 90 Min')).toBeInTheDocument();

    // Scoped to the grid: 'Fire Doors' also appears in the category filter.
    const grid = within(screen.getByRole('table'));
    expect(grid.getByText('FD-90-GS-1000')).toBeInTheDocument();
    expect(grid.getByText('Fire Doors')).toBeInTheDocument();
    expect(grid.getByText('Approval')).toBeInTheDocument();
    expect(grid.getByText('Anna Becker')).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'Completeness' })).toHaveAttribute(
      'aria-valuenow',
      '96',
    );
  });

  it('requests page 1 sorted by updated desc on first load', async () => {
    api = installFakeApi();
    renderGrid();

    await waitFor(() => expect(api.listCalls.length).toBeGreaterThan(0));
    const query = lastQuery(api);
    expect(query.get('page')).toBe('1');
    expect(query.get('pageSize')).toBe('25');
    expect(query.get('sortBy')).toBe('updated');
    expect(query.get('sortDir')).toBe('desc');
  });

  it('sorts server-side and toggles direction on repeat clicks', async () => {
    const user = userEvent.setup();
    api = installFakeApi();
    renderGrid();
    await screen.findByText('Fire Door 90 Min');

    await user.click(screen.getByRole('columnheader', { name: /Completeness/ }));
    await waitFor(() => expect(lastQuery(api).get('sortBy')).toBe('quality'));
    expect(lastQuery(api).get('sortDir')).toBe('asc');

    await user.click(screen.getByRole('columnheader', { name: /Completeness/ }));
    await waitFor(() => expect(lastQuery(api).get('sortDir')).toBe('desc'));
  });

  it('filters by status and resets to page 1', async () => {
    const user = userEvent.setup();
    api = installFakeApi({ page: makePage([makeListItem()], { totalCount: 60 }) });
    renderGrid();
    await screen.findByText('Fire Door 90 Min');

    await user.click(screen.getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(lastQuery(api).get('page')).toBe('2'));

    await user.selectOptions(screen.getByLabelText('Filter by status'), 'Draft');
    await waitFor(() => expect(lastQuery(api).get('status')).toBe('Draft'));
    expect(lastQuery(api).get('page')).toBe('1');
  });

  it('filters by category using the lookup endpoint', async () => {
    const user = userEvent.setup();
    api = installFakeApi();
    renderGrid();
    await screen.findByText('Fire Door 90 Min');

    const categorySelect = await screen.findByLabelText('Filter by category');
    await waitFor(() =>
      expect(within(categorySelect).getByText('Fire Doors')).toBeInTheDocument(),
    );

    await user.selectOptions(categorySelect, '2');
    await waitFor(() => expect(lastQuery(api).get('categoryId')).toBe('2'));
  });

  it('debounces free-text search into a single request', async () => {
    const user = userEvent.setup();
    api = installFakeApi();
    renderGrid();
    await screen.findByText('Fire Door 90 Min');

    const before = api.listCalls.length;
    await user.type(screen.getByRole('searchbox', { name: 'Search products' }), 'fire');

    await waitFor(() => expect(lastQuery(api).get('search')).toBe('fire'));
    // 4 keystrokes must not mean 4 requests.
    expect(api.listCalls.length - before).toBeLessThan(4);
  });

  it('pages forward and back, disabling controls at the boundaries', async () => {
    const user = userEvent.setup();
    api = installFakeApi({ page: makePage([makeListItem()], { totalCount: 30 }) });
    renderGrid();
    await screen.findByText('Fire Door 90 Min');

    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();

    await user.click(screen.getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(lastQuery(api).get('page')).toBe('2'));
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled();
  });

  it('changes page size', async () => {
    const user = userEvent.setup();
    api = installFakeApi({ page: makePage([makeListItem()], { totalCount: 60 }) });
    renderGrid();
    await screen.findByText('Fire Door 90 Min');

    await user.selectOptions(screen.getByLabelText('Rows per page'), '50');
    await waitFor(() => expect(lastQuery(api).get('pageSize')).toBe('50'));
  });

  it('toggles row density', async () => {
    const user = userEvent.setup();
    api = installFakeApi();
    const { container } = renderGrid();
    await screen.findByText('Fire Door 90 Min');

    expect(container.querySelector('table.dg')).not.toHaveClass('compact');
    await user.click(screen.getByRole('button', { name: 'Compact' }));
    expect(container.querySelector('table.dg')).toHaveClass('compact');
  });

  it('navigates to the detail route when a row is clicked', async () => {
    const user = userEvent.setup();
    api = installFakeApi();
    renderGrid();

    await user.click(await screen.findByText('Fire Door 90 Min'));
    expect(await screen.findByText('detail page')).toBeInTheDocument();
  });

  it('shows the API problem detail when loading fails, and retries', async () => {
    const user = userEvent.setup();
    api = installFakeApi({
      listError: { status: 400, title: 'Validation.Failed', detail: 'sortBy is not sortable.' },
    });
    renderGrid();

    expect(await screen.findByRole('alert')).toHaveTextContent('sortBy is not sortable.');

    const before = api.listCalls.length;
    await user.click(screen.getByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(api.listCalls.length).toBeGreaterThan(before));
  });

  it('shows an empty state with a create action when the catalogue is empty', async () => {
    api = installFakeApi({ page: makePage([]) });
    renderGrid();

    expect(await screen.findByText('No products yet')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'New Product' }).length).toBeGreaterThan(0);
  });

  it('offers to clear filters when a filtered search returns nothing', async () => {
    const user = userEvent.setup();
    api = installFakeApi({ page: makePage([]) });
    renderGrid();
    await screen.findByText('No products yet');

    await user.selectOptions(screen.getByLabelText('Filter by status'), 'Draft');

    expect(await screen.findByText('No products match these filters')).toBeInTheDocument();

    // One in the toolbar, one in the empty state — both are intentional.
    expect(screen.getAllByRole('button', { name: 'Clear filters' })).toHaveLength(2);

    await user.click(screen.getAllByRole('button', { name: 'Clear filters' })[0]);
    await waitFor(() => expect(lastQuery(api).get('status')).toBeNull());
  });

  it('reads initial filter state from the URL', async () => {
    api = installFakeApi();
    renderGrid('/products?status=Draft&sortBy=quality&sortDir=asc&page=2&pageSize=50');

    await waitFor(() => expect(api.listCalls.length).toBeGreaterThan(0));
    const query = lastQuery(api);
    expect(query.get('status')).toBe('Draft');
    expect(query.get('sortBy')).toBe('quality');
    expect(query.get('sortDir')).toBe('asc');
    expect(query.get('page')).toBe('2');
    expect(query.get('pageSize')).toBe('50');
  });

  it('ignores an invalid sort key in the URL rather than sending it', async () => {
    api = installFakeApi();
    renderGrid('/products?sortBy=updated_at;DROP');

    await waitFor(() => expect(api.listCalls.length).toBeGreaterThan(0));
    expect(lastQuery(api).get('sortBy')).toBe('updated');
  });

  it('selects rows and shows the bulk bar', async () => {
    const user = userEvent.setup();
    api = installFakeApi({
      page: makePage([makeListItem({ code: 'A-1' }), makeListItem({ code: 'B-2' })]),
    });
    renderGrid();
    await screen.findByText('A-1');

    expect(screen.queryByRole('region', { name: 'Bulk actions' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('checkbox', { name: 'Select A-1' }));

    const bulk = screen.getByRole('region', { name: 'Bulk actions' });
    expect(within(bulk).getByText('1')).toBeInTheDocument();
  });

  it('select-all covers the page and clears again', async () => {
    const user = userEvent.setup();
    api = installFakeApi({
      page: makePage([makeListItem({ code: 'A-1' }), makeListItem({ code: 'B-2' })]),
    });
    renderGrid();
    await screen.findByText('A-1');

    const selectAll = screen.getByRole('checkbox', { name: 'Select all products on this page' });
    await user.click(selectAll);

    expect(screen.getByRole('checkbox', { name: 'Select A-1' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Select B-2' })).toBeChecked();

    await user.click(screen.getByRole('button', { name: 'Clear' }));
    expect(screen.queryByRole('region', { name: 'Bulk actions' })).not.toBeInTheDocument();
  });

  it('exports only the selected codes', async () => {
    const user = userEvent.setup();
    api = installFakeApi({
      page: makePage([makeListItem({ code: 'A-1' }), makeListItem({ code: 'B-2' })]),
    });
    renderGrid();
    await screen.findByText('A-1');

    await user.click(screen.getByRole('checkbox', { name: 'Select B-2' }));
    await user.click(screen.getByRole('button', { name: 'Export selected' }));

    await waitFor(() => expect(api.exportCalls.length).toBe(1));
    expect(api.exportCalls[0].get('codes')).toBe('B-2');
  });

  it('exports everything matching the current filters', async () => {
    const user = userEvent.setup();
    api = installFakeApi();
    renderGrid('/products?status=Draft');
    await screen.findByText('Fire Door 90 Min');

    await user.click(screen.getByRole('button', { name: 'Export CSV' }));

    await waitFor(() => expect(api.exportCalls.length).toBe(1));
    expect(api.exportCalls[0].get('status')).toBe('Draft');
    expect(api.exportCalls[0].get('codes')).toBeNull();
  });

  it('hides a column via the column chooser', async () => {
    const user = userEvent.setup();
    api = installFakeApi();
    renderGrid();
    await screen.findByText('Fire Door 90 Min');

    expect(screen.getByRole('columnheader', { name: /Owner/ })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Columns' }));
    await user.click(screen.getByRole('checkbox', { name: 'Owner' }));

    expect(screen.queryByRole('columnheader', { name: /Owner/ })).not.toBeInTheDocument();
  });

  it('keeps the Product column non-hideable', async () => {
    const user = userEvent.setup();
    api = installFakeApi();
    renderGrid();
    await screen.findByText('Fire Door 90 Min');

    await user.click(screen.getByRole('button', { name: 'Columns' }));
    expect(screen.getByRole('checkbox', { name: /Product/ })).toBeDisabled();
  });

  it('saves and applies a view', async () => {
    const user = userEvent.setup();
    api = installFakeApi();
    renderGrid('/products?status=Draft');
    await screen.findByText('Fire Door 90 Min');

    await user.click(screen.getByRole('button', { name: /Views/ }));
    await user.type(screen.getByLabelText('View name'), 'My drafts');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    // Change the filter, then re-apply the saved view.
    await user.selectOptions(screen.getByLabelText('Filter by status'), 'Review');
    await waitFor(() => expect(lastQuery(api).get('status')).toBe('Review'));

    await user.click(screen.getByRole('button', { name: /Views/ }));
    await user.click(screen.getByRole('button', { name: 'My drafts' }));

    await waitFor(() => expect(lastQuery(api).get('status')).toBe('Draft'));
  });

  it('renders derived channel readiness with an explicit label', async () => {
    api = installFakeApi();
    renderGrid();
    await screen.findByText('Fire Door 90 Min');

    expect(screen.getByRole('columnheader', { name: /Channels \(derived\)/ })).toBeInTheDocument();
    expect(screen.getByRole('img', { name: /Channel readiness \(derived\)/ })).toBeInTheDocument();
  });

  // Regression (incident 2026-08-14): running the new UI against an older API
  // build that omits channelReadiness must not blank the screen.
  it('renders rows when the API omits channelReadiness', async () => {
    const legacyRow = makeListItem();
    delete (legacyRow as { channelReadiness?: unknown }).channelReadiness;
    api = installFakeApi({ page: makePage([legacyRow]) });

    renderGrid();

    expect(await screen.findByText('Fire Door 90 Min')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: /not yet reported by the API/ })).toBeInTheDocument();
  });
});
