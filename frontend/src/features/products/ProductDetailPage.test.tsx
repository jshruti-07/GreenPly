import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { vi } from 'vitest';
import { ToastProvider } from '../../components/Toast';
import { ProductDetailPage } from './ProductDetailPage';
import type { Product } from './types';
import { makeWorkflow } from './testApi';

function makeProduct(overrides: Partial<Product> = {}): Product {
  return {
    publicId: '11111111-2222-3333-4444-555555555555',
    code: 'FD-90-GS-1000',
    name: 'Fire Door 90 Min',
    primaryNodeId: 2,
    brandId: 1,
    baseLocale: 'de-DE',
    lifecycleState: 'Active',
    workflowState: 'Draft',
    gtin: '4012345678901',
    sapMaterialNo: '10000012345',
    ownerDisplayName: 'Anna Becker',
    createdAtUtc: '2026-05-03T00:00:00Z',
    updatedAtUtc: '2026-06-22T00:00:00Z',
    rowVersion: 3,
    ...overrides,
  };
}

interface Api {
  puts: Array<{ url: string; body: Record<string, unknown> }>;
  posts: Array<{ url: string; body: Record<string, unknown> }>;
}

function installApi(options: {
  product?: Product;
  getError?: { status: number; title: string; detail: string };
  saveError?: { status: number; title: string; detail: string };
} = {}): Api {
  const puts: Api['puts'] = [];
  const posts: Api['posts'] = [];
  const product = options.product ?? makeProduct();

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

      if (method === 'GET') {
        if (options.getError) {
          return problem(options.getError);
        }
        // The workflow document is a separate resource with a separate shape;
        // serving the product for it would make the stepper assert nothing.
        if (url.endsWith('/workflow')) {
          return new Response(
            JSON.stringify(makeWorkflow({ workflowState: product.workflowState, rowVersion: product.rowVersion })),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          );
        }
        return new Response(JSON.stringify(product), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }

      const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
      if (method === 'PUT') {
        puts.push({ url, body });
        if (options.saveError) {
          return problem(options.saveError);
        }
        return new Response(
          JSON.stringify({ ...product, ...body, rowVersion: product.rowVersion + 1 }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        );
      }

      posts.push({ url, body });

      if (url.endsWith('/workflow/actions')) {
        const landed =
          String(body.action) === 'SUBMIT'
            ? 'Review'
            : String(body.action) === 'APPROVE'
              ? 'Approval'
              : 'Draft';

        return new Response(
          JSON.stringify(makeWorkflow({ workflowState: landed, rowVersion: product.rowVersion + 1 })),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        );
      }

      return new Response(
        JSON.stringify({ ...product, rowVersion: product.rowVersion + 1 }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      );
    }),
  );

  return { puts, posts };
}

function renderDetail(code = 'FD-90-GS-1000') {
  return render(
    <MemoryRouter initialEntries={[`/products/${code}`]}>
      <ToastProvider>
        <Routes>
          <Route path="/products/:code" element={<ProductDetailPage />} />
        </Routes>
      </ToastProvider>
    </MemoryRouter>,
  );
}

describe('ProductDetailPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows the product in read mode', async () => {
    installApi();
    renderDetail();

    expect(await screen.findByRole('heading', { name: 'Fire Door 90 Min' })).toBeInTheDocument();
    expect(screen.getByText('Anna Becker')).toBeInTheDocument();
    expect(screen.getByText('4012345678901')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save changes' })).not.toBeInTheDocument();
  });

  it('shows the workflow stepper with the current step marked', async () => {
    installApi({ product: makeProduct({ workflowState: 'Review' }) });
    renderDetail();

    const current = await screen.findByText('2. Review');
    expect(current).toHaveAttribute('aria-current', 'step');
  });

  it('offers the actions the pipeline allows from the current step, labelled as it labels them', async () => {
    // The labels are the pipeline's, not the page's: since Sprint 27 nothing in
    // this component knows that Draft leads to Review.
    installApi({ product: makeProduct({ workflowState: 'Draft' }) });
    renderDetail();

    expect(await screen.findByRole('button', { name: 'Submit for review' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Approve and publish' })).not.toBeInTheDocument();
  });

  it('offers no transitions once published', async () => {
    installApi({ product: makeProduct({ workflowState: 'Published' }) });
    renderDetail();

    await screen.findByRole('heading', { name: 'Fire Door 90 Min' });
    expect(
      screen.queryByRole('button', { name: /Submit|Approve|Reject|Request changes/ }),
    ).not.toBeInTheDocument();
  });

  it('saves edited fields with the loaded rowVersion', async () => {
    const user = userEvent.setup();
    const api = installApi();
    renderDetail();

    await user.click(await screen.findByRole('button', { name: 'Edit' }));

    const name = screen.getByLabelText(/Product name/);
    await user.clear(name);
    await user.type(name, 'Fire Door 90 Renamed');
    await user.selectOptions(screen.getByLabelText(/Owner/), 'Lena Vogt');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));

    await waitFor(() => expect(api.puts).toHaveLength(1));
    expect(api.puts[0].body).toMatchObject({
      name: 'Fire Door 90 Renamed',
      ownerDisplayName: 'Lena Vogt',
      rowVersion: 3,
    });

    // Returns to read mode showing the saved value.
    expect(await screen.findByRole('button', { name: 'Edit' })).toBeInTheDocument();
  });

  it('surfaces a concurrency conflict without losing the form', async () => {
    const user = userEvent.setup();
    installApi({
      saveError: {
        status: 409,
        title: 'Product.ConcurrentModification',
        detail: 'This product was changed by someone else after you opened it.',
      },
    });
    renderDetail();

    await user.click(await screen.findByRole('button', { name: 'Edit' }));
    await user.click(screen.getByRole('button', { name: 'Save changes' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'This product was changed by someone else',
    );
    // Still editing, so the user's input is not thrown away.
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeInTheDocument();
  });

  it('cancels an edit without saving', async () => {
    const user = userEvent.setup();
    const api = installApi();
    renderDetail();

    await user.click(await screen.findByRole('button', { name: 'Edit' }));
    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(api.puts).toHaveLength(0);
    expect(screen.getByRole('button', { name: 'Edit' })).toBeInTheDocument();
  });

  it('posts the action, not a target state, with the current rowVersion', async () => {
    const user = userEvent.setup();
    const api = installApi();
    renderDetail();

    await user.click(await screen.findByRole('button', { name: 'Submit for review' }));

    await waitFor(() => expect(api.posts).toHaveLength(1));
    expect(api.posts[0].url).toContain('/workflow/actions');
    // Comment and actor are null from the header: the tab is where a reviewer
    // says something, and sending an empty string instead of null would write a
    // blank note into the approval trail.
    expect(api.posts[0].body).toEqual({
      action: 'SUBMIT',
      comment: null,
      actor: null,
      rowVersion: 3,
    });
  });

  it('shows an error with retry when the product cannot be loaded', async () => {
    installApi({
      getError: { status: 404, title: 'Product.NotFound', detail: "Product 'X-1' was not found." },
    });
    renderDetail('X-1');

    expect(await screen.findByRole('alert')).toHaveTextContent("Product 'X-1' was not found.");
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
