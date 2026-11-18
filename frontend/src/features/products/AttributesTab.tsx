import { FormEvent, useCallback, useEffect, useState } from 'react';
import { ApiError } from '../../api/client';
import { Button } from '../../components/Button';
import { QualityBar } from '../../components/QualityBar';
import { useToast } from '../../components/Toast';
import { getProductAttributes, updateProductAttributes } from './productsApi';
import type { ProductAttribute, ProductAttributes } from './types';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'loaded'; data: ProductAttributes };

export interface AttributesTabProps {
  code: string;
  /** Lets the parent refresh the header after a save bumps the row version. */
  onSaved?: (rowVersion: number) => void;
}

/**
 * Renders the product's attribute set as a form, one input per data type.
 *
 * The template comes from the server (the product's attribute_set), so the
 * fields shown and the completeness denominator are always the same set —
 * there is no client-side copy of the schema to drift.
 */
export function AttributesTab({ code, onSaved }: AttributesTabProps) {
  const { showToast } = useToast();
  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [draft, setDraft] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const applyLoaded = useCallback((data: ProductAttributes) => {
    setState({ kind: 'loaded', data });
    const values: Record<string, string> = {};
    for (const group of data.groups) {
      for (const attribute of group.attributes) {
        values[attribute.code] = attribute.value ?? '';
      }
    }
    setDraft(values);
  }, []);

  const load = useCallback(
    (signal?: { aborted: boolean }) => {
      setState({ kind: 'loading' });
      getProductAttributes(code)
        .then((data) => {
          if (!signal?.aborted) {
            applyLoaded(data);
          }
        })
        .catch((error: unknown) => {
          if (!signal?.aborted) {
            setState({
              kind: 'error',
              message: error instanceof ApiError ? error.message : 'Could not load attributes.',
            });
          }
        });
    },
    [code, applyLoaded],
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
    if (state.kind !== 'loaded') {
      return;
    }

    // Only send what actually changed: a 500-attribute form must not post
    // 500 writes because one field was touched.
    const changed = Object.entries(draft)
      .filter(([attributeCode, value]) => {
        const original = findAttribute(state.data, attributeCode)?.value ?? '';
        return value !== original;
      })
      .map(([attributeCode, value]) => ({ attributeCode, value: value === '' ? null : value }));

    if (changed.length === 0) {
      showToast('No changes to save');
      return;
    }

    setSaving(true);
    setFormError(null);

    try {
      const updated = await updateProductAttributes(code, {
        values: changed,
        rowVersion: state.data.rowVersion,
      });
      applyLoaded(updated);
      onSaved?.(updated.rowVersion);
      showToast(`Saved ${changed.length} attribute${changed.length === 1 ? '' : 's'}`);
    } catch (error: unknown) {
      setFormError(
        error instanceof ApiError ? error.message : 'Could not save attributes. Please try again.',
      );
    } finally {
      setSaving(false);
    }
  }

  if (state.kind === 'loading') {
    return (
      <p className="muted" role="status">
        Loading attributes…
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

  const { data } = state;

  return (
    <form onSubmit={onSubmit}>
      <div className="card pad" style={{ marginBottom: 14 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
          <div style={{ minWidth: 220 }}>
            <div className="muted" style={{ fontSize: 11.5, fontWeight: 600 }}>
              Completeness
            </div>
            <QualityBar value={data.completenessPct} />
          </div>
          <span className="muted">
            {data.requiredFilledCount} of {data.requiredCount} required attributes filled
          </span>
          <div style={{ marginLeft: 'auto', display: 'flex', gap: 8 }}>
            <Button variant="primary" type="submit" disabled={saving}>
              {saving ? 'Saving…' : 'Save attributes'}
            </Button>
          </div>
        </div>
        {formError && (
          <div className="alert err" role="alert">
            {formError}
          </div>
        )}
      </div>

      {data.groups.map((group) => (
        <section className="card" key={group.code} style={{ marginBottom: 14 }}>
          <div className="card-h">
            <h3>{group.name}</h3>
            <span className="sp muted">{group.attributes.length} attributes</span>
          </div>
          <div className="card-b">
            <div className="fgrid">
              {group.attributes.map((attribute) => (
                <AttributeField
                  key={attribute.code}
                  attribute={attribute}
                  value={draft[attribute.code] ?? ''}
                  onChange={(value) =>
                    setDraft((current) => ({ ...current, [attribute.code]: value }))
                  }
                />
              ))}
            </div>
          </div>
        </section>
      ))}
    </form>
  );
}

function findAttribute(data: ProductAttributes, code: string): ProductAttribute | undefined {
  for (const group of data.groups) {
    const hit = group.attributes.find((attribute) => attribute.code === code);
    if (hit) {
      return hit;
    }
  }
  return undefined;
}

interface AttributeFieldProps {
  attribute: ProductAttribute;
  value: string;
  onChange: (value: string) => void;
}

/** One input, chosen by the attribute's data type. */
function AttributeField({ attribute, value, onChange }: AttributeFieldProps) {
  const label = `${attribute.label}${attribute.unitSymbol ? ` (${attribute.unitSymbol})` : ''}`;
  const id = `attr-${attribute.code}`;

  if (!attribute.isEditable) {
    return (
      <div className="field">
        <label htmlFor={id}>{label}</label>
        <input id={id} className="fld" value={value} readOnly aria-readonly="true" />
        <div className="hint">
          {attribute.dataType} attributes are not editable yet — a richer editor ships later.
        </div>
      </div>
    );
  }

  const control = (() => {
    switch (attribute.dataType) {
      case 'boolean':
        return (
          <select
            id={id}
            className="sel"
            value={value}
            onChange={(event) => onChange(event.target.value)}
          >
            <option value="">—</option>
            <option value="true">Yes</option>
            <option value="false">No</option>
          </select>
        );

      case 'list':
        return (
          <select
            id={id}
            className="sel"
            value={value}
            onChange={(event) => onChange(event.target.value)}
          >
            <option value="">—</option>
            {attribute.options.map((option) => (
              <option key={option.code} value={option.code}>
                {option.label}
              </option>
            ))}
          </select>
        );

      case 'date':
        return (
          <input
            id={id}
            className="fld"
            type="date"
            value={value}
            onChange={(event) => onChange(event.target.value)}
          />
        );

      case 'number':
        return (
          <input
            id={id}
            className="fld"
            type="number"
            step="1"
            value={value}
            onChange={(event) => onChange(event.target.value)}
          />
        );

      case 'decimal':
      case 'measure':
        return (
          <input
            id={id}
            className="fld"
            type="number"
            step="any"
            value={value}
            onChange={(event) => onChange(event.target.value)}
          />
        );

      default:
        return (
          <input
            id={id}
            className="fld"
            value={value}
            onChange={(event) => onChange(event.target.value)}
          />
        );
    }
  })();

  return (
    <div className="field">
      <label htmlFor={id}>
        {label}
        {attribute.isRequired && (
          <span className="req" aria-hidden="true">
            {' '}
            *
          </span>
        )}
      </label>
      {control}
      {attribute.isLocalizable && (
        <div className="hint">Localizable — editing the base locale ({attribute.dataType})</div>
      )}
    </div>
  );
}
