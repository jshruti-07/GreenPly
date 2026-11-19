import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { ToastProvider } from '../../components/Toast';
import { AssetsTab } from './AssetsTab';
import type { ProductAsset, ProductAssets } from './types';

const LEAD = 'AB-CRAFT-EXTENDED-COVER-PLATE-31-X-30-CM';

function makeAsset(overrides: Partial<ProductAsset> = {}): ProductAsset {
  return {
    linkId: 1,
    assetId: 10,
    role: 'MAIN',
    assetType: 'IMAGE',
    storageProvider: 'external',
    fileName: 'craft-main.jpg',
    mimeType: 'image/jpeg',
    fileSize: 245_760,
    url: 'https://assets.example.test/craft-main.jpg',
    title: null,
    altText: 'Craft Extended Abdeckplatte',
    isPrimary: true,
    sortOrder: 1,
    isExternal: true,
    ...overrides,
  };
}

interface Api {
  /** URL as well as body: "set primary" is a POST to a sub-resource. */
  posts: Array<{ url: string; body: Record<string, unknown> }>;
  deletes: string[];
}

function installApi(
  options: {
    media?: ProductAsset[];
    documents?: ProductAsset[];
    rowVersion?: number;
    writeError?: { status: number; title: string; detail: string };
    loadError?: { status: number; title: string; detail: string };
  } = {},
): Api {
  const posts: Api['posts'] = [];
  const deletes: string[] = [];
  const rowVersion = options.rowVersion ?? 4;

  const payload = (): ProductAssets => ({
    productCode: LEAD,
    rowVersion,
    media: options.media ?? [makeAsset()],
    documents:
      options.documents ?? [
        makeAsset({
          linkId: 2,
          assetId: 20,
          role: 'DATASHEET',
          assetType: 'DOCUMENT',
          fileName: 'craft-datenblatt.pdf',
          mimeType: 'application/pdf',
          fileSize: 1_048_576,
          url: 'https://assets.example.test/craft-datenblatt.pdf',
          title: 'Technisches Datenblatt',
          altText: null,
          isPrimary: true,
        }),
      ],
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
        posts.push({ url, body: JSON.parse(String(init?.body)) as Record<string, unknown> });
        return ok(payload());
      }

      deletes.push(url);
      return ok(payload());
    }),
  );

  return { posts, deletes };
}

function renderTab(onChanged?: (rowVersion: number) => void) {
  return render(
    <ToastProvider>
      <AssetsTab code={LEAD} onChanged={onChanged} />
    </ToastProvider>,
  );
}

describe('AssetsTab', () => {
  beforeEach(() => {
    window.localStorage?.clear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    window.localStorage?.clear();
  });

  it('splits assets by whether they can be shown as a picture', async () => {
    // Images and documents are ONE module server-side; the split is by type,
    // because only one of them has something to render as a thumbnail.
    installApi();
    renderTab();

    const media = await screen.findByRole('region', { name: 'Media' });
    const documents = screen.getByRole('region', { name: 'Documents' });

    expect(within(media).getByAltText('Craft Extended Abdeckplatte')).toBeInTheDocument();
    expect(within(documents).getByText('Technisches Datenblatt')).toBeInTheDocument();
    // The datasheet is an asset, not a picture: it must not get a tile.
    expect(within(media).queryByText('Technisches Datenblatt')).not.toBeInTheDocument();
  });

  it('shows the file name under a document that carries a separate title', async () => {
    installApi();
    renderTab();

    expect(await screen.findByText('craft-datenblatt.pdf')).toBeInTheDocument();
  });

  it('formats file size in the unit a steward would use', async () => {
    installApi();
    renderTab();

    expect(await screen.findByText('1.0 MB')).toBeInTheDocument();
  });

  it('says a size is unknown rather than printing zero', async () => {
    installApi({
      documents: [makeAsset({ linkId: 3, assetType: 'DOCUMENT', role: 'BIM', fileSize: null })],
    });
    renderTab();

    expect(await screen.findByText('—')).toBeInTheDocument();
  });

  it('warns that externally hosted previews may not load', async () => {
    // The client hosts the imagery. A blank tile on this network is expected,
    // and saying so up front stops it being reported as data loss.
    installApi();
    renderTab();

    expect(await screen.findByText(/hosted on an external server/)).toBeInTheDocument();
  });

  it('does not warn when every asset is managed by the PIM', async () => {
    installApi({
      media: [makeAsset({ isExternal: false, storageProvider: 'local' })],
      documents: [],
    });
    renderTab();

    await screen.findByRole('region', { name: 'Media' });
    expect(screen.queryByText(/hosted on an external server/)).not.toBeInTheDocument();
  });

  it('replaces a broken thumbnail with an explanation, not a broken-image icon', async () => {
    installApi();
    renderTab();

    const image = await screen.findByAltText('Craft Extended Abdeckplatte');
    fireEvent.error(image);

    const placeholder = await screen.findByRole('img', {
      name: 'craft-main.jpg — preview unavailable',
    });
    expect(placeholder).toHaveTextContent('Preview unavailable');
    expect(screen.queryByAltText('Craft Extended Abdeckplatte')).not.toBeInTheDocument();
  });

  it('offers "make primary" only on assets that are not already primary', async () => {
    installApi({
      media: [
        makeAsset({ linkId: 1, isPrimary: true }),
        makeAsset({ linkId: 2, role: 'GALLERY', isPrimary: false, fileName: 'craft-2.jpg' }),
      ],
    });
    renderTab();

    await screen.findByRole('region', { name: 'Media' });
    expect(screen.getAllByRole('button', { name: 'Make primary' })).toHaveLength(1);
  });

  it('promotes an asset to primary through the link, not the asset', async () => {
    // The same file can be linked to several products; primary is a property
    // of THIS product's link, so the id in the URL must be the link id.
    const api = installApi({
      media: [
        makeAsset({ linkId: 1, isPrimary: true }),
        makeAsset({ linkId: 77, role: 'GALLERY', isPrimary: false, fileName: 'craft-2.jpg' }),
      ],
    });
    renderTab();

    await userEvent.click(await screen.findByRole('button', { name: 'Make primary' }));

    await waitFor(() => expect(api.posts).toHaveLength(1));
    expect(api.posts[0].url).toContain('/assets/77/primary');
    expect(api.posts[0].body).toMatchObject({ rowVersion: 4 });
  });

  it('unlinks an asset and names it in the confirmation', async () => {
    const api = installApi();
    renderTab();

    const media = await screen.findByRole('region', { name: 'Media' });
    await userEvent.click(within(media).getByRole('button', { name: 'Unlink' }));

    await waitFor(() => expect(api.deletes).toHaveLength(1));
    expect(api.deletes[0]).toContain('/assets/1?rowVersion=4');
    expect(await screen.findByText('craft-main.jpg unlinked')).toBeInTheDocument();
  });

  it('surfaces a rejected write instead of silently leaving the tab stale', async () => {
    installApi({
      writeError: {
        status: 409,
        title: 'Conflict',
        detail: 'This product was changed by someone else. Reload and try again.',
      },
    });
    renderTab();

    const media = await screen.findByRole('region', { name: 'Media' });
    await userEvent.click(within(media).getByRole('button', { name: 'Unlink' }));

    expect(
      await screen.findByText('This product was changed by someone else. Reload and try again.'),
    ).toBeInTheDocument();
  });

  it('offers only image roles when adding an image', async () => {
    // MAIN on a PDF is refused server-side; not offering it is cheaper than
    // explaining the rejection.
    installApi();
    renderTab();

    await screen.findByRole('region', { name: 'Media' });
    await userEvent.click(screen.getByRole('button', { name: 'Add image' }));

    const role = screen.getByLabelText('Role') as HTMLSelectElement;
    expect([...role.options].map((o) => o.value)).toEqual(['MAIN', 'GALLERY']);
  });

  it('offers only document roles when adding a document', async () => {
    installApi();
    renderTab();

    await screen.findByRole('region', { name: 'Documents' });
    await userEvent.click(screen.getByRole('button', { name: 'Add document' }));

    const role = screen.getByLabelText('Role') as HTMLSelectElement;
    expect([...role.options].map((o) => o.value)).toEqual([
      'DATASHEET',
      'INSTALL',
      'DRAWING',
      'BIM',
      'VIDEO',
    ]);
  });

  it('asks for alt text on images only', async () => {
    installApi();
    renderTab();

    await screen.findByRole('region', { name: 'Media' });
    await userEvent.click(screen.getByRole('button', { name: 'Add image' }));
    expect(screen.getByLabelText(/Alt text/)).toBeInTheDocument();
  });

  it('sends the link request with the type left for the server to infer', async () => {
    // Extension-to-type inference lives in the domain (AssetRules). The form
    // posting a type would put a second, divergent copy of that rule here.
    const api = installApi();
    renderTab();

    await screen.findByRole('region', { name: 'Media' });
    await userEvent.click(screen.getByRole('button', { name: 'Add image' }));
    await userEvent.type(
      screen.getByLabelText(/File URL or storage key/),
      'https://assets.example.test/craft-3.jpg',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Link asset' }));

    await waitFor(() => expect(api.posts).toHaveLength(1));
    expect(api.posts[0].body).toMatchObject({
      storageKey: 'https://assets.example.test/craft-3.jpg',
      // Regression guard: the drawer opens as a document picker, so the role
      // select's options change when it is reopened as an image picker. An
      // uncontrolled select forgets defaultValue after mount and falls back to
      // its first option — which posted MAIN and made every second image fail.
      role: 'GALLERY',
      assetType: null,
      rowVersion: 4,
    });
  });

  it('defaults a document to DATASHEET even after the image drawer has been open', async () => {
    const api = installApi();
    renderTab();

    await screen.findByRole('region', { name: 'Media' });
    await userEvent.click(screen.getByRole('button', { name: 'Add image' }));
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await userEvent.click(screen.getByRole('button', { name: 'Add document' }));
    await userEvent.type(
      screen.getByLabelText(/File URL or storage key/),
      'https://assets.example.test/datenblatt-2.pdf',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Link asset' }));

    await waitFor(() => expect(api.posts).toHaveLength(1));
    expect(api.posts[0].body).toMatchObject({ role: 'DATASHEET' });
  });

  it('does not carry a previously typed URL into the next open', async () => {
    installApi();
    renderTab();

    await screen.findByRole('region', { name: 'Media' });
    await userEvent.click(screen.getByRole('button', { name: 'Add image' }));
    await userEvent.type(
      screen.getByLabelText(/File URL or storage key/),
      'https://assets.example.test/abandoned.jpg',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await userEvent.click(screen.getByRole('button', { name: 'Add image' }));

    expect(screen.getByLabelText(/File URL or storage key/)).toHaveValue('');
  });

  it('keeps the drawer open and shows why when the link is refused', async () => {
    installApi({
      writeError: {
        status: 409,
        title: 'Conflict',
        detail: 'This product already has a main image.',
      },
    });
    renderTab();

    await screen.findByRole('region', { name: 'Media' });
    await userEvent.click(screen.getByRole('button', { name: 'Add image' }));
    await userEvent.type(
      screen.getByLabelText(/File URL or storage key/),
      'https://assets.example.test/craft-3.jpg',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Link asset' }));

    expect(await screen.findByText('This product already has a main image.')).toBeInTheDocument();
    expect(screen.getByLabelText(/File URL or storage key/)).toBeInTheDocument();
  });

  it('reports the new rowVersion so the rest of the page does not go stale', async () => {
    const onChanged = vi.fn();
    installApi({ rowVersion: 9 });
    renderTab(onChanged);

    const media = await screen.findByRole('region', { name: 'Media' });
    await userEvent.click(within(media).getByRole('button', { name: 'Unlink' }));

    await waitFor(() => expect(onChanged).toHaveBeenCalledWith(9));
  });

  it('tells the steward what belongs here when there is nothing yet', async () => {
    installApi({ media: [], documents: [] });
    renderTab();

    expect(await screen.findByText('No images yet')).toBeInTheDocument();
    expect(screen.getByText('No documents yet')).toBeInTheDocument();
  });

  it('offers a retry when the tab cannot load', async () => {
    installApi({
      loadError: { status: 500, title: 'Error', detail: 'Could not reach the catalog service.' },
    });
    renderTab();

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Could not reach the catalog service.',
    );
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
