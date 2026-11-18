import { apiFetch } from '../../api/client';

export interface AttributeGroupSummary {
  code: string;
  label: string;
  labelLocale: string | null;
  attributeCount: number;
}

export interface AttributeSummary {
  code: string;
  label: string;
  labelLocale: string | null;
  groupCode: string;
  groupLabel: string;
  dataType: string;
  unit: string | null;
  isLocalizable: boolean;
  isVariantAxis: boolean;
  scope: string;
  sourceAuthority: string;
  optionCount: number;
  setCount: number;
  requiredInAnySet: boolean;
  /** Products whose attribute set includes this attribute — coverage's denominator. */
  appliesToProducts: number;
  productsWithValue: number;
  coveragePct: number;
}

export interface AttributeLibrary {
  groups: AttributeGroupSummary[];
  attributes: AttributeSummary[];
  totalAttributes: number;
}

export interface AttributeLabel {
  localeCode: string;
  localeName: string;
  isBase: boolean;
  label: string | null;
  helpText: string | null;
  placeholder: string | null;
}

export interface AttributeOptionDetail {
  code: string;
  label: string;
  labelLocale: string | null;
  sortOrder: number;
  usedByProducts: number;
}

export interface AttributeSetMembership {
  setCode: string;
  setName: string;
  isRequired: boolean;
  sortOrder: number;
  productCount: number;
}

export interface AttributeDetail {
  code: string;
  label: string;
  labelLocale: string | null;
  groupCode: string;
  groupLabel: string;
  dataType: string;
  unit: string | null;
  unitCode: string | null;
  isLocalizable: boolean;
  isVariantAxis: boolean;
  scope: string;
  sourceAuthority: string;
  validationJson: string | null;
  appliesToProducts: number;
  productsWithValue: number;
  coveragePct: number;
  labels: AttributeLabel[];
  options: AttributeOptionDetail[];
  sets: AttributeSetMembership[];
}

function localeQuery(locale: string | null): string {
  return locale ? `?locale=${encodeURIComponent(locale)}` : '';
}

export function fetchAttributeLibrary(
  locale: string | null,
  signal?: AbortSignal,
): Promise<AttributeLibrary> {
  return apiFetch<AttributeLibrary>(`/api/v1/attributes${localeQuery(locale)}`, { signal });
}

export function fetchAttributeDetail(
  code: string,
  locale: string | null,
  signal?: AbortSignal,
): Promise<AttributeDetail> {
  return apiFetch<AttributeDetail>(
    `/api/v1/attributes/${encodeURIComponent(code)}${localeQuery(locale)}`,
    { signal },
  );
}
