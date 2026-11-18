import { Fragment, useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { ErrorBoundary } from '../components/ErrorBoundary';
import { Icon } from './Icon';
import { LocaleSelector } from './LocaleSelector';
import { NAV_GROUPS } from './nav';

function useCurrentPageLabel(): string {
  const { pathname } = useLocation();
  for (const group of NAV_GROUPS) {
    const hit = group.items.find((item) =>
      item.path === '/' ? pathname === '/' : pathname.startsWith(item.path),
    );
    if (hit) return hit.label;
  }
  return 'PIM';
}

export function AppShell() {
  const [collapsed, setCollapsed] = useState(false);
  const pageLabel = useCurrentPageLabel();

  return (
    <div className={collapsed ? 'app collapsed' : 'app'}>
      <aside className="side">
        <div className="brand">
          <div className="logo" aria-hidden="true">
            GP
          </div>
          <div className="brand-tx">
            <b>GreenPly</b>
            <span>Product Information</span>
          </div>
        </div>
        <nav className="navwrap" aria-label="Primary">
          {NAV_GROUPS.map((group) => (
            <Fragment key={group.caption}>
              <div className="navcap">{group.caption}</div>
              {group.items.map((item) => (
                <NavLink
                  key={item.id}
                  to={item.path}
                  end={item.path === '/'}
                  data-tip={item.label}
                  className={({ isActive }) => (isActive ? 'nav active' : 'nav')}
                >
                  <Icon name={item.icon} />
                  <span className="lbl">{item.label}</span>
                  {item.count && <span className="cnt">{item.count}</span>}
                </NavLink>
              ))}
            </Fragment>
          ))}
        </nav>
        <div className="side-foot">
          <button
            type="button"
            className="collapse-btn"
            onClick={() => setCollapsed((c) => !c)}
            aria-expanded={!collapsed}
            aria-label={collapsed ? 'Expand navigation' : 'Collapse navigation'}
          >
            <Icon name="chevL" />
            <span className="lbl">Collapse</span>
          </button>
        </div>
      </aside>

      <main className="main">
        <header className="top">
          <div className="crumb">
            <span>GreenPly</span>
            <span className="sep">/</span>
            <b>{pageLabel}</b>
          </div>
          <div className="searchtrig" role="button" tabIndex={0} aria-label="Search (coming soon)">
            <svg className="ic sm" viewBox="0 0 24 24" aria-hidden="true">
              <circle cx="11" cy="11" r="7" />
              <path d="M21 21l-4.3-4.3" />
            </svg>
            <span>Search products, attributes…</span>
            <span className="kbd">⌘K</span>
          </div>
          <LocaleSelector />
          <div className="me">
            <div className="av" aria-hidden="true">
              SK
            </div>
            <div className="me-tx">
              <b>shailesh</b>
              <br />
              <span>Administrator</span>
            </div>
          </div>
        </header>
        <div className="canvas">
          <div className="pad">
            {/* One failing screen must never blank the shell (incident 2026-08-14). */}
            <ErrorBoundary area={pageLabel}>
              <Outlet />
            </ErrorBoundary>
          </div>
        </div>
      </main>
    </div>
  );
}
