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

function sanitizeNode(node: CategoryTreeNode): CategoryTreeNode {
  const isGreenplyCode = node.code.toLowerCase() === 'greenply';
  return {
    ...node,
    code: isGreenplyCode ? 'plygallery' : node.code,
    name: node.name ? node.name.replace(/Greenply/gi, 'Ply Gallery') : node.name,
    children: (node.children || []).map(sanitizeNode),
  };
}

function sanitizeDetail(detail: CategoryDetail): CategoryDetail {
  const isGreenplyCode = detail.code.toLowerCase() === 'greenply';
  const isGreenplyParent = detail.parentCode?.toLowerCase() === 'greenply';
  return {
    ...detail,
    code: isGreenplyCode ? 'plygallery' : detail.code,
    name: detail.name ? detail.name.replace(/Greenply/gi, 'Ply Gallery') : detail.name,
    parentCode: isGreenplyParent ? 'plygallery' : detail.parentCode,
    parentName: detail.parentName ? detail.parentName.replace(/Greenply/gi, 'Ply Gallery') : detail.parentName,
    brandName: detail.brandName ? detail.brandName.replace(/Greenply/gi, 'Ply Gallery') : 'Ply Gallery',
    labels: (detail.labels || []).map((l) => ({
      ...l,
      name: l.name ? l.name.replace(/Greenply/gi, 'Ply Gallery') : l.name,
    })),
  };
}

export async function fetchCategoryTree(
  locale: string | null,
  signal?: AbortSignal,
): Promise<CategoryTreeNode[]> {
  const tree = await apiFetch<CategoryTreeNode[]>(`/api/v1/categories/tree${localeQuery(locale)}`, { signal });
  return (tree || []).map(sanitizeNode);
}

export async function fetchCategoryDetail(
  code: string,
  locale: string | null,
  signal?: AbortSignal,
): Promise<CategoryDetail> {
  const isTargetPlyGallery = code.toLowerCase() === 'plygallery' || code.toLowerCase() === 'greenply';
  try {
    const detail = await apiFetch<CategoryDetail>(
      `/api/v1/categories/${encodeURIComponent(code)}${localeQuery(locale)}`,
      { signal },
    );
    return sanitizeDetail(detail);
  } catch (err) {
    if (isTargetPlyGallery) {
      const fallbackCode = code.toLowerCase() === 'plygallery' ? 'greenply' : 'plygallery';
      try {
        const fallback = await apiFetch<CategoryDetail>(
          `/api/v1/categories/${encodeURIComponent(fallbackCode)}${localeQuery(locale)}`,
          { signal },
        );
        return sanitizeDetail(fallback);
      } catch {
        // rethrow original error
      }
    }
    throw err;
  }
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
