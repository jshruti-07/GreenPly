import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useLocale } from '../../app/LocaleContext';
import { EmptyState } from '../../components/EmptyState';
import { FallbackMark } from '../products/FallbackMark';
import {
  fetchCategoryDetail,
  fetchCategoryTree,
  walkTree,
  type CategoryDetail,
  type CategoryTreeNode,
} from './categoriesApi';

/**
 * Says where the template came from, in the panel header.
 *
 * No node in this catalogue declares an attribute set, so without this line
 * every reader would take the inferred template for a configured one. The
 * difference matters the moment somebody changes a product's set and the
 * "template" silently changes with it.
 */
function TemplateProvenance({ detail }: { detail: CategoryDetail }) {
  if (detail.templateSource === 'declared') {
    return <span className="sp chip s-neut">declared on this category</span>;
  }
  if (detail.templateSource === 'in-use') {
    return (
      <span className="sp chip s-warn" title="No attribute set is configured on this category. This is the set its products were created with.">
        inferred from its products
      </span>
    );
  }
  return null;
}

/** v4.2 shows the node type as the "Level" field. Its own vocabulary, tidied. */
const NODE_TYPE_LABEL: Record<string, string> = {
  BRAND: 'Brand',
  FAMILY: 'Family',
  CATEGORY: 'Category',
  SUBCATEGORY: 'Sub-category',
  SERIES: 'Series',
};

function nodeTypeLabel(nodeType: string): string {
  return NODE_TYPE_LABEL[nodeType] ?? nodeType;
}

/**
 * One row of the hierarchy.
 *
 * The count shown is the SUBTREE count, because that is what the reader is
 * asking when they look at a branch. The direct count is on the detail panel
 * beside it, labelled, rather than being a second unexplained number in a tree
 * that already has one.
 */
function TreeRow({
  node,
  selected,
  expanded,
  requestedLocale,
  onSelect,
  onToggle,
}: {
  node: CategoryTreeNode;
  selected: string | null;
  expanded: ReadonlySet<string>;
  requestedLocale: string;
  onSelect: (code: string) => void;
  onToggle: (code: string) => void;
}) {
  const hasChildren = node.children.length > 0;
  const isOpen = expanded.has(node.code);

  return (
    <li>
      <div
        className={`node${selected === node.code ? ' on' : ''}`}
        style={{ paddingLeft: 8 + node.depth * 16 }}
      >
        {hasChildren ? (
          <button
            type="button"
            className="tw"
            aria-label={`${isOpen ? 'Collapse' : 'Expand'} ${node.name}`}
            aria-expanded={isOpen}
            onClick={() => onToggle(node.code)}
          >
            {isOpen ? '▾' : '▸'}
          </button>
        ) : (
          <span className="tw" aria-hidden="true" />
        )}
        <button
          type="button"
          className="node-label"
          aria-current={selected === node.code ? 'true' : undefined}
          onClick={() => onSelect(node.code)}
        >
          <span>{node.name}</span>
          <FallbackMark actual={node.nameLocale} requested={requestedLocale} />
        </button>
        <span className="cnt-pill num" title={`${node.subtreeProductCount} products in this branch`}>
          {node.subtreeProductCount.toLocaleString()}
        </span>
      </div>
      {hasChildren && isOpen && (
        <ul className="ch">
          {node.children.map((child) => (
            <TreeRow
              key={child.code}
              node={child}
              selected={selected}
              expanded={expanded}
              requestedLocale={requestedLocale}
              onSelect={onSelect}
              onToggle={onToggle}
            />
          ))}
        </ul>
      )}
    </li>
  );
}

function CoverageBar({ percent }: { percent: number }) {
  return (
    <div className="bar thin" role="img" aria-label={`Coverage ${percent}%`}>
      <span
        style={{
          width: `${percent}%`,
          background: percent >= 95 ? 'var(--grn)' : percent >= 75 ? 'var(--sky)' : 'var(--amb)',
        }}
      />
    </div>
  );
}

function DetailPanel({
  detail,
  requestedLocale,
}: {
  detail: CategoryDetail;
  requestedLocale: string;
}) {
  const facts: Array<[string, React.ReactNode]> = [
    ['Level', nodeTypeLabel(detail.nodeType)],
    ['Parent', detail.parentName ?? '—'],
    ['Brand', detail.brandName ?? '—'],
    ['Products here', detail.directProductCount.toLocaleString()],
    ['Products in branch', detail.subtreeProductCount.toLocaleString()],
    ['Code', <code key="code">{detail.code}</code>],
  ];

  return (
    <div className="grid" style={{ gap: 14 }}>
      <div className="card">
        <div className="card-h">
          <h3>
            {detail.name}
            <FallbackMark actual={detail.nameLocale} requested={requestedLocale} />
          </h3>
          <Link
            className="btn sm sp"
            to={`/products?categoryId=${detail.id}`}
            title="Opens the products grid filtered to this branch"
          >
            View {detail.subtreeProductCount.toLocaleString()} products
          </Link>
        </div>
        <div className="card-b">
          <div className="dmeta" style={{ display: 'grid', gridTemplateColumns: 'repeat(3,1fr)', gap: 14 }}>
            {facts.map(([label, value]) => (
              <div className="f" key={label}>
                <label>{label}</label>
                <b>{value}</b>
              </div>
            ))}
          </div>
          {detail.description && (
            <p className="muted" style={{ fontSize: 12, marginBottom: 0 }}>
              {detail.description}
            </p>
          )}
        </div>
      </div>

      <div className="card">
        <div className="card-h">
          <h3>Label per language</h3>
        </div>
        <div className="card-b" style={{ paddingTop: 6 }}>
          <table className="dg">
            <thead>
              <tr>
                <th>Language</th>
                <th>Label</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {detail.labels.map((l) => (
                <tr key={l.localeCode} style={{ cursor: 'default' }}>
                  <td>
                    {l.localeName}
                    {l.isBase && <span className="chip s-neut sp">base</span>}
                  </td>
                  {/* An untranslated language is the fact this table exists to
                      show, so it is stated rather than left as an empty cell —
                      a blank reads as a rendering bug. */}
                  <td className={l.name ? undefined : 'muted'}>{l.name ?? 'Not translated'}</td>
                  <td className="muted">{l.translationStatus}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div className="card">
        <div className="card-h">
          <h3>
            {detail.attributeSetName
              ? `Attribute template — ${detail.attributeSetName}`
              : 'Attribute template'}
          </h3>
          <TemplateProvenance detail={detail} />
          {detail.template.length > 0 && (
            <span className="sp chip s-neut">
              {detail.template.length} attributes · {detail.requiredAttributeCount} required
            </span>
          )}
        </div>
        <div className="card-b" style={{ paddingTop: 6 }}>
          {detail.templateSource === 'mixed' ? (
            <>
              <EmptyState
                title="No single template applies here"
                description="The products beneath this category were created with more than one attribute set, so there is no one list of attributes to show. Merging them would need a rule for an attribute that is required in one set and optional in another, and no such rule has been agreed."
              />
              <ul style={{ fontSize: 12, marginTop: 10 }}>
                {detail.setsInUse.map((set) => (
                  <li key={set.code}>
                    {set.name} — {set.productCount.toLocaleString()} products
                  </li>
                ))}
              </ul>
            </>
          ) : detail.template.length === 0 ? (
            <EmptyState
              title="No attribute template"
              description="Nothing is configured on this category, and there are no products beneath it to infer one from. This is not an empty template — there is no template."
            />
          ) : (
            <>
              <p className="muted" style={{ marginTop: 0, fontSize: 11.5 }}>
                Coverage is the share of the {detail.subtreeProductCount.toLocaleString()} products
                in this branch that hold an approved value — the same rule the grid and the
                dashboard use.
              </p>
              <table className="dg">
                <thead>
                  <tr>
                    <th>Attribute</th>
                    <th>Group</th>
                    <th>Type</th>
                    <th>Required</th>
                    <th style={{ width: 150 }}>Coverage</th>
                  </tr>
                </thead>
                <tbody>
                  {detail.template.map((row) => (
                    <tr key={row.code} style={{ cursor: 'default' }}>
                      <td style={{ fontWeight: 600 }}>
                        {row.label}
                        <FallbackMark actual={row.labelLocale} requested={requestedLocale} />
                      </td>
                      <td>{row.groupLabel ? <span className="tag">{row.groupLabel}</span> : '—'}</td>
                      <td className="muted">{row.dataType}</td>
                      <td>{row.isRequired ? 'Yes' : <span className="muted">—</span>}</td>
                      <td>
                        {row.productsInScope === 0 ? (
                          <span className="muted" style={{ fontSize: 11 }}>
                            no products to measure
                          </span>
                        ) : (
                          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                            <CoverageBar percent={row.coveragePct} />
                            <span className="num muted" style={{ fontSize: 11 }}>
                              {row.coveragePct}%
                            </span>
                          </div>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
        </div>
      </div>
    </div>
  );
}

export function CategoriesPage() {
  const { locale } = useLocale();
  const [searchParams, setSearchParams] = useSearchParams();
  const selected = searchParams.get('node');

  const [tree, setTree] = useState<CategoryTreeNode[] | null>(null);
  const [treeError, setTreeError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [filter, setFilter] = useState('');

  const [detail, setDetail] = useState<CategoryDetail | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();

    fetchCategoryTree(locale, controller.signal)
      .then((nodes) => {
        setTree(nodes);
        setTreeError(null);
        // Roots open, deeper levels closed: the whole tree expanded is a wall,
        // and everything collapsed hides that there is anything under it.
        setExpanded(new Set(nodes.map((n) => n.code)));
      })
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        setTreeError(e instanceof Error ? e.message : 'The category tree could not be loaded.');
      });

    return () => controller.abort();
  }, [locale]);

  useEffect(() => {
    if (!selected) {
      setDetail(null);
      setDetailError(null);
      return;
    }

    const controller = new AbortController();

    fetchCategoryDetail(selected, locale, controller.signal)
      .then((d) => {
        setDetail(d);
        setDetailError(null);
      })
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        setDetail(null);
        setDetailError(e instanceof Error ? e.message : 'This category could not be loaded.');
      });

    return () => controller.abort();
  }, [selected, locale]);

  const selectNode = useCallback(
    (code: string) => {
      const next = new URLSearchParams(searchParams);
      next.set('node', code);
      // replace: the tree is a browsing surface, and every click leaving a
      // history entry makes Back unusable for getting off the screen.
      setSearchParams(next, { replace: true });
    },
    [searchParams, setSearchParams],
  );

  const toggle = useCallback((code: string) => {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(code)) next.delete(code);
      else next.add(code);
      return next;
    });
  }, []);

  /**
   * Filtering keeps ancestors of a match, not just the matches.
   *
   * A tree filtered to bare matches loses the path that gives each one its
   * meaning — "Standard 90" under Steel Fire Doors is not the same node as
   * "Standard 90" under Glass Partitions, and a flat list of hits cannot say
   * which is which.
   */
  const visibleTree = useMemo(() => {
    if (!tree) return null;
    const needle = filter.trim().toLowerCase();
    if (!needle) return tree;

    const keep = (node: CategoryTreeNode): CategoryTreeNode | null => {
      const children = node.children.map(keep).filter((c): c is CategoryTreeNode => c !== null);
      const hit =
        node.name.toLowerCase().includes(needle) || node.code.toLowerCase().includes(needle);
      return hit || children.length > 0 ? { ...node, children } : null;
    };

    return tree.map(keep).filter((n): n is CategoryTreeNode => n !== null);
  }, [tree, filter]);

  // While filtering, everything that survived is expanded — a match hidden
  // inside a collapsed parent is indistinguishable from no match at all.
  const effectiveExpanded = useMemo(() => {
    if (!filter.trim() || !visibleTree) return expanded;
    const all = new Set<string>();
    walkTree(visibleTree, (n) => all.add(n.code));
    return all;
  }, [filter, visibleTree, expanded]);

  const nodeCount = useMemo(() => {
    if (!tree) return 0;
    let n = 0;
    walkTree(tree, () => (n += 1));
    return n;
  }, [tree]);

  if (treeError) {
    return (
      <div className="pad">
        <div className="phead">
          <div>
            <h1>Categories</h1>
          </div>
        </div>
        <EmptyState title="The category tree could not be loaded" description={treeError} />
      </div>
    );
  }

  return (
    <div className="pad">
      <div className="phead">
        <div>
          <h1>Categories</h1>
          <p>
            The merchandising taxonomy for Plywood &amp; Engineered Panels: Brand → Family → Category → Sub-category. Each node
            carries an approved technical attribute template, and every product hangs from exactly one node.
          </p>
        </div>
      </div>

      <div className="split" style={{ display: 'grid', gridTemplateColumns: '340px 1fr', gap: 14 }}>
        <div className="card">
          <div className="card-h">
            <h3>Hierarchy</h3>
            <span className="sp muted" style={{ fontSize: 11 }}>
              {nodeCount.toLocaleString()} nodes
            </span>
          </div>
          <div className="card-b" style={{ paddingTop: 8 }}>
            <div className="inp" style={{ marginBottom: 8 }}>
              <input
                aria-label="Filter categories"
                placeholder="Filter categories…"
                value={filter}
                onChange={(e) => setFilter(e.target.value)}
              />
            </div>
            {visibleTree === null ? (
              <p className="muted" style={{ fontSize: 12 }}>
                Loading the taxonomy…
              </p>
            ) : visibleTree.length === 0 ? (
              <EmptyState
                title="No category matches that"
                description="Filtering searches the label in the current language and the node code."
              />
            ) : (
              <ul className="tree" style={{ listStyle: 'none', margin: 0, padding: 0 }}>
                {visibleTree.map((node) => (
                  <TreeRow
                    key={node.code}
                    node={node}
                    selected={selected}
                    expanded={effectiveExpanded}
                    requestedLocale={locale}
                    onSelect={selectNode}
                    onToggle={toggle}
                  />
                ))}
              </ul>
            )}
          </div>
        </div>

        <div>
          {detailError ? (
            <EmptyState title="This category could not be loaded" description={detailError} />
          ) : detail ? (
            <DetailPanel detail={detail} requestedLocale={locale} />
          ) : (
            <EmptyState
              title="Select a category"
              description="Pick a node on the left to see its attribute template, its label in each language, and how many products sit beneath it."
            />
          )}
        </div>
      </div>
    </div>
  );
}
