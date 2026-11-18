import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { ToastProvider } from '../../components/Toast';
import { VariantsTab } from './VariantsTab';
import type { ProductVariants, Sku } from './types';

function makeSku(overrides: Partial<Sku> = {}): Sku {
  return {
    code: 'FD-90-GS-1000-L',
    gtin: '4012345678901',
    erpMaterialNo: 'ERP-1',
    status: 'Active',
    sortOrder: 1,
    ...overrides,
  };
}

interface Api {
  posts: Array<Record<string, unknown>>;
  puts: Array<{ url: string; body: Record<string, unknown> }>;
  deletes: string[];
}

function installApi(options: {
  skus?: Sku[];
  rowVersion?: number;
  writeError?: { status: number; title: string; detail: string };
  loadError?: { status: number; title: string; detail: string };
} = {}): Api {
  const posts: Api['posts'] = [];
  const puts: Api['puts'] = [];
  const deletes: string[] = [];
  const rowVersion = options.rowVersion ?? 3;

  const payload = (skus: Sku[]): ProductVariants => ({
    productCode: 'FD-90-GS-1000',
    rowVersion,
    skus,
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
        return options.loadError ? problem(options.loadError) : ok(payload(options.skus ?? []));
      }

      if (options.writeError) {
        return problem(options.writeError);
      }

      if (method === 'POST') {
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
        posts.push(body);
        return ok(payload([...(options.skus ?? []), makeSku({ code: String(body.code) })]));
      }

      if (method === 'PUT') {
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
        puts.push({ url, body });
        return ok(payload([makeSku({ status: String(body.status) })]));
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
      <VariantsTab code="FD-90-GS-1000" />
    </ToastProvider>,
  );
}

describe('VariantsTab', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('lists variants with their commercial fields', async () => {
    installApi({ skus: [makeSku()] });
    renderTab();

    expect(await screen.findByText('FD-90-GS-1000-L')).toBeInTheDocument();
    const row = within(screen.getByRole('table'));
    expect(row.getByText('4012345678901')).toBeInTheDocument();
    expect(row.getByText('ERP-1')).toBeInTheDocument();
    expect(row.getByText('Active')).toBeInTheDocument();
  });

  it('shows an empty state when the product has no variants', async () => {
    installApi({ skus: [] });
    renderTab();

    expect(await screen.findByText('No variants yet')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Add variant' }).length).toBeGreaterThan(0);
  });

  it('adds a variant with the loaded rowVersion', async () => {
    const user = userEvent.setup();
    const api = installApi({ skus: [], rowVersion: 7 });
    renderTab();

    await user.click((await screen.findAllByRole('button', { name: 'Add variant' }))[0]);
    await user.type(screen.getByLabelText(/SKU code/), 'FD-90-R');
    await user.type(screen.getByLabelText(/GTIN/), '4012345678901');
    await user.click(screen.getByRole('button', { name: 'Create variant' }));

    await waitFor(() => expect(api.posts).toHaveLength(1));
    expect(api.posts[0]).toMatchObject({
      code: 'FD-90-R',
      gtin: '4012345678901',
      rowVersion: 7,
    });
  });

  it('edits a variant and keeps the code immutable', async () => {
    const user = userEvent.setup();
    const api = installApi({ skus: [makeSku()] });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Edit' }));

    // The code field is present but not editable.
    expect(screen.getByLabelText(/SKU code/)).toHaveAttribute('readonly');

    await user.selectOptions(screen.getByLabelText(/Status/), 'Blocked');
    await user.click(screen.getByRole('button', { name: 'Save variant' }));

    await waitFor(() => expect(api.puts).toHaveLength(1));
    expect(api.puts[0].url).toContain('/variants/FD-90-GS-1000-L');
    expect(api.puts[0].body).toMatchObject({ status: 'Blocked', rowVersion: 3 });
  });

  it('removes a variant, passing the rowVersion', async () => {
    const user = userEvent.setup();
    const api = installApi({ skus: [makeSku()], rowVersion: 5 });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Remove' }));

    await waitFor(() => expect(api.deletes).toHaveLength(1));
    expect(api.deletes[0]).toContain('rowVersion=5');
  });

  it('surfaces a write conflict without closing the drawer', async () => {
    const user = userEvent.setup();
    installApi({
      skus: [],
      writeError: {
        status: 409,
        title: 'Product.ConcurrentModification',
        detail: 'This product was changed by someone else after you opened it.',
      },
    });
    renderTab();

    await user.click((await screen.findAllByRole('button', { name: 'Add variant' }))[0]);
    await user.type(screen.getByLabelText(/SKU code/), 'FD-90-R');
    await user.click(screen.getByRole('button', { name: 'Create variant' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('changed by someone else');
    expect(screen.getByLabelText(/SKU code/)).toBeInTheDocument(); // drawer still open
  });

  it('surfaces a validation failure from the API', async () => {
    const user = userEvent.setup();
    installApi({
      skus: [],
      writeError: {
        status: 400,
        title: 'Sku.Code.InvalidFormat',
        detail: 'SKU code must be uppercase alphanumeric segments separated by hyphens.',
      },
    });
    renderTab();

    await user.click((await screen.findAllByRole('button', { name: 'Add variant' }))[0]);
    await user.type(screen.getByLabelText(/SKU code/), 'bad code');
    await user.click(screen.getByRole('button', { name: 'Create variant' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('uppercase alphanumeric segments');
  });

  it('shows an error with retry when variants cannot be loaded', async () => {
    installApi({
      loadError: { status: 404, title: 'Product.NotFound', detail: "Product 'X' was not found." },
    });
    renderTab();

    expect(await screen.findByRole('alert')).toHaveTextContent("Product 'X' was not found.");
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  it('tones Blocked and EndOfLife differently from Active', async () => {
    installApi({
      skus: [
        makeSku({ code: 'A-1', status: 'Active' }),
        makeSku({ code: 'A-2', status: 'Blocked' }),
        makeSku({ code: 'A-3', status: 'EndOfLife' }),
      ],
    });
    renderTab();

    expect(await screen.findByText('Active')).toHaveClass('s-ok');
    expect(screen.getByText('Blocked')).toHaveClass('s-warn');
    expect(screen.getByText('EndOfLife')).toHaveClass('s-neut');
  });
});
