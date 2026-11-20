import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LocaleProvider } from '../../app/LocaleContext';
import { AssetLibraryPage } from './AssetLibraryPage';
import { formatBytes, type AssetDetail, type AssetLibrarySummary, type AssetPage } from './assetLibraryApi';

/** An asset this service stores itself, as opposed to one imported by key. */
const LOCAL_IMAGE = {
  publicId: '33333333-3333-4333-8333-333333333333',
  fileName: 'uploaded.png',
  title: 'Uploaded tile',
  titleLocale: 'en-GB',
  assetType: 'IMAGE',
  mimeType: 'image/png',
  fileSizeBytes: 125_046,
  width: 1200,
  height: 900,
  storageProvider: 'local',
  status: 'active',
  servedRendition: 'web',
  url: 'https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcSdF2OC_VL3cXreQ5sMIhJQttCjFvYjr57dqM57vTeCObdzaTHFUlkwhls&s=10',
  linkCount: 0,
  usageRights: null,
  rightsExpiry: null,
};

const LOCALES = [
  { code: 'de-DE', name: 'Deutsch', isBase: true },
  { code: 'en-GB', name: 'English', isBase: false },
  { code: 'fr-FR', name: 'Français', isBase: false },
];

const SUMMARY: AssetLibrarySummary = {
  totalAssets: 2169,
  totalBytes: 4_294_967_296,
  byType: [
    { assetType: 'IMAGE', count: 1969, totalBytes: 4_000_000_000 },
    { assetType: 'BIM', count: 80, totalBytes: 200_000_000 },
    { assetType: 'CAD', count: 40, totalBytes: 80_000_000 },
    { assetType: 'DOCUMENT', count: 80, totalBytes: 14_967_296 },
  ],
  orphanCount: 12,
  productsWithoutMainImage: 7,
  rightsExpiringSoon: 3,
  rightsExpired: 1,
  omittedPanels: [
    // Upload and renditions left this list in Sprint 26 because they shipped.
    // Channel delivery has not: there are no channel tables in the schema.
    {
      panel: 'Channel delivery',
      reason: 'There is no channel service and no channel tables in this schema, so there is nowhere for an asset to be delivered to.',
    },
  ],
};

const PAGE: AssetPage = {
  page: 1,
  pageSize: 24,
  totalCount: 2,
  items: [
    {
      publicId: '11111111-1111-4111-8111-111111111111',
      fileName: '415003.jpg',
      title: 'Alcina floor tile',
      titleLocale: 'en-GB',
      assetType: 'IMAGE',
      mimeType: 'image/jpeg',
      fileSizeBytes: 2_400_000,
      width: 2000,
      height: 1500,
      storageProvider: 'client-magento',
      status: 'active',
      servedRendition: 'original',
      url: 'https://example.invalid/415003.jpg',
      linkCount: 3,
      usageRights: 'Owned',
      rightsExpiry: null,
    },
    {
      publicId: '22222222-2222-4222-8222-222222222222',
      fileName: 'tile_90.rvt',
      title: null,
      titleLocale: null,
      assetType: 'BIM',
      mimeType: 'application/octet-stream',
      fileSizeBytes: 18_000_000,
      width: null,
      height: null,
      storageProvider: 'client-magento',
      status: 'active',
      servedRendition: 'original',
      url: 'https://example.invalid/tile_90.rvt',
      linkCount: 0,
      usageRights: null,
      rightsExpiry: null,
    },
  ],
};

const DETAIL: AssetDetail = {
  publicId: '11111111-1111-4111-8111-111111111111',
  fileName: '415003.jpg',
  title: 'Alcina floor tile',
  titleLocale: 'en-GB',
  assetType: 'IMAGE',
  mimeType: 'image/jpeg',
  fileSizeBytes: 2_400_000,
  width: 2000,
  height: 1500,
  storageProvider: 'client-magento',
  storageKey: 'https://example.invalid/415003.jpg',
  checksum: null,
  status: 'active',
  usageRights: 'Owned',
  rightsExpiry: null,
  localeCode: null,
  createdAtUtc: '2026-08-16T09:00:00Z',
  renditions: [
    { role: 'original', uri: 'https://example.invalid/415003.jpg', width: 2000, height: 1500, format: 'jpg' },
  ],
  texts: [
    { localeCode: 'de-DE', localeName: 'Deutsch', isBase: true, title: 'Alcina Bodenfliese', altText: 'Fliese', caption: null },
    { localeCode: 'en-GB', localeName: 'English', isBase: false, title: 'Alcina floor tile', altText: null, caption: null },
    { localeCode: 'fr-FR', localeName: 'Français', isBase: false, title: null, altText: null, caption: null },
  ],
  usedBy: [
    { productCode: 'AB-ALCINA-1', productName: 'Alcina 30x60', productNameLocale: 'en-GB', role: 'MAIN', isPrimary: true },
  ],
};

function mockApi(overrides: { page?: unknown; pageStatus?: number; detail?: unknown } = {}) {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/api/v1/locales')) {
        return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(LOCALES) });
      }
      if (url.includes('/api/v1/assets/summary')) {
        return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(SUMMARY) });
      }
      // A GUID path segment is the detail route; anything else is the list.
      if (/\/api\/v1\/assets\/[0-9a-f-]{36}/.test(url)) {
        return Promise.resolve({
          ok: true, status: 200, json: () => Promise.resolve(overrides.detail ?? DETAIL),
        });
      }
      if (url.includes('/api/v1/assets')) {
        const status = overrides.pageStatus ?? 200;
        return Promise.resolve({
          ok: status < 400, status, json: () => Promise.resolve(overrides.page ?? PAGE),
        });
      }
      return Promise.reject(new Error(`unexpected fetch: ${url}`));
    }),
  );
}

function renderPage(initialEntry = '/assets') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <LocaleProvider>
        <AssetLibraryPage />
      </LocaleProvider>
    </MemoryRouter>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe('formatBytes', () => {
  it('reads as a size rather than as a number of bytes', () => {
    expect(formatBytes(null)).toBe('—');
    expect(formatBytes(0)).toBe('—');
    expect(formatBytes(900)).toBe('900 B');
    expect(formatBytes(2_400_000)).toBe('2.3 MB');
    expect(formatBytes(4_294_967_296)).toBe('4.0 GB');
  });
});

describe('AssetLibraryPage', () => {
  it('surfaces the two figures no per-product screen can answer', async () => {
    // An orphaned asset and a product with no main image are questions about
    // the whole set. Neither is visible from a product's Media tab.
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByTestId('asset-kpi-Assets')).toBeInTheDocument());
    expect(screen.getByTestId('asset-kpi-Not linked to anything')).toHaveTextContent('12');
    expect(screen.getByTestId('asset-kpi-Products with no main image')).toHaveTextContent('7');
  });

  it('says how many rights have already expired, not just how many will', async () => {
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByTestId('asset-kpi-Rights expiring')).toBeInTheDocument());
    expect(screen.getByTestId('asset-kpi-Rights expiring')).toHaveTextContent('1 already expired');
  });

  it('shows BIM and CAD as first-class types with their own counts', async () => {
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByRole('group', { name: 'Asset type' })).toBeInTheDocument());
    const facets = screen.getByRole('group', { name: 'Asset type' });
    expect(within(facets).getByRole('button', { name: /BIM 80/ })).toBeInTheDocument();
    expect(within(facets).getByRole('button', { name: /CAD 40/ })).toBeInTheDocument();
  });

  it('marks an asset that no product links to', async () => {
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByText('tile_90.rvt')).toBeInTheDocument());
    expect(screen.getByTitle('Not linked to any product')).toBeInTheDocument();
  });

  it('falls back to the file name when an asset has no title', async () => {
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByText('tile_90.rvt')).toBeInTheDocument());
  });

  it('states that no web rendition exists rather than implying one does', async () => {
    // The seed used to claim a 'web' rendition per image whose URI was the
    // original. A consumer that believes that will put a 2.3 MB file behind a
    // storefront thumbnail.
    mockApi();
    renderPage('/assets?asset=11111111-1111-4111-8111-111111111111');

    await waitFor(() => expect(screen.getByText('Renditions')).toBeInTheDocument());
    expect(screen.getByText(/No web or thumbnail rendition exists/)).toBeInTheDocument();
    expect(screen.getByText('original')).toBeInTheDocument();
  });

  it('names the languages with no title and no alt text', async () => {
    mockApi();
    renderPage('/assets?asset=11111111-1111-4111-8111-111111111111');

    await waitFor(() => expect(screen.getByText('Alcina Bodenfliese')).toBeInTheDocument());
    expect(screen.getByText('Not translated')).toBeInTheDocument();
    // Missing alt text is an accessibility gap, not an empty cell.
    expect(screen.getAllByText('No alt text').length).toBe(2);
  });

  it('links usage to the product tab that owns the link', async () => {
    mockApi();
    renderPage('/assets?asset=11111111-1111-4111-8111-111111111111');

    await waitFor(() => expect(screen.getByText('Used by', { exact: false })).toBeInTheDocument());
    expect(screen.getByRole('link', { name: 'Alcina 30x60' })).toHaveAttribute(
      'href',
      '/products/AB-ALCINA-1?tab=assets',
    );
  });

  it('sends the usage filter to the API and resets the page', async () => {
    mockApi();
    renderPage('/assets?page=4');

    await waitFor(() => expect(screen.getByLabelText('Usage filter')).toBeInTheDocument());
    await userEvent.selectOptions(screen.getByLabelText('Usage filter'), 'orphan');

    await waitFor(() => {
      const calls = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls.map((c) => String(c[0]));
      const last = calls.filter((u) => u.includes('/api/v1/assets?')).at(-1)!;
      expect(last).toContain('usage=orphan');
      expect(last).toContain('page=1');
    });
  });

  it('explains what the library cannot do yet instead of leaving gaps', async () => {
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByText('Not shown yet')).toBeInTheDocument());
    expect(screen.getByText('Channel delivery')).toBeInTheDocument();
    expect(screen.getByText(/nowhere for an asset to be delivered to/)).toBeInTheDocument();

    // And what HAS shipped must not be listed as missing. "Upload" appears
    // exactly once on this screen now — as the button that does it.
    expect(screen.getAllByText('Upload').length).toBe(1);
  });

  it('renders a real thumbnail only for assets this service stores', async () => {
    // Imported assets point at full-size originals in the client's media
    // system. Twenty-four of those in a grid is tens of megabytes to draw
    // 168px squares — the exact mistake the fake 'web' rendition invited.
    mockApi({ page: { ...PAGE, items: [...PAGE.items, LOCAL_IMAGE] } });
    renderPage();

    await waitFor(() => expect(screen.getByText('Uploaded tile')).toBeInTheDocument());

    const images = document.querySelectorAll('.aprev img');
    expect(images.length).toBe(1);
    expect(images[0].getAttribute('src')).toBe(
      '/api/v1/assets/33333333-3333-4333-8333-333333333333/file?rendition=thumb',
    );
  });

  it('says a re-uploaded file was already in the library rather than looking inert', async () => {
    // De-duplication is a success. Presented as nothing happening, a user
    // uploads the file again, and again.
    mockApi();
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input);
        if (init?.method === 'POST') {
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () => Promise.resolve({ publicId: DETAIL.publicId, deduplicated: true }),
          });
        }
        if (url.includes('/api/v1/locales')) {
          return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(LOCALES) });
        }
        if (url.includes('/api/v1/assets/summary')) {
          return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(SUMMARY) });
        }
        if (/\/api\/v1\/assets\/[0-9a-f-]{36}/.test(url)) {
          return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(DETAIL) });
        }
        return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(PAGE) });
      }),
    );

    renderPage();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Upload' })).toBeInTheDocument());

    const file = new File([new Uint8Array([1, 2, 3])], 'tile.png', { type: 'image/png' });
    await userEvent.upload(screen.getByLabelText('File to upload'), file);

    await waitFor(() =>
      expect(screen.getByRole('status')).toHaveTextContent(/already in the library/),
    );
  });

  it('surfaces an upload failure instead of failing silently', async () => {
    mockApi();
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input);
        if (init?.method === 'POST') {
          return Promise.resolve({
            ok: false,
            status: 400,
            json: () => Promise.resolve({ detail: "'.txt' is not accepted for IMAGE." }),
          });
        }
        if (url.includes('/api/v1/locales')) {
          return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(LOCALES) });
        }
        if (url.includes('/api/v1/assets/summary')) {
          return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(SUMMARY) });
        }
        return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(PAGE) });
      }),
    );

    renderPage();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Upload' })).toBeInTheDocument());

    const file = new File([new Uint8Array([1])], 'notes.txt', { type: 'text/plain' });
    await userEvent.upload(screen.getByLabelText('File to upload'), file);

    await waitFor(() =>
      expect(screen.getByRole('status')).toHaveTextContent(/not accepted for IMAGE/),
    );
  });

  it('reports a failed load rather than an empty library', async () => {
    mockApi({ pageStatus: 500, page: { title: 'Server error' } });
    renderPage();

    await waitFor(() =>
      expect(screen.getByText('These assets could not be loaded')).toBeInTheDocument(),
    );
    expect(screen.queryByText('No asset matches those filters')).not.toBeInTheDocument();
  });
});
