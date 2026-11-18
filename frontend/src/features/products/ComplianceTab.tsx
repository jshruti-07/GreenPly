import { FormEvent, useCallback, useEffect, useState } from 'react';
import { ApiError } from '../../api/client';
import { Button } from '../../components/Button';
import { Chip } from '../../components/Chip';
import { Drawer } from '../../components/Drawer';
import { EmptyState } from '../../components/EmptyState';
import { SelectField, TextField } from '../../components/Field';
import { Table, Td, Th } from '../../components/Table';
import { useToast } from '../../components/Toast';
import {
  addComplianceDocument,
  getProductCompliance,
  removeComplianceDocument,
  updateComplianceDocument,
} from './productsApi';
import type { ComplianceDocument, ExpiryState, ProductCompliance } from './types';
import { COMPLIANCE_DOC_TYPES, COMPLIANCE_STATUSES } from './types';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'loaded'; data: ProductCompliance };

export interface ComplianceTabProps {
  code: string;
  onChanged?: (rowVersion: number) => void;
}

/**
 * Expiry tone. An expired certificate must never read as healthy — in this
 * industry it is the difference between a sellable product and one that isn't.
 */
const EXPIRY_TONE: Record<ExpiryState, 'ok' | 'warn' | 'bad' | 'neut'> = {
  Valid: 'ok',
  NoExpiry: 'neut',
  Expiring: 'warn',
  Expired: 'bad',
};

// Deliberately NOT the word "Valid": the adjacent Status column already uses
// it for the administrative state, and two columns reading "Valid" for
// different things is exactly the ambiguity a compliance screen cannot afford.
const EXPIRY_LABEL: Record<ExpiryState, string> = {
  Valid: 'In date',
  NoExpiry: 'No expiry',
  Expiring: 'Expiring',
  Expired: 'Expired',
};

const DOC_TYPE_LABEL: Record<string, string> = {
  ISI: 'ISI — Bureau of Indian Standards (IS:710 / IS:303 / IS:10701 / IS:5509)',
  FSC: 'FSC — Forest Stewardship Council',
  CARB: 'CARB Phase 2 (Zero Emission E0)',
  EPA: 'EPA TSCA Title VI',
  IGBC: 'IGBC Green Building Certified',
  TEST: 'Laboratory Test Report',
  DOP: 'DoP — Declaration of Performance',
  CE: 'CE marking',
  EPD: 'EPD — Environmental Product Declaration',
  DPP: 'DPP — Digital Product Passport',
  UKCA: 'UKCA marking',
};

const STATUS_LABEL: Record<string, string> = {
  valid: 'Valid',
  withdrawn: 'Withdrawn',
  superseded: 'Superseded',
};

/** ISO date to a readable form; the API returns plain yyyy-MM-dd. */
function formatDate(iso: string | null): string {
  if (!iso) {
    return '—';
  }

  const date = new Date(`${iso}T00:00:00Z`);
  return Number.isNaN(date.getTime())
    ? iso
    : date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: '2-digit' });
}

export function ComplianceTab({ code, onChanged }: ComplianceTabProps) {
  const { showToast } = useToast();
  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [editing, setEditing] = useState<ComplianceDocument | null>(null);
  const [adding, setAdding] = useState(false);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const apply = useCallback(
    (data: ProductCompliance) => {
      setState({ kind: 'loaded', data });
      onChanged?.(data.rowVersion);
    },
    [onChanged],
  );

  const load = useCallback(
    (signal?: { aborted: boolean }) => {
      setState({ kind: 'loading' });
      getProductCompliance(code)
        .then((data) => {
          if (!signal?.aborted) {
            setState({ kind: 'loaded', data });
          }
        })
        .catch((error: unknown) => {
          if (!signal?.aborted) {
            setState({
              kind: 'error',
              message: error instanceof ApiError ? error.message : 'Could not load compliance data.',
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
    setAdding(false);
    setEditing(null);
    setFormError(null);
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (state.kind !== 'loaded') {
      return;
    }

    const form = new FormData(event.currentTarget);
    const text = (key: string) => String(form.get(key) ?? '').trim() || null;

    const base = {
      docType: String(form.get('docType') ?? 'CE'),
      reference: text('reference'),
      standard: text('standard'),
      authority: text('authority'),
      validFrom: text('validFrom'),
      validTo: text('validTo'),
      rowVersion: state.data.rowVersion,
    };

    setBusy(true);
    setFormError(null);

    try {
      const updated = editing
        ? await updateComplianceDocument(code, editing.id, {
          ...base,
          status: String(form.get('status') ?? 'valid'),
        })
        : await addComplianceDocument(code, base);

      apply(updated);
      showToast(editing ? 'Certificate updated' : 'Certificate added');
      closeDrawer();
    } catch (error: unknown) {
      setFormError(
        error instanceof ApiError
          ? error.message
          : 'Could not save the certificate. Please try again.',
      );
    } finally {
      setBusy(false);
    }
  }

  async function onRemove(document: ComplianceDocument) {
    if (state.kind !== 'loaded') {
      return;
    }

    setBusy(true);
    try {
      const updated = await removeComplianceDocument(code, document.id, state.data.rowVersion);
      apply(updated);
      showToast(`Certificate ${document.reference ?? document.docType} removed`);
    } catch (error: unknown) {
      showToast(
        error instanceof ApiError ? error.message : 'Could not remove the certificate.',
        'error',
      );
    } finally {
      setBusy(false);
    }
  }

  if (state.kind === 'loading') {
    return (
      <p className="muted" role="status">
        Loading compliance data…
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

  const { documents, indicators, materials, substances, greenBuilding, expiringCount, expiredCount } =
    state.data;

  return (
    <>
      {expiredCount > 0 && (
        <div className="alert err" role="alert" style={{ marginBottom: 12 }}>
          {expiredCount} certification{expiredCount === 1 ? ' has' : 's have'} expired — this
          product may not be sellable until they are renewed.
        </div>
      )}

      {expiringCount > 0 && (
        <div className="alert warn" role="status" style={{ marginBottom: 12 }}>
          {expiringCount} certification{expiringCount === 1 ? '' : 's'} expiring within 12 months —
          schedule renewal.
        </div>
      )}

      <div className="card" style={{ marginBottom: 14 }}>
        <div className="card-h">
          <h3>Performance certifications</h3>
          <span className="muted">
            {documents.length} {documents.length === 1 ? 'document' : 'documents'}
          </span>
          <div className="sp">
            <Button variant="primary" onClick={() => setAdding(true)} disabled={busy}>
              Add certificate
            </Button>
          </div>
        </div>

        <div className="card-b" style={{ padding: 0 }}>
          {documents.length === 0 ? (
            <div style={{ padding: 16 }}>
              <EmptyState
                title="No certificates recorded"
                description="Record the DoP, CE marking and test reports this product relies on."
                action={
                  <Button variant="primary" onClick={() => setAdding(true)}>
                    Add certificate
                  </Button>
                }
              />
            </div>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>Standard</Th>
                  <Th>Certificate №</Th>
                  <Th>Authority</Th>
                  <Th>Valid from</Th>
                  <Th>Valid to</Th>
                  <Th>Expiry</Th>
                  <Th>Status</Th>
                  <Th aria-label="Actions" />
                </tr>
              </thead>
              <tbody>
                {documents.map((document) => (
                  <tr key={document.id}>
                    <Td style={{ fontWeight: 600 }}>{document.standard ?? document.docType}</Td>
                    <Td className="num">
                      {document.reference ?? <span className="muted">—</span>}
                    </Td>
                    <Td>{document.authority ?? <span className="muted">—</span>}</Td>
                    <Td className="num">{formatDate(document.validFrom)}</Td>
                    <Td className="num">{formatDate(document.validTo)}</Td>
                    <Td>
                      <Chip tone={EXPIRY_TONE[document.expiryState] ?? 'neut'} dot>
                        {EXPIRY_LABEL[document.expiryState] ?? document.expiryState}
                      </Chip>
                    </Td>
                    <Td className="muted">
                      {STATUS_LABEL[document.status] ?? document.status}
                    </Td>
                    <Td>
                      <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                        <Button size="sm" disabled={busy} onClick={() => setEditing(document)}>
                          Edit
                        </Button>
                        <Button
                          size="sm"
                          variant="danger"
                          disabled={busy}
                          onClick={() => onRemove(document)}
                        >
                          Remove
                        </Button>
                      </div>
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
          )}
        </div>
      </div>

      <div className="grid g2">
        {indicators.length > 0 && (
          <section className="card" aria-label="Environmental indicators">
            <div className="card-h">
              <h3>Environmental — EPD</h3>
            </div>
            <div className="card-b">
              <dl className="facts">
                {indicators.map((indicator) => (
                  <div key={indicator.indicator} style={{ display: 'contents' }}>
                    <dt>{indicator.indicator}</dt>
                    <dd className="num">
                      {indicator.value ?? '—'} {indicator.unit ?? ''}
                      {indicator.stage && <span className="muted"> · {indicator.stage}</span>}
                    </dd>
                  </div>
                ))}
              </dl>
            </div>
          </section>
        )}

        {materials.length > 0 && (
          <section className="card" aria-label="Material composition">
            <div className="card-h">
              <h3>Material &amp; circularity</h3>
            </div>
            <div className="card-b">
              <Table>
                <thead>
                  <tr>
                    <Th>Material</Th>
                    <Th>Share</Th>
                    <Th>Recyclable</Th>
                  </tr>
                </thead>
                <tbody>
                  {materials.map((material) => (
                    <tr key={material.material}>
                      <Td>
                        {material.material}
                        {material.isHazardous && (
                          <Chip tone="bad" dot>
                            Hazardous
                          </Chip>
                        )}
                      </Td>
                      <Td className="num">{material.pct === null ? '—' : `${material.pct}%`}</Td>
                      <Td className="muted">
                        {material.isRecyclable === null
                          ? '—'
                          : material.isRecyclable
                            ? 'Yes'
                            : 'No'}
                      </Td>
                    </tr>
                  ))}
                </tbody>
              </Table>
            </div>
          </section>
        )}

        {substances.length > 0 && (
          <section className="card" aria-label="Substance compliance">
            <div className="card-h">
              <h3>Substance regimes</h3>
            </div>
            <div className="card-b">
              <Table>
                <thead>
                  <tr>
                    <Th>Regime</Th>
                    <Th>Result</Th>
                    <Th>Threshold</Th>
                  </tr>
                </thead>
                <tbody>
                  {substances.map((substance) => (
                    <tr key={substance.regime}>
                      <Td style={{ fontWeight: 600 }}>{substance.regime}</Td>
                      <Td>
                        <Chip tone={substance.result === 'compliant' ? 'ok' : 'warn'} dot>
                          {substance.result}
                        </Chip>
                      </Td>
                      <Td className="muted">{substance.threshold ?? '—'}</Td>
                    </tr>
                  ))}
                </tbody>
              </Table>
            </div>
          </section>
        )}

        {greenBuilding.length > 0 && (
          <section className="card" aria-label="Green building contributions">
            <div className="card-h">
              <h3>Green building</h3>
            </div>
            <div className="card-b">
              <dl className="facts">
                {greenBuilding.map((entry) => (
                  <div key={`${entry.scheme}-${entry.credit}`} style={{ display: 'contents' }}>
                    <dt>{entry.scheme}</dt>
                    <dd>
                      {entry.credit}
                      {entry.note && <span className="muted"> · {entry.note}</span>}
                    </dd>
                  </div>
                ))}
              </dl>
            </div>
          </section>
        )}
      </div>

      <p className="muted" style={{ fontSize: 11.5, marginTop: 12 }}>
        Environmental, material and substance data is read-only in this release — it is derived from
        the EPD and supplier declarations rather than entered by hand.
      </p>

      <Drawer
        open={adding || editing !== null}
        title={editing ? `Edit ${editing.reference ?? editing.docType}` : 'Add certificate'}
        onClose={closeDrawer}
        footer={
          <>
            <Button onClick={closeDrawer} disabled={busy}>
              Cancel
            </Button>
            <Button variant="primary" type="submit" form="compliance-form" disabled={busy}>
              {busy ? 'Saving…' : editing ? 'Save certificate' : 'Create certificate'}
            </Button>
          </>
        }
      >
        <form id="compliance-form" onSubmit={onSubmit}>
          <div className="fgrid">
            <div className="full">
              <SelectField
                label="Document type"
                name="docType"
                defaultValue={editing?.docType ?? 'CE'}
                options={COMPLIANCE_DOC_TYPES.map((type) => ({
                  value: type,
                  label: DOC_TYPE_LABEL[type] ?? type,
                }))}
              />
            </div>

            <TextField
              label="Certificate №"
              name="reference"
              defaultValue={editing?.reference ?? ''}
              hint="Must be unique on this product."
            />
            <TextField
              label="Standard"
              name="standard"
              defaultValue={editing?.standard ?? ''}
              placeholder="EN 1634-1"
            />
            <TextField
              label="Authority"
              name="authority"
              defaultValue={editing?.authority ?? ''}
              placeholder="TÜV Rheinland"
            />

            <TextField
              label="Valid from"
              name="validFrom"
              type="date"
              defaultValue={editing?.validFrom ?? ''}
            />
            <TextField
              label="Valid to"
              name="validTo"
              type="date"
              defaultValue={editing?.validTo ?? ''}
              hint="Leave empty for documents that do not expire, such as a DoP."
            />

            {editing && (
              <SelectField
                label="Status"
                name="status"
                defaultValue={editing.status}
                options={COMPLIANCE_STATUSES.map((status) => ({
                  value: status,
                  label: STATUS_LABEL[status] ?? status,
                }))}
                hint="Withdraw or supersede a document that was ever relied on, rather than deleting it."
              />
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
