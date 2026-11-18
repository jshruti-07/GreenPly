import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { ToastProvider } from '../../components/Toast';
import { ClassificationsTab } from './ClassificationsTab';
import type {
  ClassificationFeature,
  ProductClassificationScheme,
  ProductClassifications,
} from './types';

function makeFeature(overrides: Partial<ClassificationFeature> = {}): ClassificationFeature {
  return {
    code: 'EF000008',
    name: 'Fire resistance class',
    dataType: 'alpha',
    unit: null,
    isRequired: true,
    value: '90 min',
    ...overrides,
  };
}

function makeScheme(
  overrides: Partial<ProductClassificationScheme> = {},
): ProductClassificationScheme {
  return {
    schemeCode: 'ETIM',
    schemeName: 'ETIM 9.0',
    schemeVersion: '9.0',
    purpose: 'Trade partner exchange',
    isPrimary: true,
    mappedStatus: 'partial',
    classCode: 'EC010225',
    classLabel: 'Fire-resistant door',
    featureCount: 2,
    featureFilled: 1,
    availableClasses: [
      { code: 'EC010225', label: 'Fire-resistant door' },
      { code: 'EC010226', label: 'Acoustic door' },
    ],
    features: [makeFeature(), makeFeature({ code: 'EF000234', name: 'Smoke control', value: null })],
    ...overrides,
  };
}

interface Api {
  puts: Array<{ url: string; body: Record<string, unknown> }>;
}

function installApi(
  options: {
    schemes?: ProductClassificationScheme[];
    rowVersion?: number;
    writeError?: { status: number; title: string; detail: string };
    loadError?: { status: number; title: string; detail: string };
  } = {},
): Api {
  const puts: Api['puts'] = [];
  const rowVersion = options.rowVersion ?? 4;

  const payload = (): ProductClassifications => ({
    productCode: 'FD-90-GS-1000',
    rowVersion,
    schemes: options.schemes ?? [makeScheme()],
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

      puts.push({ url, body: JSON.parse(String(init?.body)) as Record<string, unknown> });
      return ok(payload());
    }),
  );

  return { puts };
}

function renderTab() {
  return render(
    <ToastProvider>
      <ClassificationsTab code="FD-90-GS-1000" />
    </ToastProvider>,
  );
}

describe('ClassificationsTab', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('lists each standard with its assigned class', async () => {
    installApi();
    renderTab();

    expect(await screen.findByText('ETIM 9.0')).toBeInTheDocument();
    expect(screen.getByText('EC010225')).toBeInTheDocument();
    expect(screen.getByText(/Fire-resistant door/)).toBeInTheDocument();
  });

  it('shows feature completeness as filled over declared', async () => {
    installApi();
    renderTab();

    expect(await screen.findByText('1 / 2 features')).toBeInTheDocument();
  });

  it('shows a dash for schemes whose class declares no features', async () => {
    // DataNorm and UNSPSC are a class assignment and nothing more.
    installApi({
      schemes: [
        makeScheme({
          schemeCode: 'UNSPSC',
          schemeName: 'UNSPSC',
          featureCount: 0,
          featureFilled: 0,
          features: [],
          mappedStatus: 'mapped',
        }),
      ],
    });
    renderTab();

    expect(await screen.findByText('—')).toBeInTheDocument();
  });

  it('does not offer feature editing for a scheme with no features', async () => {
    installApi({
      schemes: [makeScheme({ featureCount: 0, featureFilled: 0, features: [] })],
    });
    renderTab();

    await screen.findByText('ETIM 9.0');
    expect(screen.queryByRole('button', { name: 'Edit features' })).not.toBeInTheDocument();
  });

  it('marks an unmapped standard as a gap rather than a neutral fact', async () => {
    installApi({
      schemes: [
        makeScheme({ mappedStatus: 'mapped' }),
        makeScheme({ schemeCode: 'BMECAT', schemeName: 'BMEcat', mappedStatus: 'partial' }),
      ],
    });
    renderTab();

    expect(await screen.findByText('Mapped')).toHaveClass('s-ok');
    expect(screen.getByText('Partial')).toHaveClass('s-warn');
  });

  it('shows unfilled features as missing in the value table', async () => {
    installApi();
    renderTab();

    await screen.findByText('ETIM 9.0');
    expect(screen.getByText('Smoke control')).toBeInTheDocument();
    expect(screen.getAllByText('Missing').length).toBeGreaterThan(0);
    expect(screen.getByText('1 missing')).toBeInTheDocument();
  });

  it('assigns a class with the loaded rowVersion', async () => {
    const user = userEvent.setup();
    const api = installApi({ rowVersion: 9 });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Edit mapping' }));
    await user.selectOptions(screen.getByLabelText(/Class/), 'EC010226');
    await user.click(screen.getByRole('button', { name: 'Save mapping' }));

    await waitFor(() => expect(api.puts).toHaveLength(1));
    expect(api.puts[0].url).toContain('/classifications/ETIM');
    expect(api.puts[0].body).toMatchObject({ classCode: 'EC010226', rowVersion: 9 });
  });

  it('sends null when the mapping is cleared', async () => {
    const user = userEvent.setup();
    const api = installApi();
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Edit mapping' }));
    await user.selectOptions(screen.getByLabelText(/Class/), '');
    await user.click(screen.getByRole('button', { name: 'Save mapping' }));

    await waitFor(() => expect(api.puts).toHaveLength(1));
    expect(api.puts[0].body.classCode).toBeNull();
  });

  it('warns that changing the class discards values the new class does not declare', async () => {
    const user = userEvent.setup();
    installApi();
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Edit mapping' }));

    expect(screen.getByText(/discards feature values/i)).toBeInTheDocument();
  });

  it('saves every feature, sending null for the ones left blank', async () => {
    const user = userEvent.setup();
    const api = installApi();
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Edit features' }));
    await user.click(screen.getByRole('button', { name: 'Save features' }));

    await waitFor(() => expect(api.puts).toHaveLength(1));
    expect(api.puts[0].url).toContain('/classifications/ETIM/features');
    expect(api.puts[0].body.values).toEqual([
      { featureCode: 'EF000008', value: '90 min' },
      { featureCode: 'EF000234', value: null },
    ]);
  });

  it('surfaces a rejected feature value without closing the drawer', async () => {
    const user = userEvent.setup();
    installApi({
      writeError: {
        status: 400,
        title: 'Classification.InvalidValue',
        detail: "Value for 'EF000056' is not a valid numeric.",
      },
    });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Edit features' }));
    await user.click(screen.getByRole('button', { name: 'Save features' }));

    const alerts = await screen.findAllByRole('alert');
    expect(alerts.some((a) => a.textContent?.includes('not a valid numeric'))).toBe(true);
    expect(screen.getByRole('button', { name: 'Save features' })).toBeInTheDocument();
  });

  it('shows an error with retry when classifications cannot be loaded', async () => {
    installApi({
      loadError: { status: 404, title: 'Product.NotFound', detail: "Product 'X' was not found." },
    });
    renderTab();

    expect(await screen.findByRole('alert')).toHaveTextContent("Product 'X' was not found.");
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  it('hints at the expected shape for each feature data type', async () => {
    const user = userEvent.setup();
    installApi({
      schemes: [
        makeScheme({
          features: [
            makeFeature({ code: 'EF000056', name: 'Width', dataType: 'numeric', unit: 'mm' }),
          ],
          featureCount: 1,
          featureFilled: 1,
        }),
      ],
    });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Edit features' }));

    const input = within(screen.getByRole('dialog')).getByLabelText(/Width/);
    expect(input).toHaveAttribute('placeholder', expect.stringContaining('mm'));
  });
});
