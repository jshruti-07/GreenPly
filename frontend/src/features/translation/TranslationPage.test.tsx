import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LocaleProvider } from '../../app/LocaleContext';
import { TranslationPage } from './TranslationPage';
import type { TranslationQueue, TranslationWorkbench } from './translationApi';

const LOCALES = [
  { code: 'de-DE', name: 'Deutsch', isBase: true },
  { code: 'en-GB', name: 'English', isBase: false },
  { code: 'fr-FR', name: 'Français', isBase: false },
];

const WORKBENCH: TranslationWorkbench = {
  baseLocaleCode: 'de-DE',
  baseLocaleName: 'Deutsch',
  totalProducts: 500,
  locales: [
    {
      code: 'de-DE', name: 'Deutsch', isBase: true, totalProducts: 500,
      withText: 500, missing: 0, outdated: 0, awaitingReview: 0, approved: 500, percentTranslated: 100,
    },
    {
      code: 'en-GB', name: 'English', isBase: false, totalProducts: 500,
      withText: 500, missing: 0, outdated: 3, awaitingReview: 125, approved: 375, percentTranslated: 100,
    },
    {
      code: 'fr-FR', name: 'Français', isBase: false, totalProducts: 500,
      withText: 400, missing: 100, outdated: 0, awaitingReview: 0, approved: 400, percentTranslated: 80,
    },
  ],
  omittedPanels: [
    { panel: 'Run AI batch', reason: 'There is no machine-translation service wired to this build.' },
  ],
};

const QUEUE: TranslationQueue = {
  targetLocale: 'en-GB',
  state: 'attention',
  page: 1,
  pageSize: 25,
  totalCount: 2,
  items: [
    {
      productCode: 'AB-ALCINA-1',
      name: 'Alcina floor tile',
      nameLocale: 'en-GB',
      category: 'Tiles',
      state: 'missing',
      translationStatus: null,
      source: null,
      targetUpdatedAtUtc: null,
      baseUpdatedAtUtc: '2026-08-18T09:00:00Z',
    },
    {
      productCode: 'AB-CRAFT-2',
      name: 'Craft cover plate',
      nameLocale: 'en-GB',
      category: 'Accessories',
      state: 'outdated',
      // Approved AND outdated: the row is fine, the source moved under it.
      translationStatus: 'approved',
      source: 'human',
      targetUpdatedAtUtc: '2026-08-10T09:00:00Z',
      baseUpdatedAtUtc: '2026-08-18T09:00:00Z',
    },
  ],
};

function mockApi(overrides: { queue?: unknown; queueStatus?: number; workbench?: unknown } = {}) {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const url = String(input);

      if (url.includes('/api/v1/locales')) {
        return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(LOCALES) });
      }
      // Order matters: the queue route is a prefix match on the workbench route.
      if (url.includes('/api/v1/translations/queue')) {
        const status = overrides.queueStatus ?? 200;
        return Promise.resolve({
          ok: status < 400,
          status,
          json: () => Promise.resolve(overrides.queue ?? QUEUE),
        });
      }
      if (url.includes('/api/v1/translations')) {
        return Promise.resolve({
          ok: true, status: 200,
          json: () => Promise.resolve(overrides.workbench ?? WORKBENCH),
        });
      }
      return Promise.reject(new Error(`unexpected fetch: ${url}`));
    }),
  );
}

function renderPage(initialEntry = '/translation') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <LocaleProvider>
        <TranslationPage />
      </LocaleProvider>
    </MemoryRouter>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe('TranslationPage', () => {
  it('breaks each language down instead of showing only a percentage', async () => {
    // 80% with 100 missing is a different job from 80% awaiting review, and the
    // bar alone cannot tell them apart.
    mockApi();
    renderPage();

    // Queried by the bar's accessible label, not by the language name: the name
    // also appears as an <option> in the target select, and getByText would
    // match both.
    await waitFor(() =>
      expect(screen.getByLabelText('Français: 400 of 500 products have text')).toBeInTheDocument(),
    );
    expect(screen.getByText(/100 missing · 0 outdated · 0 awaiting review/)).toBeInTheDocument();
  });

  it('marks the base language as the source rather than as 100% translated', async () => {
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByText('Deutsch')).toBeInTheDocument());
    expect(screen.getByText('source')).toBeInTheDocument();
    expect(screen.getByText('Source language — nothing to translate into it')).toBeInTheDocument();
  });

  it('defaults the target to the first non-base language from the payload', async () => {
    // Not hard-coded: a catalogue whose second language is Spanish must not open
    // on a French tab showing nothing.
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByLabelText('Target language')).toBeInTheDocument());
    expect(screen.getByLabelText('Target language')).toHaveValue('en-GB');
    expect(screen.queryByRole('option', { name: 'Deutsch' })).not.toBeInTheDocument();
  });

  it('distinguishes missing from outdated, and keeps the stored status of an outdated row', async () => {
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByText('Alcina floor tile')).toBeInTheDocument());
    expect(screen.getByText('missing')).toBeInTheDocument();
    expect(screen.getByText('outdated')).toBeInTheDocument();
    // A row can be approved and outdated at once. Collapsing the two would hide
    // which problem it actually is.
    expect(screen.getByText(/was approved/)).toBeInTheDocument();
  });

  it('links each product to the tab where the translation is written', async () => {
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByText('Alcina floor tile')).toBeInTheDocument());
    expect(screen.getByRole('link', { name: 'Alcina floor tile' })).toHaveAttribute(
      'href',
      '/products/AB-ALCINA-1?tab=localization',
    );
  });

  it('says an empty queue is good news rather than leaving a blank table', async () => {
    // The same table, empty because of a bad filter, looks identical.
    mockApi({ queue: { ...QUEUE, items: [], totalCount: 0 } });
    renderPage();

    await waitFor(() => expect(screen.getByText('Nothing to do in English')).toBeInTheDocument());
    expect(
      screen.getByText('Every product has approved, current text in this language.'),
    ).toBeInTheDocument();
  });

  it('sends the chosen filter to the API and resets to page 1', async () => {
    mockApi();
    renderPage('/translation?page=3');

    await waitFor(() => expect(screen.getByLabelText('Queue filter')).toBeInTheDocument());
    await userEvent.selectOptions(screen.getByLabelText('Queue filter'), 'missing');

    await waitFor(() => {
      const calls = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls.map((c) => String(c[0]));
      const last = calls.filter((u) => u.includes('/translations/queue')).at(-1)!;
      expect(last).toContain('state=missing');
      expect(last).toContain('page=1');
    });
  });

  it('states why the AI panels are absent instead of leaving a gap', async () => {
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByText('Not shown yet')).toBeInTheDocument());
    expect(screen.getByText('Run AI batch')).toBeInTheDocument();
    expect(screen.getByText(/no machine-translation service/)).toBeInTheDocument();
  });

  it('reports a failed queue load rather than an empty one', async () => {
    // An empty queue means "nothing left to translate". A failed one must never
    // be able to say that.
    mockApi({ queueStatus: 500, queue: { title: 'Server error' } });
    renderPage();

    await waitFor(() =>
      expect(screen.getByText('The queue could not be loaded')).toBeInTheDocument(),
    );
    expect(screen.queryByText('Nothing to do in English')).not.toBeInTheDocument();
  });

  it('pages only when there is more than one page', async () => {
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByText('Alcina floor tile')).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'Next' })).not.toBeInTheDocument();

    vi.unstubAllGlobals();
    mockApi({ queue: { ...QUEUE, totalCount: 60 } });
    renderPage();

    await waitFor(() => expect(screen.getAllByRole('button', { name: 'Next' }).length).toBeGreaterThan(0));
  });

  it('marks a name shown in a language other than the one requested', async () => {
    mockApi({
      queue: {
        ...QUEUE,
        items: [{ ...QUEUE.items[0], name: 'Alcina Bodenfliese', nameLocale: 'de-DE' }],
      },
    });
    renderPage();

    await waitFor(() => expect(screen.getByText('Alcina Bodenfliese')).toBeInTheDocument());
    const row = screen.getByText('Alcina Bodenfliese').closest('tr')!;
    expect(within(row).getByTitle(/showing de-DE/)).toBeInTheDocument();
  });
});
