import { useCallback, useEffect, useState } from 'react';
import { ApiError } from '../../api/client';
import { Button } from '../../components/Button';
import { Chip } from '../../components/Chip';
import { TextAreaField, TextField } from '../../components/Field';
import { useToast } from '../../components/Toast';
import {
  ACTION_LABEL,
  ACTION_TONE,
  getProductWorkflow,
  performWorkflowAction,
  type ProductWorkflow,
} from '../workflow/workflowApi';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'loaded'; data: ProductWorkflow };

export interface WorkflowTabProps {
  code: string;
  /** Lets the detail page refresh the header, which shows the same state. */
  onChanged?: () => void;
}

function formatTimestamp(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString();
}

/**
 * Workflow — prototype v4.2 `paneWorkflow()`.
 *
 * Two things about this tab are worth knowing before reading the code.
 *
 * First, it does not decide anything. The steps, the buttons, their labels and
 * their order all come from `GET /workflow`, which reads the pipeline out of
 * workflow_definition. The old hardcoded WORKFLOW_TRANSITIONS map was deleted
 * in Sprint 27; if a client reconfigures the process, this tab follows without
 * a deployment.
 *
 * Second, the "Recorded as" field is a plain text box and not a signed-in
 * identity, and the panel says so. There is no authentication (R-3), so the
 * approval trail records a name the caller typed. Presenting that as
 * attribution would be the single most misleading thing this screen could do,
 * so it is labelled rather than styled to look official (R-26).
 */
export function WorkflowTab({ code, onChanged }: WorkflowTabProps) {
  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [comment, setComment] = useState('');
  const [actor, setActor] = useState('');
  const [busy, setBusy] = useState<string | null>(null);
  const { showToast } = useToast();

  const load = useCallback(
    (signal?: { aborted: boolean }) => {
      setState({ kind: 'loading' });
      getProductWorkflow(code)
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
                error instanceof ApiError ? error.message : 'Could not load the workflow.',
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

  async function perform(action: string, label: string) {
    if (state.kind !== 'loaded') {
      return;
    }

    setBusy(action);
    try {
      const updated = await performWorkflowAction(code, {
        action,
        comment: comment.trim() === '' ? null : comment.trim(),
        actor: actor.trim() === '' ? null : actor.trim(),
        rowVersion: state.data.rowVersion,
      });

      setState({ kind: 'loaded', data: updated });
      setComment('');
      showToast(`${label} — now ${updated.currentStepName}`);
      onChanged?.();
    } catch (error: unknown) {
      showToast(
        error instanceof ApiError ? error.message : 'Could not perform that action.',
        'error',
      );
    } finally {
      setBusy(null);
    }
  }

  if (state.kind === 'loading') {
    return (
      <p className="muted" role="status">
        Loading workflow…
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

  const workflow = state.data;

  return (
    <>
      <p className="muted" style={{ fontSize: 11.5, margin: '0 0 12px' }}>
        Pipeline <b>{workflow.definitionName}</b> (v{workflow.definitionVersion}). Steps and
        actions are configuration, not code — this tab shows whatever the configured process
        allows from here.
      </p>

      <div className="grid g2" style={{ alignItems: 'start' }}>
        <section className="card" aria-label="Current step">
          <div className="card-h">
            <h3>Current step — {workflow.currentStepName}</h3>
            <Chip tone={workflow.isComplete ? 'ok' : 'warn'}>
              {workflow.isComplete ? 'Complete' : 'In progress'}
            </Chip>
          </div>
          <div className="card-b">
            {/* `facts`, not `dmeta`: dmeta is a flex-wrap row of label/value
                blocks and a two-word label beside a two-word value flowed into
                a column that read as though the owning role were the assignee.
                Caught by looking at it (R-8), which is what the browser pass is
                for. */}
            <dl className="facts">
              <dt>Assigned to</dt>
              <dd>{workflow.assignee ?? '—'}</dd>
              <dt>Owning role</dt>
              <dd>{workflow.currentStepOwnerRole ?? '—'}</dd>
              <dt>Priority</dt>
              <dd>{workflow.priority}</dd>
              <dt>Due</dt>
              <dd>{workflow.dueDate ?? '—'}</dd>
            </dl>

            {workflow.availableActions.length === 0 ? (
              <p className="muted" style={{ fontSize: 11.5, marginTop: 12 }}>
                {workflow.currentStepName} is the end of this pipeline — there is nothing to
                move it to. A published product can still be edited; it is simply not
                re-approved, because draft-versus-published versioning is not built yet.
              </p>
            ) : (
              <div style={{ marginTop: 14 }}>
                <TextAreaField
                  label="Reviewer note"
                  hint="Recorded against this transition in the approval trail."
                  rows={3}
                  value={comment}
                  onChange={(event) => setComment(event.target.value)}
                />

                <TextField
                  label="Recorded as"
                  hint="Unverified — there is no sign-in yet, so this is stored as supplied."
                  value={actor}
                  placeholder="Your name"
                  onChange={(event) => setActor(event.target.value)}
                />

                <div className="toolbar" style={{ marginTop: 12 }}>
                  {workflow.availableActions.map((available) => (
                    <Button
                      key={available.action}
                      size="sm"
                      variant={
                        available.action === 'APPROVE'
                          ? 'green'
                          : available.action === 'REJECT'
                            ? 'danger'
                            : available.action === 'SUBMIT'
                              ? 'primary'
                              : 'default'
                      }
                      disabled={busy !== null}
                      onClick={() => perform(available.action, available.label)}
                    >
                      {available.label}
                    </Button>
                  ))}
                </div>
              </div>
            )}
          </div>
        </section>

        <section className="card" aria-label="Approval trail">
          <div className="card-h">
            <h3>History</h3>
            <span className="sp muted" style={{ fontSize: 11 }}>
              {workflow.history.length} {workflow.history.length === 1 ? 'entry' : 'entries'}
            </span>
          </div>
          <div className="card-b">
            {workflow.history.length === 0 ? (
              <p className="muted" style={{ fontSize: 11.5, margin: 0 }}>
                No transitions recorded yet. The catalogue was loaded with products already at a
                step, and a move that never happened is not written into an approval trail.
              </p>
            ) : (
              <ol style={{ listStyle: 'none', margin: 0, padding: 0 }}>
                {workflow.history.map((entry, index) => (
                  <li
                    key={`${entry.occurredAt}-${index}`}
                    style={{ display: 'flex', gap: 11, padding: '7px 0' }}
                  >
                    <Chip tone={ACTION_TONE[entry.action] ?? 'neut'}>
                      {ACTION_LABEL[entry.action] ?? entry.action}
                    </Chip>
                    <div style={{ flex: 1, minWidth: 0 }}>
                      <div style={{ fontSize: 12, fontWeight: 600 }}>
                        {entry.fromStep ?? '—'} → {entry.toStep}
                      </div>
                      <div className="muted" style={{ fontSize: 11 }}>
                        {entry.actor ?? 'Unknown'} · {formatTimestamp(entry.occurredAt)}
                      </div>
                      {entry.comment && (
                        <div style={{ fontSize: 11.5, marginTop: 4 }}>{entry.comment}</div>
                      )}
                    </div>
                  </li>
                ))}
              </ol>
            )}

            <div className="divider" />
            <p className="muted" style={{ fontSize: 11, margin: 0 }}>
              {workflow.actorAttribution}
            </p>
          </div>
        </section>
      </div>
    </>
  );
}
