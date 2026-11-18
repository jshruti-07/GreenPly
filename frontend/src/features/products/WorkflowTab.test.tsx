import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { ToastProvider } from '../../components/Toast';
import { WorkflowTab } from './WorkflowTab';
import { makeWorkflow } from './testApi';
import type { ProductWorkflow } from '../workflow/workflowApi';

interface Api {
  posts: Array<{ url: string; body: Record<string, unknown> }>;
}

function installApi(
  options: {
    workflow?: Partial<ProductWorkflow> & { workflowState?: string };
    loadError?: { status: number; title: string; detail: string };
    actionError?: { status: number; title: string; detail: string };
  } = {},
): Api {
  const posts: Api['posts'] = [];
  const workflow = makeWorkflow(options.workflow ?? {});

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';

      const problem = (p: { status: number; title: string; detail: string }) =>
        new Response(JSON.stringify(p), {
          status: p.status,
          headers: { 'Content-Type': 'application/problem+json' },
        });

      if (method === 'GET') {
        return options.loadError
          ? problem(options.loadError)
          : new Response(JSON.stringify(workflow), {
              status: 200,
              headers: { 'Content-Type': 'application/json' },
            });
      }

      posts.push({ url, body: JSON.parse(String(init?.body)) as Record<string, unknown> });

      if (options.actionError) {
        return problem(options.actionError);
      }

      return new Response(
        JSON.stringify(makeWorkflow({ workflowState: 'Review', rowVersion: 4 })),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      );
    }),
  );

  return { posts };
}

function renderTab(onChanged?: () => void) {
  return render(
    <ToastProvider>
      <WorkflowTab code="FD-90-GS-1000" onChanged={onChanged} />
    </ToastProvider>,
  );
}

describe('WorkflowTab', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('draws one button per action the pipeline offers, using the pipeline label', async () => {
    // Three buttons on Approval is prototype v4.2's design and the reason the
    // shipped pipeline has six transitions rather than five.
    installApi({ workflow: { workflowState: 'Approval' } });
    renderTab();

    const panel = await screen.findByLabelText('Current step');

    expect(within(panel).getByRole('button', { name: 'Approve and publish' })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Reject' })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Request changes' })).toBeInTheDocument();
    expect(within(panel).getAllByRole('button')).toHaveLength(3);
  });

  it('sends the reviewer note and the claimed actor with the action', async () => {
    const user = userEvent.setup();
    const api = installApi({ workflow: { workflowState: 'Approval' } });
    renderTab();

    await user.type(
      await screen.findByLabelText(/Reviewer note/),
      'UKCA certificate expires within 12 months',
    );
    await user.type(screen.getByLabelText(/Recorded as/), 'Sarah Muller');
    await user.click(screen.getByRole('button', { name: 'Reject' }));

    await waitFor(() => expect(api.posts).toHaveLength(1));
    expect(api.posts[0].url).toContain('/workflow/actions');
    expect(api.posts[0].body).toEqual({
      action: 'REJECT',
      comment: 'UKCA certificate expires within 12 months',
      actor: 'Sarah Muller',
      rowVersion: 3,
    });
  });

  it('sends null rather than an empty note, so blank rows do not enter the trail', async () => {
    const user = userEvent.setup();
    const api = installApi({ workflow: { workflowState: 'Draft' } });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Submit for review' }));

    await waitFor(() => expect(api.posts).toHaveLength(1));
    expect(api.posts[0].body.comment).toBeNull();
    expect(api.posts[0].body.actor).toBeNull();
  });

  it('says the trail is empty because nothing happened, not because nothing loaded', async () => {
    installApi({ workflow: { workflowState: 'Draft', history: [] } });
    renderTab();

    const trail = await screen.findByLabelText('Approval trail');
    expect(within(trail).getByText(/No transitions recorded yet/)).toBeInTheDocument();
  });

  it('renders every recorded transition with its own verb', async () => {
    installApi({
      workflow: {
        workflowState: 'Draft',
        history: [
          {
            action: 'SUBMIT',
            fromStep: 'draft',
            toStep: 'review',
            actor: 'Anna Becker',
            comment: null,
            occurredAt: '2026-08-19T09:00:00Z',
          },
          {
            action: 'REQUEST_CHANGES',
            fromStep: 'review',
            toStep: 'draft',
            actor: 'Markus Klein',
            comment: 'Fire rating missing',
            occurredAt: '2026-08-19T10:00:00Z',
          },
        ],
      },
    });
    renderTab();

    const trail = await screen.findByLabelText('Approval trail');

    expect(within(trail).getByText('Submitted')).toBeInTheDocument();
    expect(within(trail).getByText('Changes requested')).toBeInTheDocument();
    expect(within(trail).getByText('Fire rating missing')).toBeInTheDocument();
    expect(within(trail).getByText('2 entries')).toBeInTheDocument();
  });

  it('states on screen that the trail is not attribution', async () => {
    // R-26 is a risk the user has to be able to see, not one filed in a
    // document. If this assertion is ever deleted, so is the disclosure.
    installApi();
    renderTab();

    const trail = await screen.findByLabelText('Approval trail');
    expect(within(trail).getByText(/unverified/i)).toBeInTheDocument();
  });

  it('offers nothing at a terminal step and explains why', async () => {
    installApi({ workflow: { workflowState: 'Published' } });
    renderTab();

    const panel = await screen.findByLabelText('Current step');

    expect(within(panel).queryAllByRole('button')).toHaveLength(0);
    expect(within(panel).getByText(/end of this pipeline/)).toBeInTheDocument();
    // No note box either: there is no transition to attach a note to.
    expect(screen.queryByLabelText(/Reviewer note/)).not.toBeInTheDocument();
  });

  it('shows the assignee and the owning role of the step', async () => {
    installApi({ workflow: { workflowState: 'Approval', currentStepOwnerRole: 'Compliance Manager' } });
    renderTab();

    const panel = await screen.findByLabelText('Current step');
    expect(within(panel).getByText('Anna Becker')).toBeInTheDocument();
    expect(within(panel).getByText('Compliance Manager')).toBeInTheDocument();
  });

  it('surfaces a rejected action instead of pretending it landed', async () => {
    const user = userEvent.setup();
    installApi({
      workflow: { workflowState: 'Draft' },
      actionError: {
        status: 409,
        title: 'Workflow.ActionNotAvailable',
        detail: "'SUBMIT' is not available from the Draft step. Reload to see the current options.",
      },
    });
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Submit for review' }));

    expect(await screen.findByText(/is not available from the Draft step/)).toBeInTheDocument();
    // The step must NOT have advanced on a failure.
    expect(await screen.findByRole('heading', { name: 'Current step — Draft' })).toBeInTheDocument();
  });

  it('shows an error with retry when the workflow cannot be loaded', async () => {
    installApi({
      loadError: { status: 404, title: 'Product.NotFound', detail: "Product 'X-1' was not found." },
    });
    renderTab();

    expect(await screen.findByRole('alert')).toHaveTextContent("Product 'X-1' was not found.");
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
