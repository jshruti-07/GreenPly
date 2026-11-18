import type { IconName } from './Icon';

/* Navigation model ported 1:1 from PIM Prototype v4.2.html NAV constant.
   Note: the delivery plan said 4 groups; v4.2 defines 5 (prototype wins, ADR-006).
   The prototype carried static count badges (Products 1,248, Systems 48,
   Translation 34, Workflows 12). They were harmless against synthetic demo
   data, but with the client's own catalogue loaded the sidebar said 1,248
   while the page beneath it said "500 products in the catalogue" — the first
   thing anyone notices, and it makes every other number on screen suspect.
   Removed rather than faked. They come back per item when the service behind
   each one exists and can supply a real figure. */

export interface NavGroup {
  caption: string;
  items: NavItem[];
}

export interface NavItem {
  id: string;
  path: string;
  icon: IconName;
  label: string;
  count?: string;
}

export const NAV_GROUPS: ReadonlyArray<NavGroup> = [
  {
    caption: 'Workspace',
    items: [
      { id: 'dashboard', path: '/', icon: 'dashboard', label: 'Dashboard' },
      { id: 'products', path: '/products', icon: 'products', label: 'Products' },
      { id: 'systems', path: '/systems', icon: 'systems', label: 'Systems & Assemblies' },
      { id: 'taxonomy', path: '/taxonomy', icon: 'taxonomy', label: 'Categories' },
    ],
  },
  {
    caption: 'Enrichment',
    items: [
      { id: 'translation', path: '/translation', icon: 'translation', label: 'Translation' },
      { id: 'assets', path: '/assets', icon: 'assets', label: 'Digital Assets' },
      { id: 'content', path: '/content', icon: 'content', label: 'Content Components' },
      { id: 'quality', path: '/quality', icon: 'quality', label: 'Data Quality' },
    ],
  },
  {
    caption: 'Distribution',
    items: [
      { id: 'channels', path: '/channels', icon: 'channels', label: 'Channels' },
      { id: 'workflows', path: '/workflows', icon: 'workflows', label: 'Workflows' },
    ],
  },
  {
    caption: 'Configure',
    items: [
      { id: 'attributes', path: '/attributes', icon: 'attributes', label: 'Attribute Library' },
      { id: 'classifications', path: '/classifications', icon: 'classification', label: 'Classifications' },
    ],
  },
  {
    caption: 'Administration',
    items: [
      { id: 'integrations', path: '/integrations', icon: 'plug', label: 'Integrations' },
      { id: 'users', path: '/users', icon: 'users', label: 'Users & Roles' },
      { id: 'organisation', path: '/organisation', icon: 'org', label: 'Organisation' },
    ],
  },
];

/** Page titles per v4.2 PAGE_TITLE map. */
export const PAGE_TITLES: Record<string, string> = {
  dashboard: 'Dashboard',
  products: 'Products',
  systems: 'Systems & Assemblies',
  taxonomy: 'Categories',
  translation: 'Translation Workbench',
  assets: 'Digital Assets',
  content: 'Content Components',
  quality: 'Data Quality',
  channels: 'Channels & Syndication',
  workflows: 'Workflows & Tasks',
  attributes: 'Attribute Library',
  classifications: 'Classifications & Standards',
  integrations: 'Integrations & Monitoring',
  users: 'Users & Roles',
  organisation: 'Organisation & Brands',
};
