import { apiFetch } from '../../api/client';

export interface CategoryTreeNode {
  id: number;
  code: string;
  name: string;
  /** Locale the name came from; null when the node has no translation at all. */
  nameLocale: string | null;
  nodeType: string;
  depth: number;
  status: string;
  directProductCount: number;
  subtreeProductCount: number;
  children: CategoryTreeNode[];
}

export interface AttributeTemplateRow {
  code: string;
  label: string;
  labelLocale: string | null;
  groupCode: string;
  groupLabel: string;
  dataType: string;
  isRequired: boolean;
  coveragePct: number;
  productsInScope: number;
}

export interface CategoryLabel {
  localeCode: string;
  localeName: string;
  isBase: boolean;
  name: string | null;
  translationStatus: string;
}

export interface AttributeSetUsage {
  code: string;
  name: string;
  productCount: number;
}

/**
 * Where the template shown came from.
 *
 * `taxonomy_node.attribute_set_id` is the DECLARED template, and the client's
 * catalogue does not set it on any node. Rather than render an empty panel
 * forever — which reads as "this branch demands nothing" — the API falls back
 * to the set the products underneath actually use and says which it is showing.
 */
export type TemplateSource = 'none' | 'declared' | 'in-use' | 'mixed';

export interface CategoryDetail {
  id: number;
  code: string;
  name: string;
  nameLocale: string | null;
  description: string | null;
  nodeType: string;
  depth: number;
  status: string;
  parentCode: string | null;
  parentName: string | null;
  brandName: string | null;
  directProductCount: number;
  subtreeProductCount: number;
  attributeSetCode: string | null;
  attributeSetName: string | null;
  requiredAttributeCount: number;
  templateSource: TemplateSource;
  setsInUse: AttributeSetUsage[];
  template: AttributeTemplateRow[];
  labels: CategoryLabel[];
}

function localeQuery(locale: string | null): string {
  return locale ? `?locale=${encodeURIComponent(locale)}` : '';
}

export function fetchCategoryTree(
  locale: string | null,
  signal?: AbortSignal,
): Promise<CategoryTreeNode[]> {
  return apiFetch<CategoryTreeNode[]>(`/api/v1/categories/tree${localeQuery(locale)}`, { signal });
}

export function fetchCategoryDetail(
  code: string,
  locale: string | null,
  signal?: AbortSignal,
): Promise<CategoryDetail> {
  return apiFetch<CategoryDetail>(
    `/api/v1/categories/${encodeURIComponent(code)}${localeQuery(locale)}`,
    { signal },
  );
}

/** Depth-first walk, used to find a node and to seed the expanded set. */
export function walkTree(
  nodes: CategoryTreeNode[],
  visit: (node: CategoryTreeNode, ancestors: CategoryTreeNode[]) => void,
  ancestors: CategoryTreeNode[] = [],
): void {
  for (const node of nodes) {
    visit(node, ancestors);
    if (node.children.length > 0) {
      walkTree(node.children, visit, [...ancestors, node]);
    }
  }
}
