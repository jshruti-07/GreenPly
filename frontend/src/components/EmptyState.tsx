import { ReactNode } from 'react';

export interface EmptyStateProps {
  title: string;
  description?: string;
  /** Optional call-to-action, typically a Button. */
  action?: ReactNode;
}

export function EmptyState({ title, description, action }: EmptyStateProps) {
  return (
    <div className="empty">
      <p style={{ margin: 0, fontWeight: 600, color: 'var(--ink2)' }}>{title}</p>
      {description && <p style={{ margin: '6px 0 0' }}>{description}</p>}
      {action && <div style={{ marginTop: 14 }}>{action}</div>}
    </div>
  );
}
