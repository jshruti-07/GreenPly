import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { ToastProvider } from '../../components/Toast';
import { ComplianceTab } from './ComplianceTab';
import type { ComplianceDocument, ProductCompliance } from './types';

function makeDocument(overrides: Partial<ComplianceDocument> = {}): ComplianceDocument {
  return {
    id: 1,
    docType: 'TEST',
    reference: 'EN1634-1-2026-045',
    standard: 'EN 1634-1',
    authority: 'TÜV Rheinland',
    validFrom: '2026-01-01',
    validTo: '2029-12-31',
    status: 'valid',
    expiryState: 'Valid',
    ...overrides,
  };
}

interface Api {
  posts: Array<Record<string, unknown>>;
  puts: Array<{ url: string; body: Record<string, unknown> }>;
  deletes: string[];
}

function installApi(
  options: {
    documents?: ComplianceDocument[];
    expiringCount?: number;
    expiredCount?: number;
    rowVersion?: number;
    writeError?: { status: number; title: string; detail: string };
    loadError?: { status: number; title: string; detail: string };
    extras?: Partial<ProductCompliance>;
  } = {},
): Api {
  const posts: Api['posts'] = [];
  const puts: Api['puts'] = [];
  const deletes: string[] = [];
  const rowVersion = options.rowVersion ?? 3;

  const payload = (documents: ComplianceDocument[]): ProductCompliance => ({
    productCode: 'FD-90-GS-1000',
    rowVersion,
    expiringCount: options.expiringCount ?? 0,
    expiredCount: options.expiredCount ?? 0,
    documents,
    indicators: [],
    materials: [],
    substances: [],
    greenBuilding: [],
    ...options.extras,
  });

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
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
        return options.loadError
          ? problem(options.loadError)
          : ok(payload(options.documents ?? []));
      }

      if (options.writeError) {
        return problem(options.writeError);
      }

      if (method === 'POST') {
        posts.push(JSON.parse(String(init?.body)) as Record<string, unknown>);
        return ok(payload([...(options.documents ?? []), makeDocument({ id: 99 })]));
      }

      if (method === 'PUT') {
        puts.push({ url, body: JSON.parse(String(init?.body)) as Record<string, unknown> });
        return ok(payload(options.documents ?? []));
      }

      deletes.push(url);
      return ok(payload([]));
    }),
  );

  return { posts, puts, deletes };
}

function renderTab() {
  return render(
    <ToastProvider>
      <ComplianceTab code="FD-90-GS-1000" />
    </ToastProvider>,
  );
}

describe('ComplianceTab', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('lists certificates with authority and dates', async () => {
    installApi({ documents: [makeDocument()] });
    renderTab();

    expect(await screen.findByText('EN 1634-1')).toBeInTheDocument();
    const table = within(screen.getByRole('table'));
    expect(table.getByText('EN1634-1-2026-045')).toBeInTheDocument();
    expect(table.getByText('TÜV Rheinland')).toBeInTheDocument();
  });

  it('shows an empty state when nothing is recorded', async () => {
    installApi({ documents: [] });
    renderTab();

    expect(await screen.findByText('No certificates recorded')).toBeInTheDocument();
  });

  it('renders the expiring banner from the server count, not from dates', async () => {
    // The 12-month window is a business rule the server owns. The tab must
    // display what it is told rather than re-deriving it from validTo.
    installApi({
      documents: [makeDocument({ expiryState: 'Expiring', validTo: '2027-06-30' })],
      expiringCount: 1,
    });
    renderTab();

    expect(await screen.findByText(/expiring within 12 months/)).toBeInTheDocument();
  });

  it('renders an expired certificate as an error, not a warning', async () => {
    installApi({
      documents: [makeDocument({ expiryState: 'Expired', validTo: '2020-01-01' })],
      expiredCount: 1,
    });
    renderTab();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('expired');
    expect(alert).toHaveTextContent('may not be sellable');
  });

  it('shows no banner when everything is in date', async () => {
    installApi({ documents: [makeDocument()] });
    renderTab();

    await screen.findByText('EN 1634-1');
    expect(screen.queryByText(/expiring within 12 months/)).not.toBeInTheDocument();
    expect(screen.queryByText(/may not be sellable/)).not.toBeInTheDocument();
  });

  it('tones expiry states apart from one another', async () => {
    installApi({
      documents: [
        makeDocument({ id: 1, standard: 'A', expiryState: 'Valid' }),
        makeDocument({ id: 2, standard: 'B', expiryState: 'Expiring' }),
        makeDocument({ id: 3, standard: 'C', expiryState: 'Expired' }),
        makeDocument({ id: 4, standard: 'D', expiryState: 'NoExpiry' }),
      ],
      expiringCount: 1,
      expiredCount: 1,
    });
    renderTab();

    expect(await screen.findByText('In date')).toHaveClass('s-ok');
    expect(screen.getByText('Expiring')).toHaveClass('s-warn');
    expect(screen.getByText('Expired')).toHaveClass('s-bad');
    expect(screen.getByText('No expiry')).toHaveClass('s-neut');
  });

  it('adds a certificate with the loaded rowVersion', async () => {
    const user = userEvent.setup();
    const api = installApi({ documents: [], rowVersion: 7 });
    renderTab();

    await user.click((await screen.findAllByRole('button', { name: 'Add certificate' }))[0]);
    await user.type(screen.getByLabelText(/Certificate №/), 'CE-2026-1');
    await user.type(screen.getByLabelText(/^Standard/), 'EN 1634-1');
    await user.click(screen.getByRole('button', { name: 'Create certificate' }));

    await waitFor(() => expect(api.posts).toHaveLength(1));
    expect(api.posts[0]).toMatchObject({
      reference: 'CE-2026-1',
      standard: 'EN 1634-1',
      rowVersion: 7,
    });
  });

  it('does not offer status when adding, because a new document is always valid', async () => {
    const user = userEvent.setup();
    installApi({ documents: [] });
    renderTab();

    await user.click((await screen.findAllByRole('button', { name: 'Add certificate' }))[0]);

    expect(screen.queryByLabelText(/Status/)).not.toBeInTheDocument();
  });

  it('offers withdraw and supersede when editing, but never "expired"', async () => {
    const user = userEvent.setup();
    installApi({ documents: [makeDocument()] });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Edit' }));

    const status = screen.getByLabelText(/Status/);
    expect(within(status).getByRole('option', { name: 'Withdrawn' })).toBeInTheDocument();
    expect(within(status).getByRole('option', { name: 'Superseded' })).toBeInTheDocument();
    expect(within(status).queryByRole('option', { name: /Expired/i })).not.toBeInTheDocument();
  });

  it('edits a certificate against its id', async () => {
    const user = userEvent.setup();
    const api = installApi({ documents: [makeDocument({ id: 42 })] });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Edit' }));
    await user.selectOptions(screen.getByLabelText(/Status/), 'withdrawn');
    await user.click(screen.getByRole('button', { name: 'Save certificate' }));

    await waitFor(() => expect(api.puts).toHaveLength(1));
    expect(api.puts[0].url).toContain('/compliance/42');
    expect(api.puts[0].body).toMatchObject({ status: 'withdrawn', rowVersion: 3 });
  });

  it('removes a certificate, passing the rowVersion', async () => {
    const user = userEvent.setup();
    const api = installApi({ documents: [makeDocument({ id: 42 })], rowVersion: 5 });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Remove' }));

    await waitFor(() => expect(api.deletes).toHaveLength(1));
    expect(api.deletes[0]).toContain('/compliance/42?rowVersion=5');
  });

  it('surfaces a duplicate certificate number without closing the drawer', async () => {
    const user = userEvent.setup();
    installApi({
      documents: [],
      writeError: {
        status: 409,
        title: 'Compliance.DuplicateReference',
        detail: "Certificate 'CE-1' is already recorded against this product.",
      },
    });
    renderTab();

    await user.click((await screen.findAllByRole('button', { name: 'Add certificate' }))[0]);
    await user.type(screen.getByLabelText(/Certificate №/), 'CE-1');
    await user.click(screen.getByRole('button', { name: 'Create certificate' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('already recorded');
    expect(screen.getByLabelText(/Certificate №/)).toBeInTheDocument();
  });

  it('shows an error with retry when compliance cannot be loaded', async () => {
    installApi({
      loadError: { status: 404, title: 'Product.NotFound', detail: "Product 'X' was not found." },
    });
    renderTab();

    expect(await screen.findByRole('alert')).toHaveTextContent("Product 'X' was not found.");
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  it('renders the read-only sustainability panels when data is present', async () => {
    installApi({
      documents: [makeDocument()],
      extras: {
        indicators: [
          { indicator: 'GWP', value: 142, unit: 'kg CO2-eq', method: 'EN 15804+A2', stage: 'A1-A3' },
        ],
        materials: [
          { material: 'Galvanized steel', pct: 78, isRecyclable: true, isHazardous: false },
        ],
        substances: [
          { regime: 'REACH_SVHC', result: 'compliant', threshold: '0.1% w/w', notes: null },
        ],
        greenBuilding: [{ scheme: 'DGNB', credit: 'ENV1.1', note: null }],
      },
    });
    renderTab();

    expect(await screen.findByText('GWP')).toBeInTheDocument();
    expect(screen.getByText('Galvanized steel')).toBeInTheDocument();
    expect(screen.getByText('REACH_SVHC')).toBeInTheDocument();
    expect(screen.getByText('DGNB')).toBeInTheDocument();
  });

  it('omits sustainability panels entirely when there is no data', async () => {
    installApi({ documents: [makeDocument()] });
    renderTab();

    await screen.findByText('EN 1634-1');
    expect(screen.queryByLabelText('Material composition')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Green building contributions')).not.toBeInTheDocument();
  });
});
