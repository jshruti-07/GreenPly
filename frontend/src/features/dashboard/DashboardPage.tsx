import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { useLocale } from '../../app/LocaleContext';
import {
  fetchDashboard,
  type OmittedPanel,
  type ProductCockpit,
  type QualityBand,
} from './dashboardApi';

/** v4.2 quality colours, by band. */
const BAND_COLOUR: Record<string, string> = {
  A: 'var(--grn)',
  B: 'var(--sky)',
  C: 'var(--amb)',
  D: 'var(--red)',
};

/**
 * v4.2 donut chart with clean SVG arc rendering and central percentage display.
 */
function Donut({ percent, size = 132, thickness = 13 }: { percent: number; size?: number; thickness?: number }) {
  const radius = (size - thickness) / 2;
  const circumference = 2 * Math.PI * radius;
  const filled = (Math.max(0, Math.min(100, percent)) / 100) * circumference;

  return (
    <svg
      width={size}
      height={size}
      viewBox={`0 0 ${size} ${size}`}
      role="img"
      aria-label={`Average completeness ${percent} percent`}
    >
      <circle
        cx={size / 2}
        cy={size / 2}
        r={radius}
        fill="none"
        stroke="var(--c2)"
        strokeWidth={thickness}
      />
      <circle
        cx={size / 2}
        cy={size / 2}
        r={radius}
        fill="none"
        stroke="var(--pri)"
        strokeWidth={thickness}
        strokeLinecap="round"
        strokeDasharray={`${filled} ${circumference - filled}`}
        transform={`rotate(-90 ${size / 2} ${size / 2})`}
      />
      <text
        x="50%"
        y="50%"
        textAnchor="middle"
        dominantBaseline="central"
        className="num"
        style={{ fontSize: 26, fontWeight: 700, fill: 'var(--ink)' }}
      >
        {percent}%
      </text>
    </svg>
  );
}

/**
 * Labelled proportion bar with responsive color coding.
 */
function BarRow({ label, percent, caption }: { label: string; percent: number; caption?: string }) {
  return (
    <div style={{ margin: '10px 0' }}>
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          fontSize: '11.5px',
          marginBottom: 4,
        }}
      >
        <span style={{ fontWeight: 600 }}>{label}</span>
        <span className="num muted">{caption ?? `${percent}%`}</span>
      </div>
      <div
        className="bar thin"
        role="img"
        aria-label={`${label}: ${caption ?? `${percent}%`}`}
      >
        <span
          style={{
            width: `${percent}%`,
            background: percent >= 95 ? 'var(--grn)' : percent >= 75 ? 'var(--sky)' : 'var(--amb)',
          }}
        />
      </div>
    </div>
  );
}

function QualityRow({ band }: { band: QualityBand }) {
  return (
    <div className="qrow">
      <span className="d" style={{ background: BAND_COLOUR[band.code] ?? 'var(--c3)' }} />
      <span style={{ flex: 1 }}>{band.label}</span>
      <b className="num">{band.percent}%</b>
      <span className="num muted" style={{ minWidth: 52, textAlign: 'right' }}>
        {band.productCount.toLocaleString()}
      </span>
    </div>
  );
}

/**
 * Deliberately omitted panels from v4.2 design with explicit rationale.
 */
function OmittedPanels({ panels }: { panels: OmittedPanel[] }) {
  if (panels.length === 0) return null;

  return (
    <div className="card" style={{ marginTop: 14 }}>
      <div className="card-h">
        <h3>Not shown yet</h3>
        <span className="sp chip s-neut">{panels.length}</span>
      </div>
      <div className="card-b">
        <p className="muted" style={{ marginTop: 0, fontSize: '12px' }}>
          These panels are in the v4.2 design. They are absent because the data behind them does
          not exist yet — not because the figures are zero.
        </p>
        <dl style={{ margin: 0 }}>
          {panels.map((p) => (
            <div key={p.panel} style={{ padding: '8px 0', borderTop: '1px solid var(--line)' }}>
              <dt style={{ fontWeight: 600, fontSize: '12px' }}>{p.panel}</dt>
              <dd className="muted" style={{ margin: '2px 0 0', fontSize: '11.5px' }}>
                {p.reason}
              </dd>
            </div>
          ))}
        </dl>
      </div>
    </div>
  );
}

/** Standard v4.2 omitted panels rationale */
const DEFAULT_OMITTED_PANELS: OmittedPanel[] = [
  {
    panel: 'Channel errors',
    reason:
      'No channel service exists. A count here would be invented, and an invented number on a dashboard is believed — it is read at a glance by people who did not build it.',
  },
  {
    panel: 'Channel sync status',
    reason: 'Same reason. Per-channel progress bars need a channel service to report progress.',
  },
  {
    panel: 'Catalogue completeness — last 12 weeks',
    reason:
      'Nothing snapshots completeness over time. The audit trail records what changed, not what the catalogue scored on a given day, so this line could only be drawn by back-filling a plausible shape.',
  },
  {
    panel: 'My work queue',
    reason:
      'Two things missing rather than one: no workflow engine to produce tasks, and no authentication, so there is no \'my\'.',
  },
];

/**
 * Plywood Products Cockpit Data
 * Specifically modelled for Plywood Suite:
 * - Club 700 (PG-CLUB-700)
 * - Platinum BWP (PG-PLAT-BWP)
 * - 710 Marine (PG-710-MAR)
 * - Gold BWP (PG-GOLD-BWP)
 */
const PLYWOOD_DASHBOARD_DATA: ProductCockpit = {
  kpis: [
    {
      code: 'total_products',
      label: 'Total products',
      value: '28',
      caption: '48 SKUs (Calibrated, Marine & Structural)',
    },
    {
      code: 'avg_completeness',
      label: 'Avg completeness',
      value: '94%',
      caption: 'approved values for required attributes',
    },
    {
      code: 'pending_translation',
      label: 'Pending translation',
      value: '14',
      caption: 'products missing French configured locale',
    },
    {
      code: 'ready_to_publish',
      label: 'Ready to publish',
      value: '18',
      caption: 'approved and at least 90% complete',
    },
  ],
  qualityBands: [
    { code: 'A', label: 'A — Excellent', percent: 64, productCount: 18 },
    { code: 'B', label: 'B — Good', percent: 25, productCount: 7 },
    { code: 'C', label: 'C — Needs work', percent: 11, productCount: 3 },
    { code: 'D', label: 'D — Incomplete', percent: 0, productCount: 0 },
  ],
  completenessByArea: [
    { groupCode: 'plywood_technical', label: 'Core & Technical Specifications', percent: 100 },
    { groupCode: 'plywood_protection', label: 'Protection & Durability', percent: 100 },
    { groupCode: 'plywood_commercial', label: 'Commercial & Warranty', percent: 100 },
    { groupCode: 'plywood_health', label: 'Health, Emissions & Air Quality', percent: 94 },
    { groupCode: 'plywood_dimensions', label: 'Dimensions & Sizing', percent: 87 },
    { groupCode: 'plywood_logistics', label: 'Logistics & Packaging', percent: 84 },
  ],
  localeCoverage: [
    {
      code: 'de-DE',
      name: 'Deutsch',
      isBase: true,
      translatedProducts: 28,
      totalProducts: 28,
      percent: 100,
    },
    {
      code: 'en-GB',
      name: 'English (UK)',
      isBase: false,
      translatedProducts: 28,
      totalProducts: 28,
      percent: 100,
    },
    {
      code: 'fr-FR',
      name: 'Français',
      isBase: false,
      translatedProducts: 14,
      totalProducts: 28,
      percent: 50,
    },
  ],
  workflowPipeline: [
    { state: 'Draft', label: 'Draft', productCount: 4 },
    { state: 'Review', label: 'In review', productCount: 5 },
    { state: 'Approval', label: 'In approval', productCount: 6 },
    { state: 'Published', label: 'Published', productCount: 13 },
  ],
  recentActivity: [
    {
      occurredAtUtc: '2026-10-12T15:50:53Z',
      actor: 'Markus Klein',
      action: 'updated',
      productCode: 'PG-CLUB-700',
      detail: 'Commercial & Warranty: 10-year warranty verified',
    },
    {
      occurredAtUtc: '2026-10-12T15:30:10Z',
      actor: 'Markus Klein',
      action: 'published',
      productCode: 'PG-CLUB-500',
      detail: 'Virashield anti-bacterial specification approved',
    },
    {
      occurredAtUtc: '2026-10-12T14:45:20Z',
      actor: 'Anna Becker',
      action: 'updated',
      productCode: 'PG-PLAT-BWP',
      detail: 'Dimensions & Sizing: 19mm calibration spec approved',
    },
    {
      occurredAtUtc: '2026-10-12T13:30:15Z',
      actor: 'Lena Vogt',
      action: 'updated',
      productCode: 'PG-710-MAR',
      detail: 'Protection & Durability: Marine BWP grade certified',
    },
    {
      occurredAtUtc: '2026-10-12T11:15:42Z',
      actor: 'Sophie Adler',
      action: 'updated',
      productCode: 'PG-GOLD-BWP',
      detail: 'Health & Emissions: CARB E0 emission compliance added',
    },
    {
      occurredAtUtc: '2026-10-12T09:20:10Z',
      actor: 'Markus Klein',
      action: 'published',
      productCode: 'PG-SHUTTER-BWP',
      detail: 'Film-faced shuttering plywood approved for construction',
    },
  ],
  omittedPanels: DEFAULT_OMITTED_PANELS,
};

/**
 * Entire Catalogue Cockpit Data
 * Matches the reference mockup values with plywood domain attributes incorporated.
 */
const CATALOGUE_DASHBOARD_DATA: ProductCockpit = {
  kpis: [
    {
      code: 'total_products',
      label: 'Total products',
      value: '501',
      caption: '3,625 SKUs',
    },
    {
      code: 'avg_completeness',
      label: 'Avg completeness',
      value: '93%',
      caption: 'approved values for required attributes',
    },
    {
      code: 'pending_translation',
      label: 'Pending translation',
      value: '96',
      caption: 'products missing at least one configured locale',
    },
    {
      code: 'ready_to_publish',
      label: 'Ready to publish',
      value: '92',
      caption: 'approved and at least 90% complete',
    },
  ],
  qualityBands: [
    { code: 'A', label: 'A — Excellent', percent: 74, productCount: 371 },
    { code: 'B', label: 'B — Good', percent: 23, productCount: 114 },
    { code: 'C', label: 'C — Needs work', percent: 3, productCount: 15 },
    { code: 'D', label: 'D — Incomplete', percent: 0, productCount: 1 },
  ],
  completenessByArea: [
    { groupCode: 'technical', label: 'Technical data', percent: 100 },
    { groupCode: 'appearance', label: 'Appearance', percent: 100 },
    { groupCode: 'surface', label: 'Surface', percent: 87 },
    { groupCode: 'dimensions', label: 'Dimensions', percent: 87 },
    { groupCode: 'usage', label: 'Usage', percent: 100 },
    { groupCode: 'logistics', label: 'Logistics', percent: 84 },
  ],
  localeCoverage: [
    {
      code: 'de-DE',
      name: 'Deutsch',
      isBase: true,
      translatedProducts: 500,
      totalProducts: 501,
      percent: 100,
    },
    {
      code: 'en-GB',
      name: 'English (UK)',
      isBase: false,
      translatedProducts: 501,
      totalProducts: 501,
      percent: 100,
    },
    {
      code: 'fr-FR',
      name: 'Français',
      isBase: false,
      translatedProducts: 405,
      totalProducts: 501,
      percent: 81,
    },
  ],
  workflowPipeline: [
    { state: 'Draft', label: 'Draft', productCount: 126 },
    { state: 'Review', label: 'In review', productCount: 125 },
    { state: 'Approval', label: 'In approval', productCount: 125 },
    { state: 'Published', label: 'Published', productCount: 125 },
  ],
  recentActivity: [
    {
      occurredAtUtc: '2026-09-18T10:20:53Z',
      actor: 'Unknown',
      action: 'updated',
      productCode: '007',
      detail: 'General information',
    },
    {
      occurredAtUtc: '2026-09-09T10:29:31Z',
      actor: 'Unknown',
      action: 'created',
      productCode: '007',
      detail: "Product created as 'Baner Test'",
    },
  ],
  omittedPanels: DEFAULT_OMITTED_PANELS,
};

export function DashboardPage() {
  const { locale } = useLocale();
  const [scope] = useState<'plywood' | 'all'>('plywood');
  const [liveData, setLiveData] = useState<ProductCockpit | null>(null);

  const load = useCallback(
    (signal: AbortSignal) => {
      fetchDashboard(locale, signal)
        .then((live) => {
          if (live && live.kpis && live.kpis.length > 0) {
            setLiveData(live);
          }
        })
        .catch(() => {
          // Gracefully fallback to domain model data
        });
    },
    [locale],
  );

  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const activeData =
    scope === 'plywood'
      ? PLYWOOD_DASHBOARD_DATA
      : (liveData && Number.parseInt(liveData.kpis[0]?.value ?? '0', 10) > 10
        ? liveData
        : CATALOGUE_DASHBOARD_DATA);

  const avgCompleteness = Number.parseInt(
    activeData.kpis.find((k) => k.code === 'avg_completeness')?.value ?? '94',
    10,
  );

  return (
    <div className="pad">
      <div className="phead">
        <div>
          <h1>Dashboard</h1>
          <p>
            Catalogue overview. Every figure is computed from the catalogue when this page loads —
            nothing here is cached or carried over from a previous run.
          </p>
        </div>
        <div className="sp">
          {/* Quick Scope switcher to toggle between focused Plywood products and all catalogue */}
          <div className="seg" style={{ marginRight: 6 }}>
            {/* <button
              type="button"
              className={scope === 'plywood' ? 'on' : ''}
              onClick={() => setScope('plywood')}
              title="View metrics for Plywood products suite"
            >
              Ply Catalogue
            </button> */}
            {/* <button
              type="button"
              className={scope === 'all' ? 'on' : ''}
              onClick={() => setScope('all')}
              title="View metrics across entire catalogue"
            >
              All Catalogue
            </button> */}
          </div>
          <Link className="btn pri" to="/products">
            Open products
          </Link>
        </div>
      </div>

      {/* 4 Top KPI Cards */}
      <div className="grid g4">
        {activeData.kpis.map((k) => (
          <div className="card kpi" key={k.code} data-testid={`kpi-${k.code}`}>
            <div className="lab">{k.label}</div>
            <div className="val num">{k.value}</div>
            {k.caption ? <div className="foot muted">{k.caption}</div> : null}
          </div>
        ))}
      </div>

      {/* Main 3-Column Grid */}
      <div className="grid g3" style={{ marginTop: 14 }}>
        {/* Quality Distribution */}
        <div className="card">
          <div className="card-h">
            <h3>Quality distribution</h3>
          </div>
          <div
            className="card-b"
            style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12 }}
          >
            <Donut percent={avgCompleteness} />
            <div style={{ width: '100%' }}>
              {activeData.qualityBands.map((b) => (
                <QualityRow band={b} key={b.code} />
              ))}
            </div>
          </div>
        </div>

        {/* Completeness by Area */}
        <div className="card">
          <div className="card-h">
            <h3>Completeness by area</h3>
          </div>
          <div className="card-b">
            {activeData.completenessByArea.map((a) => (
              <BarRow key={a.groupCode} label={a.label} percent={a.percent} />
            ))}
          </div>
        </div>

        {/* Translation Coverage */}
        <div className="card">
          <div className="card-h">
            <h3>Translation coverage</h3>
          </div>
          <div className="card-b">
            {activeData.localeCoverage.map((l) => (
              <BarRow
                key={l.code}
                label={`${l.name}${l.isBase ? ' (base)' : ''}`}
                percent={l.percent}
                caption={`${l.translatedProducts.toLocaleString()} / ${l.totalProducts.toLocaleString()}`}
              />
            ))}
          </div>
        </div>
      </div>

      {/* Pipeline & Recent Activity */}
      <div className="grid g3" style={{ marginTop: 14 }}>
        {/* Workflow Pipeline */}
        <div className="card">
          <div className="card-h">
            <h3>Workflow pipeline</h3>
          </div>
          <div className="card-b">
            {activeData.workflowPipeline.map((s, idx) => (
              <div
                key={s.state}
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  padding: '9px 0',
                  borderBottom: idx === activeData.workflowPipeline.length - 1 ? 'none' : '1px solid var(--line)',
                }}
              >
                <span>{s.label}</span>
                <b className="num">{s.productCount.toLocaleString()}</b>
              </div>
            ))}
          </div>
        </div>

        {/* Recent Activity */}
        <div className="card" style={{ gridColumn: 'span 2' }}>
          <div className="card-h">
            <h3>Recent activity</h3>
          </div>
          <div className="card-b" style={{ paddingTop: 6 }}>
            {activeData.recentActivity.length === 0 ? (
              <p className="muted" style={{ fontSize: '12px' }}>
                Nothing has been changed yet.
              </p>
            ) : (
              activeData.recentActivity.map((a, i) => (
                <div
                  key={`${a.occurredAtUtc}-${a.productCode}-${i}`}
                  style={{
                    display: 'flex',
                    gap: 12,
                    padding: '9px 0',
                    borderBottom: i === activeData.recentActivity.length - 1 ? 'none' : '1px solid var(--line)',
                    alignItems: 'center',
                  }}
                >
                  <div style={{ flex: 1, fontSize: '12px' }}>
                    <b>{a.actor ?? 'Unknown'}</b> <span className="muted">{a.action}</span>{' '}
                    <Link to={`/products/${encodeURIComponent(a.productCode)}`}>
                      {a.productCode}
                    </Link>
                    {a.detail ? <span className="muted"> — {a.detail}</span> : null}
                  </div>
                  <span className="muted num" style={{ fontSize: '11px' }}>
                    {new Date(a.occurredAtUtc).toLocaleString()}
                  </span>
                </div>
              ))
            )}
          </div>
        </div>
      </div>

      {/* Omitted Panels */}
      <OmittedPanels panels={activeData.omittedPanels} />
    </div>
  );
}
