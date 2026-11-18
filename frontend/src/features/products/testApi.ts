import { vi } from 'vitest';
import type { PagedResult, ProductListItem } from './types';
import type { ProductWorkflow, WorkflowAction } from '../workflow/workflowApi';

/**
 * Minimal fake of the catalog API over `fetch`, pinned to the real contract
 * (paths, query parameter names, RFC 7807 error shape). Tests assert against
 * the captured requests, so a contract drift fails here rather than in prod.
 */
export interface FakeApi {
  /** Every product list request, in order, as parsed query parameters. */
  listCalls: URLSearchParams[];
  exportCalls: URLSearchParams[];
  createBodies: unknown[];
  restore: () => void;
}

export function makeListItem(overrides: Partial<ProductListItem> = {}): ProductListItem {
  return {
    publicId: crypto.randomUUID(),
    code: 'FD-90-GS-1000',
    name: 'Fire Door 90 Min',
    category: 'Fire Doors',
    lifecycleState: 'Active',
    workflowState: 'Approval',
    completenessPct: 96,
    languages: ['de-DE', 'en-GB'],
    variantCount: 4,
    owner: 'Anna Becker',
    updatedAtUtc: '2026-06-22T00:00:00Z',
    // Derived server-side; 1 = partial for a 96% unpublished product.
    channelReadiness: [1, 1, 1, 1],
    // Default to "this is a real translation in the selected language", so a
    // test only opts into the fallback marker when it is what is being tested.
    nameLocale: 'en-GB',
    categoryLocale: 'en-GB',
    ...overrides,
  };
}

export function makePage(
  items: ProductListItem[],
  overrides: Partial<PagedResult<ProductListItem>> = {},
): PagedResult<ProductListItem> {
  return { items, page: 1, pageSize: 25, totalCount: items.length, ...overrides };
}

export interface FakeApiOptions {
  page?: PagedResult<ProductListItem>;
  /** Fails the list request with this RFC 7807 payload and status. */
  listError?: { status: number; title: string; detail: string };
  createError?: { status: number; title: string; detail: string };
  exportError?: { status: number; title: string; detail: string };
  brands?: Array<{ id: number; code: string; name: string }>;
  categories?: Array<{ id: number; code: string; name: string; attributeSetId: number | null }>;
}

export function installFakeApi(options: FakeApiOptions = {}): FakeApi {
  const listCalls: URLSearchParams[] = [];
  const exportCalls: URLSearchParams[] = [];
  const createBodies: unknown[] = [];

  const brands = options.brands ?? [{ id: 1, code: 'buildcore', name: 'BuildCore' }];
  const categories = options.categories ?? [
    { id: 2, code: 'fire_doors', name: 'Fire Doors', attributeSetId: 7 },
  ];

  function json(body: unknown, status = 200): Response {
    return new Response(JSON.stringify(body), {
      status,
      headers: { 'Content-Type': 'application/json' },
    });
  }

  function problem(p: { status: number; title: string; detail: string }): Response {
    return new Response(JSON.stringify(p), {
      status: p.status,
      headers: { 'Content-Type': 'application/problem+json' },
    });
  }

  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://localhost');

    if (url.pathname === '/api/v1/brands') {
      return json(brands);
    }

    // The shell asks for this on mount. Two locales so the selector renders —
    // it hides itself below two, and a test that cannot see the control would
    // pass while the feature was broken.
    if (url.pathname === '/api/v1/locales') {
      return json([
        { code: 'de-DE', name: 'Deutsch', isBase: true },
        { code: 'en-GB', name: 'English (UK)', isBase: false },
      ]);
    }

    if (url.pathname === '/api/v1/taxonomy-nodes') {
      const nodeType = url.searchParams.get('nodeType');
      if (nodeType === 'FAMILY') {
        return json([{ id: 1, code: 'products', name: 'Doors & Frames', nodeType: 'FAMILY', parentId: null, attributeSetId: null }]);
      }
      return json(
        categories.map((c) => ({ ...c, nodeType: 'CATEGORY', parentId: 1 })),
      );
    }

    if (url.pathname === '/api/v1/products/export.csv') {
      exportCalls.push(url.searchParams);
      if (options.exportError) {
        return problem(options.exportError);
      }
      return new Response('Code,Name\nFD-90-GS-1000,Fire Door 90 Min\n', {
        status: 200,
        headers: { 'Content-Type': 'text/csv; charset=utf-8' },
      });
    }

    if (url.pathname === '/api/v1/products' && (init?.method ?? 'GET') === 'GET') {
      listCalls.push(url.searchParams);
      if (options.listError) {
        return problem(options.listError);
      }
      return json(options.page ?? makePage([makeListItem()]));
    }

    if (url.pathname === '/api/v1/products' && init?.method === 'POST') {
      createBodies.push(JSON.parse(String(init.body)));
      if (options.createError) {
        return problem(options.createError);
      }
      const body = JSON.parse(String(init.body)) as { code: string; name: string };
      return json({ publicId: crypto.randomUUID(), code: body.code, name: body.name }, 201);
    }

    throw new Error(`Unexpected request: ${init?.method ?? 'GET'} ${url.pathname}`);
  });

  vi.stubGlobal('fetch', fetchMock);

  return { listCalls, exportCalls, createBodies, restore: () => vi.unstubAllGlobals() };
}

/**
 * A fake of GET /products/{code}/workflow.
 *
 * It restates the shipped pipeline, which is a third copy after the schema and
 * the domain test fixture — and it is the right kind of copy: this one stands
 * in for the SERVER, so a test using it is asserting what the UI does with a
 * given pipeline rather than re-asserting what the pipeline is. The pipeline
 * itself is checked against a real database in WorkflowPipelineTests.
 */
const WORKFLOW_STEP_NAMES: ReadonlyArray<readonly [string, string]> = [
  ['draft', 'Draft'],
  ['review', 'Review'],
  ['approval', 'Approval'],
  ['published', 'Published'],
];

const WORKFLOW_ACTIONS: Record<string, WorkflowAction[]> = {
  draft: [{ action: 'SUBMIT', label: 'Submit for review', targetStep: 'review', targetStepName: 'Review' }],
  review: [
    { action: 'APPROVE', label: 'Pass review', targetStep: 'approval', targetStepName: 'Approval' },
    { action: 'REQUEST_CHANGES', label: 'Request changes', targetStep: 'draft', targetStepName: 'Draft' },
  ],
  approval: [
    { action: 'APPROVE', label: 'Approve and publish', targetStep: 'published', targetStepName: 'Published' },
    { action: 'REJECT', label: 'Reject', targetStep: 'draft', targetStepName: 'Draft' },
    { action: 'REQUEST_CHANGES', label: 'Request changes', targetStep: 'draft', targetStepName: 'Draft' },
  ],
  published: [],
};

export function makeWorkflow(
  overrides: Partial<ProductWorkflow> & { workflowState?: string } = {},
): ProductWorkflow {
  const state = overrides.workflowState ?? overrides.currentStep ?? 'Draft';
  const code = state.toLowerCase();
  const index = WORKFLOW_STEP_NAMES.findIndex(([stepCode]) => stepCode === code);
  const resolved = index === -1 ? 0 : index;

  const rest = { ...overrides };
  delete rest.workflowState;

  return {
    productCode: 'FD-90-GS-1000',
    definitionCode: 'product_publishing',
    definitionName: 'Product Publishing',
    definitionVersion: 1,
    currentStep: WORKFLOW_STEP_NAMES[resolved][0],
    currentStepName: WORKFLOW_STEP_NAMES[resolved][1],
    currentStepOwnerRole: 'Data Steward',
    isComplete: resolved === WORKFLOW_STEP_NAMES.length - 1,
    assignee: 'Anna Becker',
    priority: 'normal',
    dueDate: null,
    taskStatus: 'open',
    rowVersion: 3,
    actorAttribution:
      'Names in this trail are unverified: there is no sign-in yet, so each entry records ' +
      'what the caller supplied rather than who they are.',
    steps: WORKFLOW_STEP_NAMES.map(([stepCode, name], i) => ({
      code: stepCode,
      name,
      sortOrder: i + 1,
      ownerRole: null,
      isTerminal: i === WORKFLOW_STEP_NAMES.length - 1,
      isCurrent: i === resolved,
      isPassed: i < resolved,
    })),
    availableActions: WORKFLOW_ACTIONS[WORKFLOW_STEP_NAMES[resolved][0]] ?? [],
    history: [],
    ...rest,
  };
}
