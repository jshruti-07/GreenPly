import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { ToastProvider } from '../../components/Toast';
import { PlanningTab } from './PlanningTab';
import type { BimReference, GeometryFile, PlanningParameter, ProductPlanning } from './types';

const LEAD = 'AB-CRAFT-EXTENDED-COVER-PLATE-31-X-30-CM';

function parameter(overrides: Partial<PlanningParameter> = {}): PlanningParameter {
  return {
    code: 'mfg_width_mm',
    label: 'Herstellmaß Breite',
    value: '300',
    unitSymbol: 'mm',
    dataType: 'measure',
    ...overrides,
  };
}

function reference(overrides: Partial<BimReference> = {}): BimReference {
  return {
    schemeCode: 'IFC',
    schemeName: 'IFC 4.3',
    schemeVersion: '4.3',
    classCode: 'IfcCovering.CLADDING',
    classLabel: 'IfcCovering / CLADDING',
    ...overrides,
  };
}

function geometry(overrides: Partial<GeometryFile> = {}): GeometryFile {
  return {
    linkId: 9,
    role: 'BIM',
    assetType: 'BIM',
    fileName: 'craft-bim.ifc',
    fileSize: 2_097_152,
    url: 'local://docs/craft/bim',
    title: 'BIM-Objekt',
    isExternal: false,
    ...overrides,
  };
}

function installApi(
  options: {
    payload?: Partial<ProductPlanning>;
    loadError?: { status: number; title: string; detail: string };
  } = {},
) {
  const body = (): ProductPlanning => ({
    productCode: LEAD,
    rowVersion: 3,
    bimReady: true,
    parameterCount: 2,
    parameterFilledCount: 1,
    parameters: [parameter(), parameter({ code: 'lrv', label: 'Hellbezugswert Y', value: null, unitSymbol: null, dataType: 'decimal' })],
    bimReferences: [reference()],
    geometryFiles: [geometry()],
    ...options.payload,
  });

  vi.stubGlobal(
    'fetch',
    vi.fn(async () =>
      options.loadError
        ? new Response(JSON.stringify(options.loadError), {
            status: options.loadError.status,
            headers: { 'Content-Type': 'application/problem+json' },
          })
        : new Response(JSON.stringify(body()), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
    ),
  );
}

function renderTab(onNavigateToTab?: (tabId: string) => void) {
  return render(
    <ToastProvider>
      <PlanningTab code={LEAD} onNavigateToTab={onNavigateToTab} />
    </ToastProvider>,
  );
}

describe('PlanningTab', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('reports BIM readiness from the product data, not from whether files exist', async () => {
    installApi();
    renderTab();

    expect(await screen.findByText('BIM object available')).toBeInTheDocument();
  });

  it('distinguishes "no BIM object" from "not recorded"', async () => {
    // A missing flag and a false flag mean different things to a planner: one
    // is a data gap, the other is an answer.
    installApi({ payload: { bimReady: null } });
    renderTab();

    expect(await screen.findByText('Not recorded')).toBeInTheDocument();
  });

  it('shows a parameter with its unit and its code', async () => {
    installApi();
    renderTab();

    const parameters = await screen.findByRole('region', { name: 'Planning parameters' });
    expect(within(parameters).getByText('Herstellmaß Breite')).toBeInTheDocument();
    expect(within(parameters).getByText('mfg_width_mm')).toBeInTheDocument();
    expect(within(parameters).getByText('300')).toBeInTheDocument();
  });

  it('marks an unfilled parameter rather than showing a blank cell', async () => {
    installApi();
    renderTab();

    const parameters = await screen.findByRole('region', { name: 'Planning parameters' });
    expect(within(parameters).getByText('Not set')).toBeInTheDocument();
  });

  it('renders a boolean parameter as a word, not as true/false', async () => {
    installApi({
      payload: {
        parameters: [
          parameter({
            code: 'bim_ready',
            label: 'BIM-Objekt verfügbar',
            value: 'true',
            unitSymbol: null,
            dataType: 'boolean',
          }),
        ],
        parameterCount: 1,
        parameterFilledCount: 1,
      },
    });
    renderTab();

    const parameters = await screen.findByRole('region', { name: 'Planning parameters' });
    expect(within(parameters).getByText('Yes')).toBeInTheDocument();
    expect(within(parameters).queryByText('true')).not.toBeInTheDocument();
  });

  it('lists an unassigned BIM scheme so the gap is visible', async () => {
    installApi({
      payload: {
        bimReferences: [
          reference(),
          reference({
            schemeCode: 'UNICLASS',
            schemeName: 'Uniclass 2015',
            schemeVersion: '2015',
            classCode: null,
            classLabel: null,
          }),
        ],
      },
    });
    renderTab();

    const references = await screen.findByRole('region', {
      name: 'BIM classification references',
    });
    expect(within(references).getByText('Unassigned')).toBeInTheDocument();
    expect(within(references).getByText('1 of 2 assigned')).toBeInTheDocument();
  });

  it('lists geometry files with their format', async () => {
    installApi();
    renderTab();

    const files = await screen.findByRole('region', { name: 'Geometry and planning files' });
    expect(within(files).getByText('BIM-Objekt')).toBeInTheDocument();
    expect(within(files).getByText('craft-bim.ifc')).toBeInTheDocument();
    expect(within(files).getByText('2.0 MB')).toBeInTheDocument();
  });

  it('sends the steward to the tab that owns each value', async () => {
    // The tab is read-only on purpose. If it did not say where to make a
    // change, "read-only" would just read as broken.
    const onNavigate = vi.fn();
    installApi();
    renderTab(onNavigate);

    await userEvent.click(await screen.findByRole('button', { name: 'Edit on Attributes' }));
    expect(onNavigate).toHaveBeenCalledWith('attributes');

    await userEvent.click(screen.getByRole('button', { name: 'Edit on Classifications' }));
    expect(onNavigate).toHaveBeenCalledWith('classifications');

    await userEvent.click(
      screen.getByRole('button', { name: 'Manage in Media & Documents' }),
    );
    expect(onNavigate).toHaveBeenCalledWith('assets');
  });

  it('offers no publish action, because there is no channel service', async () => {
    installApi();
    renderTab();

    await screen.findByRole('region', { name: 'BIM readiness' });
    expect(screen.queryByRole('button', { name: /publish/i })).not.toBeInTheDocument();
    expect(screen.getByText(/needs the channel service/)).toBeInTheDocument();
  });

  it('explains an empty geometry list instead of showing an empty table', async () => {
    installApi({ payload: { geometryFiles: [] } });
    renderTab();

    expect(await screen.findByText('No geometry files')).toBeInTheDocument();
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
