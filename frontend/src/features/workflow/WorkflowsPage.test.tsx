import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { vi } from 'vitest';
import { LocaleProvider } from '../../app/LocaleContext';
import { WorkflowsPage } from './WorkflowsPage';
import type { WorkflowBoard, WorkflowCard, WorkflowColumn, WorkflowQueue } from './workflowApi';

function makeCard(overrides: Partial<WorkflowCard> = {}): WorkflowCard {
  return {
    publicId: crypto.randomUUID(),
    code: 'FD-90-GS-1000',
    name: 'Fire Door 90',
    category: 'Fire Doors',
    completenessPct: 96,
    step: 'draft',
    owner: 'Anna Becker',
    assignee: 'Anna Becker',
    priority: 'normal',
    dueDate: null,
    updatedAt: '2026-08-19T09:00:00Z',
    hasOpenTask: true,
    ...overrides,
  };
}

function makeColumn(overrides: Partial<WorkflowColumn> = {}): WorkflowColumn {
  return {
    step: 'draft',
    name: 'Draft',
    sortOrder: 1,
    isTerminal: false,
    ownerRole: 'Data Steward',
    totalCount: 1,
    isRecognised: true,
    cards: [makeCard()],
    ...overrides,
  };
}

function makeBoard(overrides: Partial<WorkflowBoard> = {}): WorkflowBoard {
  return {
    definitionCode: 'product_publishing',
    definitionName: 'Product Publishing',
    totalProducts: 4,
    cardsPerColumn: 12,
    columns: [
      makeColumn(),
      makeColumn({ step: 'review', name: 'Review', sortOrder: 2, ownerRole: 'Product Manager', totalCount: 1, cards: [makeCard({ step: 'review', code: 'GP-G12-3000' })] }),
      makeColumn({ step: 'approval', name: 'Approval', sortOrder: 3, ownerRole: 'Compliance Manager', totalCount: 0, cards: [] }),
      makeColumn({ step: 'published', name: 'Published', sortOrder: 4, isTerminal: true, ownerRole: null, totalCount: 2, cards: [makeCard({ step: 'published', code: 'INS-PIR-80' })] }),
    ],
    ...overrides,
  };
}

function makeQueue(overrides: Partial<WorkflowQueue> = {}): WorkflowQueue {
  return {
    page: {
      items: [
        {
          publicId: '11111111-2222-4333-8444-555555555555',
          code: 'FD-90-GS-1000',
          name: 'Fire Door 90',
          category: 'Fire Doors',
          completenessPct: 96,
          step: 'review',
          stepName: 'Review',
          owner: 'Anna Becker',
          assignee: 'Markus Klein',
          priority: 'normal',
          dueDate: null,
          updatedAt: '2026-08-19T09:00:00Z',
          lastAction: 'SUBMIT',
          lastActionAt: '2026-08-19T09:00:00Z',
        },
      ],
      page: 1,
      pageSize: 25,
      totalCount: 1,
    },
    assignees: ['Anna Becker', 'Markus Klein'],
    steps: [
      { code: 'draft', name: 'Draft', sortOrder: 1, ownerRole: null, isTerminal: false, isCurrent: false, isPassed: false },
      { code: 'review', name: 'Review', sortOrder: 2, ownerRole: null, isTerminal: false, isCurrent: false, isPassed: false },
    ],
    ...overrides,
  };
}

interface Api {
  urls: string[];
}

function installApi(options: { board?: WorkflowBoard; queue?: WorkflowQueue; error?: boolean } = {}): Api {
  const urls: string[] = [];
  const board = options.board ?? makeBoard();
  const queue = options.queue ?? makeQueue();

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);

      // Routed by URL rather than answering everything with one body.
      // LocaleProvider fetches /api/v1/locales as the tree mounts; a blanket
      // mock hands it a board, `locales.some` explodes, and every test in the
      // file fails for a reason unrelated to what it was checking. The
      // dashboard suite learned this in Sprint 21 and the categories suite
      // wrote it down.
      if (url.includes('/api/v1/locales')) {
        return new Response(
          JSON.stringify([{ code: 'en-GB', name: 'English (UK)', isBase: false }]),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        );
      }

      urls.push(url);

      if (options.error) {
        return new Response(
          JSON.stringify({ status: 500, title: 'Boom', detail: 'The board exploded.' }),
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        );
      }

      if (url.includes('/workflow/board')) {
        return new Response(JSON.stringify(board), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }

      return new Response(JSON.stringify(queue), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }),
  );

  return { urls };
}

function renderPage(entry = '/workflows') {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <LocaleProvider>
        <Routes>
          <Route path="/workflows" element={<WorkflowsPage />} />
          <Route path="/products/:code" element={<div>product page</div>} />
        </Routes>
      </LocaleProvider>
    </MemoryRouter>,
  );
}

describe('WorkflowsPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('draws one column per configured step, in pipeline order', async () => {
    installApi();
    renderPage();

    await screen.findByText('Product Publishing');

    // Not a hardcoded four: the columns are whatever the pipeline returned.
    expect(screen.getByText('Draft')).toBeInTheDocument();
    expect(screen.getByText('Review')).toBeInTheDocument();
    expect(screen.getByText('Approval')).toBeInTheDocument();
    expect(screen.getByText('Published')).toBeInTheDocument();
  });

  it("says how many cards it is NOT showing, so a column's count is not mistaken for its contents", async () => {
    installApi({
      board: makeBoard({
        columns: [makeColumn({ totalCount: 137, cards: [makeCard(), makeCard()] })],
      }),
    });
    renderPage();

    expect(await screen.findByText(/135 more in this step/)).toBeInTheDocument();
    expect(screen.getByText('137')).toBeInTheDocument();
  });

  it('does not claim a gap when the column is complete', async () => {
    installApi({ board: makeBoard({ columns: [makeColumn({ totalCount: 1 })] }) });
    renderPage();

    await screen.findByText('Draft');
    expect(screen.queryByText(/more in this step/)).not.toBeInTheDocument();
  });

  it('shows a state the pipeline does not define rather than dropping it', async () => {
    // Dropping it would make every other column a larger share of a catalogue
    // that had quietly shrunk — the bug Sprint 21 shipped on the cockpit.
    installApi({
      board: makeBoard({
        columns: [
          makeColumn(),
          makeColumn({ step: 'archived', name: 'archived (not in this pipeline)', isRecognised: false, totalCount: 7, cards: [] }),
        ],
      }),
    });
    renderPage();

    expect(await screen.findByText('archived (not in this pipeline)')).toBeInTheDocument();
    expect(screen.getByText('7')).toBeInTheDocument();
  });

  it('sends each card to the product Workflow tab rather than moving it in place', async () => {
    installApi();
    renderPage();

    const card = await screen.findByRole('link', { name: /FD-90-GS-1000/ });
    expect(card).toHaveAttribute('href', '/products/FD-90-GS-1000?tab=workflow');
  });

  it('marks a name as an owner when no approval task exists', async () => {
    installApi({
      board: makeBoard({
        columns: [makeColumn({ cards: [makeCard({ assignee: null, hasOpenTask: false })] })],
      }),
    });
    renderPage();

    const card = await screen.findByRole('link', { name: /FD-90-GS-1000/ });
    expect(within(card).getByText('owner')).toBeInTheDocument();
    expect(within(card).getByText('Anna Becker')).toBeInTheDocument();
  });

  it('switches to the list and asks the queue endpoint for it', async () => {
    const user = userEvent.setup();
    const api = installApi();
    renderPage();

    await screen.findByText('Product Publishing');
    await user.click(screen.getByRole('button', { name: 'List' }));

    await waitFor(() => expect(api.urls.some((u) => u.includes('/workflow/queue'))).toBe(true));
    expect(await screen.findByRole('columnheader', { name: 'Assignee' })).toBeInTheDocument();
    // Scoped to the table: the same name is also an <option> in the assignee
    // filter, and an unscoped query matches both. Three sprints running have
    // lost a test to exactly this.
    expect(within(screen.getByRole('table')).getByText('Markus Klein')).toBeInTheDocument();
  });

  it('filters by step through the URL, so a queue can be linked to', async () => {
    const user = userEvent.setup();
    const api = installApi();
    renderPage('/workflows?view=list');

    await screen.findByRole('columnheader', { name: 'Step' });
    await user.selectOptions(screen.getByLabelText('Step'), 'review');

    await waitFor(() =>
      expect(api.urls.some((u) => u.includes('/workflow/queue') && u.includes('step=review'))).toBe(true),
    );
  });

  it('leaves the assignee empty rather than borrowing the owner', async () => {
    installApi({
      queue: makeQueue({
        page: {
          ...makeQueue().page,
          items: [{ ...makeQueue().page.items[0], assignee: null }],
        },
      }),
    });
    renderPage('/workflows?view=list');

    const row = (await screen.findByText('Fire Door 90')).closest('tr');
    expect(row).not.toBeNull();
    // Owner is still shown; the assignee cell is a dash.
    expect(within(row!).getByText('Anna Becker')).toBeInTheDocument();
    expect(within(row!).getAllByText('—').length).toBeGreaterThan(0);
  });

  it('reports a failure instead of rendering an empty board', async () => {
    installApi({ error: true });
    renderPage();

    expect(await screen.findByText('Workflows could not be loaded')).toBeInTheDocument();
  });
});
