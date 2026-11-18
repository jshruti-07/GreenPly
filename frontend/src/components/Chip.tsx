import { ReactNode } from 'react';

/** Tone maps 1:1 to prototype status classes (s-ok, s-warn, ...). */
export type ChipTone = 'ok' | 'warn' | 'bad' | 'info' | 'pri' | 'vio' | 'neut' | 'tea';

export interface ChipProps {
  tone?: ChipTone;
  /** Leading status dot, as used in grid status cells. */
  dot?: boolean;
  children: ReactNode;
}

export function Chip({ tone = 'neut', dot = false, children }: ChipProps) {
  return (
    <span className={`chip s-${tone}`}>
      {dot && <span className="bd" aria-hidden="true" />}
      {children}
    </span>
  );
}

/** Product lifecycle/workflow states → chip tone, per v4.2 prototype colors. */
export const statusTone: Record<string, ChipTone> = {
  Active: 'ok',
  Draft: 'neut',
  InReview: 'warn',
  Approved: 'info',
  Published: 'ok',
  Discontinued: 'bad',
  Archived: 'neut',
};

export interface StatusBadgeProps {
  status: string;
}

export function StatusBadge({ status }: StatusBadgeProps) {
  return (
    <Chip tone={statusTone[status] ?? 'neut'} dot>
      {status}
    </Chip>
  );
}
