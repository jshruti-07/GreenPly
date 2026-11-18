import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { ToastProvider } from '../../components/Toast';
import { PackagingTab } from './PackagingTab';
import type { PackagingUnit, ProductPackaging, SkuPackaging } from './types';

function makeUnit(overrides: Partial<PackagingUnit> = {}): PackagingUnit {
  return {
    id: 1,
    level: 'EACH',
    parentLevel: null,
    containsQty: 1,
    gtin: '4012345678901',
    sscc: null,
    grossWeight: 68,
    netWeight: 66,
    lengthMm: 1100,
    widthMm: 2200,
    heightMm: 80,
    isBaseUnit: true,
    dangerousGoods: false,
    unClass: null,
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
    skus?: SkuPackaging[];
    rowVersion?: number;
    writeError?: { status: number; title: string; detail: string };
    loadError?: { status: number; title: string; detail: string };
  } = {},
): Api {
  const posts: Api['posts'] = [];
  const puts: Api['puts'] = [];
  const deletes: string[] = [];
  const rowVersion = options.rowVersion ?? 3;

  const payload = (): ProductPackaging => ({
    productCode: 'FD-90-GS-1000',
    rowVersion,
    skus: options.skus ?? [{ skuCode: 'FD-90-GS-1000-V1', units: [makeUnit()] }],
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
        return options.loadError ? problem(options.loadError) : ok(payload());
      }

      if (options.writeError) {
        return problem(options.writeError);
      }

      if (method === 'POST') {
        posts.push(JSON.parse(String(init?.body)) as Record<string, unknown>);
        return ok(payload());
      }

      if (method === 'PUT') {
        puts.push({ url, body: JSON.parse(String(init?.body)) as Record<string, unknown> });
        return ok(payload());
      }

      deletes.push(url);
      return ok(payload());
    }),
  );

  return { posts, puts, deletes };
}

function renderTab() {
  return render(
    <ToastProvider>
      <PackagingTab code="FD-90-GS-1000" />
    </ToastProvider>,
  );
}

describe('PackagingTab', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('groups packaging under its SKU, because packaging belongs to the variant', async () => {
    installApi();
    renderTab();

    expect(await screen.findByText('FD-90-GS-1000-V1')).toBeInTheDocument();
    // Scoped: the add-level drawer stays mounted and lists the same labels.
    expect(within(screen.getByRole('table')).getByText('Each / piece')).toBeInTheDocument();
  });

  it('marks the orderable unit', async () => {
    installApi();
    renderTab();

    expect(await screen.findByText('Orderable')).toBeInTheDocument();
  });

  it('flags dangerous goods with its UN class', async () => {
    installApi({
      skus: [
        {
          skuCode: 'S1',
          units: [makeUnit({ dangerousGoods: true, unClass: 'UN1950', isBaseUnit: false })],
        },
      ],
    });
    renderTab();

    expect(await screen.findByText('UN1950')).toBeInTheDocument();
  });

  it('tells the steward when a variant has no packaging yet', async () => {
    installApi({ skus: [{ skuCode: 'S1', units: [] }] });
    renderTab();

    expect(await screen.findByText('No packaging recorded for this variant.')).toBeInTheDocument();
  });

  it('explains that packaging needs a variant when none exist', async () => {
    installApi({ skus: [] });
    renderTab();

    expect(await screen.findByText('No variants yet')).toBeInTheDocument();
  });

  it('adds a level with the loaded rowVersion and the SKU it was opened from', async () => {
    const user = userEvent.setup();
    const api = installApi({ skus: [{ skuCode: 'S1', units: [] }], rowVersion: 9 });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Add level' }));
    await user.click(screen.getByRole('button', { name: 'Create level' }));

    await waitFor(() => expect(api.posts).toHaveLength(1));
    expect(api.posts[0]).toMatchObject({ skuCode: 'S1', rowVersion: 9 });
  });

  it('only offers levels the variant does not already have', async () => {
    const user = userEvent.setup();
    installApi({
      skus: [
        {
          skuCode: 'S1',
          units: [makeUnit({ level: 'EACH' }), makeUnit({ id: 2, level: 'CARTON' })],
        },
      ],
    });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Add level' }));

    const levelSelect = screen.getByLabelText(/^Level/);
    expect(within(levelSelect).queryByRole('option', { name: 'Each / piece' })).not.toBeInTheDocument();
    expect(within(levelSelect).queryByRole('option', { name: 'Carton' })).not.toBeInTheDocument();
    expect(within(levelSelect).getByRole('option', { name: 'Pallet' })).toBeInTheDocument();
  });

  it('keeps the level immutable when editing', async () => {
    const user = userEvent.setup();
    installApi();
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Edit' }));

    expect(screen.getByLabelText(/^Level/)).toHaveAttribute('readonly');
  });

  it('sends blank measurements as null rather than zero', async () => {
    // Zero is a claim ("this weighs nothing"); blank means "not recorded".
    const user = userEvent.setup();
    const api = installApi({ skus: [{ skuCode: 'S1', units: [] }] });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Add level' }));
    await user.click(screen.getByRole('button', { name: 'Create level' }));

    await waitFor(() => expect(api.posts).toHaveLength(1));
    expect(api.posts[0].grossWeight).toBeNull();
    expect(api.posts[0].lengthMm).toBeNull();
  });

  it('removes a level, passing the rowVersion', async () => {
    const user = userEvent.setup();
    const api = installApi({ rowVersion: 5 });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Remove' }));

    await waitFor(() => expect(api.deletes).toHaveLength(1));
    expect(api.deletes[0]).toContain('rowVersion=5');
  });

  it('surfaces a duplicate GTIN without closing the drawer', async () => {
    const user = userEvent.setup();
    installApi({
      skus: [{ skuCode: 'S1', units: [] }],
      writeError: {
        status: 409,
        title: 'Packaging.GtinInUse',
        detail: "GTIN '4012345678901' is already used by another packaging unit.",
      },
    });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Add level' }));
    await user.click(screen.getByRole('button', { name: 'Create level' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('already used');
    expect(screen.getByRole('button', { name: 'Create level' })).toBeInTheDocument();
  });

  it('shows an error with retry when packaging cannot be loaded', async () => {
    installApi({
      loadError: { status: 404, title: 'Product.NotFound', detail: "Product 'X' was not found." },
    });
    renderTab();

    expect(await screen.findByRole('alert')).toHaveTextContent("Product 'X' was not found.");
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
