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
  addPackagingUnit,
  getProductPackaging,
  removePackagingUnit,
  updatePackagingUnit,
} from './productsApi';
import type { PackagingUnit, ProductPackaging } from './types';
import { PACKAGING_LEVELS } from './types';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'loaded'; data: ProductPackaging };

export interface PackagingTabProps {
  code: string;
  onChanged?: (rowVersion: number) => void;
}

const LEVEL_LABEL: Record<string, string> = {
  EACH: 'Sheet (Individual piece)',
  INNER: 'Pack / Sub-bundle',
  CARTON: 'Banded Bundle / Crate',
  LAYER: 'Layer',
  PALLET: 'Pallet / Shipping Skids',
};

/** Reads a numeric form field, treating blank as "not recorded". */
function num(form: FormData, key: string): number | null {
  const raw = String(form.get(key) ?? '').trim();
  if (raw.length === 0) {
    return null;
  }

  const parsed = Number(raw);
  return Number.isFinite(parsed) ? parsed : null;
}

function dimensions(unit: PackagingUnit): string {
  const { lengthMm, widthMm, heightMm } = unit;
  return lengthMm && widthMm && heightMm ? `${lengthMm} × ${widthMm} × ${heightMm} mm` : '—';
}

export function PackagingTab({ code, onChanged }: PackagingTabProps) {
  const { showToast } = useToast();
  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [addingTo, setAddingTo] = useState<string | null>(null);
  const [editing, setEditing] = useState<PackagingUnit | null>(null);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const apply = useCallback(
    (data: ProductPackaging) => {
      setState({ kind: 'loaded', data });
      onChanged?.(data.rowVersion);
    },
    [onChanged],
  );

  const load = useCallback(
    (signal?: { aborted: boolean }) => {
      setState({ kind: 'loading' });
      getProductPackaging(code)
        .then((data) => {
          if (!signal?.aborted) {
            setState({ kind: 'loaded', data });
          }
        })
        .catch((error: unknown) => {
          if (!signal?.aborted) {
            setState({
              kind: 'error',
              message: error instanceof ApiError ? error.message : 'Could not load packaging.',
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
    setAddingTo(null);
    setEditing(null);
    setFormError(null);
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (state.kind !== 'loaded') {
      return;
    }

    const form = new FormData(event.currentTarget);
    const shared = {
      containsQty: num(form, 'containsQty') ?? 1,
      gtin: String(form.get('gtin') ?? '').trim() || null,
      sscc: String(form.get('sscc') ?? '').trim() || null,
      grossWeight: num(form, 'grossWeight'),
      netWeight: num(form, 'netWeight'),
      lengthMm: num(form, 'lengthMm'),
      widthMm: num(form, 'widthMm'),
      heightMm: num(form, 'heightMm'),
      isBaseUnit: form.get('isBaseUnit') === 'on',
      dangerousGoods: form.get('dangerousGoods') === 'on',
      unClass: String(form.get('unClass') ?? '').trim() || null,
      rowVersion: state.data.rowVersion,
    };

    setBusy(true);
    setFormError(null);

    try {
      const updated = editing
        ? await updatePackagingUnit(code, editing.id, shared)
        : await addPackagingUnit(code, {
          ...shared,
          skuCode: addingTo ?? '',
          level: String(form.get('level') ?? 'EACH'),
          parentLevel: String(form.get('parentLevel') ?? '').trim() || null,
        });

      apply(updated);
      showToast(editing ? 'Packaging updated' : 'Packaging level added');
      closeDrawer();
    } catch (error: unknown) {
      setFormError(
        error instanceof ApiError ? error.message : 'Could not save the packaging level.',
      );
    } finally {
      setBusy(false);
    }
  }

  async function onRemove(unit: PackagingUnit) {
    if (state.kind !== 'loaded') {
      return;
    }

    setBusy(true);
    try {
      const updated = await removePackagingUnit(code, unit.id, state.data.rowVersion);
      apply(updated);
      showToast(`${LEVEL_LABEL[unit.level] ?? unit.level} removed`);
    } catch (error: unknown) {
      showToast(
        error instanceof ApiError ? error.message : 'Could not remove the packaging level.',
        'error',
      );
    } finally {
      setBusy(false);
    }
  }

  if (state.kind === 'loading') {
    return (
      <p className="muted" role="status">
        Loading packaging…
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
  const drawerSku = skus.find((s) => s.skuCode === addingTo);
  const takenLevels = new Set(drawerSku?.units.map((u) => u.level) ?? []);

  return (
    <>
      <p className="muted" style={{ fontSize: 11.5, margin: '0 0 12px' }}>
        Packaging belongs to the SKU, not the product — two variants can ship differently, and the
        GTIN a wholesaler scans identifies the packaging level rather than the article.
      </p>

      {skus.length === 0 && (
        <EmptyState
          title="No variants yet"
          description="Packaging hangs off a SKU. Add a variant first, then record how it ships."
        />
      )}

      {skus.map((sku) => (
        <div className="card" key={sku.skuCode} style={{ marginBottom: 14 }}>
          <div className="card-h">
            <h3>{sku.skuCode}</h3>
            <span className="muted">
              {sku.units.length} {sku.units.length === 1 ? 'level' : 'levels'}
            </span>
            <div className="sp">
              <Button size="sm" disabled={busy} onClick={() => setAddingTo(sku.skuCode)}>
                Add level
              </Button>
            </div>
          </div>

          <div className="card-b" style={{ padding: 0 }}>
            {sku.units.length === 0 ? (
              <p className="muted" style={{ padding: 16, margin: 0, fontSize: 12 }}>
                No packaging recorded for this variant.
              </p>
            ) : (
              <Table>
                <thead>
                  <tr>
                    <Th>Level</Th>
                    <Th>Contains</Th>
                    <Th>GTIN / SSCC</Th>
                    <Th>Gross</Th>
                    <Th>Dimensions</Th>
                    <Th aria-label="Actions" />
                  </tr>
                </thead>
                <tbody>
                  {sku.units.map((unit) => (
                    <tr key={unit.id}>
                      <Td>
                        <span style={{ fontWeight: 600 }}>
                          {LEVEL_LABEL[unit.level] ?? unit.level}
                        </span>
                        {unit.isBaseUnit && (
                          <Chip tone="info" dot>
                            Orderable
                          </Chip>
                        )}
                        {unit.dangerousGoods && (
                          <Chip tone="bad" dot>
                            {unit.unClass ?? 'Dangerous goods'}
                          </Chip>
                        )}
                      </Td>
                      <Td className="num">
                        {unit.containsQty}
                        {unit.parentLevel && (
                          <span className="muted">
                            {' '}
                            in {LEVEL_LABEL[unit.parentLevel] ?? unit.parentLevel}
                          </span>
                        )}
                      </Td>
                      <Td className="num">
                        {unit.gtin ?? unit.sscc ?? <span className="muted">—</span>}
                      </Td>
                      <Td className="num">
                        {unit.grossWeight === null ? '—' : `${unit.grossWeight} kg`}
                      </Td>
                      <Td className="num muted">{dimensions(unit)}</Td>
                      <Td>
                        <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                          <Button size="sm" disabled={busy} onClick={() => setEditing(unit)}>
                            Edit
                          </Button>
                          <Button
                            size="sm"
                            variant="danger"
                            disabled={busy}
                            onClick={() => onRemove(unit)}
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
      ))}

      <Drawer
        open={addingTo !== null || editing !== null}
        title={
          editing
            ? `Edit ${LEVEL_LABEL[editing.level] ?? editing.level}`
            : `Add packaging level to ${addingTo ?? ''}`
        }
        onClose={closeDrawer}
        footer={
          <>
            <Button onClick={closeDrawer} disabled={busy}>
              Cancel
            </Button>
            <Button variant="primary" type="submit" form="packaging-form" disabled={busy}>
              {busy ? 'Saving…' : editing ? 'Save level' : 'Create level'}
            </Button>
          </>
        }
      >
        <form id="packaging-form" onSubmit={onSubmit}>
          <div className="fgrid">
            {editing ? (
              <div className="full">
                <TextField
                  label="Level"
                  value={LEVEL_LABEL[editing.level] ?? editing.level}
                  readOnly
                />
                <p className="hint">
                  The level is immutable — remove and re-add to change where it sits.
                </p>
              </div>
            ) : (
              <>
                <SelectField
                  label="Level"
                  name="level"
                  options={PACKAGING_LEVELS.filter((l) => !takenLevels.has(l)).map((l) => ({
                    value: l,
                    label: LEVEL_LABEL[l] ?? l,
                  }))}
                  hint="Each level may only appear once per variant."
                />
                <SelectField
                  label="Contained by"
                  name="parentLevel"
                  options={[
                    { value: '', label: '— Nothing (outermost) —' },
                    ...[...takenLevels].map((l) => ({ value: l, label: LEVEL_LABEL[l] ?? l })),
                  ]}
                  hint="Must be a larger level than this one."
                />
              </>
            )}

            <TextField
              label="Contains quantity"
              name="containsQty"
              type="number"
              min="0"
              step="any"
              defaultValue={editing?.containsQty ?? 1}
              hint="How many of the level below fit inside this one."
            />
            <TextField
              label="UN class"
              name="unClass"
              defaultValue={editing?.unClass ?? ''}
              placeholder="UN1950"
            />

            <TextField label="GTIN" name="gtin" defaultValue={editing?.gtin ?? ''} hint="8-14 digits" />
            <TextField
              label="SSCC"
              name="sscc"
              defaultValue={editing?.sscc ?? ''}
              hint="Exactly 18 digits"
            />

            <TextField
              label="Net weight (kg)"
              name="netWeight"
              type="number"
              min="0"
              step="any"
              defaultValue={editing?.netWeight ?? ''}
            />
            <TextField
              label="Gross weight (kg)"
              name="grossWeight"
              type="number"
              min="0"
              step="any"
              defaultValue={editing?.grossWeight ?? ''}
              hint="Includes the packaging, so never below net."
            />

            <TextField
              label="Length (mm)"
              name="lengthMm"
              type="number"
              min="0"
              step="any"
              defaultValue={editing?.lengthMm ?? ''}
            />
            <TextField
              label="Width (mm)"
              name="widthMm"
              type="number"
              min="0"
              step="any"
              defaultValue={editing?.widthMm ?? ''}
            />
            <TextField
              label="Height (mm)"
              name="heightMm"
              type="number"
              min="0"
              step="any"
              defaultValue={editing?.heightMm ?? ''}
            />

            <div className="full" style={{ display: 'flex', gap: 18, marginTop: 4 }}>
              <label style={{ display: 'flex', alignItems: 'center', gap: 7, fontSize: 12 }}>
                <input type="checkbox" name="isBaseUnit" defaultChecked={editing?.isBaseUnit} />
                Orderable unit
              </label>
              <label style={{ display: 'flex', alignItems: 'center', gap: 7, fontSize: 12 }}>
                <input
                  type="checkbox"
                  name="dangerousGoods"
                  defaultChecked={editing?.dangerousGoods}
                />
                Dangerous goods
              </label>
            </div>
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
