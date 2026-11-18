/**
 * Marks text that is not in the language the user asked for.
 *
 * The whole argument for this component: a PIM exists to make translation gaps
 * visible, so falling back silently would make the tool work against its own
 * purpose. Showing nothing at all instead of a fallback was the alternative
 * considered and rejected — with 1 of 3,034 products untranslated it would read
 * as a bug rather than as a gap (decision 2026-08-17).
 *
 * Renders nothing in the normal case, which is almost every row.
 */
export function FallbackMark({
  actual,
  requested,
}: {
  /** Locale the text actually came from; null when there was no translation at all. */
  actual?: string | null;
  /** Locale the user selected. */
  requested: string;
}) {
  // Older API build that does not send the field: no marker rather than a
  // false one. Undefined means "unknown", which is not the same as "fallback".
  if (actual === undefined) {
    return null;
  }

  if (actual === null) {
    return (
      <span className="loc-mark loc-mark-missing" title="No translation in any language — showing the product code">
        untranslated
      </span>
    );
  }

  if (actual === requested) {
    return null;
  }

  return (
    <span className="loc-mark" title={`No ${requested} translation — showing ${actual}`}>
      {actual}
    </span>
  );
}
