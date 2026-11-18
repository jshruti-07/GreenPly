import { FormEvent, useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { ApiError } from '../../api/client';
import { Button } from '../../components/Button';
import { StatusBadge } from '../../components/Chip';
import { SelectField, TextField } from '../../components/Field';
import { QualityBar } from '../../components/QualityBar';
import { useToast } from '../../components/Toast';
import { ActivityTab } from './ActivityTab';
import { ApplicationsTab } from './ApplicationsTab';
import { AssetsTab } from './AssetsTab';
import { AttributesTab } from './AttributesTab';
import { ClassificationsTab } from './ClassificationsTab';
import { ComplianceTab } from './ComplianceTab';
import { LocalizationTab } from './LocalizationTab';
import { PackagingTab } from './PackagingTab';
import { PlanningTab } from './PlanningTab';
import { RelationshipsTab } from './RelationshipsTab';
import { VariantsTab } from './VariantsTab';
import { WorkflowTab } from './WorkflowTab';
import { getProductByCode, updateProductGeneral } from './productsApi';
import { stripGreen } from './plywoodAssetsData';
import { getProductWorkflow, performWorkflowAction, type ProductWorkflow } from '../workflow/workflowApi';
import type { Product } from './types';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'loaded'; product: Product };

const OWNERS = ['Anna Becker', 'Markus Klein', 'Lena Vogt', 'Sophie Adler'];

/**
 * Detail tabs shipped so far. Exported so the accessibility suite can assert
 * against the real list instead of a hard-coded count that goes stale every
 * time a tab is added.
 */
export const TABS = [
  { id: 'general', label: 'General' },
  { id: 'attributes', label: 'Attributes' },
  { id: 'variants', label: 'Variants' },
  { id: 'planning', label: 'Planning & BIM' },
  { id: 'classifications', label: 'Classifications' },
  { id: 'compliance', label: 'Compliance' },
  { id: 'packaging', label: 'Packaging' },
  { id: 'relationships', label: 'Relationships' },
  { id: 'applications', label: 'Applications' },
  { id: 'assets', label: 'Media & Documents' },
  { id: 'localization', label: 'Localization' },
  { id: 'workflow', label: 'Workflow' },
  { id: 'activity', label: 'Activity' },
] as const;

type TabId = (typeof TABS)[number]['id'];

function formatDate(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString();
}

/**
 * Product detail — General (Sprint 5), Attributes (Sprint 6), Variants (Sprint 7),
 * Classifications and Compliance (Sprint 8), Packaging, Relationships,
 * Applications and Activity (Sprints 9-10), Media & Documents (Sprint 12),
 * Planning & BIM and Localization (Sprint 13).
 *
 * Edits are optimistic-concurrency safe: the rowVersion loaded with the
 * product is sent back on save, so a concurrent edit returns 409 rather than
 * silently overwriting. Because the child tabs also bump the row version, they
 * call back into load() so the header never holds a stale version.
 *
 * Workflow arrived in Sprint 27 and with it the end of this page deciding
 * anything about the pipeline: the stepper and the transition buttons are both
 * drawn from GET /workflow, which reads workflow_definition. Channels is the
 * one designed tab still missing, and it is blocked on channel tables that do
 * not exist.
 */
export function ProductDetailPage() {
  const { code = '' } = useParams<{ code: string }>();
  const { showToast } = useToast();

  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [transitioning, setTransitioning] = useState(false);
  // Held apart from `state` because the pipeline is a separate document with a
  // separate failure mode: the product can load while the workflow does not.
  const [workflow, setWorkflow] = useState<ProductWorkflow | null>(null);
  /**
   * The open tab lives in the URL.
   *
   * Added in Sprint 24 so the Translation queue can send someone straight to
   * the tab where the fix is made. A queue that lands you on General and leaves
   * you to find Localization yourself is a list, not a workflow.
   *
   * An unrecognised or absent ?tab= falls back to General rather than rendering
   * nothing — a stale bookmark should open the product, not an empty page.
   */
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedTab = searchParams.get('tab');
  const tab: TabId = TABS.some((t) => t.id === requestedTab)
    ? (requestedTab as TabId)
    : 'general';

  const setTab = useCallback(
    (next: TabId) => {
      const params = new URLSearchParams(searchParams);
      params.set('tab', next);
      // replace: switching tabs is not navigation. Leaving a history entry per
      // tab means Back walks the tab strip instead of returning to the grid.
      setSearchParams(params, { replace: true });
    },
    [searchParams, setSearchParams],
  );
  const tabRefs = useRef(new Map<TabId, HTMLButtonElement>());

  /**
   * Arrow-key navigation, per the WAI-ARIA tabs pattern. Without it the tablist
   * announces itself as tabs to a screen reader and then does not behave like
   * them, which is worse than plain buttons would have been.
   */
  function onTabKeyDown(event: React.KeyboardEvent<HTMLButtonElement>, index: number) {
    const lastIndex = TABS.length - 1;
    let next: number | null = null;

    switch (event.key) {
      case 'ArrowRight':
        next = index === lastIndex ? 0 : index + 1;
        break;
      case 'ArrowLeft':
        next = index === 0 ? lastIndex : index - 1;
        break;
      case 'Home':
        next = 0;
        break;
      case 'End':
        next = lastIndex;
        break;
      default:
        return;
    }

    event.preventDefault();
    const target = TABS[next].id;
    setTab(target);
    focusTab(target);
  }

  /**
   * Focus a tab AND bring it into view.
   *
   * The strip scrolls now that there are twelve tabs, so End could focus a tab
   * that was off-screen: the focus ring was rendered outside the viewport and
   * a keyboard user had no way to tell where they were. `block: 'nearest'`
   * keeps the page from jumping vertically while doing it.
   */
  function focusTab(target: TabId) {
    const element = tabRefs.current.get(target);
    element?.focus();
    // Optional call: jsdom has no layout and so no scrollIntoView, and this
    // must not turn a focus move into an exception.
    element?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
  }

  const load = useCallback(
    (signal?: { aborted: boolean }) => {
      setState({ kind: 'loading' });
      getProductByCode(code)
        .then((product) => {
          if (!signal?.aborted) {
            setState({ kind: 'loaded', product });
          }
        })
        .catch((error: unknown) => {
          if (!signal?.aborted) {
            setState({
              kind: 'error',
              message: error instanceof ApiError ? error.message : 'Could not load this product.',
            });
          }
        });

      // The pipeline is fetched separately and its failure is NOT fatal: a
      // product page that will not render because the workflow service is
      // unavailable would be a worse outcome than a page with no transition
      // buttons. The stepper falls back to nothing rather than to a guess.
      getProductWorkflow(code)
        .then((workflow) => {
          if (!signal?.aborted) {
            setWorkflow(workflow);
          }
        })
        .catch(() => {
          if (!signal?.aborted) {
            setWorkflow(null);
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

  async function onSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (state.kind !== 'loaded') {
      return;
    }

    const form = new FormData(event.currentTarget);
    setSaving(true);
    setFormError(null);

    try {
      const updated = await updateProductGeneral(state.product.code, {
        name: String(form.get('name') ?? '').trim(),
        ownerDisplayName: String(form.get('owner') ?? '') || null,
        gtin: String(form.get('gtin') ?? '').trim() || null,
        sapMaterialNo: String(form.get('sapMaterialNo') ?? '').trim() || null,
        rowVersion: state.product.rowVersion,
      });

      setState({ kind: 'loaded', product: updated });
      setEditing(false);
      showToast('Changes saved');
    } catch (error: unknown) {
      setFormError(
        error instanceof ApiError ? error.message : 'Could not save changes. Please try again.',
      );
    } finally {
      setSaving(false);
    }
  }

  /**
   * Performs a pipeline ACTION rather than a move to a named state.
   *
   * The header used to offer "Move to Draft" from two different steps, which
   * flattened Reject and Request changes into one button and one entry in the
   * approval trail. Posting the action keeps the two apart; the label comes
   * from the pipeline, so it is whatever the configured process calls it.
   */
  async function onAction(action: string, label: string) {
    if (state.kind !== 'loaded') {
      return;
    }

    setTransitioning(true);
    try {
      const updated = await performWorkflowAction(state.product.code, {
        action,
        comment: null,
        actor: null,
        rowVersion: state.product.rowVersion,
      });

      setWorkflow(updated);
      showToast(`${label} — now ${updated.currentStepName}`);

      // The product's own row version changed with the transition, so the
      // header has to reload rather than patch a field.
      load();
    } catch (error: unknown) {
      showToast(
        error instanceof ApiError ? error.message : 'Could not change the workflow state.',
        'error',
      );
    } finally {
      setTransitioning(false);
    }
  }

  if (state.kind === 'loading') {
    return (
      <p className="muted" role="status">
        Loading product…
      </p>
    );
  }

  if (state.kind === 'error') {
    return (
      <>
        <div className="crumb" style={{ marginBottom: 12 }}>
          <Link to="/products">Products</Link>
          <span className="sep">/</span>
          <b>{code}</b>
        </div>
        <div className="alert err" role="alert">
          {state.message}
        </div>
        <Button style={{ marginTop: 12 }} onClick={() => load()}>
          Retry
        </Button>
      </>
    );
  }

  const { product } = state;

  return (
    <>
      <div className="phead">
        <div>
          <div className="crumb" style={{ marginBottom: 6 }}>
            <Link to="/products">Products</Link>
            <span className="sep">/</span>
            <b>{product.code}</b>
          </div>
          <h1>{stripGreen(product.name ?? product.code)}</h1>
          <p>
            <StatusBadge status={product.workflowState} /> · {product.lifecycleState} · base locale{' '}
            {product.baseLocale}
          </p>
        </div>
        <div className="sp">
          {(workflow?.availableActions ?? []).map((available) => (
            <Button
              key={available.action}
              variant={available.action === 'REJECT' ? 'danger' : 'primary'}
              disabled={transitioning || editing}
              onClick={() => onAction(available.action, available.label)}
            >
              {available.label}
            </Button>
          ))}
          {!editing && <Button onClick={() => setEditing(true)}>Edit</Button>}
        </div>
      </div>

      {/* Workflow stepper (v4.2). Steps come from the configured pipeline, so a
          client who adds or removes one sees it here without a deployment. */}
      {workflow && (
        <div className="card pad" style={{ marginBottom: 14 }}>
          <div
            style={{ display: 'flex', gap: 10, alignItems: 'center' }}
            aria-label="Workflow progress"
          >
            {workflow.steps.map((step, index) => (
              <span
                key={step.code}
                className={`chip ${step.isCurrent || step.isPassed ? 's-pri' : 's-neut'}`}
                aria-current={step.isCurrent ? 'step' : undefined}
              >
                {index + 1}. {step.name}
              </span>
            ))}
          </div>
        </div>
      )}

      <div
        className="dtabs"
        role="tablist"
        aria-label="Product detail tabs"
        style={{ marginBottom: 14 }}
      >
        {TABS.map(({ id, label }, index) => (
          <button
            key={id}
            type="button"
            role="tab"
            id={`tab-${id}`}
            aria-selected={tab === id}
            aria-controls="detail-tabpanel"
            // Roving tabindex: the tablist is ONE tab stop, and arrow keys move
            // within it. Leaving every tab focusable made a keyboard user press
            // Tab nine times to reach the panel.
            tabIndex={tab === id ? 0 : -1}
            ref={(element) => {
              if (element) {
                tabRefs.current.set(id, element);
              } else {
                tabRefs.current.delete(id);
              }
            }}
            className={tab === id ? 'dtab on' : 'dtab'}
            onClick={() => setTab(id)}
            onKeyDown={(event) => onTabKeyDown(event, index)}
          >
            {label}
          </button>
        ))}
      </div>

      {/*
        A role="tab" with nothing pointing at a role="tabpanel" leaves a screen
        reader unable to move from the tab to the content it selects. One panel
        wraps whichever tab is active, so aria-controls stays stable.
      */}
      <div id="detail-tabpanel" role="tabpanel" aria-labelledby={`tab-${tab}`} tabIndex={-1}>

      {tab === 'attributes' && <AttributesTab code={product.code} onSaved={() => load()} />}

      {tab === 'variants' && <VariantsTab code={product.code} onChanged={() => load()} />}

      {tab === 'classifications' && (
        <ClassificationsTab code={product.code} onChanged={() => load()} />
      )}

      {tab === 'compliance' && <ComplianceTab code={product.code} onChanged={() => load()} />}

      {tab === 'packaging' && <PackagingTab code={product.code} onChanged={() => load()} />}

      {tab === 'relationships' && (
        <RelationshipsTab code={product.code} onChanged={() => load()} />
      )}

      {tab === 'applications' && (
        <ApplicationsTab code={product.code} onChanged={() => load()} />
      )}

      {tab === 'assets' && <AssetsTab code={product.code} onChanged={() => load()} />}

      {tab === 'planning' && <PlanningTab code={product.code} onNavigateToTab={(id) => setTab(id as TabId)} />}

      {tab === 'localization' && (
        <LocalizationTab code={product.code} onChanged={() => load()} />
      )}

      {tab === 'workflow' && (
        <WorkflowTab
          code={product.code}
          onChanged={() => load()}
        />
      )}
      {tab === 'activity' && <ActivityTab code={product.code} />}

      {tab === 'general' && (
      <div className="grid g2">
        <section className="card" aria-label="General">
          <div className="card-h">
            <h3>General</h3>
          </div>
          <div className="card-b">
            {editing ? (
              <form onSubmit={onSave}>
                <div className="fgrid">
                  <div className="full">
                    <TextField
                      label="Product name"
                      name="name"
                      required
                      defaultValue={product.name ?? ''}
                    />
                  </div>
                  <SelectField
                    label="Owner"
                    name="owner"
                    defaultValue={product.ownerDisplayName ?? ''}
                    options={[
                      { value: '', label: '— Unassigned —' },
                      ...OWNERS.map((owner) => ({ value: owner, label: owner })),
                    ]}
                  />
                  <TextField
                    label="GTIN"
                    name="gtin"
                    defaultValue={product.gtin ?? ''}
                    hint="8-14 digits"
                  />
                  <TextField
                    label="SAP Number"
                    name="sapMaterialNo"
                    defaultValue={product.sapMaterialNo ?? ''}
                  />
                </div>

                {formError && (
                  <div className="alert err" role="alert">
                    {formError}
                  </div>
                )}

                <div style={{ display: 'flex', gap: 8, marginTop: 14 }}>
                  <Button variant="primary" type="submit" disabled={saving}>
                    {saving ? 'Saving…' : 'Save changes'}
                  </Button>
                  <Button
                    type="button"
                    disabled={saving}
                    onClick={() => {
                      setEditing(false);
                      setFormError(null);
                    }}
                  >
                    Cancel
                  </Button>
                </div>
              </form>
            ) : (
              <dl className="facts">
                <dt>Name</dt>
                <dd>{product.name ?? '—'}</dd>
                <dt>Owner</dt>
                <dd>{product.ownerDisplayName ?? <span className="muted">Unassigned</span>}</dd>
                <dt>GTIN</dt>
                <dd className="num">{product.gtin ?? '—'}</dd>
                <dt>SAP Number</dt>
                <dd className="num">{product.sapMaterialNo ?? '—'}</dd>
                <dt>Lifecycle</dt>
                <dd>{product.lifecycleState}</dd>
                <dt>Base locale</dt>
                <dd>{product.baseLocale}</dd>
              </dl>
            )}
          </div>
        </section>

        <section className="card" aria-label="Record">
          <div className="card-h">
            <h3>Record</h3>
          </div>
          <div className="card-b">
            <dl className="facts">
              <dt>Code</dt>
              <dd className="num">{product.code}</dd>
              <dt>Public id</dt>
              <dd className="num" style={{ fontSize: 11 }}>
                {product.publicId}
              </dd>
              <dt>Created</dt>
              <dd>{formatDate(product.createdAtUtc)}</dd>
              <dt>Updated</dt>
              <dd>{formatDate(product.updatedAtUtc)}</dd>
              <dt>Version</dt>
              <dd className="num">{product.rowVersion}</dd>
            </dl>

            <div className="divider" />
            <p className="muted" style={{ margin: 0, fontSize: 11.5 }}>
              Channels is the one designed tab still missing. It needs channel tables that do not
              exist yet, so there is nowhere for a product to be published to.
            </p>
            <div style={{ marginTop: 10 }}>
              <QualityBar value={0} hideLabel />
            </div>
          </div>
        </section>
      </div>
      )}
      </div>
    </>
  );
}
