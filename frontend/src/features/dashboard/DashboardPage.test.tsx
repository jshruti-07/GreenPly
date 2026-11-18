import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LocaleProvider } from '../../app/LocaleContext';
import { DashboardPage } from './DashboardPage';
import type { ProductCockpit } from './dashboardApi';

const cockpit: ProductCockpit = {
  kpis: [
    { code: 'total_products', label: 'Total products', value: '500', caption: '3,625 SKUs' },
    { code: 'avg_completeness', label: 'Avg completeness', value: '87%', caption: null },
    { code: 'pending_translation', label: 'Pending translation', value: '120', caption: null },
    { code: 'ready_to_publish', label: 'Ready to publish', value: '41', caption: null },
  ],
  qualityBands: [
    { code: 'A', label: 'A — Excellent', productCount: 310, percent: 62 },
    { code: 'B', label: 'B — Good', productCount: 105, percent: 21 },
    { code: 'C', label: 'C — Needs work', productCount: 55, percent: 11 },
    { code: 'D', label: 'D — Incomplete', productCount: 30, percent: 6 },
  ],
  completenessByArea: [
    { groupCode: 'technical', label: 'Technical data', percent: 96 },
    { groupCode: 'surface', label: 'Surface', percent: 71 },
  ],
  // The domain's four states (WorkflowState: Draft, Review, Approval,
  // Published), spelt as the products grid spells them. The fixture used to
  // carry three, including an "Approved" the domain never produces — a fixture
  // agreeing with a defect rather than with the system.
  workflowPipeline: [
    { state: 'Draft', label: 'Draft', productCount: 125 },
    { state: 'Review', label: 'In review', productCount: 125 },
    { state: 'Approval', label: 'In approval', productCount: 125 },
    { state: 'Published', label: 'Published', productCount: 125 },
  ],
  localeCoverage: [
    { code: 'de-DE', name: 'Deutsch', isBase: true, translatedProducts: 500, totalProducts: 500, percent: 100 },
    { code: 'fr-FR', name: 'Français', isBase: false, translatedProducts: 400, totalProducts: 500, percent: 80 },
  ],
  recentActivity: [
    {
      occurredAtUtc: '2026-08-18T09:00:00Z',
      actor: null,
      action: 'updated',
      productCode: 'AB-ALCINA-1',
      detail: 'Attributes saved',
    },
  ],
  omittedPanels: [
    { panel: 'Channel errors', reason: 'No channel service exists, so a count here would be invented.' },
  ],
};

/** The locale list LocaleProvider fetches on mount. */
const LOCALES = [
  { code: 'de-DE', name: 'Deutsch', isBase: true },
  { code: 'en-GB', name: 'English (UK)', isBase: false },
  { code: 'fr-FR', name: 'Fran\u00e7ais', isBase: false },
];

/**
 * Routed by URL, not blanket.
 *
 * A single mockResolvedValue answers EVERY request with the same body, so
 * LocaleProvider -- which fetches /api/v1/locales on mount -- received a
 * cockpit object and died on `locales.some is not a function`, taking the whole
 * tree with it. The five failures that produced all looked like the dashboard
 * was broken; none of them were about the dashboard.
 */
function mockFetch(dashboardBody: unknown, dashboardOk = true) {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();

      if (url.includes('/api/v1/locales')) {
        return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(LOCALES) });
      }

      return Promise.resolve({
        ok: dashboardOk,
        status: dashboardOk ? 200 : 500,
        json: () => Promise.resolve(dashboardBody),
      });
    }),
  );
}

function renderPage() {
  return render(
    <MemoryRouter>
      <LocaleProvider>
        <DashboardPage />
      </LocaleProvider>
    </MemoryRouter>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe('DashboardPage', () => {
  it('renders every KPI the API returns rather than a fixed four', async () => {
    // Fixed markup for four cards would silently drop a fifth KPI, or render an
    // empty card if one were removed. The row follows the payload.
    mockFetch(cockpit);
    renderPage();

    await waitFor(() => expect(screen.getByTestId('kpi-total_products')).toBeInTheDocument());
    expect(screen.getByTestId('kpi-avg_completeness')).toHaveTextContent('87%');
    expect(screen.getByTestId('kpi-ready_to_publish')).toHaveTextContent('41');
  });

  it('shows all four quality bands including ones with few products', async () => {
    mockFetch(cockpit);
    renderPage();

    await waitFor(() => expect(screen.getByText('A — Excellent')).toBeInTheDocument());
    expect(screen.getByText('D — Incomplete')).toBeInTheDocument();
  });

  it('states why a panel is missing instead of leaving a gap', async () => {
    // The whole point of the omissions panel: an absent chart reads as "zero"
    // unless something says otherwise, and zero channel errors is the opposite
    // of no channel service.
    mockFetch(cockpit);
    renderPage();

    await waitFor(() => expect(screen.getByText('Not shown yet')).toBeInTheDocument());
    expect(screen.getByText('Channel errors')).toBeInTheDocument();
    expect(screen.getByText(/no channel service exists/i)).toBeInTheDocument();
  });

  it('renders a null actor as Unknown rather than inventing a name', async () => {
    mockFetch(cockpit);
    renderPage();

    await waitFor(() => expect(screen.getByText('Unknown')).toBeInTheDocument());
  });

  it('marks the base locale and shows partial coverage honestly', async () => {
    mockFetch(cockpit);
    renderPage();

    await waitFor(() => expect(screen.getByText(/Deutsch \(base\)/)).toBeInTheDocument());
    expect(screen.getByLabelText(/Français: 400 \/ 500/)).toBeInTheDocument();
  });

  it('renders every workflow stage the API returns, not a fixed three', async () => {
    // The pipeline is read as a partition of the catalogue. Rendering a subset
    // makes the remaining stages look like the whole, and nothing on screen
    // says otherwise — which is exactly how a four-state domain was showing
    // 251 of 501 products before this was checked.
    mockFetch(cockpit);
    renderPage();

    await waitFor(() => expect(screen.getByText('Draft')).toBeInTheDocument());
    expect(screen.getByText('In approval')).toBeInTheDocument();
    expect(screen.getByText('Published')).toBeInTheDocument();
  });

  it('says the dashboard failed rather than rendering zeroes', async () => {
    // A failed load that painted an empty cockpit would read as an empty
    // catalogue — the single most misleading thing this screen could do.
    mockFetch({ title: 'Server error' }, false);
    renderPage();

    await waitFor(() =>
      expect(screen.getByText('The dashboard could not be loaded')).toBeInTheDocument(),
    );
    expect(screen.queryByTestId('kpi-total_products')).not.toBeInTheDocument();
  });
});
