import { useCallback, useEffect, useState } from 'react';
import { ApiError } from '../../api/client';
import { Button } from '../../components/Button';
import { Chip, ChipTone } from '../../components/Chip';
import { EmptyState } from '../../components/EmptyState';
import { SelectField } from '../../components/Field';
import { Table, Td, Th } from '../../components/Table';
import type { ProductActivity } from './types';
import { getProductActivity } from './productsApi';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'loaded'; data: ProductActivity };

export interface ActivityTabProps {
  code: string;
}

const PAGE_SIZE = 25;

/** Verbs share tones with the rest of the app: destructive reads as destructive. */
const ACTION_TONE: Record<string, ChipTone> = {
  created: 'ok',
  updated: 'info',
  deleted: 'bad',
  published: 'pri',
  workflow: 'vio',
  lifecycle: 'vio',
  classified: 'tea',
  linked: 'info',
  unlinked: 'warn',
};

const ACTION_LABEL: Record<string, string> = {
  created: 'Created',
  updated: 'Updated',
  deleted: 'Deleted',
  published: 'Published',
  workflow: 'Workflow',
  lifecycle: 'Lifecycle',
  classified: 'Classified',
  linked: 'Linked',
  unlinked: 'Unlinked',
};

function formatTimestamp(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString();
}

export function ActivityTab({ code }: ActivityTabProps) {
  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [page, setPage] = useState(1);
  const [action, setAction] = useState('');
  const [search, setSearch] = useState('');

  // Held outside `state` on purpose. The filter list must survive a reload,
  // because the toolbar stays mounted while the results below it refresh —
  // see the render note below.
  const [actions, setActions] = useState<string[]>([]);

  const load = useCallback(
    (signal?: { aborted: boolean }) => {
      setState({ kind: 'loading' });
      getProductActivity(code, {
        page,
        pageSize: PAGE_SIZE,
        action: action || undefined,
        search: search || undefined,
      })
        .then((data) => {
          if (!signal?.aborted) {
            setState({ kind: 'loaded', data });
            setActions(data.availableActions);
          }
        })
        .catch((error: unknown) => {
          if (!signal?.aborted) {
            setState({
              kind: 'error',
              message: error instanceof ApiError ? error.message : 'Could not load activity.',
            });
          }
        });
    },
    [code, page, action, search],
  );

  useEffect(() => {
    const signal = { aborted: false };
    load(signal);
    return () => {
      signal.aborted = true;
    };
  }, [load]);

  const entries = state.kind === 'loaded' ? state.data.entries : null;
  const totalPages = entries ? Math.max(1, Math.ceil(entries.totalCount / entries.pageSize)) : 1;
  const isFiltered = action !== '' || search !== '';

  return (
    <>
      <p className="muted" style={{ fontSize: 11.5, margin: '0 0 12px' }}>
        Every change to this product, newest first. Attribution is not yet recorded — the platform
        has no authentication, so entries show <b>Unknown</b> rather than guessing who acted.
      </p>

      {/*
        The toolbar renders in every state, including while loading. An earlier
        version returned early on `loading`, which unmounted the search box on
        every keystroke: the field lost focus and a steward could not type more
        than one character. Only the results below are swapped.
      */}
      <div className="toolbar" style={{ marginBottom: 12, display: 'flex', gap: 10 }}>
        <SelectField
          label="Action"
          value={action}
          onChange={(event) => {
            setAction(event.target.value);
            setPage(1);
          }}
          options={[
            { value: '', label: 'All actions' },
            ...actions.map((a) => ({ value: a, label: ACTION_LABEL[a] ?? a })),
          ]}
        />
        <div className="field" style={{ flex: 1, maxWidth: 320 }}>
          <label htmlFor="activity-search">Search</label>
          <input
            id="activity-search"
            className="fld"
            placeholder="Search activity…"
            value={search}
            onChange={(event) => {
              setSearch(event.target.value);
              setPage(1);
            }}
          />
        </div>
      </div>

      {state.kind === 'error' && (
        <>
          <div className="alert err" role="alert">
            {state.message}
          </div>
          <Button style={{ marginTop: 12 }} onClick={() => load()}>
            Retry
          </Button>
        </>
      )}

      {state.kind === 'loading' && (
        <p className="muted" role="status">
          Loading activity…
        </p>
      )}

      {entries && (
        <div className="card" style={{ overflow: 'hidden' }}>
          <div className="card-b" style={{ padding: 0 }}>
            {entries.items.length === 0 ? (
              <div style={{ padding: 16 }}>
                <EmptyState
                  title={isFiltered ? 'No matching activity' : 'No activity yet'}
                  description={
                    isFiltered
                      ? 'Try a different action or search term.'
                      : 'Changes to this product will appear here as they happen.'
                  }
                />
              </div>
            ) : (
              <Table>
                <thead>
                  <tr>
                    <Th>Timestamp</Th>
                    <Th>User</Th>
                    <Th>Action</Th>
                    <Th>Detail</Th>
                    <Th>Source</Th>
                  </tr>
                </thead>
                <tbody>
                  {entries.items.map((entry) => (
                    <tr key={entry.id}>
                      <Td className="num muted">{formatTimestamp(entry.occurredAt)}</Td>
                      <Td>
                        {entry.actor ?? (
                          <span className="muted" style={{ fontStyle: 'italic' }}>
                            Unknown
                          </span>
                        )}
                      </Td>
                      <Td>
                        <Chip tone={ACTION_TONE[entry.action] ?? 'neut'} dot>
                          {ACTION_LABEL[entry.action] ?? entry.action}
                        </Chip>
                      </Td>
                      <Td>{entry.detail ?? <span className="muted">—</span>}</Td>
                      <Td className="muted">{entry.source ?? '—'}</Td>
                    </tr>
                  ))}
                </tbody>
              </Table>
            )}
          </div>
        </div>
      )}

      {entries && entries.totalCount > 0 && (
        <div
          style={{ display: 'flex', gap: 10, alignItems: 'center', marginTop: 12 }}
          aria-label="Activity paging"
        >
          <Button disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
            Previous
          </Button>
          <span className="muted" style={{ fontSize: 11.5 }}>
            Page {entries.page} of {totalPages} · {entries.totalCount}{' '}
            {entries.totalCount === 1 ? 'entry' : 'entries'}
          </span>
          <Button disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
            Next
          </Button>
        </div>
      )}
    </>
  );
}
