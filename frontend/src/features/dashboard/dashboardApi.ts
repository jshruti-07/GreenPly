import { apiFetch } from '../../api/client';

export interface DashboardKpi {
  code: string;
  label: string;
  value: string;
  caption: string | null;
}

export interface QualityBand {
  code: string;
  label: string;
  productCount: number;
  percent: number;
}

export interface CompletenessByArea {
  groupCode: string;
  label: string;
  percent: number;
}

export interface WorkflowStage {
  state: string;
  label: string;
  productCount: number;
}

export interface LocaleCoverage {
  code: string;
  name: string;
  isBase: boolean;
  translatedProducts: number;
  totalProducts: number;
  percent: number;
}

export interface DashboardActivity {
  occurredAtUtc: string;
  actor: string | null;
  action: string;
  productCode: string;
  detail: string | null;
}

/**
 * A v4.2 panel this build does not render, and why.
 *
 * Carried in the payload rather than hard-coded here so the gap belongs to the
 * contract: when the channel service lands, the panel appears because the
 * reason disappears, and nobody has to remember to delete a note from a
 * component.
 */
export interface OmittedPanel {
  panel: string;
  reason: string;
}

export interface ProductCockpit {
  kpis: DashboardKpi[];
  qualityBands: QualityBand[];
  completenessByArea: CompletenessByArea[];
  workflowPipeline: WorkflowStage[];
  localeCoverage: LocaleCoverage[];
  recentActivity: DashboardActivity[];
  omittedPanels: OmittedPanel[];
}

export function fetchDashboard(locale: string | null, signal?: AbortSignal): Promise<ProductCockpit> {
  const query = locale ? `?locale=${encodeURIComponent(locale)}` : '';
  return apiFetch<ProductCockpit>(`/api/v1/dashboard${query}`, { signal });
}
