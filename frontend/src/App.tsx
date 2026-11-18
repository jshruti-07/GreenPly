import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { AppShell } from './app/AppShell';
import { LocaleProvider } from './app/LocaleContext';
import { PAGE_TITLES } from './app/nav';
import { PlaceholderPage } from './app/PlaceholderPage';
import { ToastProvider } from './components/Toast';
import { ProductDetailPage } from './features/products/ProductDetailPage';
import { AttributeLibraryPage } from './features/attributes/AttributeLibraryPage';
import { AssetLibraryPage } from './features/assets/AssetLibraryPage';
import { CategoriesPage } from './features/categories/CategoriesPage';
import { TranslationPage } from './features/translation/TranslationPage';
import { DashboardPage } from './features/dashboard/DashboardPage';
import { WorkflowsPage } from './features/workflow/WorkflowsPage';
import { ProductsGrid } from './features/products/ProductsGrid';
import { SystemsPage } from './features/systems/SystemsPage';

const PLACEHOLDER_ROUTES = [
  'content',
  'quality',
  'channels',
  'classifications',
  'integrations',
  'users',
  'organisation',
] as const;

export function App() {
  return (
    <BrowserRouter>
      {/* Outside the router: the display language is a workstation preference,
          not route state, and must survive navigation without refetching. */}
      <LocaleProvider>
        <ToastProvider>
          <Routes>
            <Route element={<AppShell />}>
              <Route index element={<DashboardPage />} />
              <Route path="products" element={<ProductsGrid />} />
              <Route path="products/:code" element={<ProductDetailPage />} />
              <Route path="taxonomy" element={<CategoriesPage />} />
              <Route path="systems" element={<SystemsPage />} />
              <Route path="attributes" element={<AttributeLibraryPage />} />
              <Route path="translation" element={<TranslationPage />} />
              <Route path="assets" element={<AssetLibraryPage />} />
              <Route path="workflows" element={<WorkflowsPage />} />
              {PLACEHOLDER_ROUTES.map((id) => (
                <Route key={id} path={id} element={<PlaceholderPage title={PAGE_TITLES[id]} />} />
              ))}
              <Route
                path="*"
                element={<PlaceholderPage title="Page not found" plannedFor="— check the URL" />}
              />
            </Route>
          </Routes>
        </ToastProvider>
      </LocaleProvider>
    </BrowserRouter>
  );
}
