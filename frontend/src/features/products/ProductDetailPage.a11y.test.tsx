import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { vi } from 'vitest';
import { ToastProvider } from '../../components/Toast';
import { ProductDetailPage, TABS } from './ProductDetailPage';
import { makeWorkflow } from './testApi';

/**
 * Keyboard and ARIA behaviour of the detail tablist.
 *
 * These are pinned because the tablist declares role="tab", and a control that
 * announces itself as a tab but does not behave like one is worse for a screen
 * reader user than a plain button would have been.
 */
function installApi() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);

      const json = (body: unknown) =>
        new Response(JSON.stringify(body), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });

      if (url.includes('/workflow')) {
        return json(makeWorkflow());
      }

      if (url.includes('/activity')) {
        return json({
          productCode: 'FD-90-GS-1000',
          availableActions: [],
          entries: { items: [], page: 1, pageSize: 25, totalCount: 0 },
        });
      }

      // Every tab's payload shape differs; each tab renders its own empty state
      // from these, which is all this suite needs.
      if (url.includes('/attributes')) {
        return json({
          productCode: 'FD-90-GS-1000',
          baseLocale: 'de-DE',
          completenessPct: 0,
          requiredCount: 0,
          requiredFilledCount: 0,
          rowVersion: 1,
          groups: [],
        });
      }
      if (url.includes('/variants')) {
        return json({ productCode: 'FD-90-GS-1000', rowVersion: 1, skus: [] });
      }
      if (url.includes('/classifications')) {
        return json({ productCode: 'FD-90-GS-1000', rowVersion: 1, schemes: [] });
      }
      if (url.includes('/compliance')) {
        return json({
          productCode: 'FD-90-GS-1000',
          rowVersion: 1,
          expiringCount: 0,
          expiredCount: 0,
          documents: [],
          indicators: [],
          materials: [],
          substances: [],
          greenBuilding: [],
        });
      }
      if (url.includes('/packaging')) {
        return json({ productCode: 'FD-90-GS-1000', rowVersion: 1, skus: [] });
      }
      if (url.includes('/relationships')) {
        return json({ productCode: 'FD-90-GS-1000', rowVersion: 1, groups: [] });
      }
      if (url.includes('/assets')) {
        return json({
          productCode: 'FD-90-GS-1000',
          rowVersion: 1,
          media: [],
          documents: [],
        });
      }
      if (url.includes('/applications')) {
        return json({ productCode: 'FD-90-GS-1000', rowVersion: 1, groups: [] });
      }
      if (url.includes('/planning')) {
        return json({
          productCode: 'FD-90-GS-1000',
          rowVersion: 1,
          bimReady: null,
          parameterCount: 0,
          parameterFilledCount: 0,
          parameters: [],
          bimReferences: [],
          geometryFiles: [],
        });
      }
      if (url.includes('/localization')) {
        return json({
          productCode: 'FD-90-GS-1000',
          baseLocale: 'de-DE',
          rowVersion: 1,
          pendingLocaleCount: 0,
          locales: [],
        });
      }

      return json({
        code: 'FD-90-GS-1000',
        publicId: '00000000-0000-0000-0000-000000000001',
        name: 'Fire Door 90',
        ownerDisplayName: 'Anna Becker',
        gtin: null,
        sapMaterialNo: null,
        lifecycleState: 'Active',
        workflowState: 'Draft',
        baseLocale: 'de-DE',
        createdAtUtc: '2026-01-01T00:00:00Z',
        updatedAtUtc: '2026-01-01T00:00:00Z',
        rowVersion: 1,
      });
    }),
  );
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/products/FD-90-GS-1000']}>
      <ToastProvider>
        <Routes>
          <Route path="/products/:code" element={<ProductDetailPage />} />
        </Routes>
      </ToastProvider>
    </MemoryRouter>,
  );
}

describe('ProductDetailPage — tablist accessibility', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('exposes every declared tab as a tab', async () => {
    installApi();
    renderPage();

    await screen.findByRole('tablist');
    expect(screen.getAllByRole('tab')).toHaveLength(TABS.length);
  });

  it('is a single tab stop — only the selected tab is reachable by Tab', async () => {
    // Roving tabindex. Without it a keyboard user presses Tab nine times to
    // get past the tablist to the content.
    installApi();
    renderPage();

    await screen.findByRole('tablist');
    const tabs = screen.getAllByRole('tab');
    const focusable = tabs.filter((t) => t.getAttribute('tabindex') === '0');

    expect(focusable).toHaveLength(1);
    expect(focusable[0]).toHaveAccessibleName('General');
  });

  it('moves selection with the right arrow key', async () => {
    const user = userEvent.setup();
    installApi();
    renderPage();

    await screen.findByRole('tablist');
    const general = screen.getByRole('tab', { name: 'General' });
    general.focus();
    await user.keyboard('{ArrowRight}');

    expect(screen.getByRole('tab', { name: 'Attributes' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
  });

  it('wraps from the last tab to the first', async () => {
    const user = userEvent.setup();
    installApi();
    renderPage();

    await screen.findByRole('tablist');
    screen.getByRole('tab', { name: 'General' }).focus();
    await user.keyboard('{ArrowLeft}');

    expect(screen.getByRole('tab', { name: 'Activity' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
  });

  it('jumps to the first and last tab with Home and End', async () => {
    const user = userEvent.setup();
    installApi();
    renderPage();

    await screen.findByRole('tablist');
    screen.getByRole('tab', { name: 'General' }).focus();

    await user.keyboard('{End}');
    expect(screen.getByRole('tab', { name: 'Activity' })).toHaveAttribute('aria-selected', 'true');

    await user.keyboard('{Home}');
    expect(screen.getByRole('tab', { name: 'General' })).toHaveAttribute('aria-selected', 'true');
  });

  it('moves DOM focus with the selection, not just the highlight', async () => {
    const user = userEvent.setup();
    installApi();
    renderPage();

    await screen.findByRole('tablist');
    screen.getByRole('tab', { name: 'General' }).focus();
    await user.keyboard('{ArrowRight}');

    await waitFor(() =>
      expect(screen.getByRole('tab', { name: 'Attributes' })).toHaveFocus(),
    );
  });

  it('points each tab at the panel it controls', async () => {
    installApi();
    renderPage();

    await screen.findByRole('tablist');
    const panel = screen.getByRole('tabpanel');
    const selected = screen.getByRole('tab', { selected: true });

    expect(selected).toHaveAttribute('aria-controls', panel.id);
    expect(panel).toHaveAttribute('aria-labelledby', selected.id);
  });

  it('keeps exactly one tab selected as the panel changes', async () => {
    const user = userEvent.setup();
    installApi();
    renderPage();

    await screen.findByRole('tablist');
    await user.click(screen.getByRole('tab', { name: 'Packaging' }));

    const selected = screen.getAllByRole('tab').filter(
      (t) => t.getAttribute('aria-selected') === 'true',
    );
    expect(selected).toHaveLength(1);
    expect(selected[0]).toHaveAccessibleName('Packaging');
  });

  it('scrolls a keyboard-selected tab into view', async () => {
    // The strip overflows at twelve tabs. Without this, End focused a tab whose
    // focus ring was rendered outside the viewport — the user's position was
    // announced but invisible.
    const scrollIntoView = vi
      .spyOn(HTMLElement.prototype, 'scrollIntoView')
      .mockImplementation(() => {});

    const user = userEvent.setup();
    installApi();
    renderPage();

    await screen.findByRole('tablist');
    screen.getByRole('tab', { name: 'General' }).focus();
    await user.keyboard('{End}');

    expect(scrollIntoView).toHaveBeenCalledWith({ block: 'nearest', inline: 'nearest' });
    scrollIntoView.mockRestore();
  });

  it('shows only the active tab panel — the General panel must not leak below others', async () => {
    // Regression guard. The General panel used the `hidden` attribute, which
    // `.grid { display: grid }` overrides, so it rendered underneath every
    // other tab from Sprint 5 to Sprint 10 without any test noticing.
    const user = userEvent.setup();
    installApi();
    renderPage();

    await screen.findByRole('tablist');
    expect(screen.getByText('Record')).toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: 'Activity' }));

    expect(screen.queryByText('Record')).not.toBeInTheDocument();
  });
});
