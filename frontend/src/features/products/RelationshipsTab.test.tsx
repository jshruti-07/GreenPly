import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { vi } from 'vitest';
import { ToastProvider } from '../../components/Toast';
import { RelationshipsTab } from './RelationshipsTab';
import type { ProductRelationships, RelationshipGroup } from './types';
import { RELATION_TYPES } from './types';

/** Every type is always returned by the API, empty ones included. */
function allGroups(overrides: Partial<Record<string, RelationshipGroup['items']>> = {}) {
  return RELATION_TYPES.map((relationType) => ({
    relationType,
    items: overrides[relationType] ?? [],
  }));
}

interface Api {
  posts: Array<Record<string, unknown>>;
  deletes: string[];
}

function installApi(
  options: {
    groups?: ReturnType<typeof allGroups>;
    rowVersion?: number;
    writeError?: { status: number; title: string; detail: string };
    loadError?: { status: number; title: string; detail: string };
  } = {},
): Api {
  const posts: Api['posts'] = [];
  const deletes: string[] = [];
  const rowVersion = options.rowVersion ?? 4;

  const payload = (): ProductRelationships => ({
    productCode: 'FD-90-GS-1000',
    rowVersion,
    groups: options.groups ?? allGroups(),
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

      deletes.push(url);
      return ok(payload());
    }),
  );

  return { posts, deletes };
}

function renderTab() {
  return render(
    <MemoryRouter>
      <ToastProvider>
        <RelationshipsTab code="FD-90-GS-1000" />
      </ToastProvider>
    </MemoryRouter>,
  );
}

describe('RelationshipsTab', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows every relationship type, including the empty ones', async () => {
    // An empty card tells a steward the link kind exists and is unpopulated;
    // omitting it would hide the option entirely.
    installApi();
    renderTab();

    expect(await screen.findByLabelText('Accessories')).toBeInTheDocument();
    expect(screen.getByLabelText('Compatible products')).toBeInTheDocument();
    expect(screen.getByLabelText('Replacement parts')).toBeInTheDocument();
    expect(screen.getByLabelText('Cross-sell')).toBeInTheDocument();
    expect(screen.getByLabelText('Up-sell')).toBeInTheDocument();
  });

  it('links to the related product rather than just naming it', async () => {
    installApi({
      groups: allGroups({
        ACCESSORY: [
          {
            relationType: 'ACCESSORY',
            targetCode: 'DH-CL-300',
            targetName: 'Door Closer DC-300',
            targetWorkflowState: 'Published',
            priority: 1,
          },
        ],
      }),
    });
    renderTab();

    const link = await screen.findByRole('link', { name: 'Door Closer DC-300' });
    expect(link).toHaveAttribute('href', '/products/DH-CL-300');
  });

  it('shows the target workflow state, so a draft link is visible as such', async () => {
    installApi({
      groups: allGroups({
        ACCESSORY: [
          {
            relationType: 'ACCESSORY',
            targetCode: 'X-1',
            targetName: 'Draft accessory',
            targetWorkflowState: 'Draft',
            priority: 1,
          },
        ],
      }),
    });
    renderTab();

    const card = within(await screen.findByLabelText('Accessories'));
    expect(card.getByText('Draft')).toBeInTheDocument();
  });

  it('says explicitly that links are not mirrored', async () => {
    installApi();
    renderTab();

    expect(await screen.findByText(/not mirrored automatically/i)).toBeInTheDocument();
  });

  it('adds a link with the type of the card it was opened from', async () => {
    const user = userEvent.setup();
    const api = installApi({ rowVersion: 8 });
    renderTab();

    const card = within(await screen.findByLabelText('Cross-sell'));
    await user.click(card.getByRole('button', { name: 'Add link' }));
    await user.type(screen.getByLabelText(/Product code/), 'INS-MW-100');
    await user.click(screen.getByRole('button', { name: 'Create link' }));

    await waitFor(() => expect(api.posts).toHaveLength(1));
    expect(api.posts[0]).toMatchObject({
      relationType: 'CROSS_SELL',
      targetCode: 'INS-MW-100',
      rowVersion: 8,
    });
  });

  it('removes a link by type and target', async () => {
    const user = userEvent.setup();
    const api = installApi({
      rowVersion: 6,
      groups: allGroups({
        ACCESSORY: [
          {
            relationType: 'ACCESSORY',
            targetCode: 'DH-CL-300',
            targetName: 'Door Closer',
            targetWorkflowState: 'Published',
            priority: 1,
          },
        ],
      }),
    });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Remove' }));

    await waitFor(() => expect(api.deletes).toHaveLength(1));
    expect(api.deletes[0]).toContain('/relationships/ACCESSORY/DH-CL-300');
    expect(api.deletes[0]).toContain('rowVersion=6');
  });

  it('surfaces a self-reference rejection without closing the drawer', async () => {
    const user = userEvent.setup();
    installApi({
      writeError: {
        status: 400,
        title: 'Relationship.SelfReference',
        detail: 'A product cannot be related to itself.',
      },
    });
    renderTab();

    const card = within(await screen.findByLabelText('Accessories'));
    await user.click(card.getByRole('button', { name: 'Add link' }));
    await user.type(screen.getByLabelText(/Product code/), 'FD-90-GS-1000');
    await user.click(screen.getByRole('button', { name: 'Create link' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('cannot be related to itself');
    expect(screen.getByLabelText(/Product code/)).toBeInTheDocument();
  });

  it('shows an error with retry when relationships cannot be loaded', async () => {
    installApi({
      loadError: { status: 404, title: 'Product.NotFound', detail: "Product 'X' was not found." },
    });
    renderTab();

    expect(await screen.findByRole('alert')).toHaveTextContent("Product 'X' was not found.");
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
