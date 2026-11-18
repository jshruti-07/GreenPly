import { useEffect, useRef } from 'react';

export interface CheckboxProps {
  checked: boolean;
  /** Renders the mixed state used by a header select-all. */
  indeterminate?: boolean;
  onChange: (checked: boolean) => void;
  label: string;
}

/**
 * Native input styled as the v4.2 `.ckbox`. Native rather than a div so
 * keyboard focus, space toggling and screen-reader state come for free.
 */
export function Checkbox({ checked, indeterminate = false, onChange, label }: CheckboxProps) {
  const ref = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (ref.current) {
      ref.current.indeterminate = indeterminate;
    }
  }, [indeterminate]);

  return (
    <input
      ref={ref}
      type="checkbox"
      aria-label={label}
      checked={checked}
      onChange={(event) => onChange(event.target.checked)}
      onClick={(event) => event.stopPropagation()}
      style={{ width: 16, height: 16, accentColor: 'var(--pri)', cursor: 'pointer' }}
    />
  );
}
