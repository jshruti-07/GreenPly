/* Icon paths extracted 1:1 from PIM Prototype v4.2.html ICON map (ADR-006). */

export type IconName =
  | 'dashboard'
  | 'products'
  | 'systems'
  | 'taxonomy'
  | 'translation'
  | 'assets'
  | 'content'
  | 'quality'
  | 'channels'
  | 'workflows'
  | 'attributes'
  | 'classification'
  | 'plug'
  | 'users'
  | 'org'
  | 'chevL';

const PATHS: Record<IconName, string> = {
  dashboard:
    '<rect x="3" y="3" width="7" height="9" rx="1.5"/><rect x="14" y="3" width="7" height="5" rx="1.5"/><rect x="14" y="12" width="7" height="9" rx="1.5"/><rect x="3" y="16" width="7" height="5" rx="1.5"/>',
  products:
    '<path d="M21 16V8a2 2 0 00-1-1.7l-7-4a2 2 0 00-2 0l-7 4A2 2 0 003 8v8a2 2 0 001 1.7l7 4a2 2 0 002 0l7-4A2 2 0 0021 16z"/><path d="M3.3 7L12 12l8.7-5M12 22V12"/>',
  systems:
    '<rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="8.5" y="14" width="7" height="7" rx="1"/><path d="M6.5 10v2a2 2 0 002 2h3.5M17.5 10v2a2 2 0 01-2 2H12"/>',
  taxonomy:
    '<rect x="3" y="3" width="6" height="6" rx="1"/><rect x="15" y="15" width="6" height="6" rx="1"/><rect x="15" y="3" width="6" height="6" rx="1"/><path d="M6 9v6a2 2 0 002 2h7M18 9v3"/>',
  translation:
    '<path d="M4 5h7M9 3v2c0 4-2.5 7-6 8"/><path d="M5 9c0 2.5 3 5 6 6"/><path d="M13 21l4-9 4 9M14.5 17h5"/>',
  assets:
    '<rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8.5" cy="8.5" r="1.8"/><path d="M21 15l-5-5L5 21"/>',
  content:
    '<rect x="4" y="3" width="16" height="18" rx="2"/><path d="M8 8h8M8 12h8M8 16h5"/>',
  quality:
    '<path d="M9 11l3 3L22 4"/><path d="M21 12v7a2 2 0 01-2 2H5a2 2 0 01-2-2V5a2 2 0 012-2h11"/>',
  channels: '<path d="M22 12h-4l-3 9L9 3l-3 9H2"/>',
  workflows:
    '<circle cx="6" cy="6" r="2.5"/><circle cx="6" cy="18" r="2.5"/><circle cx="18" cy="12" r="2.5"/><path d="M8.5 6H14a2 2 0 012 2v1.5M8.5 18H14a2 2 0 002-2v-1.5"/>',
  attributes:
    '<path d="M4 7h16M4 12h16M4 17h10"/><circle cx="18" cy="17" r="1.6" fill="currentColor" stroke="none"/>',
  classification:
    '<path d="M3 7v10a2 2 0 002 2h6M3 7l9-4 9 4M3 7l9 4 9-4M21 7v6"/><path d="M16 16h6M19 13v6"/>',
  plug: '<path d="M9 2v6M15 2v6M6 8h12v3a6 6 0 01-12 0z"/><path d="M12 17v5"/>',
  users:
    '<circle cx="9" cy="8" r="3"/><path d="M3 20a6 6 0 0112 0"/><path d="M16 6a3 3 0 010 6M21 20a6 6 0 00-4-5.7"/>',
  org: '<rect x="3" y="13" width="6" height="8"/><rect x="15" y="13" width="6" height="8"/><rect x="9" y="3" width="6" height="8"/><path d="M12 11v2M6 13v-1a2 2 0 012-2h8a2 2 0 012 2v1"/>',
  chevL: '<path d="M15 6l-6 6 6 6"/>',
};

export interface IconProps {
  name: IconName;
  size?: 'md' | 'sm' | 'xs';
}

export function Icon({ name, size = 'md' }: IconProps) {
  const cls = size === 'md' ? 'ic' : `ic ${size}`;
  return (
    <svg
      className={cls}
      viewBox="0 0 24 24"
      aria-hidden="true"
      // Trusted static markup from the design prototype, not user input.
      dangerouslySetInnerHTML={{ __html: PATHS[name] }}
    />
  );
}
