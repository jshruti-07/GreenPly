import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useLocale } from '../../app/LocaleContext';
import { EmptyState } from '../../components/EmptyState';
import { FallbackMark } from '../products/FallbackMark';
import {
  fetchAttributeDetail,
  fetchAttributeLibrary,
  type AttributeDetail,
  type AttributeLibrary,
  type AttributeSummary,
} from './attributeLibraryApi';

function CoverageCell({ attribute }: { attribute: AttributeSummary }) {
  // An attribute no product is expected to carry has no coverage to report.
  // Drawing a 0% bar there would accuse somebody of not filling in a field that
  // was never on their screen.
  if (attribute.appliesToProducts === 0) {
    return (
      <span className="muted" style={{ fontSize: 11 }}>
        not in any product&apos;s set
      </span>
    );
  }

  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
      <div
        className="bar thin"
        role="img"
        aria-label={`${attribute.label}: ${attribute.productsWithValue} of ${attribute.appliesToProducts} products`}
      >
        <span
          style={{
            width: `${attribute.coveragePct}%`,
            background:
              attribute.coveragePct >= 95
                ? 'var(--grn)'
                : attribute.coveragePct >= 75
                  ? 'var(--sky)'
                  : 'var(--amb)',
          }}
        />
      </div>
      <span className="num muted" style={{ fontSize: 11, whiteSpace: 'nowrap' }}>
        {attribute.coveragePct}%
      </span>
    </div>
  );
}

function DetailPanel({
  detail,
  requestedLocale,
  onClose,
}: {
  detail: AttributeDetail;
  requestedLocale: string;
  onClose: () => void;
}) {
  const facts: Array<[string, string]> = [
    ['Group', detail.groupLabel || '—'],
    ['Type', detail.dataType],
    ['Unit', detail.unit ?? detail.unitCode ?? '—'],
    ['Scope', detail.scope],
    ['Localizable', detail.isLocalizable ? 'Yes' : 'No'],
    ['Variant axis', detail.isVariantAxis ? 'Yes' : 'No'],
    ['Owned by', detail.sourceAuthority],
    [
      'Filled in',
      detail.appliesToProducts === 0
        ? 'not in any set'
        : `${detail.productsWithValue.toLocaleString()} of ${detail.appliesToProducts.toLocaleString()} (${detail.coveragePct}%)`,
    ],
  ];

  return (
    <div className="card" style={{ marginTop: 14 }}>
      <div className="card-h">
        <h3>
          {detail.label}
          <FallbackMark actual={detail.labelLocale} requested={requestedLocale} />
        </h3>
        <span className="sp muted num" style={{ fontSize: 11 }}>
          {detail.code}
        </span>
        <button type="button" className="btn sm" onClick={onClose}>
          Close
        </button>
      </div>
      <div className="card-b">
        <div className="dmeta">
          {facts.map(([label, value]) => (
            <div className="f" key={label}>
              <label>{label}</label>
              <b>{value}</b>
            </div>
          ))}
        </div>

        <h4 style={{ margin: '18px 0 6px', fontSize: 12 }}>Text per language</h4>
        <table className="dg">
          <thead>
            <tr>
              <th>Language</th>
              <th>Label</th>
              <th>Help text</th>
            </tr>
          </thead>
          <tbody>
            {detail.labels.map((l) => (
              <tr key={l.localeCode} style={{ cursor: 'default' }}>
                <td>
                  {l.localeName}
                  {l.isBase && <span className="chip s-neut sp">base</span>}
                </td>
                <td className={l.label ? undefined : 'muted'}>{l.label ?? 'Not translated'}</td>
                <td className="muted">{l.helpText ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>

        {detail.options.length > 0 && (
          <>
            <h4 style={{ margin: '18px 0 6px', fontSize: 12 }}>
              Options <span className="muted">({detail.options.length})</span>
            </h4>
            <table className="dg">
              <thead>
                <tr>
                  <th>Option</th>
                  <th>Code</th>
                  <th>Used by</th>
                </tr>
              </thead>
              <tbody>
                {detail.options.map((o) => (
                  <tr key={o.code} style={{ cursor: 'default' }}>
                    <td>
                      {o.label}
                      <FallbackMark actual={o.labelLocale} requested={requestedLocale} />
                    </td>
                    <td className="muted num">{o.code}</td>
                    {/* An option no product has chosen is either dead vocabulary
                        or an enrichment gap. Both are worth seeing; neither is
                        visible from a list of options alone. */}
                    <td className={o.usedByProducts === 0 ? 'muted' : undefined}>
                      {o.usedByProducts === 0
                        ? 'never used'
                        : `${o.usedByProducts.toLocaleString()} products`}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        )}

        <h4 style={{ margin: '18px 0 6px', fontSize: 12 }}>Used in attribute sets</h4>
        {detail.sets.length === 0 ? (
          <EmptyState
            title="Not in any attribute set"
            description="Defined in the library but attached to nothing, so no product can carry it. That is a modelling gap rather than an error, and it is why this panel says so instead of showing an empty table."
          />
        ) : (
          <table className="dg">
            <thead>
              <tr>
                <th>Set</th>
                <th>Required</th>
                <th>Products</th>
              </tr>
            </thead>
            <tbody>
              {detail.sets.map((s) => (
                <tr key={s.setCode} style={{ cursor: 'default' }}>
                  <td>{s.setName}</td>
                  <td>{s.isRequired ? 'Yes' : <span className="muted">—</span>}</td>
                  <td className="num">{s.productCount.toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {detail.validationJson && (
          <>
            <h4 style={{ margin: '18px 0 6px', fontSize: 12 }}>Validation</h4>
            {/* Shown raw. There is no schema for this column in this build, and
                a friendly summary of a rule nobody has specified would be a
                guess presented as documentation. */}
            <pre
              className="muted"
              style={{ fontSize: 11, margin: 0, whiteSpace: 'pre-wrap', wordBreak: 'break-all' }}
            >
              {detail.validationJson}
            </pre>
          </>
        )}
      </div>
    </div>
  );
}

export function AttributeLibraryPage() {
  const { locale } = useLocale();
  const [searchParams, setSearchParams] = useSearchParams();
  const selected = searchParams.get('attribute');
  const group = searchParams.get('group');

  const [library, setLibrary] = useState<AttributeLibrary | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [detail, setDetail] = useState<AttributeDetail | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [dataType, setDataType] = useState('');

  useEffect(() => {
    const controller = new AbortController();

    fetchAttributeLibrary(locale, controller.signal)
      .then((l) => {
        setLibrary(l);
        setError(null);
      })
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        setError(e instanceof Error ? e.message : 'The attribute library could not be loaded.');
      });

    return () => controller.abort();
  }, [locale]);

  useEffect(() => {
    if (!selected) {
      setDetail(null);
      setDetailError(null);
      return;
    }

    const controller = new AbortController();

    fetchAttributeDetail(selected, locale, controller.signal)
      .then((d) => {
        setDetail(d);
        setDetailError(null);
      })
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        setDetail(null);
        setDetailError(e instanceof Error ? e.message : 'This attribute could not be loaded.');
      });

    return () => controller.abort();
  }, [selected, locale]);

  function setParam(key: string, value: string | null) {
    const next = new URLSearchParams(searchParams);
    if (value === null) next.delete(key);
    else next.set(key, value);
    setSearchParams(next, { replace: true });
  }

  const dataTypes = useMemo(() => {
    if (!library) return [];
    return [...new Set(library.attributes.map((a) => a.dataType))].sort();
  }, [library]);

  const visible = useMemo(() => {
    if (!library) return [];
    const needle = search.trim().toLowerCase();

    return library.attributes.filter((a) => {
      if (group && a.groupCode !== group) return false;
      if (dataType && a.dataType !== dataType) return false;
      if (!needle) return true;
      return a.label.toLowerCase().includes(needle) || a.code.toLowerCase().includes(needle);
    });
  }, [library, group, dataType, search]);

  if (error) {
    return (
      <div className="pad">
        <div className="phead">
          <div>
            <h1>Attribute Library</h1>
          </div>
        </div>
        <EmptyState title="The attribute library could not be loaded" description={error} />
      </div>
    );
  }

  return (
    <div className="pad">
      <div className="phead">
        <div>
          <h1>Attribute Library</h1>
          <p>
            Every attribute the catalogue defines. Coverage is measured against the products whose
            attribute set actually includes the attribute — not against the whole catalogue, which
            would make every specialised attribute look abandoned.
          </p>
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '260px 1fr', gap: 14 }}>
        <div className="card">
          <div className="card-h">
            <h3>Groups</h3>
          </div>
          <div className="card-b" style={{ paddingTop: 6 }}>
            <button
              type="button"
              className={`node${group === null ? ' on' : ''}`}
              style={{ width: '100%', border: 0, background: 'none', font: 'inherit', cursor: 'pointer' }}
              onClick={() => setParam('group', null)}
            >
              <span style={{ flex: 1, textAlign: 'left' }}>All groups</span>
              <span className="cnt-pill num">{library?.totalAttributes ?? 0}</span>
            </button>
            {library?.groups.map((g) => (
              <button
                type="button"
                key={g.code}
                className={`node${group === g.code ? ' on' : ''}`}
                style={{ width: '100%', border: 0, background: 'none', font: 'inherit', cursor: 'pointer' }}
                onClick={() => setParam('group', g.code)}
              >
                <span style={{ flex: 1, textAlign: 'left' }}>
                  {g.label}
                  <FallbackMark actual={g.labelLocale} requested={locale} />
                </span>
                {/* Groups with nothing in them are shown, not filtered out: an
                    empty group is a real state somebody created. */}
                <span className="cnt-pill num">{g.attributeCount}</span>
              </button>
            ))}
          </div>
        </div>

        <div>
          <div className="toolbar" style={{ marginBottom: 12, display: 'flex', gap: 8 }}>
            <div className="inp" style={{ minWidth: 220 }}>
              <input
                aria-label="Search attributes"
                placeholder="Search attributes…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
            <select
              className="sel"
              aria-label="Filter by type"
              value={dataType}
              onChange={(e) => setDataType(e.target.value)}
            >
              <option value="">All types</option>
              {dataTypes.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
            <span className="muted sp" style={{ fontSize: 11.5, alignSelf: 'center' }}>
              {visible.length} of {library?.totalAttributes ?? 0}
            </span>
          </div>

          <div className="card" style={{ overflow: 'hidden' }}>
            <div className="tablewrap">
              <table className="dg">
                <thead>
                  <tr>
                    <th>Attribute</th>
                    <th>Group</th>
                    <th>Type</th>
                    <th>Unit</th>
                    <th>Required</th>
                    <th>Sets</th>
                    <th style={{ width: 170 }}>Filled in</th>
                  </tr>
                </thead>
                <tbody>
                  {visible.map((a) => (
                    <tr
                      key={a.code}
                      className={selected === a.code ? 'sel' : undefined}
                      onClick={() => setParam('attribute', a.code)}
                    >
                      <td style={{ fontWeight: 600 }}>
                        {a.label}
                        <FallbackMark actual={a.labelLocale} requested={locale} />
                        {a.isLocalizable && (
                          <span className="chip s-info sp" title="Has a value per language">
                            i18n
                          </span>
                        )}
                        {a.isVariantAxis && (
                          <span className="chip s-vio sp" title="Defines a variant axis">
                            axis
                          </span>
                        )}
                      </td>
                      <td>{a.groupLabel ? <span className="tag">{a.groupLabel}</span> : '—'}</td>
                      <td className="muted">{a.dataType}</td>
                      <td className="muted">{a.unit ?? '—'}</td>
                      <td>{a.requiredInAnySet ? 'Yes' : <span className="muted">—</span>}</td>
                      <td className="num">{a.setCount}</td>
                      <td>
                        <CoverageCell attribute={a} />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {library && visible.length === 0 && (
              <EmptyState
                title="No attribute matches those filters"
                description="Search looks at the label in the current language and at the attribute code."
              />
            )}
          </div>

          {detailError && (
            <EmptyState title="This attribute could not be loaded" description={detailError} />
          )}
          {detail && (
            <DetailPanel
              detail={detail}
              requestedLocale={locale}
              onClose={() => setParam('attribute', null)}
            />
          )}
        </div>
      </div>
    </div>
  );
}
