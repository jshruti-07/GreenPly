import { FormEvent, useCallback, useEffect, useState } from 'react';
import { ApiError } from '../../api/client';
import { Button } from '../../components/Button';
import { Chip } from '../../components/Chip';
import { Drawer } from '../../components/Drawer';
import { EmptyState } from '../../components/EmptyState';
import { SelectField, TextField } from '../../components/Field';
import { Table, Td, Th } from '../../components/Table';
import { useToast } from '../../components/Toast';
import { addSku, getProductVariants, removeSku, updateSku } from './productsApi';
import type { ProductVariants, Sku } from './types';
import { SKU_STATUSES } from './types';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'loaded'; data: ProductVariants };

export interface VariantsTabProps {
  code: string;
  /** Lets the parent refresh its header after a change bumps the row version. */
  onChanged?: (rowVersion: number) => void;
}

/** SKU status → chip tone. Blocked and EOL must not look healthy. */
const STATUS_TONE: Record<string, 'ok' | 'warn' | 'neut'> = {
  Active: 'ok',
  Blocked: 'warn',
  EndOfLife: 'neut',
};

export function VariantsTab({ code, onChanged }: VariantsTabProps) {
  const { showToast } = useToast();
  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [editing, setEditing] = useState<Sku | null>(null);
  const [adding, setAdding] = useState(false);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const apply = useCallback(
    (data: ProductVariants) => {
      setState({ kind: 'loaded', data });
      onChanged?.(data.rowVersion);
    },
    [onChanged],
  );

  const load = useCallback(
    (signal?: { aborted: boolean }) => {
      setState({ kind: 'loading' });
      getProductVariants(code)
        .then((data) => {
          if (!signal?.aborted) {
            setState({ kind: 'loaded', data });
          }
        })
        .catch((error: unknown) => {
          if (!signal?.aborted) {
            setState({
              kind: 'error',
              message: error instanceof ApiError ? error.message : 'Could not load variants.',
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
    const gtin = String(form.get('gtin') ?? '').trim() || null;
    const erp = String(form.get('erpMaterialNo') ?? '').trim() || null;

    setBusy(true);
    setFormError(null);

    try {
      const updated = editing
        ? await updateSku(code, editing.code, {
            gtin,
            erpMaterialNo: erp,
            status: String(form.get('status') ?? 'Active'),
            rowVersion: state.data.rowVersion,
          })
        : await addSku(code, {
            code: String(form.get('code') ?? '').trim(),
            gtin,
            erpMaterialNo: erp,
            rowVersion: state.data.rowVersion,
          });

      apply(updated);
      showToast(editing ? 'Variant updated' : 'Variant added');
      closeDrawer();
    } catch (error: unknown) {
      setFormError(
        error instanceof ApiError ? error.message : 'Could not save the variant. Please try again.',
      );
    } finally {
      setBusy(false);
    }
  }

  async function onRemove(sku: Sku) {
    if (state.kind !== 'loaded') {
      return;
    }

    setBusy(true);
    try {
      const updated = await removeSku(code, sku.code, state.data.rowVersion);
      apply(updated);
      showToast(`Variant ${sku.code} removed`);
    } catch (error: unknown) {
      showToast(
        error instanceof ApiError ? error.message : 'Could not remove the variant.',
        'error',
      );
    } finally {
      setBusy(false);
    }
  }

  if (state.kind === 'loading') {
    return (
      <p className="muted" role="status">
        Loading variants…
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

  const { skus } = state.data;

  return (
    <>
      <div className="card">
        <div className="card-h">
          <h3>Variants</h3>
          <span className="muted">
            {skus.length} {skus.length === 1 ? 'variant' : 'variants'}
          </span>
          <div className="sp">
            <Button variant="primary" onClick={() => setAdding(true)} disabled={busy}>
              Add variant
            </Button>
          </div>
        </div>

        <div className="card-b" style={{ padding: 0 }}>
          {skus.length === 0 ? (
            <div style={{ padding: 16 }}>
              <EmptyState
                title="No variants yet"
                description="Add a SKU for each saleable combination of this product."
                action={
                  <Button variant="primary" onClick={() => setAdding(true)}>
                    Add variant
                  </Button>
                }
              />
            </div>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>SKU</Th>
                  <Th>GTIN</Th>
                  <Th>ERP number</Th>
                  <Th>Status</Th>
                  <Th aria-label="Actions" />
                </tr>
              </thead>
              <tbody>
                {skus.map((sku) => (
                  <tr key={sku.code}>
                    <Td className="num">{sku.code}</Td>
                    <Td className="num">{sku.gtin ?? <span className="muted">—</span>}</Td>
                    <Td className="num">{sku.erpMaterialNo ?? <span className="muted">—</span>}</Td>
                    <Td>
                      <Chip tone={STATUS_TONE[sku.status] ?? 'neut'} dot>
                        {sku.status}
                      </Chip>
                    </Td>
                    <Td>
                      <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                        <Button size="sm" disabled={busy} onClick={() => setEditing(sku)}>
                          Edit
                        </Button>
                        <Button
                          size="sm"
                          variant="danger"
                          disabled={busy}
                          onClick={() => onRemove(sku)}
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

      <Drawer
        open={adding || editing !== null}
        title={editing ? `Edit ${editing.code}` : 'Add variant'}
        onClose={closeDrawer}
        footer={
          <>
            <Button onClick={closeDrawer} disabled={busy}>
              Cancel
            </Button>
            <Button variant="primary" type="submit" form="variant-form" disabled={busy}>
              {busy ? 'Saving…' : editing ? 'Save variant' : 'Create variant'}
            </Button>
          </>
        }
      >
        <form id="variant-form" onSubmit={onSubmit}>
          <div className="fgrid">
            {editing ? (
              <div className="full">
                <TextField label="SKU code" value={editing.code} readOnly />
                <p className="hint">The SKU code is immutable — remove and re-add to change it.</p>
              </div>
            ) : (
              <div className="full">
                <TextField
                  label="SKU code"
                  name="code"
                  required
                  placeholder="FD-90-GS-1000-L"
                  hint="Uppercase alphanumeric segments separated by hyphens."
                />
              </div>
            )}

            <TextField
              label="GTIN"
              name="gtin"
              defaultValue={editing?.gtin ?? ''}
              hint="8-14 digits"
            />
            <TextField
              label="ERP number"
              name="erpMaterialNo"
              defaultValue={editing?.erpMaterialNo ?? ''}
            />

            {editing && (
              <SelectField
                label="Status"
                name="status"
                defaultValue={editing.status}
                options={SKU_STATUSES.map((status) => ({ value: status, label: status }))}
                hint="Retire a distributed SKU with End of Life rather than removing it."
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
