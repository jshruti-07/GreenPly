import { FormEvent, useCallback, useEffect, useState } from 'react';
import { ApiError, apiUrl } from '../../api/client';
import { Button } from '../../components/Button';
import { Chip } from '../../components/Chip';
import { Drawer } from '../../components/Drawer';
import { EmptyState } from '../../components/EmptyState';
import { SelectField, TextField } from '../../components/Field';
import { Table, Td, Th } from '../../components/Table';
import { useToast } from '../../components/Toast';
import { uploadAsset } from '../assets/assetLibraryApi';
import {
  addProductAsset,
  getProductAssets,
  removeProductAsset,
  resetProductAssets,
  setPrimaryAsset,
} from './productsApi';
import { getPlywoodImageForRole } from './plywoodSvgImages';
import type { ProductAsset, ProductAssets } from './types';
import { ASSET_ROLES } from './types';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'loaded'; data: ProductAssets };

export interface AssetsTabProps {
  code: string;
  onChanged?: (rowVersion: number) => void;
}

const ROLE_LABEL: Record<string, string> = {
  MAIN: 'Main image',
  GALLERY: 'Gallery',
  DATASHEET: 'Datasheet',
  INSTALL: 'Installation guide',
  DRAWING: 'CAD drawing',
  BIM: 'BIM object',
  VIDEO: 'Video',
  SPIN: '360° spin',
};

const TYPE_LABEL: Record<string, string> = {
  IMAGE: 'Image',
  DOCUMENT: 'Document',
  BIM: 'BIM',
  CAD: 'CAD',
  VIDEO: 'Video',
  SPIN_360: '360°',
};

/** Roles a document-shaped asset can take; used to bound the add form. */
const DOCUMENT_ROLES = ['DATASHEET', 'INSTALL', 'DRAWING', 'BIM', 'VIDEO'];

function formatSize(bytes: number | null): string {
  if (bytes === null) {
    return '—';
  }

  if (bytes < 1024) {
    return `${bytes} B`;
  }

  const kb = bytes / 1024;
  return kb < 1024 ? `${Math.round(kb)} KB` : `${(kb / 1024).toFixed(1)} MB`;
}



export function normalizeGoogleImageUrl(inputUrl: string): string {
  if (!inputUrl) return '';
  const trimmed = inputUrl.trim().replace(/^["']|["']$/g, '');
  try {
    if (trimmed.includes('google.') && (trimmed.includes('/imgres') || trimmed.includes('/url') || trimmed.includes('/search'))) {
      const parsed = new URL(trimmed);
      const direct =
        parsed.searchParams.get('imgurl') ||
        parsed.searchParams.get('url') ||
        parsed.searchParams.get('q');
      if (direct && (direct.startsWith('http://') || direct.startsWith('https://'))) {
        return direct;
      }
    }
  } catch {
    // not a valid URL structure
  }
  return trimmed;
}

/**
 * One image tile.
 *
 * Thumbnails come from the client's own server, so a failure to load is an
 * expected state rather than a bug: the record is correct and the host is
 * simply unreachable from here. Saying that plainly beats a broken-image icon,
 * which reads as "the PIM lost your picture".
 */
function AssetThumbnail({ asset }: { asset: ProductAsset }) {
  const [failed, setFailed] = useState(false);

  if (failed) {
    return (
      <div
        className="asset-thumb err"
        role="img"
        aria-label={`${asset.fileName ?? 'Image'} — preview unavailable`}
      >
        <span>Preview unavailable</span>
      </div>
    );
  }

  const fallbackUrl = getPlywoodImageForRole(
    asset.role,
    asset.sortOrder - 1,
    asset.fileName ?? '',
  );

  return (
    <img
      className="asset-thumb"
      src={asset.url || fallbackUrl}
      alt={asset.altText ?? asset.title ?? asset.fileName ?? 'Product image'}
      loading="lazy"
      referrerPolicy="no-referrer"
      onError={() => {
        setFailed(true);
      }}
    />
  );
}

export function AssetsTab({ code, onChanged }: AssetsTabProps) {
  const { showToast } = useToast();
  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [adding, setAdding] = useState<'media' | 'document' | null>(null);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [fileDataUrl, setFileDataUrl] = useState<string | null>(null);
  const [uploadedFileName, setUploadedFileName] = useState<string | null>(null);

  const apply = useCallback(
    (data: ProductAssets) => {
      setState({ kind: 'loaded', data });
      onChanged?.(data.rowVersion);
    },
    [onChanged],
  );

  const load = useCallback(
    (signal?: { aborted: boolean }) => {
      setState({ kind: 'loading' });
      getProductAssets(code)
        .then((data) => {
          if (!signal?.aborted) {
            setState({ kind: 'loaded', data });
          }
        })
        .catch((error: unknown) => {
          if (!signal?.aborted) {
            setState({
              kind: 'error',
              message: error instanceof ApiError ? error.message : 'Could not load assets.',
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

  function closeDrawer() {
    setAdding(null);
    setFormError(null);
    setSelectedFile(null);
    setFileDataUrl(null);
    setUploadedFileName(null);
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (state.kind !== 'loaded') {
      return;
    }

    const form = new FormData(event.currentTarget);
    const rawKey = String(form.get('storageKey') ?? '').trim();
    let finalStorageKey = normalizeGoogleImageUrl(rawKey);

    setBusy(true);
    setFormError(null);

    try {
      if (selectedFile) {
        const role = String(form.get('role') ?? (adding === 'media' ? 'GALLERY' : 'DATASHEET'));
        const isVisual = adding === 'media' || role === 'MAIN' || role === 'GALLERY';
        try {
          const uploaded = await uploadAsset(selectedFile, {
            assetType: isVisual ? 'IMAGE' : undefined,
            title: String(form.get('title') ?? '').trim() || selectedFile.name,
            altText: String(form.get('altText') ?? '').trim() || undefined,
          });
          finalStorageKey = apiUrl(`/api/v1/assets/${uploaded.publicId}/file`);
        } catch (uploadErr) {
          if (rawKey) {
            finalStorageKey = rawKey;
          } else if (fileDataUrl) {
            finalStorageKey = fileDataUrl;
          } else {
            throw uploadErr;
          }
        }
      }

      if (!finalStorageKey) {
        setFormError('Please select an image file or enter a valid URL.');
        setBusy(false);
        return;
      }

      const role = String(form.get('role') ?? (adding === 'media' ? 'GALLERY' : 'DATASHEET'));
      const isVisual = adding === 'media' || role === 'MAIN' || role === 'GALLERY';
      const hasRecognizedExtension = /\.(jpe?g|png|webp|gif|svg|avif|bmp|ico|pdf|doc|docx|xls|xlsx|ifc|rvt|dwg|dxf|mp4|mov|webm)($|\?)/i.test(finalStorageKey);

      const updated = await addProductAsset(code, {
        storageKey: finalStorageKey,
        role,
        assetType: hasRecognizedExtension ? null : (isVisual ? 'IMAGE' : null),
        title: String(form.get('title') ?? '').trim() || uploadedFileName || null,
        altText: String(form.get('altText') ?? '').trim() || null,
        rowVersion: state.data.rowVersion,
      });

      apply(updated);
      showToast('Asset linked');
      closeDrawer();
    } catch (error: unknown) {
      setFormError(
        error instanceof ApiError
          ? error.message
          : error instanceof Error
            ? error.message
            : 'Could not link the asset.',
      );
    } finally {
      setBusy(false);
    }
  }

  async function onSetPrimary(asset: ProductAsset) {
    if (state.kind !== 'loaded') {
      return;
    }

    setBusy(true);
    try {
      const updated = await setPrimaryAsset(code, asset.linkId, state.data.rowVersion);
      apply(updated);
      showToast('Primary asset updated');
    } catch (error: unknown) {
      showToast(
        error instanceof ApiError ? error.message : 'Could not set the primary asset.',
        'error',
      );
    } finally {
      setBusy(false);
    }
  }

  async function onRemove(asset: ProductAsset) {
    if (state.kind !== 'loaded') {
      return;
    }

    setBusy(true);
    try {
      const updated = await removeProductAsset(code, asset.linkId, state.data.rowVersion);
      apply(updated);
      showToast(`${asset.fileName ?? 'Asset'} unlinked`);
    } catch (error: unknown) {
      showToast(
        error instanceof ApiError ? error.message : 'Could not unlink the asset.',
        'error',
      );
    } finally {
      setBusy(false);
    }
  }

  if (state.kind === 'loading') {
    return (
      <p className="muted" role="status">
        Loading assets…
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

  const { media, documents } = state.data;
  const externalCount = [...media, ...documents].filter((a) => a.isExternal).length;

  return (
    <>
      <p className="muted" style={{ fontSize: 11.5, margin: '0 0 12px' }}>
        Images and documents are one module — a datasheet is an asset whose type is Document. The
        PIM stores where each file lives, never the file itself.
      </p>

      {externalCount > 0 && (
        <div className="alert warn" role="status" style={{ marginBottom: 12 }}>
          {externalCount} {externalCount === 1 ? 'asset is' : 'assets are'} hosted on an external
          server. Previews load directly from there and may be unavailable from this network.
        </div>
      )}

      <section className="card" aria-label="Media" style={{ marginBottom: 14 }}>
        <div className="card-h">
          <h3>Media</h3>
          <span className="muted">
            {media.length} {media.length === 1 ? 'image' : 'images'}
          </span>
          <div className="sp" style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
            <Button
              variant="ghost"
              disabled={busy}
              onClick={() => {
                if (window.confirm('Reset media and documents for this product back to default plywood assets?')) {
                  const def = resetProductAssets(code);
                  apply(def);
                  showToast('Reset to default assets');
                }
              }}
              title="Reset custom images back to default illustrations"
            >
              Reset to defaults
            </Button>
            <Button variant="primary" disabled={busy} onClick={() => setAdding('media')}>
              Add image
            </Button>
          </div>
        </div>

        <div className="card-b">
          {media.length === 0 ? (
            <EmptyState
              title="No images yet"
              description="Link the product photography that channels and the webshop will use."
            />
          ) : (
            <div className="asset-grid">
              {media.map((asset) => (
                <figure className="asset-card" key={asset.linkId}>
                  <AssetThumbnail asset={asset} />

                  <figcaption>
                    <div className="asset-card-head">
                      <Chip tone={asset.role === 'MAIN' ? 'pri' : 'neut'}>
                        {ROLE_LABEL[asset.role] ?? asset.role}
                      </Chip>
                      {asset.isPrimary && (
                        <Chip tone="ok" dot>
                          Primary
                        </Chip>
                      )}
                    </div>

                    <div className="asset-card-name num" title={asset.fileName ?? asset.url}>
                      {asset.fileName ?? asset.url}
                    </div>

                    <div style={{ display: 'flex', gap: 6, marginTop: 6 }}>
                      {!asset.isPrimary && (
                        <Button size="sm" disabled={busy} onClick={() => onSetPrimary(asset)}>
                          Make primary
                        </Button>
                      )}
                      <Button
                        size="sm"
                        variant="danger"
                        disabled={busy}
                        onClick={() => onRemove(asset)}
                      >
                        Unlink
                      </Button>
                    </div>
                  </figcaption>
                </figure>
              ))}
            </div>
          )}
        </div>
      </section>

      <section className="card" aria-label="Documents">
        <div className="card-h">
          <h3>Documents</h3>
          <span className="muted">
            {documents.length} {documents.length === 1 ? 'file' : 'files'}
          </span>
          <div className="sp">
            <Button variant="primary" disabled={busy} onClick={() => setAdding('document')}>
              Add document
            </Button>
          </div>
        </div>

        <div className="card-b" style={{ padding: 0 }}>
          {documents.length === 0 ? (
            <div style={{ padding: 16 }}>
              <EmptyState
                title="No documents yet"
                description="Datasheets, installation guides, BIM objects and CAD drawings live here."
              />
            </div>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>File</Th>
                  <Th>Role</Th>
                  <Th>Type</Th>
                  <Th>Size</Th>
                  <Th>Location</Th>
                  <Th aria-label="Actions" />
                </tr>
              </thead>
              <tbody>
                {documents.map((asset) => (
                  <tr key={asset.linkId}>
                    <Td>
                      <span style={{ fontWeight: 600 }}>
                        {asset.title ?? asset.fileName ?? asset.url}
                      </span>
                      {asset.title && asset.fileName && (
                        <div className="muted num" style={{ fontSize: 10.5 }}>
                          {asset.fileName}
                        </div>
                      )}
                    </Td>
                    <Td>
                      <Chip tone="neut">{ROLE_LABEL[asset.role] ?? asset.role}</Chip>
                    </Td>
                    <Td className="muted">{TYPE_LABEL[asset.assetType] ?? asset.assetType}</Td>
                    <Td className="num muted">{formatSize(asset.fileSize)}</Td>
                    <Td className="muted">{asset.isExternal ? 'External' : 'Managed'}</Td>
                    <Td>
                      <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                        <Button
                          size="sm"
                          variant="danger"
                          disabled={busy}
                          onClick={() => onRemove(asset)}
                        >
                          Unlink
                        </Button>
                      </div>
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
          )}
        </div>
      </section>

      <Drawer
        open={adding !== null}
        title={adding === 'media' ? 'Add image' : 'Add document'}
        onClose={closeDrawer}
        footer={
          <>
            <Button onClick={closeDrawer} disabled={busy}>
              Cancel
            </Button>
            <Button variant="primary" type="submit" form="asset-form" disabled={busy}>
              {busy ? 'Saving…' : 'Link asset'}
            </Button>
          </>
        }
      >
        {/*
          Keyed on the mode so the fields remount whenever the drawer changes
          purpose or closes.

          Without this the role <select> is mounted once, as a document picker.
          Switching to "Add image" swaps its options to MAIN/GALLERY, but an
          uncontrolled select keeps no memory of a defaultValue past mount and
          the browser falls back to the FIRST option — so every "Add image"
          posted role=MAIN, and the second image a steward added was rejected
          with "this product already has a main image". The re-key also clears
          a URL left in the box from a previous open.
        */}
        <form id="asset-form" key={adding ?? 'closed'} onSubmit={onSubmit}>
          <div className="fgrid">
            {adding === 'media' && (
              <div className="full" style={{ marginBottom: 10 }}>
                <label style={{ display: 'block', fontSize: 12, fontWeight: 600, marginBottom: 6, color: 'var(--ink)' }}>
                  Choose image from your computer
                </label>
                <input
                  type="file"
                  accept="image/*"
                  onChange={(e) => {
                    const file = e.target.files?.[0];
                    if (!file) return;
                    setSelectedFile(file);
                    setUploadedFileName(file.name);
                    try {
                      setFileDataUrl(URL.createObjectURL(file));
                    } catch {
                      const reader = new FileReader();
                      reader.onload = () => {
                        if (typeof reader.result === 'string') {
                          setFileDataUrl(reader.result);
                        }
                      };
                      reader.readAsDataURL(file);
                    }
                  }}
                  style={{
                    display: 'block',
                    width: '100%',
                    padding: '8px 10px',
                    fontSize: 12,
                    border: '1px dashed var(--line2)',
                    borderRadius: 'var(--r2)',
                    background: 'var(--c1)',
                    cursor: 'pointer',
                  }}
                />
                {fileDataUrl && (
                  <div style={{ marginTop: 8, display: 'flex', alignItems: 'center', gap: 10, padding: 8, background: 'var(--c2)', borderRadius: 4 }}>
                    <img
                      src={fileDataUrl}
                      alt="Selected preview"
                      style={{ width: 56, height: 42, objectFit: 'cover', borderRadius: 3, border: '1px solid var(--line)' }}
                    />
                    <div style={{ fontSize: 11.5, color: 'var(--ink)' }}>
                      <b>{uploadedFileName}</b>
                      <div className="muted" style={{ fontSize: 10.5 }}>Ready to link to this product</div>
                    </div>
                  </div>
                )}
                <div style={{ textAlign: 'center', margin: '10px 0 4px', fontSize: 11, color: 'var(--ink3)' }}>
                  — OR paste a Web URL / local path below —
                </div>
              </div>
            )}

            <div className="full">
              <TextField
                label="File URL or storage key"
                name="storageKey"
                required={!fileDataUrl}
                placeholder={adding === 'media' ? 'https://…/image.jpg or /images/sheet.jpg' : 'https://…/datasheet.pdf'}
                hint="The type is inferred from the extension. A key already registered is reused rather than duplicated."
              />
            </div>

            <SelectField
              label="Role"
              name="role"
              defaultValue={adding === 'media' ? 'GALLERY' : 'DATASHEET'}
              options={ASSET_ROLES.filter((r) =>
                adding === 'media' ? r === 'MAIN' || r === 'GALLERY' : DOCUMENT_ROLES.includes(r),
              ).map((r) => ({ value: r, label: ROLE_LABEL[r] ?? r }))}
              hint={
                adding === 'media'
                  ? 'A product may have only one main image.'
                  : 'What the file is for, independent of its format.'
              }
            />

            <TextField label="Title" name="title" placeholder="Technisches Datenblatt" />

            {adding === 'media' && (
              <div className="full">
                <TextField
                  label="Alt text"
                  name="altText"
                  hint="Described for screen readers and used when the image cannot load."
                />
              </div>
            )}
          </div>

          {formError && (
            <div className="alert err" role="alert">
              {formError}
            </div>
          )}
        </form>
      </Drawer>
    </>
  );
}
