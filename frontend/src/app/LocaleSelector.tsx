import { useLocale } from './LocaleContext';

/**
 * Language selector for catalogue data, in the shell header.
 *
 * A native `<select>` on purpose. The list is three items today and a dozen at
 * most ever; a custom dropdown would cost keyboard handling, focus trapping and
 * screen-reader semantics that the platform control already gets right.
 *
 * The label says "Data language" rather than "Language" because it does not
 * translate the interface, and a control labelled "Language" that leaves every
 * button in English reads as broken rather than as scoped.
 */
export function LocaleSelector() {
  const { locale, available, loading, setLocale } = useLocale();

  // Nothing useful to offer: either still loading, or the lookup failed, or the
  // catalogue has a single language and a selector would be furniture.
  if (loading || available.length < 2) {
    return null;
  }

  return (
    <div className="locale-picker">
      <label className="locale-label" htmlFor="display-locale">
        Data language
      </label>
      <select
        id="display-locale"
        className="locale-select"
        value={locale}
        onChange={(event) => setLocale(event.target.value)}
      >
        {available.map((option) => (
          <option key={option.code} value={option.code}>
            {option.name}
            {option.isBase ? ' (source)' : ''}
          </option>
        ))}
      </select>
    </div>
  );
}
