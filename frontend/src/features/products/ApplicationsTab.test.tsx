import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { ToastProvider } from '../../components/Toast';
import { ApplicationsTab } from './ApplicationsTab';
import type { ApplicationGroup, ProductApplications } from './types';

function defaultGroups(): ApplicationGroup[] {
  return [
    {
      groupCode: 'INDUSTRY',
      groupLabel: 'Industries',
      options: [
        { code: 'commercial', label: 'Commercial', isAssigned: true },
        { code: 'healthcare', label: 'Healthcare', isAssigned: false },
      ],
    },
    {
      groupCode: 'USE_CASE',
      groupLabel: 'Use cases',
      options: [{ code: 'smoke_control', label: 'Smoke control', isAssigned: false }],
    },
  ];
}

interface Api {
  puts: Array<Record<string, unknown>>;
}

function installApi(
  options: {
    groups?: ApplicationGroup[];
    rowVersion?: number;
    writeError?: { status: number; title: string; detail: string };
    loadError?: { status: number; title: string; detail: string };
  } = {},
): Api {
  const puts: Api['puts'] = [];
  const rowVersion = options.rowVersion ?? 2;

  const payload = (): ProductApplications => ({
    productCode: 'FD-90-GS-1000',
    rowVersion,
    groups: options.groups ?? defaultGroups(),
  });

  vi.stubGlobal(
    'fetch',
    vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      const method = init?.method ?? 'GET';

      const problem = (p: { status: number; title: string; detail: string }) =>
        new Response(JSON.stringify(p), {
          status: p.status,
          headers: { 'Content-Type': 'application/problem+json' },
        });

      const ok = (body: unknown) =>
        new Response(JSON.stringify(body), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });

      if (method === 'GET') {
        return options.loadError ? problem(options.loadError) : ok(payload());
      }

      if (options.writeError) {
        return problem(options.writeError);
      }

      puts.push(JSON.parse(String(init?.body)) as Record<string, unknown>);
      return ok(payload());
    }),
  );

  return { puts };
}

function renderTab() {
  return render(
    <ToastProvider>
      <ApplicationsTab code="FD-90-GS-1000" />
    </ToastProvider>,
  );
}

describe('ApplicationsTab', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows every option, not only the assigned ones', async () => {
    // The tab is a picker: hiding unassigned options would make it impossible
    // to discover what can be selected.
    installApi();
    renderTab();

    expect(await screen.findByRole('button', { name: 'Commercial' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Healthcare' })).toBeInTheDocument();
  });

  it('reflects assignment through aria-pressed, not colour alone', async () => {
    installApi();
    renderTab();

    expect(await screen.findByRole('button', { name: 'Commercial' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(screen.getByRole('button', { name: 'Healthcare' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
  });

  it('counts selections per group', async () => {
    installApi();
    renderTab();

    const industries = within(await screen.findByLabelText('Industries'));
    expect(industries.getByText('1 of 2')).toBeInTheDocument();
  });

  it('starts clean and cannot be saved until something changes', async () => {
    installApi();
    renderTab();

    expect(await screen.findByText('All changes saved')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save applications' })).toBeDisabled();
  });

  it('reports unsaved changes once a tag is toggled', async () => {
    const user = userEvent.setup();
    installApi();
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Healthcare' }));

    expect(screen.getByText('Unsaved changes')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save applications' })).toBeEnabled();
  });

  it('saves the whole set, not a delta', async () => {
    const user = userEvent.setup();
    const api = installApi({ rowVersion: 11 });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Healthcare' }));
    await user.click(screen.getByRole('button', { name: 'Save applications' }));

    await waitFor(() => expect(api.puts).toHaveLength(1));
    expect(api.puts[0].rowVersion).toBe(11);
    expect(api.puts[0].applicationCodes).toEqual(
      expect.arrayContaining(['commercial', 'healthcare']),
    );
  });

  it('can send an empty set to clear every assignment', async () => {
    const user = userEvent.setup();
    const api = installApi();
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Commercial' }));
    await user.click(screen.getByRole('button', { name: 'Save applications' }));

    await waitFor(() => expect(api.puts).toHaveLength(1));
    expect(api.puts[0].applicationCodes).toEqual([]);
  });

  it('discards local changes back to the saved set', async () => {
    const user = userEvent.setup();
    installApi();
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Healthcare' }));
    await user.click(screen.getByRole('button', { name: 'Discard changes' }));

    expect(screen.getByText('All changes saved')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Healthcare' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
  });

  it('surfaces a write conflict and keeps the selection', async () => {
    const user = userEvent.setup();
    installApi({
      writeError: {
        status: 409,
        title: 'Product.ConcurrentModification',
        detail: 'This product was changed by someone else after you opened it.',
      },
    });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Healthcare' }));
    await user.click(screen.getByRole('button', { name: 'Save applications' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('changed by someone else');
    expect(screen.getByRole('button', { name: 'Healthcare' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });

  it('shows an error with retry when applications cannot be loaded', async () => {
    installApi({
      loadError: { status: 404, title: 'Product.NotFound', detail: "Product 'X' was not found." },
    });
    renderTab();

    expect(await screen.findByRole('alert')).toHaveTextContent("Product 'X' was not found.");
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
