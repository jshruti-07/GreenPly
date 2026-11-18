import { FormEvent, useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError } from '../../api/client';
import { Button } from '../../components/Button';
import { Chip, StatusBadge } from '../../components/Chip';
import { Drawer } from '../../components/Drawer';
import { SelectField, TextField } from '../../components/Field';
import { useToast } from '../../components/Toast';
import { addRelationship, getProductRelationships, removeRelationship } from './productsApi';
import type { ProductRelationship, ProductRelationships } from './types';
import { RELATION_TYPES } from './types';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'loaded'; data: ProductRelationships };

export interface RelationshipsTabProps {
  code: string;
  onChanged?: (rowVersion: number) => void;
}

const TYPE_LABEL: Record<string, string> = {
  ACCESSORY: 'Accessories',
  COMPATIBLE: 'Compatible products',
  REPLACEMENT: 'Replacement parts',
  CROSS_SELL: 'Cross-sell',
  UP_SELL: 'Up-sell',
};

const TYPE_HINT: Record<string, string> = {
  ACCESSORY: 'Fitted or sold alongside this product.',
  COMPATIBLE: 'Verified to work together.',
  REPLACEMENT: 'Spare parts a service engineer would order.',
  CROSS_SELL: 'Commonly bought together.',
  UP_SELL: 'A higher specification of the same thing.',
};

export function RelationshipsTab({ code, onChanged }: RelationshipsTabProps) {
  const { showToast } = useToast();
  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [addingType, setAddingType] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const apply = useCallback(
    (data: ProductRelationships) => {
      setState({ kind: 'loaded', data });
      onChanged?.(data.rowVersion);
    },
    [onChanged],
  );

  const load = useCallback(
    (signal?: { aborted: boolean }) => {
      setState({ kind: 'loading' });
      getProductRelationships(code)
        .then((data) => {
          if (!signal?.aborted) {
            setState({ kind: 'loaded', data });
          }
        })
        .catch((error: unknown) => {
          if (!signal?.aborted) {
            setState({
              kind: 'error',
              message: error instanceof ApiError ? error.message : 'Could not load relationships.',
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

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (state.kind !== 'loaded' || addingType === null) {
      return;
    }

    const form = new FormData(event.currentTarget);
    setBusy(true);
    setFormError(null);

    try {
      const updated = await addRelationship(code, {
        relationType: addingType,
        targetCode: String(form.get('targetCode') ?? '').trim(),
        priority: Number(form.get('priority') ?? 0) || 0,
        rowVersion: state.data.rowVersion,
      });

      apply(updated);
      showToast('Link added');
      setAddingType(null);
      setFormError(null);
    } catch (error: unknown) {
      setFormError(error instanceof ApiError ? error.message : 'Could not add the link.');
    } finally {
      setBusy(false);
    }
  }

  async function onRemove(item: ProductRelationship) {
    if (state.kind !== 'loaded') {
      return;
    }

    setBusy(true);
    try {
      const updated = await removeRelationship(
        code,
        item.relationType,
        item.targetCode,
        state.data.rowVersion,
      );
      apply(updated);
      showToast(`Link to ${item.targetCode} removed`);
    } catch (error: unknown) {
      showToast(
        error instanceof ApiError ? error.message : 'Could not remove the link.',
        'error',
      );
    } finally {
      setBusy(false);
    }
  }

  if (state.kind === 'loading') {
    return (
      <p className="muted" role="status">
        Loading relationships…
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

  return (
    <>
      <p className="muted" style={{ fontSize: 11.5, margin: '0 0 12px' }}>
        Links are one-directional and are not mirrored automatically — an accessory of this door is
        not necessarily a product that has this door as an accessory.
      </p>

      <div className="grid g2">
        {state.data.groups.map((group) => (
          <section
            className="card"
            key={group.relationType}
            aria-label={TYPE_LABEL[group.relationType] ?? group.relationType}
          >
            <div className="card-h">
              <h3>{TYPE_LABEL[group.relationType] ?? group.relationType}</h3>
              <Chip tone="neut">{group.items.length}</Chip>
              <div className="sp">
                <Button
                  size="sm"
                  disabled={busy}
                  onClick={() => setAddingType(group.relationType)}
                >
                  Add link
                </Button>
              </div>
            </div>

            <div className="card-b">
              <p className="muted" style={{ fontSize: 11, marginTop: 0 }}>
                {TYPE_HINT[group.relationType]}
              </p>

              {group.items.length === 0 ? (
                <p className="muted" style={{ fontSize: 12, margin: 0 }}>
                  None linked.
                </p>
              ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                  {group.items.map((item) => (
                    <div
                      key={`${item.relationType}-${item.targetCode}`}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: 10,
                        padding: 8,
                        border: '1px solid var(--line)',
                        borderRadius: 8,
                      }}
                    >
                      <div style={{ flex: 1, minWidth: 0 }}>
                        <Link to={`/products/${encodeURIComponent(item.targetCode)}`}>
                          {item.targetName ?? item.targetCode}
                        </Link>
                        <div className="muted num" style={{ fontSize: 10.5 }}>
                          {item.targetCode}
                        </div>
                      </div>
                      <StatusBadge status={item.targetWorkflowState} />
                      <Button
                        size="sm"
                        variant="danger"
                        disabled={busy}
                        onClick={() => onRemove(item)}
                      >
                        Remove
                      </Button>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </section>
        ))}
      </div>

      <Drawer
        open={addingType !== null}
        title={addingType ? `Add ${TYPE_LABEL[addingType] ?? addingType}` : 'Add link'}
        onClose={() => {
          setAddingType(null);
          setFormError(null);
        }}
        footer={
          <>
            <Button
              onClick={() => {
                setAddingType(null);
                setFormError(null);
              }}
              disabled={busy}
            >
              Cancel
            </Button>
            <Button variant="primary" type="submit" form="relationship-form" disabled={busy}>
              {busy ? 'Saving…' : 'Create link'}
            </Button>
          </>
        }
      >
        <form id="relationship-form" onSubmit={onSubmit}>
          <div className="fgrid">
            <div className="full">
              <TextField
                label="Product code"
                name="targetCode"
                required
                placeholder="DH-CL-300"
                hint="The code of the product to link to."
              />
            </div>
            <TextField
              label="Priority"
              name="priority"
              type="number"
              min="0"
              defaultValue={0}
              hint="Lower sorts first."
            />
            <SelectField
              label="Relationship"
              name="relationTypeDisplay"
              value={addingType ?? ''}
              disabled
              options={RELATION_TYPES.map((t) => ({ value: t, label: TYPE_LABEL[t] ?? t }))}
              hint="Set by the card you opened this from."
            />
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
