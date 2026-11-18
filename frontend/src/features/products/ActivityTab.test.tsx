import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { ToastProvider } from '../../components/Toast';
import { ActivityTab } from './ActivityTab';
import type { ActivityEntry, ProductActivity } from './types';

function makeEntry(overrides: Partial<ActivityEntry> = {}): ActivityEntry {
  return {
    id: 1,
    occurredAt: '2026-08-15T10:20:00Z',
    actor: null,
    action: 'updated',
    detail: 'General information',
    source: 'api',
    ...overrides,
  };
}

interface Api {
  urls: string[];
}

function installApi(
  options: {
    entries?: ActivityEntry[];
    totalCount?: number;
    availableActions?: string[];
    loadError?: { status: number; title: string; detail: string };
  } = {},
): Api {
  const urls: string[] = [];
  const entries = options.entries ?? [makeEntry()];

  const payload = (page: number, pageSize: number): ProductActivity => ({
    productCode: 'FD-90-GS-1000',
    availableActions: options.availableActions ?? ['created', 'updated'],
    entries: {
      items: entries,
      page,
      pageSize,
      totalCount: options.totalCount ?? entries.length,
    },
  });

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      urls.push(url);

      if (options.loadError) {
        return new Response(JSON.stringify(options.loadError), {
          status: options.loadError.status,
          headers: { 'Content-Type': 'application/problem+json' },
        });
      }

      const parsed = new URL(url, 'http://localhost');
      const page = Number(parsed.searchParams.get('page') ?? 1);
      const pageSize = Number(parsed.searchParams.get('pageSize') ?? 25);

      return new Response(JSON.stringify(payload(page, pageSize)), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }),
  );

  return { urls };
}

function renderTab() {
  return render(
    <ToastProvider>
      <ActivityTab code="FD-90-GS-1000" />
    </ToastProvider>,
  );
}

describe('ActivityTab', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('lists entries with their action and detail', async () => {
    installApi();
    renderTab();

    expect(await screen.findByText('General information')).toBeInTheDocument();
    // Scoped: the action filter renders the same words as the chips.
    expect(within(screen.getByRole('table')).getByText('Updated')).toBeInTheDocument();
  });

  it('renders a missing actor as Unknown rather than inventing one', async () => {
    // There is no auth yet. A fabricated actor in an audit trail is worse than
    // an absent one, so the tab must show the absence explicitly.
    installApi({ entries: [makeEntry({ actor: null })] });
    renderTab();

    const table = within(await screen.findByRole('table'));
    expect(table.getByText('Unknown')).toBeInTheDocument();
  });

  it('says plainly why attribution is missing', async () => {
    installApi();
    renderTab();

    expect(await screen.findByText(/no authentication/i)).toBeInTheDocument();
  });

  it('shows a real actor once one is recorded', async () => {
    installApi({ entries: [makeEntry({ actor: 'Anna Becker' })] });
    renderTab();

    const table = within(await screen.findByRole('table'));
    expect(table.getByText('Anna Becker')).toBeInTheDocument();
    expect(table.queryByText('Unknown')).not.toBeInTheDocument();
  });

  it('offers only the actions this product actually has', async () => {
    // A filter that can return nothing is a trap; the server sends the set.
    installApi({ availableActions: ['created', 'published'] });
    renderTab();

    const select = await screen.findByLabelText('Action');
    expect(select).toHaveTextContent('Created');
    expect(select).toHaveTextContent('Published');
    expect(select).not.toHaveTextContent('Deleted');
  });

  it('sends the action filter to the server rather than filtering the page', async () => {
    const user = userEvent.setup();
    const api = installApi({ availableActions: ['created', 'updated'] });
    renderTab();

    await user.selectOptions(await screen.findByLabelText('Action'), 'created');

    await waitFor(() => expect(api.urls.some((u) => u.includes('action=created'))).toBe(true));
  });

  it('sends the search term to the server', async () => {
    const user = userEvent.setup();
    const api = installApi();
    renderTab();

    await user.type(await screen.findByPlaceholderText('Search activity…'), 'variant');

    await waitFor(() => expect(api.urls.some((u) => u.includes('search=variant'))).toBe(true));
  });

  it('resets to the first page when a filter changes', async () => {
    // Otherwise a steward on page 4 filters and sees an empty page.
    const user = userEvent.setup();
    const api = installApi({ totalCount: 200, availableActions: ['created'] });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Next' }));
    await waitFor(() => expect(api.urls.some((u) => u.includes('page=2'))).toBe(true));

    await user.selectOptions(screen.getByLabelText('Action'), 'created');

    await waitFor(() =>
      expect(api.urls.some((u) => u.includes('action=created') && u.includes('page=1'))).toBe(true),
    );
  });

  it('pages through a long history', async () => {
    installApi({ totalCount: 60 });
    renderTab();

    expect(await screen.findByText(/Page 1 of 3/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Next' })).toBeEnabled();
  });

  it('hides paging when there is nothing to page', async () => {
    installApi({ entries: [], totalCount: 0 });
    renderTab();

    await screen.findByText('No activity yet');
    expect(screen.queryByRole('button', { name: 'Next' })).not.toBeInTheDocument();
  });

  it('distinguishes an empty history from an empty filter result', async () => {
    const user = userEvent.setup();
    installApi({ entries: [], totalCount: 0, availableActions: ['created'] });
    renderTab();

    expect(await screen.findByText('No activity yet')).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Action'), 'created');

    expect(await screen.findByText('No matching activity')).toBeInTheDocument();
  });

  it('tones a deletion apart from a creation', async () => {
    installApi({
      entries: [
        makeEntry({ id: 1, action: 'created' }),
        makeEntry({ id: 2, action: 'deleted' }),
      ],
    });
    renderTab();

    const table = within(await screen.findByRole('table'));
    expect(table.getByText('Created')).toHaveClass('s-ok');
    expect(table.getByText('Deleted')).toHaveClass('s-bad');
  });

  it('shows an error with retry when activity cannot be loaded', async () => {
    installApi({
      loadError: { status: 404, title: 'Product.NotFound', detail: "Product 'X' was not found." },
    });
    renderTab();

    expect(await screen.findByRole('alert')).toHaveTextContent("Product 'X' was not found.");
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
