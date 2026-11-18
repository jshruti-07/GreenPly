import { apiFetch } from '../../api/client';

export interface LocaleProgress {
  code: string;
  name: string;
  isBase: boolean;
  totalProducts: number;
  /** Products with text in this locale, whatever its status. */
  withText: number;
  missing: number;
  /** Base-locale text edited since this translation was written. */
  outdated: number;
  awaitingReview: number;
  approved: number;
  percentTranslated: number;
}

export interface TranslationOmittedPanel {
  panel: string;
  reason: string;
}

export interface TranslationWorkbench {
  baseLocaleCode: string;
  baseLocaleName: string;
  totalProducts: number;
  locales: LocaleProgress[];
  omittedPanels: TranslationOmittedPanel[];
}

export interface TranslationQueueItem {
  productCode: string;
  name: string;
  nameLocale: string | null;
  category: string;
  /** 'missing' | 'outdated' | whatever product_tr.translation_status holds. */
  state: string;
  translationStatus: string | null;
  source: string | null;
  targetUpdatedAtUtc: string | null;
  baseUpdatedAtUtc: string | null;
}

export interface TranslationQueue {
  targetLocale: string;
  state: string;
  page: number;
  pageSize: number;
  totalCount: number;
  items: TranslationQueueItem[];
}

export type QueueState = 'attention' | 'missing' | 'outdated' | 'review' | 'all';

export function fetchTranslationWorkbench(
  locale: string | null,
  signal?: AbortSignal,
): Promise<TranslationWorkbench> {
  const query = locale ? `?locale=${encodeURIComponent(locale)}` : '';
  return apiFetch<TranslationWorkbench>(`/api/v1/translations${query}`, { signal });
}

export function fetchTranslationQueue(
  params: {
    target: string;
    state: QueueState;
    search?: string;
    page: number;
    pageSize: number;
    locale: string | null;
  },
  signal?: AbortSignal,
): Promise<TranslationQueue> {
  const query = new URLSearchParams({
    target: params.target,
    state: params.state,
    page: String(params.page),
    pageSize: String(params.pageSize),
  });
  if (params.search) query.set('search', params.search);
  if (params.locale) query.set('locale', params.locale);

  return apiFetch<TranslationQueue>(`/api/v1/translations/queue?${query}`, { signal });
}
