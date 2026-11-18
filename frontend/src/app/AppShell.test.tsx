import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { installFakeApi } from '../features/products/testApi';
import { AppShell } from './AppShell';
import { LocaleProvider } from './LocaleContext';
import { NAV_GROUPS } from './nav';

// The shell now fetches the locale list on mount. Without a stubbed fetch the
// provider swallows the failure and the selector stays hidden, which would let
// these tests pass while the header was broken.
beforeEach(() => {
  installFakeApi();
});

function renderShell(initialPath = '/') {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <LocaleProvider>
        <Routes>
          <Route element={<AppShell />}>
            <Route index element={<div>dashboard page</div>} />
            <Route path="products" element={<div>products page</div>} />
            <Route path="*" element={<div>other page</div>} />
          </Route>
        </Routes>
      </LocaleProvider>
    </MemoryRouter>,
  );
}

describe('AppShell', () => {
  it('renders all five v4.2 nav groups with their items', () => {
    renderShell();
    const nav = screen.getByRole('navigation', { name: 'Primary' });
    for (const group of NAV_GROUPS) {
      expect(within(nav).getByText(group.caption)).toBeInTheDocument();
      for (const item of group.items) {
        expect(within(nav).getByRole('link', { name: new RegExp(item.label) })).toBeInTheDocument();
      }
    }
  });

  it('marks the current route active and renders its page', () => {
    renderShell('/products');
    const link = screen.getByRole('link', { name: /Products/ });
    expect(link).toHaveClass('active');
    expect(screen.getByText('products page')).toBeInTheDocument();
  });

  it('shows the current page label in the breadcrumb', () => {
    renderShell('/products');
    const header = screen.getByRole('banner');
    expect(within(header).getByText('Products')).toBeInTheDocument();
  });

  it('toggles sidebar collapse', async () => {
    const user = userEvent.setup();
    const { container } = renderShell();
    const app = container.querySelector('.app');
    expect(app).not.toHaveClass('collapsed');
    await user.click(screen.getByRole('button', { name: 'Collapse navigation' }));
    expect(app).toHaveClass('collapsed');
    await user.click(screen.getByRole('button', { name: 'Expand navigation' }));
    expect(app).not.toHaveClass('collapsed');
  });
});
