import { useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useLocale } from '../../app/LocaleContext';
import { EmptyState } from '../../components/EmptyState';
import { FallbackMark } from '../products/FallbackMark';
import {
  fetchTranslationQueue,
  fetchTranslationWorkbench,
  type LocaleProgress,
  type QueueState,
  type TranslationQueue,
  type TranslationWorkbench,
} from './translationApi';

const PAGE_SIZE = 25;

const STATES: Array<{ id: QueueState; label: string; hint: string }> = [
  { id: 'attention', label: 'Needs attention', hint: 'Missing, outdated, or not yet approved' },
  { id: 'missing', label: 'Missing', hint: 'No text in this language at all' },
  { id: 'outdated', label: 'Outdated', hint: 'The source text was edited after this translation' },
  { id: 'review', label: 'Awaiting review', hint: 'Text exists but is not approved' },
  { id: 'all', label: 'All products', hint: 'Including those already done' },
];

/** Colours the state chip. Missing and outdated are different problems. */
function stateChipClass(state: string): string {
  if (state === 'missing') return 'chip s-bad';
  if (state === 'outdated') return 'chip s-warn';
  if (state === 'approved') return 'chip s-ok';
  return 'chip s-neut';
}

function ProgressRow({ locale }: { locale: LocaleProgress }) {
  return (
    <div style={{ margin: '12px 0' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, marginBottom: 4 }}>
        <span style={{ fontWeight: 600 }}>
          {locale.name}
          {locale.isBase && <span className="chip s-neut sp">source</span>}
        </span>
        <span className="num muted">
          {locale.withText.toLocaleString()} / {locale.totalProducts.toLocaleString()}
        </span>
      </div>
      <div
        className="bar thin"
        role="img"
        aria-label={`${locale.name}: ${locale.withText} of ${locale.totalProducts} products have text`}
      >
        <span
          style={{
            width: `${locale.percentTranslated}%`,
            background:
              locale.percentTranslated >= 95
                ? 'var(--grn)'
                : locale.percentTranslated >= 75
                  ? 'var(--sky)'
                  : 'var(--amb)',
          }}
        />
      </div>
      {/* The breakdown under the bar is what makes it actionable: 80% with 100
          missing is a different job from 80% with 100 awaiting review. */}
      <div className="muted" style={{ fontSize: 11, marginTop: 3 }}>
        {locale.isBase ? (
          'Source language — nothing to translate into it'
        ) : (
          <>
            {locale.missing.toLocaleString()} missing · {locale.outdated.toLocaleString()} outdated ·{' '}
            {locale.awaitingReview.toLocaleString()} awaiting review
          </>
        )}
      </div>
    </div>
  );
}

export function TranslationPage() {
  const { locale } = useLocale();
  const [searchParams, setSearchParams] = useSearchParams();

  const [workbench, setWorkbench] = useState<TranslationWorkbench | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [queue, setQueue] = useState<TranslationQueue | null>(null);
  const [queueError, setQueueError] = useState<string | null>(null);
  const [searchInput, setSearchInput] = useState(searchParams.get('search') ?? '');

  const target = searchParams.get('target');
  const state = (searchParams.get('state') as QueueState | null) ?? 'attention';
  const page = Math.max(1, Number(searchParams.get('page') ?? '1') || 1);
  const search = searchParams.get('search') ?? '';

  useEffect(() => {
    const controller = new AbortController();

    fetchTranslationWorkbench(locale, controller.signal)
      .then((w) => {
        setWorkbench(w);
        setError(null);
      })
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        setError(e instanceof Error ? e.message : 'Translation progress could not be loaded.');
      });

    return () => controller.abort();
  }, [locale]);

  // The first non-base language becomes the target if the URL does not name
  // one. Chosen from the payload rather than hard-coded: a catalogue whose
  // second language is Spanish should not open on a French tab that shows
  // nothing.
  const targets = useMemo(
    () => workbench?.locales.filter((l) => !l.isBase) ?? [],
    [workbench],
  );
  const effectiveTarget = target ?? targets[0]?.code ?? null;

  useEffect(() => {
    if (!effectiveTarget) return;

    const controller = new AbortController();

    fetchTranslationQueue(
      { target: effectiveTarget, state, search, page, pageSize: PAGE_SIZE, locale },
      controller.signal,
    )
      .then((q) => {
        setQueue(q);
        setQueueError(null);
      })
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        setQueue(null);
        setQueueError(e instanceof Error ? e.message : 'The queue could not be loaded.');
      });

    return () => controller.abort();
  }, [effectiveTarget, state, search, page, locale]);

  function update(next: Record<string, string | null>) {
    const params = new URLSearchParams(searchParams);
    for (const [key, value] of Object.entries(next)) {
      if (value === null) params.delete(key);
      else params.set(key, value);
    }
    // Any change of language, filter or search resets to page 1. Landing on
    // page 7 of a 2-page result shows an empty table that reads as "nothing to
    // do here", which is the opposite of the truth.
    if (!('page' in next)) params.delete('page');
    setSearchParams(params, { replace: true });
  }

  if (error) {
    return (
      <div className="pad">
        <div className="phead">
          <div>
            <h1>Translation</h1>
          </div>
        </div>
        <EmptyState title="Translation progress could not be loaded" description={error} />
      </div>
    );
  }

  const totalPages = queue ? Math.max(1, Math.ceil(queue.totalCount / queue.pageSize)) : 1;
  const targetName = targets.find((t) => t.code === effectiveTarget)?.name ?? effectiveTarget;

  return (
    <div className="pad">
      <div className="phead">
        <div>
          <h1>Translation</h1>
          <p>
            What each language still needs. Translations are written on a product&apos;s
            Localization tab; this screen finds the products that need one.
          </p>
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '320px 1fr', gap: 14 }}>
        <div>
          <div className="card">
            <div className="card-h">
              <h3>Language progress</h3>
              {workbench && (
                <span className="sp muted" style={{ fontSize: 11 }}>
                  {workbench.totalProducts.toLocaleString()} products
                </span>
              )}
            </div>
            <div className="card-b">
              {!workbench ? (
                <p className="muted" style={{ fontSize: 12 }}>
                  Loading…
                </p>
              ) : (
                workbench.locales.map((l) => <ProgressRow key={l.code} locale={l} />)
              )}
            </div>
          </div>

          {workbench && workbench.omittedPanels.length > 0 && (
            <div className="card" style={{ marginTop: 14 }}>
              <div className="card-h">
                <h3>Not shown yet</h3>
                <span className="sp chip s-neut">{workbench.omittedPanels.length}</span>
              </div>
              <div className="card-b">
                <p className="muted" style={{ marginTop: 0, fontSize: 11.5 }}>
                  These are in the v4.2 design. They are absent because the data behind them does
                  not exist — not because the figures are zero.
                </p>
                <dl style={{ margin: 0 }}>
                  {workbench.omittedPanels.map((p) => (
                    <div key={p.panel} style={{ padding: '8px 0', borderTop: '1px solid var(--line)' }}>
                      <dt style={{ fontWeight: 600, fontSize: 12 }}>{p.panel}</dt>
                      <dd className="muted" style={{ margin: '2px 0 0', fontSize: 11.5 }}>
                        {p.reason}
                      </dd>
                    </div>
                  ))}
                </dl>
              </div>
            </div>
          )}
        </div>

        <div>
          <div className="toolbar" style={{ marginBottom: 12, display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            <select
              className="sel"
              aria-label="Target language"
              value={effectiveTarget ?? ''}
              onChange={(e) => update({ target: e.target.value })}
            >
              {targets.map((t) => (
                <option key={t.code} value={t.code}>
                  {t.name}
                </option>
              ))}
            </select>

            <select
              className="sel"
              aria-label="Queue filter"
              value={state}
              onChange={(e) => update({ state: e.target.value })}
            >
              {STATES.map((s) => (
                <option key={s.id} value={s.id} title={s.hint}>
                  {s.label}
                </option>
              ))}
            </select>

            <form
              onSubmit={(e) => {
                e.preventDefault();
                update({ search: searchInput.trim() || null });
              }}
              style={{ display: 'flex', gap: 6 }}
            >
              <div className="inp" style={{ minWidth: 200 }}>
                <input
                  aria-label="Search products"
                  placeholder="Search code or name…"
                  value={searchInput}
                  onChange={(e) => setSearchInput(e.target.value)}
                />
              </div>
              <button type="submit" className="btn">
                Search
              </button>
            </form>

            {queue && (
              <span className="muted sp" style={{ fontSize: 11.5, alignSelf: 'center' }}>
                {queue.totalCount.toLocaleString()} products
              </span>
            )}
          </div>

          {queueError ? (
            <EmptyState title="The queue could not be loaded" description={queueError} />
          ) : !queue ? (
            <p className="muted" style={{ fontSize: 12 }}>
              Loading…
            </p>
          ) : queue.items.length === 0 ? (
            /* An empty queue is good news here, and it has to say so — the same
               table empty because of a bad filter looks identical. */
            <EmptyState
              title={`Nothing to do in ${targetName}`}
              description={
                state === 'attention'
                  ? 'Every product has approved, current text in this language.'
                  : 'No product matches this filter. Try "All products" or a different language.'
              }
            />
          ) : (
            <div className="card" style={{ overflow: 'hidden' }}>
              <div className="tablewrap">
                <table className="dg">
                  <thead>
                    <tr>
                      <th>Product</th>
                      <th>Category</th>
                      <th>State</th>
                      <th>Source</th>
                      <th>Target updated</th>
                    </tr>
                  </thead>
                  <tbody>
                    {queue.items.map((item) => (
                      <tr key={item.productCode}>
                        <td>
                          <Link to={`/products/${encodeURIComponent(item.productCode)}?tab=localization`}>
                            {item.name}
                          </Link>
                          <FallbackMark actual={item.nameLocale} requested={locale} />
                          <div className="sk muted num" style={{ fontSize: 11 }}>
                            {item.productCode}
                          </div>
                        </td>
                        <td>
                          <span className="tag">{item.category}</span>
                        </td>
                        <td>
                          <span className={stateChipClass(item.state)}>{item.state}</span>
                          {/* A row can be approved AND outdated. Showing only the
                              derived state would hide which of the two it is. */}
                          {item.state === 'outdated' && item.translationStatus && (
                            <span className="muted" style={{ fontSize: 11 }}>
                              {' '}
                              was {item.translationStatus}
                            </span>
                          )}
                        </td>
                        <td className="muted">{item.source ?? '—'}</td>
                        <td className="muted num" style={{ fontSize: 11 }}>
                          {item.targetUpdatedAtUtc
                            ? new Date(item.targetUpdatedAtUtc).toLocaleDateString()
                            : '—'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {totalPages > 1 && (
                <div
                  style={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    padding: '10px 14px',
                    borderTop: '1px solid var(--line)',
                  }}
                >
                  <span className="muted" style={{ fontSize: 11.5 }}>
                    Page {queue.page} of {totalPages}
                  </span>
                  <div style={{ display: 'flex', gap: 8 }}>
                    <button
                      type="button"
                      className="btn sm"
                      disabled={queue.page <= 1}
                      onClick={() => update({ page: String(queue.page - 1) })}
                    >
                      Previous
                    </button>
                    <button
                      type="button"
                      className="btn sm"
                      disabled={queue.page >= totalPages}
                      onClick={() => update({ page: String(queue.page + 1) })}
                    >
                      Next
                    </button>
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
