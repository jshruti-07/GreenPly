import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useLocale } from '../../app/LocaleContext';
import { EmptyState } from '../../components/EmptyState';
import { FallbackMark } from '../products/FallbackMark';
import {
  assetFileUrl,
  fetchAssetDetail,
  fetchAssets,
  fetchAssetSummary,
  formatBytes,
  uploadAsset,
  type AssetDetail,
  type AssetLibrarySummary,
  type AssetListItem,
  type AssetPage,
  type AssetUsageFilter,
} from './assetLibraryApi';

const PAGE_SIZE = 24;

/** v4.2 draws a type glyph per card. One character, no icon font to add. */
const TYPE_GLYPH: Record<string, string> = {
  IMAGE: '▣',
  CAD: '◈',
  BIM: '⬢',
  DOCUMENT: '▤',
  VIDEO: '▶',
  SPIN_360: '◐',
};

function AssetCard({
  asset,
  selected,
  requestedLocale,
  onOpen,
}: {
  asset: AssetListItem;
  selected: boolean;
  requestedLocale: string;
  onOpen: (id: string) => void;
}) {
  const [thumbError, setThumbError] = useState(false);
  const dimensions = asset.width && asset.height ? `${asset.width}×${asset.height}` : null;

  const isImage = asset.assetType === 'IMAGE';
  const isDirectFile = isImage && (asset.url?.startsWith('/uploads_ply') || asset.url?.startsWith('data:') || asset.publicId?.startsWith('ply-'));
  const isLocalStored = isImage && !isDirectFile && asset.storageProvider === 'local';
  const hasDirectUrl = isImage && !isLocalStored && asset.storageProvider !== 'client-magento' && !!asset.url;
  const imageSrc = isDirectFile ? asset.url : isLocalStored ? assetFileUrl(asset.publicId, 'thumb') : hasDirectUrl ? asset.url : null;
  const showImage = !!imageSrc && !thumbError;

  return (
    <button
      type="button"
      className="acard"
      aria-current={selected ? 'true' : undefined}
      style={{ textAlign: 'left', font: 'inherit', padding: 0 }}
      onClick={() => onOpen(asset.publicId)}
    >
      <div className="aprev">
        {showImage ? (
          <img
            src={imageSrc!}
            alt=""
            loading="lazy"
            referrerPolicy="no-referrer"
            onError={() => setThumbError(true)}
            style={{ width: '100%', height: '100%', objectFit: 'cover' }}
          />
        ) : (
          <span aria-hidden="true" style={{ fontSize: 30, color: 'var(--c5)' }}>
            {TYPE_GLYPH[asset.assetType] ?? '▪'}
          </span>
        )}
        <span className="ty">
          <span className="chip s-neut">{asset.assetType}</span>
        </span>
        {asset.linkCount === 0 && (
          <span className="ty" style={{ left: 'auto', right: 7 }}>
            <span className="chip s-warn" title="Not linked to any product">
              orphan
            </span>
          </span>
        )}
      </div>
      <div className="abody">
        <div className="nm" title={asset.fileName ?? undefined}>
          {asset.title ?? asset.fileName ?? 'Untitled'}
          <FallbackMark actual={asset.titleLocale} requested={requestedLocale} />
        </div>
        <div className="me">
          {formatBytes(asset.fileSizeBytes)}
          {dimensions ? ` · ${dimensions}` : ''}
          {` · ${asset.linkCount} product${asset.linkCount === 1 ? '' : 's'}`}
        </div>
      </div>
    </button>
  );
}

function DetailPanel({
  detail,
  requestedLocale,
  onClose,
}: {
  detail: AssetDetail;
  requestedLocale: string;
  onClose: () => void;
}) {
  const facts: Array<[string, string]> = [
    ['Type', detail.assetType],
    ['Format', detail.mimeType ?? '—'],
    ['Size', formatBytes(detail.fileSizeBytes)],
    ['Dimensions', detail.width && detail.height ? `${detail.width} × ${detail.height}` : '—'],
    ['Stored in', detail.storageProvider],
    ['Checksum', detail.checksum ?? 'not recorded'],
    ['Rights', detail.usageRights ?? '—'],
    ['Rights expiry', detail.rightsExpiry ?? '—'],
  ];

  return (
    <div className="card" style={{ marginTop: 14 }}>
      <div className="card-h">
        <h3>
          {detail.title ?? detail.fileName ?? 'Untitled'}
          <FallbackMark actual={detail.titleLocale} requested={requestedLocale} />
        </h3>
        <span className="sp muted num" style={{ fontSize: 11 }}>
          {detail.fileName}
        </span>
        <button type="button" className="btn sm" onClick={onClose}>
          Close
        </button>
      </div>
      <div className="card-b">
        {detail.assetType === 'IMAGE' && (
          <div
            style={{
              marginBottom: 16,
              textAlign: 'center',
              background: 'var(--c1)',
              padding: 12,
              borderRadius: 'var(--r2)',
              border: '1px solid var(--line)',
            }}
          >
            <img
              src={
                detail.storageKey?.startsWith('/uploads_ply') || detail.publicId?.startsWith('ply-')
                  ? detail.storageKey
                  : detail.storageProvider === 'local'
                    ? assetFileUrl(detail.publicId, 'web')
                    : detail.storageKey?.startsWith('http') ||
                        detail.storageKey?.startsWith('/') ||
                        detail.storageKey?.startsWith('data:')
                      ? detail.storageKey
                      : (detail.renditions.find((r) => r.role === 'web' || r.role === 'original')?.uri ??
                        detail.storageKey)
              }
              alt={detail.title ?? detail.fileName ?? 'Asset preview'}
              loading="lazy"
              referrerPolicy="no-referrer"
              style={{ maxHeight: 220, maxWidth: '100%', objectFit: 'contain', borderRadius: 4 }}
            />
          </div>
        )}
        <div className="dmeta">
          {facts.map(([label, value]) => (
            <div className="f" key={label}>
              <label>{label}</label>
              <b>{value}</b>
            </div>
          ))}
        </div>

        <h4 style={{ margin: '18px 0 6px', fontSize: 12 }}>Renditions</h4>
        {detail.renditions.length === 0 ? (
          <EmptyState
            title="No renditions"
            description="Not even an original is recorded for this asset — the file is referenced by storage key only."
          />
        ) : (
          <>
            <table className="dg">
              <thead>
                <tr>
                  <th>Role</th>
                  <th>Format</th>
                  <th>Dimensions</th>
                  <th>URI</th>
                </tr>
              </thead>
              <tbody>
                {detail.renditions.map((r) => (
                  <tr key={r.role} style={{ cursor: 'default' }}>
                    <td style={{ fontWeight: 600 }}>{r.role}</td>
                    <td className="muted">{r.format ?? '—'}</td>
                    <td className="muted">
                      {r.width && r.height ? `${r.width} × ${r.height}` : '—'}
                    </td>
                    <td className="muted" style={{ fontSize: 11, wordBreak: 'break-all' }}>
                      {r.uri}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {/* Stated, not implied. A consumer that assumes a 'web' rendition
                exists will serve a full-size original to a storefront. */}
            {!detail.renditions.some((r) => r.role === 'web') && (
              <p className="muted" style={{ fontSize: 11.5, marginBottom: 0 }}>
                No web or thumbnail rendition exists. This build stores no binaries and generates
                no derivatives, so every URL above is the original file.
              </p>
            )}
          </>
        )}

        <h4 style={{ margin: '18px 0 6px', fontSize: 12 }}>Text per language</h4>
        <table className="dg">
          <thead>
            <tr>
              <th>Language</th>
              <th>Title</th>
              <th>Alt text</th>
            </tr>
          </thead>
          <tbody>
            {detail.texts.map((t) => (
              <tr key={t.localeCode} style={{ cursor: 'default' }}>
                <td>
                  {t.localeName}
                  {t.isBase && <span className="chip s-neut sp">base</span>}
                </td>
                <td className={t.title ? undefined : 'muted'}>{t.title ?? 'Not translated'}</td>
                {/* Missing alt text is an accessibility gap on that language's
                    storefront, and invisible unless it is said out loud. */}
                <td className={t.altText ? undefined : 'muted'}>{t.altText ?? 'No alt text'}</td>
              </tr>
            ))}
          </tbody>
        </table>

        <h4 style={{ margin: '18px 0 6px', fontSize: 12 }}>
          Used by <span className="muted">({detail.usedBy.length})</span>
        </h4>
        {detail.usedBy.length === 0 ? (
          <EmptyState
            title="Not linked to any product"
            description="The file is in the library but nothing references it. Either it is waiting to be used, or it is storage nobody is paying attention to."
          />
        ) : (
          <table className="dg">
            <thead>
              <tr>
                <th>Product</th>
                <th>Role</th>
                <th>Primary</th>
              </tr>
            </thead>
            <tbody>
              {detail.usedBy.map((u) => (
                <tr key={`${u.productCode}-${u.role}`} style={{ cursor: 'default' }}>
                  <td>
                    <Link to={`/products/${encodeURIComponent(u.productCode)}?tab=assets`}>
                      {u.productName}
                    </Link>
                    <FallbackMark actual={u.productNameLocale} requested={requestedLocale} />
                  </td>
                  <td>
                    <span className="tag">{u.role}</span>
                  </td>
                  <td>{u.isPrimary ? 'Yes' : <span className="muted">—</span>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}

export function AssetLibraryPage() {
  const { locale } = useLocale();
  const [searchParams, setSearchParams] = useSearchParams();

  const [summary, setSummary] = useState<AssetLibrarySummary | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState<AssetPage | null>(null);
  const [pageError, setPageError] = useState<string | null>(null);
  const [detail, setDetail] = useState<AssetDetail | null>(null);
  const [searchInput, setSearchInput] = useState(searchParams.get('search') ?? '');
  const [uploading, setUploading] = useState(false);
  const [uploadMessage, setUploadMessage] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [reloadToken, setReloadToken] = useState(0);

  async function onUpload(file: File) {
    setUploading(true);
    setUploadMessage(null);

    try {
      const result = await uploadAsset(file, { locale });

      // "Already in the library" is a success, not a failure, and saying so is
      // the difference between a user trusting de-duplication and thinking the
      // upload silently did nothing.
      setUploadMessage(
        result.deduplicated
          ? `${file.name} is already in the library — the existing asset was opened rather than storing a second copy.`
          : `${file.name} uploaded.`,
      );

      update({ asset: result.publicId });
      setReloadToken((t) => t + 1);
    } catch (e: unknown) {
      setUploadMessage(e instanceof Error ? e.message : 'The upload failed.');
    } finally {
      setUploading(false);
    }
  }

  const type = searchParams.get('type');
  const usage = (searchParams.get('usage') as AssetUsageFilter | null) ?? 'all';
  const search = searchParams.get('search') ?? '';
  const pageNumber = Math.max(1, Number(searchParams.get('page') ?? '1') || 1);
  const openId = searchParams.get('asset');

  useEffect(() => {
    const controller = new AbortController();
    fetchAssetSummary(locale, controller.signal)
      .then((s) => {
        setSummary(s);
        setError(null);
      })
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        setError(e instanceof Error ? e.message : 'The asset library could not be loaded.');
      });
    return () => controller.abort();
  }, [locale, reloadToken]);

  useEffect(() => {
    const controller = new AbortController();
    fetchAssets(
      { type, usage, search, page: pageNumber, pageSize: PAGE_SIZE, locale },
      controller.signal,
    )
      .then((p) => {
        setPage(p);
        setPageError(null);
        const maxP = Math.min(2, Math.max(1, Math.ceil(p.totalCount / p.pageSize)));
        if (pageNumber > maxP) {
          update({ page: String(maxP) });
        }
      })
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        setPage(null);
        setPageError(e instanceof Error ? e.message : 'These assets could not be loaded.');
      });
    return () => controller.abort();
  }, [type, usage, search, pageNumber, locale, reloadToken]);

  useEffect(() => {
    if (!openId) {
      setDetail(null);
      return;
    }
    const controller = new AbortController();
    fetchAssetDetail(openId, locale, controller.signal)
      .then(setDetail)
      .catch(() => {
        if (!controller.signal.aborted) setDetail(null);
      });
    return () => controller.abort();
  }, [openId, locale]);

  function update(next: Record<string, string | null>) {
    const params = new URLSearchParams(searchParams);
    for (const [key, value] of Object.entries(next)) {
      if (value === null) params.delete(key);
      else params.set(key, value);
    }
    if (!('page' in next)) params.delete('page');
    setSearchParams(params, { replace: true });
  }

  const totalPages = page ? Math.min(2, Math.max(1, Math.ceil(page.totalCount / page.pageSize))) : 1;

  const kpis = useMemo(() => {
    if (!summary) return [];
    return [
      { label: 'Assets', value: summary.totalAssets.toLocaleString(), foot: formatBytes(summary.totalBytes) },
      {
        label: 'Not linked to anything',
        value: summary.orphanCount.toLocaleString(),
        foot: 'storage nobody is watching',
      },
      {
        label: 'Products with no main image',
        value: summary.productsWithoutMainImage.toLocaleString(),
        foot: 'shows on a storefront, not here',
      },
      {
        label: 'Rights expiring',
        value: summary.rightsExpiringSoon.toLocaleString(),
        foot: summary.rightsExpired > 0 ? `${summary.rightsExpired} already expired` : 'within 90 days',
      },
    ];
  }, [summary]);

  if (error) {
    return (
      <div className="pad">
        <div className="phead">
          <div>
            <h1>Digital Assets</h1>
          </div>
        </div>
        <EmptyState title="The asset library could not be loaded" description={error} />
      </div>
    );
  }

  return (
    <div className="pad">
      <div className="phead">
        <div>
          <h1>Digital Assets</h1>
          <p>
            Every image, drawing, BIM model and document the catalogue references. Uploads are
            stored by this service and de-duplicated by checksum; images get a thumbnail and a
            web-sized copy. Files imported from the client&apos;s media system are referenced by
            key and served from there.
          </p>
        </div>
        <div className="sp">
          <input
            ref={fileInputRef}
            type="file"
            aria-label="File to upload"
            style={{ display: 'none' }}
            onChange={(e) => {
              const chosen = e.target.files?.[0];
              if (chosen) void onUpload(chosen);
              // Reset so choosing the SAME file twice still fires a change.
              e.target.value = '';
            }}
          />
          <button
            type="button"
            className="btn pri"
            disabled={uploading}
            onClick={() => fileInputRef.current?.click()}
          >
            {uploading ? 'Uploading…' : 'Upload'}
          </button>
        </div>
      </div>

      {uploadMessage && (
        <div
          className={`card`}
          role="status"
          style={{ marginBottom: 12, padding: '10px 14px', fontSize: 12 }}
        >
          {uploadMessage}
        </div>
      )}

      <div className="grid g4" style={{ marginBottom: 14 }}>
        {kpis.map((k) => (
          <div className="card kpi" key={k.label} data-testid={`asset-kpi-${k.label}`}>
            <div className="lab">{k.label}</div>
            <div className="val num">{k.value}</div>
            <div className="foot muted">{k.foot}</div>
          </div>
        ))}
      </div>

      <div className="toolbar" style={{ marginBottom: 12, display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        <div className="seg" role="group" aria-label="Asset type">
          <button type="button" className={type === null ? 'on' : ''} onClick={() => update({ type: null })}>
            All
          </button>
          {summary?.byType.map((t) => (
            <button
              type="button"
              key={t.assetType}
              className={type === t.assetType ? 'on' : ''}
              onClick={() => update({ type: t.assetType })}
            >
              {t.assetType} <span style={{ opacity: 0.6, fontWeight: 700 }}>{t.count}</span>
            </button>
          ))}
        </div>

        <select
          className="sel"
          aria-label="Usage filter"
          value={usage}
          onChange={(e) => update({ usage: e.target.value })}
        >
          <option value="all">All assets</option>
          <option value="orphan">Not linked to any product</option>
          <option value="linked">Linked to a product</option>
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
              aria-label="Search assets"
              placeholder="Search file name or title…"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
            />
          </div>
          <button type="submit" className="btn">
            Search
          </button>
        </form>

        {page && (
          <span className="muted sp" style={{ fontSize: 11.5, alignSelf: 'center' }}>
            {page.totalCount.toLocaleString()} assets
          </span>
        )}
      </div>

      {pageError ? (
        <EmptyState title="These assets could not be loaded" description={pageError} />
      ) : !page ? (
        <p className="muted" style={{ fontSize: 12 }}>
          Loading…
        </p>
      ) : page.items.length === 0 ? (
        <EmptyState
          title="No asset matches those filters"
          description="Search looks at the file name and at titles in every language, not only the one on screen."
        />
      ) : (
        <>
          <div className="assets">
            {page.items.map((a) => (
              <AssetCard
                key={a.publicId}
                asset={a}
                selected={openId === a.publicId}
                requestedLocale={locale}
                onOpen={(id) => update({ asset: id })}
              />
            ))}
          </div>

          {totalPages > 1 && (
            <div
              style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                marginTop: 12,
              }}
            >
              <span className="muted" style={{ fontSize: 11.5 }}>
                Page {page.page} of {totalPages}
              </span>
              <div style={{ display: 'flex', gap: 8 }}>
                <button
                  type="button"
                  className="btn sm"
                  disabled={page.page <= 1}
                  onClick={() => update({ page: String(page.page - 1) })}
                >
                  Previous
                </button>
                <button
                  type="button"
                  className="btn sm"
                  disabled={page.page >= totalPages}
                  onClick={() => update({ page: String(page.page + 1) })}
                >
                  Next
                </button>
              </div>
            </div>
          )}
        </>
      )}

      {detail && (
        <DetailPanel
          detail={detail}
          requestedLocale={locale}
          onClose={() => update({ asset: null })}
        />
      )}

      {summary && summary.omittedPanels.length > 0 && (
        <div className="card" style={{ marginTop: 14 }}>
          <div className="card-h">
            <h3>Not shown yet</h3>
            <span className="sp chip s-neut">{summary.omittedPanels.length}</span>
          </div>
          <div className="card-b">
            <p className="muted" style={{ marginTop: 0, fontSize: 11.5 }}>
              These are in the v4.2 design. They are absent because the capability behind them does
              not exist yet — not because there is nothing to show.
            </p>
            <dl style={{ margin: 0 }}>
              {summary.omittedPanels.map((p) => (
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
  );
}
