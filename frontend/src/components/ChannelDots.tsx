/** Mirrors the backend ChannelReadinessLevel enum. */
export type ChannelReadinessLevel = 0 | 1 | 2;

export interface ChannelDotsProps {
  /**
   * One level per channel. Optional and length-tolerant on purpose: an older
   * API build (or a future one that adds a channel) must degrade to neutral
   * dots, never throw. A crash here previously blanked the whole grid.
   */
  levels?: ReadonlyArray<ChannelReadinessLevel | number> | null;
  /** Channel names; drives both the dot count and the accessible label. */
  channels: readonly string[];
}

const LEVEL_CLASS = ['no', 'warn', 'ok'] as const;
const LEVEL_LABEL = ['not ready', 'partially ready', 'ready'] as const;

function normalize(value: number | undefined): 0 | 1 | 2 {
  return value === 1 || value === 2 ? value : 0;
}

/**
 * v4.2 `.chdots`. Values are DERIVED from completeness and workflow state
 * until the channel service exists — the column header says so, and the label
 * repeats it per row so the signal is never mistaken for authoritative.
 */
export function ChannelDots({ levels, channels }: ChannelDotsProps) {
  // Always render one dot per known channel, whatever the API sent.
  const resolved = channels.map((_, index) => normalize(levels?.[index]));
  const unknown = !levels || levels.length === 0;

  const description = unknown
    ? 'not yet reported by the API'
    : resolved.map((level, index) => `${channels[index]}: ${LEVEL_LABEL[level]}`).join(', ');

  return (
    <span
      className="chdots"
      role="img"
      aria-label={`Channel readiness (derived) — ${description}`}
      title={
        unknown
          ? 'Channel readiness not reported by the API'
          : `Derived from completeness and workflow state — ${description}`
      }
    >
      {resolved.map((level, index) => (
        <i key={channels[index]} className={LEVEL_CLASS[level]} />
      ))}
    </span>
  );
}
