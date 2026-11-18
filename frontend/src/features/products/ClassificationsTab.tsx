import { FormEvent, useCallback, useEffect, useState } from 'react';
import { ApiError } from '../../api/client';
import { Button } from '../../components/Button';
import { Chip } from '../../components/Chip';
import { Drawer } from '../../components/Drawer';
import { SelectField, TextField } from '../../components/Field';
import { Table, Td, Th } from '../../components/Table';
import { useToast } from '../../components/Toast';
import {
  assignClassification,
  getProductClassifications,
  updateClassificationFeatures,
} from './productsApi';
import type {
  ClassificationFeature,
  ProductClassificationScheme,
  ProductClassifications,
} from './types';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'loaded'; data: ProductClassifications };

export interface ClassificationsTabProps {
  code: string;
  onChanged?: (rowVersion: number) => void;
}

/** An unmapped standard is a gap, not a neutral fact — tone it as a warning. */
const STATUS_TONE: Record<string, 'ok' | 'warn' | 'neut'> = {
  mapped: 'ok',
  partial: 'warn',
  unmapped: 'neut',
};

const STATUS_LABEL: Record<string, string> = {
  mapped: 'Mapped',
  partial: 'Partial',
  unmapped: 'Unmapped',
};

/** '12 / 14 features', or a dash for schemes whose class declares none. */
function featureSummary(scheme: ProductClassificationScheme): string {
  return scheme.featureCount === 0
    ? '—'
    : `${scheme.featureFilled} / ${scheme.featureCount} features`;
}

/** Placeholder that tells the steward what shape the value should take. */
function placeholderFor(feature: ClassificationFeature): string {
  switch (feature.dataType) {
    case 'numeric':
      return feature.unit ? `e.g. 1000 (${feature.unit})` : 'e.g. 1000';
    case 'logical':
      return 'true or false';
    case 'range':
      return 'e.g. 10-20';
    default:
      return '';
  }
}

export function ClassificationsTab({ code, onChanged }: ClassificationsTabProps) {
  const { showToast } = useToast();
  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [editingScheme, setEditingScheme] = useState<string | null>(null);
  const [featuresScheme, setFeaturesScheme] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const apply = useCallback(
    (data: ProductClassifications) => {
      setState({ kind: 'loaded', data });
      onChanged?.(data.rowVersion);
    },
    [onChanged],
  );

  const load = useCallback(
    (signal?: { aborted: boolean }) => {
      setState({ kind: 'loading' });
      getProductClassifications(code)
        .then((data) => {
          if (!signal?.aborted) {
            setState({ kind: 'loaded', data });
          }
        })
        .catch((error: unknown) => {
          if (!signal?.aborted) {
            setState({
              kind: 'error',
              message:
                error instanceof ApiError ? error.message : 'Could not load classifications.',
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

  function closeDrawers() {
    setEditingScheme(null);
    setFeaturesScheme(null);
    setFormError(null);
  }

  function schemeByCode(schemeCode: string | null): ProductClassificationScheme | undefined {
    return state.kind === 'loaded'
      ? state.data.schemes.find((s) => s.schemeCode === schemeCode)
      : undefined;
  }

  async function onAssign(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (state.kind !== 'loaded' || editingScheme === null) {
      return;
    }

    const form = new FormData(event.currentTarget);
    const classCode = String(form.get('classCode') ?? '').trim() || null;

    setBusy(true);
    setFormError(null);

    try {
      const updated = await assignClassification(code, editingScheme, {
        classCode,
        rowVersion: state.data.rowVersion,
      });

      apply(updated);
      showToast(classCode ? 'Classification updated' : 'Classification cleared');
      closeDrawers();
    } catch (error: unknown) {
      setFormError(
        error instanceof ApiError
          ? error.message
          : 'Could not save the classification. Please try again.',
      );
    } finally {
      setBusy(false);
    }
  }

  async function onSaveFeatures(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const scheme = schemeByCode(featuresScheme);
    if (state.kind !== 'loaded' || !scheme) {
      return;
    }

    const form = new FormData(event.currentTarget);
    const values = scheme.features.map((feature) => ({
      featureCode: feature.code,
      value: String(form.get(feature.code) ?? '').trim() || null,
    }));

    setBusy(true);
    setFormError(null);

    try {
      const updated = await updateClassificationFeatures(code, scheme.schemeCode, {
        values,
        rowVersion: state.data.rowVersion,
      });

      apply(updated);
      showToast('Feature values saved');
      closeDrawers();
    } catch (error: unknown) {
      setFormError(
        error instanceof ApiError ? error.message : 'Could not save the feature values.',
      );
    } finally {
      setBusy(false);
    }
  }

  if (state.kind === 'loading') {
    return (
      <p className="muted" role="status">
        Loading classifications…
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

  const { schemes } = state.data;
  const editing = schemeByCode(editingScheme);
  const featureScheme = schemeByCode(featuresScheme);

  return (
    <>
      <p className="muted" style={{ fontSize: 11.5, margin: '0 0 12px' }}>
        One product, multiple parallel classifications. Trade partners consume ETIM and BMEcat;
        procurement consumes UNSPSC; SAP consumes DataNorm. Each standard is mapped independently.
      </p>

      <div className="card" style={{ marginBottom: 14 }}>
        <div className="card-h">
          <h3>Classification standards</h3>
        </div>
        <div className="card-b" style={{ padding: 0 }}>
          <Table>
            <thead>
              <tr>
                <Th>Standard</Th>
                <Th>Assigned class</Th>
                <Th>Feature completeness</Th>
                <Th>Status</Th>
                <Th aria-label="Actions" />
              </tr>
            </thead>
            <tbody>
              {schemes.map((scheme) => (
                <tr key={scheme.schemeCode}>
                  <Td>
                    <span style={{ fontWeight: 600 }}>{scheme.schemeName}</span>
                    {scheme.purpose && (
                      <div className="muted" style={{ fontSize: 10.5 }}>
                        {scheme.purpose}
                      </div>
                    )}
                  </Td>
                  <Td>
                    {scheme.classCode ? (
                      <>
                        <span className="num">{scheme.classCode}</span>
                        {scheme.classLabel && <span className="muted"> — {scheme.classLabel}</span>}
                      </>
                    ) : (
                      <span className="muted">Not assigned</span>
                    )}
                  </Td>
                  <Td className="muted">{featureSummary(scheme)}</Td>
                  <Td>
                    <Chip tone={STATUS_TONE[scheme.mappedStatus] ?? 'neut'} dot>
                      {STATUS_LABEL[scheme.mappedStatus] ?? scheme.mappedStatus}
                    </Chip>
                  </Td>
                  <Td>
                    <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                      <Button
                        size="sm"
                        disabled={busy}
                        onClick={() => setEditingScheme(scheme.schemeCode)}
                      >
                        Edit mapping
                      </Button>
                      {scheme.featureCount > 0 && (
                        <Button
                          size="sm"
                          disabled={busy}
                          onClick={() => setFeaturesScheme(scheme.schemeCode)}
                        >
                          Edit features
                        </Button>
                      )}
                    </div>
                  </Td>
                </tr>
              ))}
            </tbody>
          </Table>
        </div>
      </div>

      {schemes
        .filter((scheme) => scheme.featureCount > 0)
        .map((scheme) => {
          const missing = scheme.featureCount - scheme.featureFilled;
          return (
            <div className="card" key={`features-${scheme.schemeCode}`} style={{ marginBottom: 14 }}>
              <div className="card-h">
                <h3>
                  {scheme.schemeName} — feature values ({scheme.classCode})
                </h3>
                <div className="sp">
                  {missing > 0 ? (
                    <Chip tone="warn">{missing} missing</Chip>
                  ) : (
                    <Chip tone="ok">Complete</Chip>
                  )}
                </div>
              </div>
              <div className="card-b" style={{ padding: 0 }}>
                <Table>
                  <thead>
                    <tr>
                      <Th>Feature code</Th>
                      <Th>Feature</Th>
                      <Th>Value</Th>
                      <Th>Unit</Th>
                      <Th>Status</Th>
                    </tr>
                  </thead>
                  <tbody>
                    {scheme.features.map((feature) => (
                      <tr key={feature.code}>
                        <Td className="num" style={{ fontWeight: 600 }}>
                          {feature.code}
                        </Td>
                        <Td>
                          {feature.name ?? feature.code}
                          {feature.isRequired && (
                            <span className="req" aria-label="required">
                              {' '}
                              *
                            </span>
                          )}
                        </Td>
                        <Td>
                          {feature.value ?? (
                            <span className="muted" style={{ fontStyle: 'italic' }}>
                              —
                            </span>
                          )}
                        </Td>
                        <Td className="muted">{feature.unit ?? '—'}</Td>
                        <Td>
                          {feature.value ? (
                            <Chip tone="ok" dot>
                              Approved
                            </Chip>
                          ) : (
                            <Chip tone={feature.isRequired ? 'warn' : 'neut'} dot>
                              Missing
                            </Chip>
                          )}
                        </Td>
                      </tr>
                    ))}
                  </tbody>
                </Table>
              </div>
            </div>
          );
        })}

      <Drawer
        open={editing !== undefined}
        title={editing ? `${editing.schemeName} — assign class` : 'Assign class'}
        onClose={closeDrawers}
        footer={
          <>
            <Button onClick={closeDrawers} disabled={busy}>
              Cancel
            </Button>
            <Button variant="primary" type="submit" form="assign-form" disabled={busy}>
              {busy ? 'Saving…' : 'Save mapping'}
            </Button>
          </>
        }
      >
        {editing && (
          <form id="assign-form" onSubmit={onAssign}>
            <div className="fgrid">
              <div className="full">
                <SelectField
                  label="Class"
                  name="classCode"
                  defaultValue={editing.classCode ?? ''}
                  options={[
                    { value: '', label: '— Not assigned —' },
                    ...editing.availableClasses.map((c) => ({
                      value: c.code,
                      label: c.label ? `${c.code} — ${c.label}` : c.code,
                    })),
                  ]}
                  hint="Changing the class discards feature values the new class does not declare."
                />
              </div>
            </div>

            {formError && (
              <div className="alert err" role="alert">
                {formError}
              </div>
            )}
          </form>
        )}
      </Drawer>

      <Drawer
        open={featureScheme !== undefined}
        title={featureScheme ? `${featureScheme.schemeName} — feature values` : 'Feature values'}
        onClose={closeDrawers}
        footer={
          <>
            <Button onClick={closeDrawers} disabled={busy}>
              Cancel
            </Button>
            <Button variant="primary" type="submit" form="features-form" disabled={busy}>
              {busy ? 'Saving…' : 'Save features'}
            </Button>
          </>
        }
      >
        {featureScheme && (
          <form id="features-form" onSubmit={onSaveFeatures}>
            <div className="fgrid">
              {featureScheme.features.map((feature) => (
                <div className="full" key={feature.code}>
                  <TextField
                    label={`${feature.name ?? feature.code}${feature.isRequired ? ' *' : ''}`}
                    name={feature.code}
                    defaultValue={feature.value ?? ''}
                    placeholder={placeholderFor(feature)}
                    hint={`${feature.code} · ${feature.dataType}${
                      feature.unit ? ` · ${feature.unit}` : ''
                    }`}
                  />
                </div>
              ))}
            </div>

            {formError && (
              <div className="alert err" role="alert">
                {formError}
              </div>
            )}
          </form>
        )}
      </Drawer>
    </>
  );
}
