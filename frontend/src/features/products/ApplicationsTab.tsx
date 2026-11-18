import { useCallback, useEffect, useMemo, useState } from 'react';
import { ApiError } from '../../api/client';
import { Button } from '../../components/Button';
import { useToast } from '../../components/Toast';
import { getProductApplications, setProductApplications } from './productsApi';
import type { ProductApplications } from './types';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'loaded'; data: ProductApplications };

export interface ApplicationsTabProps {
  code: string;
  onChanged?: (rowVersion: number) => void;
}

export function ApplicationsTab({ code, onChanged }: ApplicationsTabProps) {
  const { showToast } = useToast();
  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const adopt = useCallback((data: ProductApplications) => {
    setState({ kind: 'loaded', data });
    setSelected(
      new Set(
        data.groups.flatMap((g) => g.options.filter((o) => o.isAssigned).map((o) => o.code)),
      ),
    );
  }, []);

  const load = useCallback(
    (signal?: { aborted: boolean }) => {
      setState({ kind: 'loading' });
      getProductApplications(code)
        .then((data) => {
          if (!signal?.aborted) {
            adopt(data);
          }
        })
        .catch((error: unknown) => {
          if (!signal?.aborted) {
            setState({
              kind: 'error',
              message: error instanceof ApiError ? error.message : 'Could not load applications.',
            });
          }
        });
    },
    [code, adopt],
  );

  useEffect(() => {
    const signal = { aborted: false };
    load(signal);
    return () => {
      signal.aborted = true;
    };
  }, [load]);

  /**
   * The saved set, for comparison. Selection is local until saved, so the tab
   * must be able to tell the steward whether they have unsaved changes.
   */
  const assigned = useMemo(
    () =>
      state.kind === 'loaded'
        ? new Set(
            state.data.groups.flatMap((g) =>
              g.options.filter((o) => o.isAssigned).map((o) => o.code),
            ),
          )
        : new Set<string>(),
    [state],
  );

  const isDirty =
    assigned.size !== selected.size || [...selected].some((code) => !assigned.has(code));

  function toggle(optionCode: string) {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(optionCode)) {
        next.delete(optionCode);
      } else {
        next.add(optionCode);
      }
      return next;
    });
  }

  async function onSave() {
    if (state.kind !== 'loaded') {
      return;
    }

    setBusy(true);
    setSaveError(null);

    try {
      const updated = await setProductApplications(code, {
        applicationCodes: [...selected],
        rowVersion: state.data.rowVersion,
      });

      adopt(updated);
      onChanged?.(updated.rowVersion);
      showToast('Applications saved');
    } catch (error: unknown) {
      setSaveError(
        error instanceof ApiError ? error.message : 'Could not save the applications.',
      );
    } finally {
      setBusy(false);
    }
  }

  if (state.kind === 'loading') {
    return (
      <p className="muted" role="status">
        Loading applications…
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
        Where this product may be used. Specifiers filter on these, so an over-broad selection is
        as unhelpful as an empty one.
      </p>

      <div className="grid g2">
        {state.data.groups.map((group) => (
          <section className="card" key={group.groupCode} aria-label={group.groupLabel}>
            <div className="card-h">
              <h3>{group.groupLabel}</h3>
              <span className="muted">
                {group.options.filter((o) => selected.has(o.code)).length} of {group.options.length}
              </span>
            </div>
            <div className="card-b">
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 7 }}>
                {group.options.map((option) => {
                  const isOn = selected.has(option.code);
                  return (
                    <button
                      key={option.code}
                      type="button"
                      aria-pressed={isOn}
                      disabled={busy}
                      onClick={() => toggle(option.code)}
                      className={isOn ? 'chip s-pri' : 'chip s-neut'}
                      style={{ cursor: 'pointer', border: 'none' }}
                    >
                      {option.label}
                    </button>
                  );
                })}
              </div>
            </div>
          </section>
        ))}
      </div>

      {saveError && (
        <div className="alert err" role="alert">
          {saveError}
        </div>
      )}

      <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginTop: 14 }}>
        <Button variant="primary" disabled={busy || !isDirty} onClick={onSave}>
          {busy ? 'Saving…' : 'Save applications'}
        </Button>
        {isDirty && (
          <Button
            disabled={busy}
            onClick={() => {
              setSelected(new Set(assigned));
              setSaveError(null);
            }}
          >
            Discard changes
          </Button>
        )}
        <span className="muted" style={{ fontSize: 11.5 }}>
          {isDirty ? 'Unsaved changes' : 'All changes saved'}
        </span>
      </div>
    </>
  );
}
