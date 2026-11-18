import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { listLocales } from '../features/products/productsApi';
import type { Locale } from '../features/products/types';

/**
 * Display language for catalogue data.
 *
 * This is a *view* preference and nothing more. It never changes a product's
 * base locale, never writes anything, and does not translate the interface —
 * buttons and column headers stay English (decision 2026-08-17; UI-chrome
 * translation is deferred decision A3 and needs an i18n library).
 *
 * Held in the shell rather than in the grid because the detail screens need the
 * same answer, and two independent copies would drift the moment one of them
 * failed to re-render.
 */

const STORAGE_KEY = 'pim.display.locale.v1';

/**
 * Default before the locale list has loaded, and the fallback when a stored
 * preference no longer exists. English by product decision: the catalogue's
 * base locale is de-DE because the client's export is German, but the people
 * using this tool read English.
 */
const DEFAULT_LOCALE = 'en-GB';

interface LocaleContextValue {
  /** Currently selected display locale. */
  locale: string;
  /** Every locale the catalogue is configured for; empty until loaded. */
  available: Locale[];
  /** True while the locale list is in flight. */
  loading: boolean;
  setLocale: (code: string) => void;
}

const LocaleContext = createContext<LocaleContextValue | null>(null);

/** Reads the stored preference defensively — a corrupt value must not break the shell. */
function readStored(): string {
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    return stored && stored.trim().length > 0 ? stored : DEFAULT_LOCALE;
  } catch {
    // Private browsing, or storage disabled. Not worth failing over.
    return DEFAULT_LOCALE;
  }
}

export function LocaleProvider({ children }: { children: ReactNode }) {
  const [locale, setLocaleState] = useState<string>(readStored);
  const [available, setAvailable] = useState<Locale[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;

    listLocales()
      .then((locales) => {
        if (cancelled) return;

        setAvailable(locales);

        // A stored preference for a locale that has since been removed would
        // otherwise send `locale=xx-XX` on every request and get a 400 back on
        // every screen. Fall back rather than strand the user.
        setLocaleState((current) => {
          if (locales.some((l) => l.code === current)) return current;
          const fallback =
            locales.find((l) => l.code === DEFAULT_LOCALE) ??
            locales.find((l) => l.isBase) ??
            locales[0];
          return fallback ? fallback.code : current;
        });
      })
      .catch(() => {
        // The selector stays empty and the app keeps working in the stored
        // locale. A failed lookup must not blank the shell.
        if (!cancelled) setAvailable([]);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const setLocale = useCallback((code: string) => {
    setLocaleState(code);
    try {
      window.localStorage.setItem(STORAGE_KEY, code);
    } catch {
      // Preference is lost on reload; the session still works.
    }
  }, []);

  const value = useMemo<LocaleContextValue>(
    () => ({ locale, available, loading, setLocale }),
    [locale, available, loading, setLocale],
  );

  return <LocaleContext.Provider value={value}>{children}</LocaleContext.Provider>;
}

export function useLocale(): LocaleContextValue {
  const context = useContext(LocaleContext);
  if (!context) {
    throw new Error('useLocale must be used inside a LocaleProvider.');
  }
  return context;
}

export { DEFAULT_LOCALE, STORAGE_KEY };
