import { useCallback, useEffect, useState } from 'react';
import { ApiError } from '../../api/client';
import { Button } from '../../components/Button';
import { Chip } from '../../components/Chip';
import { EmptyState } from '../../components/EmptyState';
import { QualityBar } from '../../components/QualityBar';
import { Table, Td, Th } from '../../components/Table';
import { getProductPlanning } from './productsApi';
import type { ProductPlanning } from './types';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'loaded'; data: ProductPlanning };

export interface PlanningTabProps {
  code: string;
  /** Switches the detail page to another tab, for the "edit these" links. */
  onNavigateToTab?: (tabId: string) => void;
}

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

/**
 * Booleans read as words on this tab. A planner scanning a property set for
 * "is it frost resistant" should not have to decode `true`.
 */
function formatValue(value: string | null, dataType: string): string {
  if (value === null || value === '') {
    return '—';
  }

  if (dataType === 'boolean') {
    return value === 'true' ? 'Yes' : 'No';
  }

  return value;
}

/**
 * Planning & BIM.
 *
 * Composed from three modules that already exist rather than being a module of
 * its own: parameters are attributes in the 'planning' group, references are
 * classification schemes, geometry files are assets typed BIM or CAD.
 *
 * That is also why this tab is read-only. Each panel links to the tab that owns
 * the write, so there is exactly one place to change any given value — three
 * editors over the same rows would need three copies of the concurrency rules.
 *
 * There is deliberately no "Publish to BIM" button. Publishing needs the channel
 * service, which does not exist (ADR-004); a button that only raises a toast
 * would suggest to a client team that syndication works.
 */
export function PlanningTab({ code, onNavigateToTab }: PlanningTabProps) {
  const [state, setState] = useState<LoadState>({ kind: 'loading' });

  const load = useCallback(
    (signal?: { aborted: boolean }) => {
      setState({ kind: 'loading' });
      getProductPlanning(code)
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
                error instanceof ApiError ? error.message : 'Could not load planning data.',
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

  if (state.kind === 'loading') {
    return (
      <p className="muted" role="status">
        Loading planning data…
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

  const { bimReady, parameters, parameterCount, parameterFilledCount, bimReferences, geometryFiles } =
    state.data;

  const filledPct =
    parameterCount === 0 ? 0 : Math.round((parameterFilledCount / parameterCount) * 100);

  return (
    <>
      <p className="muted" style={{ fontSize: 11.5, margin: '0 0 12px' }}>
        What a specifier needs to place this product in a model. Everything here is edited on
        another tab — parameters on Attributes, references on Classifications, files on Media &amp;
        Documents — so there is one place to change each value.
      </p>

      <section className="card" aria-label="BIM readiness" style={{ marginBottom: 14 }}>
        <div className="card-h">
          <h3>BIM readiness</h3>
          <div className="sp">
            {bimReady === null ? (
              <Chip tone="neut">Not recorded</Chip>
            ) : bimReady ? (
              <Chip tone="ok" dot>
                BIM object available
              </Chip>
            ) : (
              <Chip tone="neut">No BIM object</Chip>
            )}
          </div>
        </div>
        <div className="card-b">
          <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
            <div style={{ minWidth: 220 }}>
              <QualityBar value={filledPct} />
            </div>
            <span className="muted" style={{ fontSize: 12 }}>
              {parameterFilledCount} of {parameterCount} planning parameters filled
            </span>
          </div>
          <p className="muted" style={{ fontSize: 11.5, marginTop: 10, marginBottom: 0 }}>
            Publishing to a BIM portal needs the channel service, which is not built yet — so this
            tab reports readiness rather than offering a publish action.
          </p>
        </div>
      </section>

      <section className="card" aria-label="Planning parameters" style={{ marginBottom: 14 }}>
        <div className="card-h">
          <h3>Planning parameters</h3>
          <span className="muted">{parameterCount} parameters</span>
          {onNavigateToTab && (
            <div className="sp">
              <Button size="sm" onClick={() => onNavigateToTab('attributes')}>
                Edit on Attributes
              </Button>
            </div>
          )}
        </div>
        <div className="card-b" style={{ padding: 0 }}>
          {parameters.length === 0 ? (
            <div style={{ padding: 16 }}>
              <EmptyState
                title="No planning parameters"
                description="This product's attribute set does not include the planning group."
              />
            </div>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>Parameter</Th>
                  <Th>Value</Th>
                  <Th>Unit</Th>
                  <Th>State</Th>
                </tr>
              </thead>
              <tbody>
                {parameters.map((parameter) => (
                  <tr key={parameter.code}>
                    <Td>
                      <span style={{ fontWeight: 600 }}>{parameter.label}</span>
                      <div className="muted num" style={{ fontSize: 10.5 }}>
                        {parameter.code}
                      </div>
                    </Td>
                    <Td className={parameter.dataType === 'text' ? undefined : 'num'}>
                      {formatValue(parameter.value, parameter.dataType)}
                    </Td>
                    <Td className="muted">{parameter.unitSymbol ?? '—'}</Td>
                    <Td>
                      {parameter.value === null || parameter.value === '' ? (
                        <Chip tone="neut">Not set</Chip>
                      ) : (
                        <Chip tone="ok" dot>
                          Set
                        </Chip>
                      )}
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
          )}
        </div>
      </section>

      <section className="card" aria-label="BIM classification references" style={{ marginBottom: 14 }}>
        <div className="card-h">
          <h3>Classification references</h3>
          <span className="muted">
            {bimReferences.filter((r) => r.classCode !== null).length} of {bimReferences.length}{' '}
            assigned
          </span>
          {onNavigateToTab && (
            <div className="sp">
              <Button size="sm" onClick={() => onNavigateToTab('classifications')}>
                Edit on Classifications
              </Button>
            </div>
          )}
        </div>
        <div className="card-b" style={{ padding: 0 }}>
          <Table>
            <thead>
              <tr>
                <Th>Scheme</Th>
                <Th>Version</Th>
                <Th>Class</Th>
                <Th>Label</Th>
              </tr>
            </thead>
            <tbody>
              {bimReferences.map((reference) => (
                <tr key={reference.schemeCode}>
                  <Td>
                    <span style={{ fontWeight: 600 }}>{reference.schemeName}</span>
                  </Td>
                  <Td className="muted num">{reference.schemeVersion}</Td>
                  <Td className="num">
                    {reference.classCode ?? <Chip tone="neut">Unassigned</Chip>}
                  </Td>
                  <Td className="muted">{reference.classLabel ?? '—'}</Td>
                </tr>
              ))}
            </tbody>
          </Table>
        </div>
      </section>

      <section className="card" aria-label="Geometry and planning files">
        <div className="card-h">
          <h3>Geometry &amp; planning files</h3>
          <span className="muted">
            {geometryFiles.length} {geometryFiles.length === 1 ? 'file' : 'files'}
          </span>
          {onNavigateToTab && (
            <div className="sp">
              <Button size="sm" onClick={() => onNavigateToTab('assets')}>
                Manage in Media &amp; Documents
              </Button>
            </div>
          )}
        </div>
        <div className="card-b" style={{ padding: 0 }}>
          {geometryFiles.length === 0 ? (
            <div style={{ padding: 16 }}>
              <EmptyState
                title="No geometry files"
                description="IFC, Revit and CAD files linked to this product appear here."
              />
            </div>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>File</Th>
                  <Th>Format</Th>
                  <Th>Role</Th>
                  <Th>Size</Th>
                  <Th>Location</Th>
                </tr>
              </thead>
              <tbody>
                {geometryFiles.map((file) => (
                  <tr key={file.linkId}>
                    <Td>
                      <span style={{ fontWeight: 600 }}>
                        {file.title ?? file.fileName ?? file.url}
                      </span>
                      {file.title && file.fileName && (
                        <div className="muted num" style={{ fontSize: 10.5 }}>
                          {file.fileName}
                        </div>
                      )}
                    </Td>
                    <Td>
                      <Chip tone="neut">{file.assetType}</Chip>
                    </Td>
                    <Td className="muted">{file.role}</Td>
                    <Td className="num muted">{formatSize(file.fileSize)}</Td>
                    <Td className="muted">{file.isExternal ? 'External' : 'Managed'}</Td>
                  </tr>
                ))}
              </tbody>
            </Table>
          )}
        </div>
      </section>
    </>
  );
}
