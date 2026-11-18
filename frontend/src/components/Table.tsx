import { ReactNode, TdHTMLAttributes, ThHTMLAttributes } from 'react';

export type SortDir = 'asc' | 'desc';

export interface TableProps {
  compact?: boolean;
  children: ReactNode;
}

/** Data grid per prototype `table.dg` inside `.tablewrap`. */
export function Table({ compact = false, children }: TableProps) {
  return (
    <div className="tablewrap">
      <table className={compact ? 'dg compact' : 'dg'}>{children}</table>
    </div>
  );
}

export interface ThProps extends ThHTMLAttributes<HTMLTableCellElement> {
  /** Current sort direction when this column is sorted. */
  sorted?: SortDir;
  /** Marks column sortable; renders indicator and click affordance. */
  onSort?: () => void;
  children?: ReactNode;
}

export function Th({ sorted, onSort, children, ...rest }: ThProps) {
  const ariaSort = sorted === 'asc' ? 'ascending' : sorted === 'desc' ? 'descending' : undefined;
  return (
    <th aria-sort={ariaSort} onClick={onSort} {...rest}>
      {children}
      {onSort && (
        <span className="so" aria-hidden="true">
          {sorted === 'asc' ? '↑' : sorted === 'desc' ? '↓' : '↕'}
        </span>
      )}
    </th>
  );
}

export interface TdProps extends TdHTMLAttributes<HTMLTableCellElement> {
  children?: ReactNode;
}

export function Td({ children, ...rest }: TdProps) {
  return <td {...rest}>{children}</td>;
}

export interface ProductCellProps {
  name: string;
  sku: string;
  /** Short text shown in the thumbnail placeholder. */
  thumbText?: string;
}

/** Product name + SKU cell (.cellp) with thumbnail placeholder. */
export function ProductCell({ name, sku, thumbText = '—' }: ProductCellProps) {
  return (
    <div className="cellp">
      <div className="thumb" aria-hidden="true">
        {thumbText}
      </div>
      <div>
        <div className="nm">{name}</div>
        <div className="sk">{sku}</div>
      </div>
    </div>
  );
}
