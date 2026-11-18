import { useCallback, useEffect, useMemo, useState } from 'react';
import { ApiError } from '../../api/client';
import { Button } from '../../components/Button';
import { Chip, ChipTone } from '../../components/Chip';
import { QualityBar } from '../../components/QualityBar';
import { useToast } from '../../components/Toast';
import { getProductLocalization, saveProductLocalization } from './productsApi';
import type { ProductLocale, ProductLocalization, TranslationValueInput } from './types';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'loaded'; data: ProductLocalization };

export interface LocalizationTabProps {
  code: string;
  onChanged?: (rowVersion: number) => void;
}

const STATUS_TONE: Record<string, ChipTone> = {
  missing: 'bad',
  pending: 'warn',
  translated: 'info',
  approved: 'ok',
};

const STATUS_LABEL: Record<string, string> = {
  missing: 'Not started',
  pending: 'In progress',
  translated: 'Translated',
  approved: 'Approved',
};

function statusTone(status: string): ChipTone {
  return STATUS_TONE[status] ?? 'neut';
}

function statusLabel(status: string): string {
  return STATUS_LABEL[status] ?? status;
}

/**
 * Localization.
 *
 * One target locale at a time, beside the base locale it is translated from.
 * The base column is read-only here on purpose: German copy is owned by the
 * General and Attributes tabs, and an editable master column invites a
 * translator to "correct" the source in a screen that has none of that field's
 * validation.
 *
 * Two storage shapes are edited through one list — product copy in `product_tr`
 * columns and localizable attribute values — because a translator does not care
 * which table a sentence lives in.
 *
 * There is no "AI translate" action. Machine translation needs a service that
 * has not been chosen (assumption A3), and a button that silently does nothing
 * is worse than its absence.
 */
export function LocalizationTab({ code, onChanged }: LocalizationTabProps) {
  const { showToast } = useToast();
  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [selected, setSelected] = useState<string | null>(null);
  const [draft, setDraft] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const load = useCallback(
    (signal?: { aborted: boolean }) => {
      setState({ kind: 'loading' });
      getProductLocalization(code)
        .then((data) => {
          if (!signal?.aborted) {
            setState({ kind: 'loaded', data });
            setDraft({});
            setFormError(null);
          }
        })
        .catch((error: unknown) => {
          if (!signal?.aborted) {
            setState({
              kind: 'error',
              message: error instanceof ApiError ? error.message : 'Could not load translations.',
            });
          }
        });
    },
    [code],
  );

  useEffect(() => {
    const signal = { aborted: false };
    load(signal);
    return () => {
      signal.aborted = true;
    };
  }, [load]);

  const data = state.kind === 'loaded' ? state.data : null;

  // Default to the first locale that still needs work, not simply the first
  // one: a translator opening this tab wants the backlog, not the base locale.
  const activeLocale: ProductLocale | null = useMemo(() => {
    if (!data) {
      return null;
    }

    const targets = data.locales.filter((l) => !l.isBase);
    if (targets.length === 0) {
      return null;
    }

    if (selected) {
      return targets.find((l) => l.localeCode === selected) ?? targets[0];
    }

    return targets.find((l) => l.translationStatus !== 'approved') ?? targets[0];
  }, [data, selected]);

  if (state.kind === 'loading') {
    return (
      <p className="muted" role="status">
        Loading translations…
      </p>
    );
  }

  if (state.kind === 'error') {
    return (
      <>
        <div className="alert err" role="alert">
          {state.message}
        </div>
        <Button style={{ marginTop: 12 }} onClick={() => load()}>
          Retry
        </Button>
      </>
    );
  }

  const loaded = state.data;
  const baseLocaleRow = loaded.locales.find((l) => l.isBase);
  const dirty = Object.keys(draft).length > 0;

  function valueFor(field: string, current: string | null): string {
    return draft[field] ?? current ?? '';
  }

  function onFieldChange(field: string, value: string) {
    setDraft((previous) => ({ ...previous, [field]: value }));
  }

  async function save(status: string | null) {
    if (!activeLocale) {
      return;
    }

    const values: TranslationValueInput[] = Object.entries(draft).map(([field, value]) => ({
      field,
      value: value.trim() === '' ? null : value,
    }));

    if (values.length === 0 && status === null) {
      return;
    }

    setBusy(true);
    setFormError(null);

    try {
      const updated = await saveProductLocalization(code, activeLocale.localeCode, {
        values,
        translationStatus: status,
        rowVersion: loaded.rowVersion,
      });

      setState({ kind: 'loaded', data: updated });
      setDraft({});
      onChanged?.(updated.rowVersion);
      showToast(
        status === 'approved'
          ? `${activeLocale.localeName} approved`
          : `${activeLocale.localeName} saved`,
      );
    } catch (error: unknown) {
      setFormError(
        error instanceof ApiError ? error.message : 'Could not save the translation.',
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <p className="muted" style={{ fontSize: 11.5, margin: '0 0 12px' }}>
        {loaded.baseLocale} is the master. It is shown for reference and edited on the General and
        Attributes tabs — this tab writes only the target language.
      </p>

      <section className="card" aria-label="Languages" style={{ marginBottom: 14 }}>
        <div className="card-h">
          <h3>Languages</h3>
          <span className="muted">
            {loaded.pendingLocaleCount === 0
              ? 'All languages approved'
              : `${loaded.pendingLocaleCount} awaiting work`}
          </span>
        </div>
        <div className="card-b">
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            {loaded.locales.map((locale) => (
              <button
                key={locale.localeCode}
                type="button"
                className={
                  locale.localeCode === activeLocale?.localeCode ? 'btn pri sm' : 'btn sm'
                }
                disabled={locale.isBase || busy}
                aria-current={locale.localeCode === activeLocale?.localeCode ? 'true' : undefined}
                onClick={() => {
                  setSelected(locale.localeCode);
                  setDraft({});
                  setFormError(null);
                }}
              >
                {locale.localeName}
                {locale.isBase ? ' · master' : ` · ${locale.completeness}%`}
              </button>
            ))}
          </div>
        </div>
      </section>

      {activeLocale === null ? (
        <div className="alert warn" role="status">
          Only the base locale is configured, so there is nothing to translate yet.
        </div>
      ) : (
        <section className="card" aria-label={`${activeLocale.localeName} translation`}>
          <div className="card-h">
            <h3>
              {baseLocaleRow?.localeName ?? loaded.baseLocale} → {activeLocale.localeName}
            </h3>
            <Chip tone={statusTone(activeLocale.translationStatus)} dot>
              {statusLabel(activeLocale.translationStatus)}
            </Chip>
            <div className="sp" style={{ display: 'flex', gap: 6 }}>
              <Button disabled={busy || !dirty} onClick={() => void save(null)}>
                {busy ? 'Saving…' : 'Save'}
              </Button>
              <Button
                variant="primary"
                disabled={busy}
                onClick={() => void save('approved')}
              >
                Approve language
              </Button>
            </div>
          </div>

          <div className="card-b">
            <div
              style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 14 }}
            >
              <div style={{ minWidth: 200 }}>
                <QualityBar value={activeLocale.completeness} />
              </div>
              <span className="muted" style={{ fontSize: 12 }}>
                {activeLocale.filledCount} of {activeLocale.fieldCount} fields translated
              </span>
            </div>

            {formError && (
              <div className="alert err" role="alert" style={{ marginBottom: 12 }}>
                {formError}
              </div>
            )}

            {activeLocale.fields.map((field) => (
              <div
                key={field.field}
                className="grid g2"
                style={{ gap: 12, marginBottom: 14, alignItems: 'start' }}
              >
                <div className="field">
                  <label htmlFor={`base-${field.field}`}>
                    {field.label}
                    <span className="muted" style={{ fontWeight: 400 }}>
                      {' '}
                      · {baseLocaleRow?.localeName ?? loaded.baseLocale}
                    </span>
                  </label>
                  {/* Readonly rather than disabled: a disabled control is not
                      reachable by a screen reader, and this text is the whole
                      reason a translator is on this screen. */}
                  <textarea
                    id={`base-${field.field}`}
                    className="fld"
                    readOnly
                    rows={field.isMultiline ? 3 : 1}
                    value={field.baseValue ?? ''}
                    placeholder="Not set in the master"
                  />
                </div>

                <div className="field">
                  <label htmlFor={`target-${field.field}`}>
                    {field.label}
                    <span className="muted" style={{ fontWeight: 400 }}>
                      {' '}
                      · {activeLocale.localeName}
                    </span>
                    {field.isRequired && <span aria-hidden="true"> *</span>}
                    {field.isAttribute && (
                      <span className="muted" style={{ fontWeight: 400 }}>
                        {' '}
                        · attribute
                      </span>
                    )}
                  </label>
                  <textarea
                    id={`target-${field.field}`}
                    className="fld"
                    rows={field.isMultiline ? 3 : 1}
                    required={field.isRequired}
                    disabled={busy}
                    value={valueFor(field.field, field.value)}
                    onChange={(event) => onFieldChange(field.field, event.target.value)}
                  />
                </div>
              </div>
            ))}
          </div>
        </section>
      )}
    </>
  );
}
