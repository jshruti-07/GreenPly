import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LocaleProvider } from '../../app/LocaleContext';
import { CategoriesPage } from './CategoriesPage';
import type { CategoryDetail, CategoryTreeNode } from './categoriesApi';

const LOCALES = [
  { code: 'de-DE', name: 'Deutsch', isBase: true },
  { code: 'en-GB', name: 'English', isBase: false },
  { code: 'fr-FR', name: 'Français', isBase: false },
];

function node(partial: Partial<CategoryTreeNode> & { code: string; name: string }): CategoryTreeNode {
  return {
    id: 1,
    nameLocale: 'en-GB',
    nodeType: 'CATEGORY',
    depth: 0,
    status: 'active',
    directProductCount: 0,
    subtreeProductCount: 0,
    children: [],
    ...partial,
  };
}

const TREE: CategoryTreeNode[] = [
  node({
    id: 1,
    code: 'buildcore',
    name: 'BuildCore',
    nodeType: 'BRAND',
    depth: 0,
    directProductCount: 0,
    subtreeProductCount: 300,
    children: [
      node({
        id: 2,
        code: 'fire-doors',
        name: 'Fire Doors',
        nodeType: 'FAMILY',
        depth: 1,
        directProductCount: 0,
        subtreeProductCount: 300,
        children: [
          node({
            id: 3,
            code: 'steel-fire-doors',
            name: 'Steel Fire Doors',
            nodeType: 'CATEGORY',
            depth: 2,
            directProductCount: 300,
            subtreeProductCount: 300,
          }),
        ],
      }),
    ],
  }),
  node({
    id: 4,
    code: 'glas-trennwaende',
    name: 'Glas-Trennwände',
    // Untranslated in the requested language: the tree still shows it.
    nameLocale: 'de-DE',
    nodeType: 'FAMILY',
    depth: 0,
    directProductCount: 12,
    subtreeProductCount: 12,
  }),
];

const DETAIL: CategoryDetail = {
  id: 3,
  code: 'steel-fire-doors',
  name: 'Steel Fire Doors',
  nameLocale: 'en-GB',
  description: null,
  nodeType: 'CATEGORY',
  depth: 2,
  status: 'active',
  parentCode: 'fire-doors',
  parentName: 'Fire Doors',
  brandName: 'BuildCore',
  directProductCount: 300,
  subtreeProductCount: 300,
  attributeSetCode: 'std90',
  attributeSetName: 'Standard 90 Set',
  requiredAttributeCount: 1,
  templateSource: 'in-use',
  setsInUse: [{ code: 'std90', name: 'Standard 90 Set', productCount: 300 }],
  template: [
    {
      code: 'fire_rating',
      label: 'Fire Resistance Rating',
      labelLocale: 'en-GB',
      groupCode: 'performance',
      groupLabel: 'Performance',
      dataType: 'number',
      isRequired: true,
      coveragePct: 82,
      productsInScope: 300,
    },
    {
      code: 'smoke_control',
      label: 'Smoke Control',
      labelLocale: 'en-GB',
      groupCode: 'performance',
      groupLabel: 'Performance',
      dataType: 'list',
      isRequired: false,
      coveragePct: 0,
      productsInScope: 0,
    },
  ],
  labels: [
    { localeCode: 'de-DE', localeName: 'Deutsch', isBase: true, name: 'Stahl-Brandschutztüren', translationStatus: 'approved' },
    { localeCode: 'en-GB', localeName: 'English', isBase: false, name: 'Steel Fire Doors', translationStatus: 'approved' },
    { localeCode: 'fr-FR', localeName: 'Français', isBase: false, name: null, translationStatus: 'missing' },
  ],
};

/**
 * Routes by URL rather than answering everything with one body.
 *
 * LocaleProvider fetches /api/v1/locales as the tree mounts. A blanket mock
 * hands it the category tree, `locales.some` explodes, and every test in the
 * file fails for a reason that has nothing to do with what it was checking —
 * which is exactly what happened to the dashboard suite in Sprint 21.
 */
function mockApi(overrides: { detail?: unknown; detailStatus?: number } = {}) {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const url = String(input);

      if (url.includes('/api/v1/locales')) {
        return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(LOCALES) });
      }
      if (url.includes('/api/v1/categories/tree')) {
        return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(TREE) });
      }
      if (url.includes('/api/v1/categories/')) {
        const status = overrides.detailStatus ?? 200;
        return Promise.resolve({
          ok: status < 400,
          status,
          json: () => Promise.resolve(overrides.detail ?? DETAIL),
        });
      }
      return Promise.reject(new Error(`unexpected fetch: ${url}`));
    }),
  );
}

function renderPage(initialEntry = '/taxonomy') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <LocaleProvider>
        <CategoriesPage />
      </LocaleProvider>
    </MemoryRouter>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe('CategoriesPage', () => {
  it('renders the hierarchy with roots expanded and deeper levels reachable', async () => {
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByText('BuildCore')).toBeInTheDocument());
    // Root's children are visible because roots start expanded...
    expect(screen.getByText('Fire Doors')).toBeInTheDocument();
    // ...but the grandchild is behind its own collapsed parent.
    expect(screen.queryByText('Steel Fire Doors')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /Expand Fire Doors/ }));
    expect(screen.getByText('Steel Fire Doors')).toBeInTheDocument();
  });

  it('shows a node that has no translation in the reading language rather than hiding it', async () => {
    // The taxonomy is the same taxonomy in every language. A tree that dropped
    // untranslated nodes would silently shrink, and the products beneath them
    // would become unreachable with nothing on screen to say why.
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByText('Glas-Trennwände')).toBeInTheDocument());
    expect(screen.getAllByTitle(/showing de-DE/).length).toBeGreaterThan(0);
  });

  it('loads the detail panel for the node in the URL', async () => {
    mockApi();
    renderPage('/taxonomy?node=steel-fire-doors');

    await waitFor(() => expect(screen.getByText('Standard 90 Set', { exact: false })).toBeInTheDocument());
    expect(screen.getByText('Fire Resistance Rating')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /View 300 products/ })).toHaveAttribute(
      'href',
      '/products?categoryId=3',
    );
  });

  it('says "no products to measure" instead of drawing a 0% bar', async () => {
    // 0% coverage over 0 products is not "nobody filled this in" — it is
    // "there is nothing here to fill in". The two look identical as a bar.
    mockApi();
    renderPage('/taxonomy?node=steel-fire-doors');

    await waitFor(() => expect(screen.getByText('Smoke Control')).toBeInTheDocument());
    expect(screen.getByText('no products to measure')).toBeInTheDocument();
    expect(screen.getByLabelText('Coverage 82%')).toBeInTheDocument();
  });

  it('names the languages with no label rather than leaving the cell blank', async () => {
    mockApi();
    renderPage('/taxonomy?node=steel-fire-doors');

    await waitFor(() => expect(screen.getByText('Stahl-Brandschutztüren')).toBeInTheDocument());
    expect(screen.getByText('Not translated')).toBeInTheDocument();
  });

  it('keeps the ancestors of a filter match so the path stays readable', async () => {
    mockApi();
    renderPage();

    await waitFor(() => expect(screen.getByText('BuildCore')).toBeInTheDocument());
    await userEvent.type(screen.getByLabelText('Filter categories'), 'steel');

    expect(screen.getByText('Steel Fire Doors')).toBeInTheDocument();
    expect(screen.getByText('BuildCore')).toBeInTheDocument();
    expect(screen.getByText('Fire Doors')).toBeInTheDocument();
    expect(screen.queryByText('Glas-Trennwände')).not.toBeInTheDocument();
  });

  it('says the template was inferred rather than configured', async () => {
    // No node in the client's catalogue declares an attribute set, so every
    // template on this screen is inferred from the products beneath. Presented
    // as configured, it would look like a rule somebody set — and would change
    // silently the day a product's set changes.
    mockApi();
    renderPage('/taxonomy?node=steel-fire-doors');

    await waitFor(() => expect(screen.getByText('inferred from its products')).toBeInTheDocument());
  });

  it('refuses to merge templates when the branch uses more than one set', async () => {
    mockApi({
      detail: {
        ...DETAIL,
        templateSource: 'mixed',
        template: [],
        setsInUse: [
          { code: 'std90', name: 'Standard 90 Set', productCount: 200 },
          { code: 'glass', name: 'Glass Partition Set', productCount: 100 },
        ],
      },
    });
    renderPage('/taxonomy?node=steel-fire-doors');

    await waitFor(() =>
      expect(screen.getByText('No single template applies here')).toBeInTheDocument(),
    );
    expect(screen.getByText(/Standard 90 Set — 200 products/)).toBeInTheDocument();
    expect(screen.getByText(/Glass Partition Set — 100 products/)).toBeInTheDocument();
  });

  it('reports a failed detail load instead of showing an empty category', async () => {
    mockApi({ detailStatus: 404, detail: { title: 'Category not found' } });
    renderPage('/taxonomy?node=nope');

    await waitFor(() =>
      expect(screen.getByText('This category could not be loaded')).toBeInTheDocument(),
    );
  });
});
