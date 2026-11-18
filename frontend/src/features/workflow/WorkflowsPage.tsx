import { useCallback, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { ApiError } from '../../api/client';
import { useLocale } from '../../app/LocaleContext';
import { Button } from '../../components/Button';
import { Chip } from '../../components/Chip';
import { EmptyState } from '../../components/EmptyState';
import { QualityBar } from '../../components/QualityBar';
import {
  getWorkflowBoard,
  getWorkflowQueue,
  STEP_TONE,
  type WorkflowBoard,
  type WorkflowCard,
  type WorkflowColumn,
  type WorkflowQueue,
} from './workflowApi';

const PAGE_SIZE = 25;

function initials(name: string | null): string {
  if (!name) {
    return '—';
  }
  return name
    .split(' ')
    .filter(Boolean)
    .map((word) => word[0])
    .slice(0, 2)
    .join('');
}

function formatDate(iso: string | null): string {
  if (!iso) {
    return '—';
  }
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleDateString();
}

/**
 * One card. It is a LINK to the product's Workflow tab, and deliberately not
 * draggable.
 *
 * Dragging a card between columns would have to decide which action it meant,
 * and it cannot: Approval to Draft is REJECT or REQUEST_CHANGES, and a drag
 * carries no reviewer note. Flattening those two into one gesture would undo
 * the reason Sprint 27 separated them. The card takes you to where the note is
 * typed instead.
 */
function BoardCard({ card }: { card: WorkflowCard }) {
  return (
    <Link
      to={`/products/${encodeURIComponent(card.code)}?tab=workflow`}
      className="card pad wf-card"
      style={{ padding: 11, display: 'block', textDecoration: 'none', color: 'inherit' }}
    >
      <div style={{ fontWeight: 600, fontSize: 12, overflowWrap: 'anywhere' }}>{card.code}</div>
      <div className="muted" style={{ fontSize: 10.5, marginBottom: 8, overflowWrap: 'anywhere' }}>
        {card.category}
      </div>

      <QualityBar value={card.completenessPct} thin />

      <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 8 }}>
        <span
          className="av"
          aria-hidden="true"
          style={{ width: 20, height: 20, fontSize: 8, background: 'var(--c3)', color: 'var(--ink2)' }}
        >
          {initials(card.assignee ?? card.owner)}
        </span>
        <span className="muted" style={{ fontSize: 10.5 }}>
          {card.assignee ?? card.owner ?? 'Unassigned'}
        </span>
        {/* A product that has never been through the pipeline has no task, so
            the name beside it is its OWNER rather than an assignee. Saying so
            is the difference between "somebody is on this" and "somebody owns
            the record". */}
        {!card.hasOpenTask && (
          <span className="sp muted" style={{ fontSize: 9.5 }} title="No approval task has been opened for this product">
            owner
          </span>
        )}
      </div>
    </Link>
  );
}

function BoardColumn({ column }: { column: WorkflowColumn }) {
  const hidden = column.totalCount - column.cards.length;

  return (
    <div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 10 }}>
        <Chip tone={column.isRecognised ? (STEP_TONE[column.step] ?? 'neut') : 'bad'}>{column.name}</Chip>
        <span className="muted num" style={{ fontWeight: 700 }}>
          {column.totalCount.toLocaleString()}
        </span>
        {column.ownerRole && (
          <span className="sp muted" style={{ fontSize: 10.5 }}>
            {column.ownerRole}
          </span>
        )}
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 9 }}>
        {column.cards.length === 0 ? (
          <p className="muted" style={{ fontSize: 11.5, margin: 0 }}>
            Nothing here.
          </p>
        ) : (
          column.cards.map((card) => <BoardCard key={card.publicId} card={card} />)
        )}
      </div>

      {/* The count above is the whole column; these are its most recently
          touched. Stating the gap is the point — a column headed 137 showing
          twelve cards, with nothing to say the rest exist, is a figure that
          means something other than it appears to. */}
      {hidden > 0 && (
        <p className="muted" style={{ fontSize: 10.5, marginTop: 9 }}>
          Showing the {column.cards.length} most recently changed · {hidden.toLocaleString()} more in
          this step
        </p>
      )}
    </div>
  );
}

/**
 * Workflows &amp; Tasks — prototype v4.2 renderWorkflows().
 *
 * The board is over PRODUCTS, not over approval tasks, which is what the
 * screen's own subtitle promises: most of the catalogue has never been through
 * the pipeline, and a board built from tasks would show a handful of cards
 * while claiming to cover the catalogue.
 *
 * Columns come from the configured pipeline, so reconfiguring the process
 * changes this screen without a deployment. A workflow state the pipeline does
 * not define still gets a column, marked as unrecognised — dropping it would
 * make every other column a larger share of a catalogue that had quietly
 * shrunk, which is the bug Sprint 21 shipped on the cockpit.
 */
export function WorkflowsPage() {
  const { locale } = useLocale();
  const [params, setParams] = useSearchParams();

  const view = params.get('view') === 'list' ? 'list' : 'board';
  const step = params.get('step');
  const assignee = params.get('assignee');
  const search = params.get('q') ?? '';
  const page = Math.max(1, Number(params.get('page') ?? '1') || 1);

  const [board, setBoard] = useState<WorkflowBoard | null>(null);
  const [queue, setQueue] = useState<WorkflowQueue | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [searchInput, setSearchInput] = useState(search);

  const update = useCallback(
    (next: Record<string, string | null>) => {
      const merged = new URLSearchParams(params);
      for (const [key, value] of Object.entries(next)) {
        if (value === null || value === '') {
          merged.delete(key);
        } else {
          merged.set(key, value);
        }
      }
      // Any filter change invalidates the page number: page 4 of a narrower
      // result set is an empty table that looks like "no matches".
      if (!('page' in next)) {
        merged.delete('page');
      }
      setParams(merged, { replace: true });
    },
    [params, setParams],
  );

  useEffect(() => {
    const signal = { aborted: false };
    setError(null);

    if (view === 'board') {
      getWorkflowBoard(locale)
        .then((data) => {
          if (!signal.aborted) {
            setBoard(data);
          }
        })
        .catch((e: unknown) => {
          if (!signal.aborted) {
            setError(e instanceof ApiError ? e.message : 'The board could not be loaded.');
          }
        });
    } else {
      getWorkflowQueue({ page, pageSize: PAGE_SIZE, step, assignee, search, locale })
        .then((data) => {
          if (!signal.aborted) {
            setQueue(data);
          }
        })
        .catch((e: unknown) => {
          if (!signal.aborted) {
            setError(e instanceof ApiError ? e.message : 'The list could not be loaded.');
          }
        });
    }

    return () => {
      signal.aborted = true;
    };
  }, [view, locale, page, step, assignee, search]);

  const totalPages = queue ? Math.max(1, Math.ceil(queue.page.totalCount / queue.page.pageSize)) : 1;

  return (
    <div className="pad">
      <div className="phead">
        <div>
          <h1>Workflows &amp; Tasks</h1>
          <p>
            The editorial pipeline across the catalogue. Steps come from the configured process,
            not from this screen — moving a product happens on its Workflow tab, where the
            reviewer note is written.
          </p>
        </div>
        <div className="sp">
          <div className="seg" role="group" aria-label="View">
            <button
              type="button"
              className={view === 'board' ? 'on' : ''}
              onClick={() => update({ view: null })}
            >
              Board
            </button>
            <button
              type="button"
              className={view === 'list' ? 'on' : ''}
              onClick={() => update({ view: 'list' })}
            >
              List
            </button>
          </div>
        </div>
      </div>

      {error && (
        <EmptyState title="Workflows could not be loaded" description={error} />
      )}

      {!error && view === 'board' && (
        <>
          {board && (
            <p className="muted" style={{ fontSize: 11.5, margin: '0 0 12px' }}>
              Pipeline <b>{board.definitionName}</b> · {board.totalProducts.toLocaleString()} products
            </p>
          )}
          {!board ? (
            <p className="muted" role="status">
              Loading board…
            </p>
          ) : (
            <div className="grid g4" style={{ alignItems: 'start' }}>
              {board.columns.map((column) => (
                <BoardColumn key={column.step} column={column} />
              ))}
            </div>
          )}
        </>
      )}

      {!error && view === 'list' && (
        <>
          <div className="toolbar" style={{ marginBottom: 12, display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            <select
              className="sel"
              aria-label="Step"
              value={step ?? ''}
              onChange={(e) => update({ step: e.target.value || null })}
            >
              <option value="">All steps</option>
              {queue?.steps.map((s) => (
                <option key={s.code} value={s.code}>
                  {s.name}
                </option>
              ))}
            </select>

            <select
              className="sel"
              aria-label="Assignee"
              value={assignee ?? ''}
              onChange={(e) => update({ assignee: e.target.value || null })}
            >
              <option value="">Anyone</option>
              {queue?.assignees.map((name) => (
                <option key={name} value={name}>
                  {name}
                </option>
              ))}
            </select>

            <form
              onSubmit={(e) => {
                e.preventDefault();
                update({ q: searchInput });
              }}
              style={{ display: 'flex', gap: 8 }}
            >
              <div className="inp" style={{ minWidth: 220 }}>
                <input
                  aria-label="Search products"
                  placeholder="Search code or name…"
                  value={searchInput}
                  onChange={(e) => setSearchInput(e.target.value)}
                />
              </div>
              <button type="submit" className="btn">
                Search
              </button>
            </form>

            {queue && (
              <span className="muted sp" style={{ fontSize: 11.5, alignSelf: 'center' }}>
                {queue.page.totalCount.toLocaleString()} products
              </span>
            )}
          </div>

          {!queue ? (
            <p className="muted" role="status">
              Loading list…
            </p>
          ) : queue.page.items.length === 0 ? (
            <EmptyState
              title="No product matches this filter"
              description="Widen the step or assignee filter, or clear the search."
            />
          ) : (
            <>
              <div className="card" style={{ overflow: 'hidden' }}>
                <div className="tablewrap">
                  <table className="dg">
                    <thead>
                      <tr>
                        <th>Product</th>
                        <th>Step</th>
                        <th>Assignee</th>
                        <th>Owner</th>
                        <th>Completeness</th>
                        <th>Last action</th>
                      </tr>
                    </thead>
                    <tbody>
                      {queue.page.items.map((item) => (
                        <tr key={item.publicId} style={{ cursor: 'default' }}>
                          <td>
                            <Link to={`/products/${encodeURIComponent(item.code)}?tab=workflow`}>
                              {item.name}
                            </Link>
                            <div className="muted" style={{ fontSize: 10.5 }}>
                              {item.code} · {item.category}
                            </div>
                          </td>
                          <td>
                            <Chip tone={STEP_TONE[item.step] ?? 'neut'}>{item.stepName}</Chip>
                          </td>
                          {/* Not backfilled from the owner: a product with no
                              task has nobody assigned, and saying otherwise
                              would put a name against work nobody accepted. */}
                          <td>{item.assignee ?? <span className="muted">—</span>}</td>
                          <td>{item.owner ?? <span className="muted">—</span>}</td>
                          <td style={{ width: 140 }}>
                            <QualityBar value={item.completenessPct} />
                          </td>
                          <td className="muted" style={{ fontSize: 11.5 }}>
                            {item.lastAction ? `${item.lastAction} · ${formatDate(item.lastActionAt)}` : '—'}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>

              <div className="toolbar" style={{ marginTop: 12, display: 'flex', gap: 8 }}>
                <Button
                  size="sm"
                  disabled={page <= 1}
                  onClick={() => update({ page: String(page - 1) })}
                >
                  Previous
                </Button>
                <span className="muted" style={{ fontSize: 11.5, alignSelf: 'center' }}>
                  Page {page} of {totalPages}
                </span>
                <Button
                  size="sm"
                  disabled={page >= totalPages}
                  onClick={() => update({ page: String(page + 1) })}
                >
                  Next
                </Button>
              </div>
            </>
          )}
        </>
      )}
    </div>
  );
}
