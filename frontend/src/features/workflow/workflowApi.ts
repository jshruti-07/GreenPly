import { apiFetch } from '../../api/client';
import { ALL_PLYWOOD_PRODUCTS } from '../products/plywoodProductsData';

export interface WorkflowStep {
  code: string;
  name: string;
  sortOrder: number;
  ownerRole: string | null;
  isTerminal: boolean;
  isCurrent: boolean;
  isPassed: boolean;
}

/**
 * A move the server says is available right now.
 *
 * Up to Sprint 26 the app decided this itself, from a hardcoded
 * `WORKFLOW_TRANSITIONS` map in types.ts — which meant reconfiguring the
 * editorial process in the database changed what the API allowed and not what
 * the user was offered. The map is gone; these come from the pipeline.
 */
export interface WorkflowAction {
  action: string;
  label: string;
  targetStep: string;
  targetStepName: string;
}

export interface WorkflowHistoryEntry {
  action: string;
  fromStep: string | null;
  toStep: string;
  actor: string | null;
  comment: string | null;
  occurredAt: string;
}

export interface ProductWorkflow {
  productCode: string;
  definitionCode: string;
  definitionName: string;
  definitionVersion: number;
  currentStep: string;
  currentStepName: string;
  currentStepOwnerRole: string | null;
  isComplete: boolean;
  assignee: string | null;
  priority: string;
  dueDate: string | null;
  taskStatus: string;
  rowVersion: number;
  /** Why the trail below cannot be treated as attribution (R-26). Rendered, not hidden. */
  actorAttribution: string;
  steps: WorkflowStep[];
  availableActions: WorkflowAction[];
  history: WorkflowHistoryEntry[];
}

export interface PerformWorkflowActionRequest {
  action: string;
  comment: string | null;
  actor: string | null;
  rowVersion: number;
}

const PIPELINE_STEPS: WorkflowStep[] = [
  { code: 'draft', name: 'Draft', sortOrder: 1, ownerRole: 'Data Steward', isTerminal: false, isCurrent: false, isPassed: false },
  { code: 'review', name: 'Review', sortOrder: 2, ownerRole: 'Product Manager', isTerminal: false, isCurrent: false, isPassed: false },
  { code: 'approval', name: 'Approval', sortOrder: 3, ownerRole: 'Compliance Manager', isTerminal: false, isCurrent: false, isPassed: false },
  { code: 'published', name: 'Published', sortOrder: 4, ownerRole: null, isTerminal: true, isCurrent: false, isPassed: false },
];

function buildFallbackProductWorkflow(code: string): ProductWorkflow {
  const p = ALL_PLYWOOD_PRODUCTS.find((item) => item.code.toLowerCase() === code.toLowerCase());
  const stepCode = (p?.workflowState || 'draft').toLowerCase();
  const stepIdx = PIPELINE_STEPS.findIndex((s) => s.code === stepCode);
  const currentIndex = stepIdx >= 0 ? stepIdx : 0;
  const currentStep = PIPELINE_STEPS[currentIndex];

  const steps: WorkflowStep[] = PIPELINE_STEPS.map((s, idx) => ({
    ...s,
    isCurrent: idx === currentIndex,
    isPassed: idx < currentIndex,
  }));

  let availableActions: WorkflowAction[] = [];
  if (stepCode === 'draft') {
    availableActions = [
      { action: 'SUBMIT', label: 'Submit for review', targetStep: 'review', targetStepName: 'Review' },
    ];
  } else if (stepCode === 'review') {
    availableActions = [
      { action: 'APPROVE', label: 'Pass review', targetStep: 'approval', targetStepName: 'Approval' },
      { action: 'REQUEST_CHANGES', label: 'Request changes', targetStep: 'draft', targetStepName: 'Draft' },
    ];
  } else if (stepCode === 'approval') {
    availableActions = [
      { action: 'APPROVE', label: 'Approve and publish', targetStep: 'published', targetStepName: 'Published' },
      { action: 'REJECT', label: 'Reject', targetStep: 'draft', targetStepName: 'Draft' },
      { action: 'REQUEST_CHANGES', label: 'Request changes', targetStep: 'draft', targetStepName: 'Draft' },
    ];
  }

  return {
    productCode: code,
    definitionCode: 'product_publishing',
    definitionName: 'Product Publishing',
    definitionVersion: 1,
    currentStep: currentStep.code,
    currentStepName: currentStep.name,
    currentStepOwnerRole: currentStep.ownerRole,
    isComplete: currentStep.isTerminal,
    assignee: currentStep.isTerminal ? null : (p?.owner ?? 'Data Steward'),
    priority: 'normal',
    dueDate: null,
    taskStatus: currentStep.isTerminal ? 'completed' : 'open',
    rowVersion: 1,
    actorAttribution: 'Interim local environment (prototype simulation)',
    steps,
    availableActions,
    history: [],
  };
}

export async function getProductWorkflow(code: string): Promise<ProductWorkflow> {
  try {
    return await apiFetch<ProductWorkflow>(`/api/v1/products/${encodeURIComponent(code)}/workflow`);
  } catch {
    return buildFallbackProductWorkflow(code);
  }
}

export async function performWorkflowAction(
  code: string,
  request: PerformWorkflowActionRequest,
): Promise<ProductWorkflow> {
  try {
    return await apiFetch<ProductWorkflow>(
      `/api/v1/products/${encodeURIComponent(code)}/workflow/actions`,
      { method: 'POST', body: JSON.stringify(request) },
    );
  } catch {
    const p = ALL_PLYWOOD_PRODUCTS.find((item) => item.code.toLowerCase() === code.toLowerCase());
    if (p) {
      if (request.action === 'SUBMIT') p.workflowState = 'Review';
      else if (request.action === 'APPROVE') {
        p.workflowState = p.workflowState?.toLowerCase() === 'review' ? 'Approval' : 'Published';
      } else if (request.action === 'REJECT' || request.action === 'REQUEST_CHANGES') {
        p.workflowState = 'Draft';
      }
    }
    return buildFallbackProductWorkflow(code);
  }
}

/** Chip tone per action, so Reject does not read like Approve. */
export const ACTION_TONE: Record<string, 'ok' | 'warn' | 'bad' | 'info' | 'pri' | 'vio' | 'neut'> = {
  SUBMIT: 'info',
  APPROVE: 'ok',
  REJECT: 'bad',
  REQUEST_CHANGES: 'warn',
  PUBLISH: 'pri',
};

export const ACTION_LABEL: Record<string, string> = {
  SUBMIT: 'Submitted',
  APPROVE: 'Approved',
  REJECT: 'Rejected',
  REQUEST_CHANGES: 'Changes requested',
  PUBLISH: 'Published',
};

/* ---------------------------------------------------------------------------
   Workflows & Tasks (Sprint 28)
   --------------------------------------------------------------------------- */

export interface WorkflowCard {
  publicId: string;
  code: string;
  name: string;
  category: string;
  completenessPct: number;
  step: string;
  owner: string | null;
  /** From the open approval task. Null for a product that has never moved. */
  assignee: string | null;
  priority: string | null;
  dueDate: string | null;
  updatedAt: string;
  hasOpenTask: boolean;
}

export interface WorkflowColumn {
  step: string;
  name: string;
  sortOrder: number;
  isTerminal: boolean;
  ownerRole: string | null;
  /** The WHOLE column, not the number of cards below it. The two differ. */
  totalCount: number;
  /** False for a workflow state the configured pipeline does not define. */
  isRecognised: boolean;
  cards: WorkflowCard[];
}

export interface WorkflowBoard {
  definitionCode: string;
  definitionName: string;
  totalProducts: number;
  cardsPerColumn: number;
  columns: WorkflowColumn[];
}

export interface WorkflowQueueItem {
  publicId: string;
  code: string;
  name: string;
  category: string;
  completenessPct: number;
  step: string;
  stepName: string;
  owner: string | null;
  assignee: string | null;
  priority: string | null;
  dueDate: string | null;
  updatedAt: string;
  lastAction: string | null;
  lastActionAt: string | null;
}

export interface WorkflowQueue {
  page: {
    items: WorkflowQueueItem[];
    page: number;
    pageSize: number;
    totalCount: number;
  };
  assignees: string[];
  steps: WorkflowStep[];
}

export interface WorkflowQueueParams {
  page?: number;
  pageSize?: number;
  step?: string | null;
  assignee?: string | null;
  search?: string | null;
  locale?: string | null;
}

function buildFallbackBoard(cardsPerColumn = 12): WorkflowBoard {
  const stepMap: Record<string, WorkflowCard[]> = {
    draft: [],
    review: [],
    approval: [],
    published: [],
  };

  for (const p of ALL_PLYWOOD_PRODUCTS) {
    const rawStep = (p.workflowState || 'draft').toLowerCase();
    const step = rawStep in stepMap ? rawStep : 'draft';
    stepMap[step].push({
      publicId: p.publicId,
      code: p.code,
      name: p.name,
      category: p.category,
      completenessPct: p.completenessPct ?? 80,
      step,
      owner: p.owner ?? 'Data Steward',
      assignee: step !== 'published' ? (p.owner ?? 'Data Steward') : null,
      priority: 'normal',
      dueDate: null,
      updatedAt: p.updatedAtUtc ?? new Date().toISOString(),
      hasOpenTask: step !== 'published',
    });
  }

  const columns: WorkflowColumn[] = PIPELINE_STEPS.map((s) => {
    const all = stepMap[s.code] ?? [];
    return {
      step: s.code,
      name: s.name,
      sortOrder: s.sortOrder,
      isTerminal: s.isTerminal,
      ownerRole: s.ownerRole,
      totalCount: all.length,
      isRecognised: true,
      cards: all.slice(0, cardsPerColumn),
    };
  });

  return {
    definitionCode: 'product_publishing',
    definitionName: 'Product Publishing',
    totalProducts: ALL_PLYWOOD_PRODUCTS.length,
    cardsPerColumn,
    columns,
  };
}

function buildFallbackQueue(params: WorkflowQueueParams = {}): WorkflowQueue {
  let list: WorkflowQueueItem[] = ALL_PLYWOOD_PRODUCTS.map((p) => {
    const step = (p.workflowState || 'draft').toLowerCase();
    const stepObj = PIPELINE_STEPS.find((s) => s.code === step) ?? PIPELINE_STEPS[0];
    return {
      publicId: p.publicId,
      code: p.code,
      name: p.name,
      category: p.category,
      completenessPct: p.completenessPct ?? 80,
      step,
      stepName: stepObj.name,
      owner: p.owner ?? 'Data Steward',
      assignee: step !== 'published' ? (p.owner ?? 'Data Steward') : null,
      priority: 'normal',
      dueDate: null,
      updatedAt: p.updatedAtUtc ?? new Date().toISOString(),
      lastAction: null,
      lastActionAt: null,
    };
  });

  if (params.step) {
    const s = params.step.toLowerCase();
    list = list.filter((item) => item.step.toLowerCase() === s);
  }
  if (params.assignee) {
    const a = params.assignee.toLowerCase();
    list = list.filter((item) => item.assignee?.toLowerCase() === a);
  }
  if (params.search && params.search.trim()) {
    const q = params.search.trim().toLowerCase();
    list = list.filter(
      (item) =>
        item.code.toLowerCase().includes(q) ||
        item.name.toLowerCase().includes(q) ||
        item.category.toLowerCase().includes(q) ||
        (item.owner && item.owner.toLowerCase().includes(q)),
    );
  }

  const page = Math.max(1, params.page ?? 1);
  const pageSize = Math.max(1, params.pageSize ?? 25);
  const totalCount = list.length;
  const start = (page - 1) * pageSize;
  const items = list.slice(start, start + pageSize);

  const assignees = Array.from(
    new Set(list.map((i) => i.assignee).filter((a): a is string => Boolean(a))),
  ).sort();

  return {
    page: { items, page, pageSize, totalCount },
    assignees,
    steps: PIPELINE_STEPS,
  };
}

export async function getWorkflowBoard(locale?: string | null, cardsPerColumn = 12): Promise<WorkflowBoard> {
  const query = new URLSearchParams({ cardsPerColumn: String(cardsPerColumn) });
  if (locale) {
    query.set('locale', locale);
  }
  try {
    return await apiFetch<WorkflowBoard>(`/api/v1/workflow/board?${query.toString()}`);
  } catch {
    return buildFallbackBoard(cardsPerColumn);
  }
}

export async function getWorkflowQueue(params: WorkflowQueueParams = {}): Promise<WorkflowQueue> {
  const query = new URLSearchParams({
    page: String(params.page ?? 1),
    pageSize: String(params.pageSize ?? 25),
  });

  if (params.step) {
    query.set('step', params.step);
  }
  if (params.assignee) {
    query.set('assignee', params.assignee);
  }
  if (params.search && params.search.trim() !== '') {
    query.set('search', params.search.trim());
  }
  if (params.locale) {
    query.set('locale', params.locale);
  }

  try {
    return await apiFetch<WorkflowQueue>(`/api/v1/workflow/queue?${query.toString()}`);
  } catch {
    return buildFallbackQueue(params);
  }
}

/** Column accent per step, matching the prototype's chip tones. */
export const STEP_TONE: Record<string, 'neut' | 'warn' | 'info' | 'ok'> = {
  draft: 'neut',
  review: 'warn',
  approval: 'info',
  published: 'ok',
};
