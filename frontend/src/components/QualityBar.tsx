export interface QualityBarProps {
  /** Completeness percentage 0–100; values outside are clamped. */
  value: number;
  /** Hide the numeric percentage label. */
  hideLabel?: boolean;
  thin?: boolean;
}

function barColor(value: number): string {
  if (value >= 80) return 'var(--grn)';
  if (value >= 50) return 'var(--amb)';
  return 'var(--red)';
}

export function QualityBar({ value, hideLabel = false, thin = false }: QualityBarProps) {
  const clamped = Math.min(100, Math.max(0, Math.round(value)));
  return (
    <div
      className="cbar"
      role="progressbar"
      aria-valuenow={clamped}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-label="Completeness"
    >
      <div className={thin ? 'bar thin' : 'bar'}>
        <span style={{ width: `${clamped}%`, background: barColor(clamped) }} />
      </div>
      {!hideLabel && <span className="pc">{clamped}%</span>}
    </div>
  );
}
