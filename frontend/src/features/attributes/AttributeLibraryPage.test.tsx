import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LocaleProvider } from '../../app/LocaleContext';
import { AttributeLibraryPage } from './AttributeLibraryPage';
import type { AttributeDetail, AttributeLibrary } from './attributeLibraryApi';

const LOCALES = [
  { code: 'de-DE', name: 'Deutsch', isBase: true },
  { code: 'en-GB', name: 'English', isBase: false },
  { code: 'fr-FR', name: 'Français', isBase: false },
];

const LIBRARY: AttributeLibrary = {
  groups: [
    { code: 'technical', label: 'Technical data', labelLocale: 'en-GB', attributeCount: 2 },
    { code: 'surface', label: 'Surface', labelLocale: 'en-GB', attributeCount: 1 },
    // A group nobody has put anything in. Shown, not filtered away.
    { code: 'planning', label: 'Planning', labelLocale: 'en-GB', attributeCount: 0 },
  ],
  attributes: [
    {
      code: 'material_type',
      label: 'Material type',
      labelLocale: 'en-GB',
      groupCode: 'technical',
      groupLabel: 'Technical data',
      dataType: 'list',
      unit: null,
      isLocalizable: false,
      isVariantAxis: false,
      scope: 'PRODUCT',
      sourceAuthority: 'PIM',
      optionCount: 4,
      setCount: 1,
      requiredInAnySet: true,
      appliesToProducts: 500,
      productsWithValue: 480,
      coveragePct: 96,
    },
    {
      code: 'frost_resistance',
      label: 'Frostbeständigkeit',
      // Untranslated in the reading language.
      labelLocale: 'de-DE',
      groupCode: 'technical',
      groupLabel: 'Technical data',
      dataType: 'boolean',
      unit: null,
      isLocalizable: false,
      isVariantAxis: false,
      scope: 'PRODUCT',
      sourceAuthority: 'PIM',
      optionCount: 0,
      setCount: 1,
      requiredInAnySet: false,
      appliesToProducts: 500,
      productsWithValue: 100,
      coveragePct: 20,
    },
    {
      code: 'orphan_attr',
      label: 'Orphan attribute',
      labelLocale: 'en-GB',
      groupCode: 'surface',
      groupLabel: 'Surface',
      dataType: 'text',
      unit: null,
      isLocalizable: true,
      isVariantAxis: false,
      scope: 'PRODUCT',
      sourceAuthority: 'PIM',
      optionCount: 0,
      setCount: 0,
      requiredInAnySet: false,
      // In no set, so no product can carry it: there is nothing to measure.
      appliesToProducts: 0,
      productsWithValue: 0,
      coveragePct: 0,
    },
  ],
  totalAttributes: 3,
};

const DETAIL: AttributeDetail = {
  code: 'material_type',
  label: 'Material type',
  labelLocale: 'en-GB',
  groupCode: 'technical',
  groupLabel: 'Technical data',
  dataType: 'list',
  unit: null,
  unitCode: null,
  isLocalizable: false,
  isVariantAxis: false,
  scope: 'PRODUCT',
  sourceAuthority: 'PIM',
  validationJson: null,
  appliesToProducts: 500,
  productsWithValue: 480,
  coveragePct: 96,
  labels: [
    { localeCode: 'de-DE', localeName: 'Deutsch', isBase: true, label: 'Materialart', helpText: null, placeholder: null },
    { localeCode: 'en-GB', localeName: 'English', isBase: false, label: 'Material type', helpText: null, placeholder: null },
    { localeCode: 'fr-FR', localeName: 'Français', isBase: false, label: null, helpText: null, placeholder: null },
  ],
  options: [
    { code: 'porcelain', label: 'Porcelain stoneware', labelLocale: 'en-GB', sortOrder: 1, usedByProducts: 300 },
    { code: 'unused_opt', label: 'Terracotta', labelLocale: 'en-GB', sortOrder: 2, usedByProducts: 0 },
  ],
  sets: [{ setCode: 'tile_core', setName: 'Keramik – Kernattribute', isRequired: true, sortOrder: 1, productCount: 500 }],
};

function mockApi(overrides: { detail?: unknown; detailStatus?: number } = {}) {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/api/v1/locales')) {
        return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(LOCALES) });
      }
      // Order matters: the detail route is a prefix match on the list route.
      if (/\/api\/v1\/attributes\/[^?]/.test(url)) {
        const status = overrides.detailStatus ?? 200;
        return Promise.resolve({
          ok: status < 400,
          status,
          json: () => Promise.resolve(overrides.detail ?? DETAIL),
        });
      }
      if (url.includes('/api/v1/attributes')) {
        return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(LIBRARY) });
      }
      return Promise.reject(new Error(`unexpected fetch: ${url}`));
    }),
  );
}

function renderPage(initialEntry = '/attributes') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <LocaleProvider>
        <AttributeLibraryPage />
      </LocaleProvider>
    </MemoryRouter>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe('AttributeLibraryPage', () => {
  it('lists every attribute with its group and type', async () => {
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByText('Material type')).toBeInTheDocument());
    expect(screen.getByText('Orphan attribute')).toBeInTheDocument();
    expect(screen.getByText('3 of 3')).toBeInTheDocument();
  });

  it('says an attribute in no set has nothing to measure rather than showing 0%', async () => {
    // 0% would accuse somebody of not filling in a field that has never been on
    // anyone's screen. The two states look identical as a bar.
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByText('Orphan attribute')).toBeInTheDocument());
    expect(screen.getByText("not in any product's set")).toBeInTheDocument();
    expect(
      screen.getByLabelText('Material type: 480 of 500 products'),
    ).toBeInTheDocument();
  });

  it('shows an empty group instead of hiding it', async () => {
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByText('Planning')).toBeInTheDocument());
  });

  it('filters by group, by type and by search together', async () => {
    mockApi();
    renderPage('/attributes?group=technical');

    await waitFor(() => expect(screen.getByText('Material type')).toBeInTheDocument());
    expect(screen.queryByText('Orphan attribute')).not.toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText('Filter by type'), 'boolean');
    expect(screen.queryByText('Material type')).not.toBeInTheDocument();
    expect(screen.getByText('Frostbeständigkeit')).toBeInTheDocument();
  });

  it('marks a label that is not in the reading language', async () => {
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByText('Frostbeständigkeit')).toBeInTheDocument());
    expect(screen.getAllByTitle(/showing de-DE/).length).toBeGreaterThan(0);
  });

  it('opens the detail panel and names the languages with no label', async () => {
    mockApi();
    renderPage('/attributes?attribute=material_type');

    await waitFor(() => expect(screen.getByText('Text per language')).toBeInTheDocument());
    expect(screen.getByText('Materialart')).toBeInTheDocument();
    expect(screen.getByText('Not translated')).toBeInTheDocument();
  });

  it('flags an option no product has ever chosen', async () => {
    mockApi();
    renderPage('/attributes?attribute=material_type');

    await waitFor(() => expect(screen.getByText('Terracotta')).toBeInTheDocument());
    expect(screen.getByText('never used')).toBeInTheDocument();
    expect(screen.getByText('300 products')).toBeInTheDocument();
  });

  it('states when an attribute belongs to no set at all', async () => {
    mockApi({ detail: { ...DETAIL, sets: [], appliesToProducts: 0, productsWithValue: 0, coveragePct: 0 } });
    renderPage('/attributes?attribute=material_type');

    await waitFor(() => expect(screen.getByText('Not in any attribute set')).toBeInTheDocument());
  });

  it('reports a failed detail load instead of an empty panel', async () => {
    mockApi({ detailStatus: 404, detail: { title: 'Attribute not found' } });
    renderPage('/attributes?attribute=nope');

    await waitFor(() =>
      expect(screen.getByText('This attribute could not be loaded')).toBeInTheDocument(),
    );
  });

  it('keeps the coverage denominator visible in the accessible label', async () => {
    // The percentage alone is unreadable without knowing what it is a
    // percentage OF — 20% of 500 and 20% of 4 are different problems.
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByText('Frostbeständigkeit')).toBeInTheDocument());
    const row = screen.getByText('Frostbeständigkeit').closest('tr')!;
    expect(within(row).getByLabelText(/100 of 500 products/)).toBeInTheDocument();
  });
});
