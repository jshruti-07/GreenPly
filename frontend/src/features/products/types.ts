/** Mirrors Pim.Catalog.Application ProductDto (create/lookup responses). */
export interface Product {
  publicId: string;
  code: string;
  name: string | null;
  primaryNodeId: number;
  brandId: number | null;
  baseLocale: string;
  lifecycleState: string;
  workflowState: string;
  gtin: string | null;
  sapMaterialNo: string | null;
  ownerDisplayName: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  /** Send back on update; a mismatch means someone else saved first. */
  rowVersion: number;
}

export interface UpdateProductGeneralRequest {
  name: string;
  ownerDisplayName: string | null;
  gtin: string | null;
  sapMaterialNo: string | null;
  rowVersion: number;
}

export interface AttributeOption {
  code: string;
  label: string;
}

export interface ProductAttribute {
  code: string;
  /** Translated into the product's base locale; the code is not for display. */
  label: string;
  dataType: string;
  isRequired: boolean;
  isLocalizable: boolean;
  /** False for data types this release cannot edit (multilist, html, reference). */
  isEditable: boolean;
  unitSymbol: string | null;
  value: string | null;
  dataState: string | null;
  options: AttributeOption[];
}

export interface ProductAttributeGroup {
  code: string;
  name: string;
  attributes: ProductAttribute[];
}

export interface ProductAttributes {
  productCode: string;
  baseLocale: string;
  completenessPct: number;
  requiredCount: number;
  requiredFilledCount: number;
  rowVersion: number;
  groups: ProductAttributeGroup[];
}

export interface UpdateAttributesRequest {
  values: Array<{ attributeCode: string; value: string | null }>;
  rowVersion: number;
}

export interface Sku {
  code: string;
  gtin: string | null;
  erpMaterialNo: string | null;
  status: string;
  sortOrder: number;
}

export interface ProductVariants {
  productCode: string;
  rowVersion: number;
  skus: Sku[];
}

/** Mirrors the backend SkuStatus enum. */
export const SKU_STATUSES = ['Active', 'Blocked', 'EndOfLife'] as const;

export interface AddSkuRequest {
  code: string;
  gtin: string | null;
  erpMaterialNo: string | null;
  rowVersion: number;
}

export interface UpdateSkuRequest {
  gtin: string | null;
  erpMaterialNo: string | null;
  status: string;
  rowVersion: number;
}

/** Mirrors ProductListItemDto — one row of the grid. */
export interface ProductListItem {
  publicId: string;
  code: string;
  name: string;
  category: string;
  lifecycleState: string;
  workflowState: string;
  completenessPct: number;
  languages: string[];
  variantCount: number;
  owner: string | null;
  updatedAtUtc: string;
  /**
   * Derived placeholder until the channel service exists: 0 none, 1 partial,
   * 2 ready. Optional because an older API build may not send it — the UI
   * degrades to neutral dots rather than crashing.
   */
  channelReadiness?: Array<0 | 1 | 2>;
  /**
   * The locale `name` was actually read from.
   *
   * Equal to the requested display locale normally. Different from it when no
   * translation existed and the row fell back to the product's base locale —
   * the UI marks that, because a PIM hiding a translation gap is a PIM working
   * against its own purpose.
   *
   * Null means neither locale had a translation and `name` is the product code
   * standing in. Optional so an older API build degrades to no marker rather
   * than crashing.
   */
  nameLocale?: string | null;
  /** As `nameLocale`, for `category`. */
  categoryLocale?: string | null;
}

/** One configured display language, from GET /api/v1/locales. */
export interface Locale {
  code: string;
  name: string;
  /** The catalogue's source language — the one translations are made from. */
  isBase: boolean;
}

/** Channel names, index-aligned with channelReadiness (v4.2, Print excluded). */
export const CHANNELS = ['Website', 'Dealer Portal', 'Mobile App', 'API Feed'] as const;

/** Mirrors PagedResult<T>. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export type SortDirection = 'asc' | 'desc';

/** Whitelisted server-side sort keys — must match ListProductsQuery.SortableColumns. */
export type ProductSortKey =
  | 'code'
  | 'name'
  | 'category'
  | 'status'
  | 'quality'
  | 'owner'
  | 'updated';

export interface ExportParams {
  sortBy: ProductSortKey;
  sortDir: SortDirection;
  status?: string;
  categoryId?: number;
  search?: string;
  /** Explicit selection; omitted to export everything matching the filters. */
  codes?: string[];
  /** Display language, so the file matches the grid it was exported from. */
  locale?: string;
}

export interface ProductListParams {
  page: number;
  pageSize: number;
  sortBy: ProductSortKey;
  sortDir: SortDirection;
  status?: string;
  categoryId?: number;
  search?: string;
  /** Display language for the returned text. Omitted means the server default. */
  locale?: string;
}

/** Workflow states, matching the backend WorkflowState enum. */
export const WORKFLOW_STATES = ['Draft', 'Review', 'Approval', 'Published'] as const;

export interface BrandLookup {
  id: number;
  code: string;
  name: string;
}

export interface TaxonomyNodeLookup {
  id: number;
  code: string;
  name: string;
  nodeType: string;
  parentId: number | null;
  attributeSetId: number | null;
}

export interface CreateProductRequest {
  code: string;
  primaryNodeId: number;
  baseLocale: string;
  name: string;
  brandId: number | null;
  attributeSetId: number | null;
  gtin: string | null;
  sapMaterialNo: string | null;
  ownerDisplayName: string | null;
}

// ---------------------------------------------------------------------------
// Classifications (Sprint 8)
// ---------------------------------------------------------------------------

export interface ClassificationClass {
  code: string;
  label: string | null;
}

export interface ClassificationFeature {
  code: string;
  name: string | null;
  dataType: 'numeric' | 'alpha' | 'logical' | 'range';
  unit: string | null;
  isRequired: boolean;
  /** Null means unfilled. Canonical string form; the server owns the formatting. */
  value: string | null;
}

export interface ProductClassificationScheme {
  schemeCode: string;
  schemeName: string;
  schemeVersion: string;
  purpose: string | null;
  isPrimary: boolean;
  mappedStatus: 'mapped' | 'partial' | 'unmapped';
  classCode: string | null;
  classLabel: string | null;
  featureCount: number;
  featureFilled: number;
  availableClasses: ClassificationClass[];
  features: ClassificationFeature[];
}

export interface ProductClassifications {
  productCode: string;
  rowVersion: number;
  schemes: ProductClassificationScheme[];
}

export interface AssignClassRequest {
  classCode: string | null;
  rowVersion: number;
}

export interface ClassificationFeatureInput {
  featureCode: string;
  value: string | null;
}

export interface UpdateFeaturesRequest {
  values: ClassificationFeatureInput[];
  rowVersion: number;
}

// ---------------------------------------------------------------------------
// Compliance (Sprint 8)
// ---------------------------------------------------------------------------

/** Certificate types the backend accepts. Order matches the doc-type select. */
export const COMPLIANCE_DOC_TYPES = ['ISI', 'FSC', 'CARB', 'EPA', 'IGBC', 'TEST', 'DOP', 'CE', 'EPD', 'DPP', 'UKCA'] as const;
export type ComplianceDocType = (typeof COMPLIANCE_DOC_TYPES)[number];

/** Administrative state. Expiry is NOT one of these — the server derives it. */
export const COMPLIANCE_STATUSES = ['valid', 'withdrawn', 'superseded'] as const;
export type ComplianceStatus = (typeof COMPLIANCE_STATUSES)[number];

/** Server-derived. Never recompute this client-side: the window is a business rule. */
export type ExpiryState = 'NoExpiry' | 'Valid' | 'Expiring' | 'Expired';

export interface ComplianceDocument {
  id: number;
  docType: ComplianceDocType;
  reference: string | null;
  standard: string | null;
  authority: string | null;
  validFrom: string | null;
  validTo: string | null;
  status: ComplianceStatus;
  expiryState: ExpiryState;
}

export interface SustainabilityIndicator {
  indicator: string;
  value: number | null;
  unit: string | null;
  method: string | null;
  stage: string | null;
}

export interface MaterialComposition {
  material: string;
  pct: number | null;
  isRecyclable: boolean | null;
  isHazardous: boolean;
}

export interface SubstanceCompliance {
  regime: string;
  result: string;
  threshold: string | null;
  notes: string | null;
}

export interface GreenBuildingContribution {
  scheme: string;
  credit: string;
  note: string | null;
}

export interface ProductCompliance {
  productCode: string;
  rowVersion: number;
  expiringCount: number;
  expiredCount: number;
  documents: ComplianceDocument[];
  indicators: SustainabilityIndicator[];
  materials: MaterialComposition[];
  substances: SubstanceCompliance[];
  greenBuilding: GreenBuildingContribution[];
}

export interface ComplianceDocumentRequest {
  docType: string;
  reference: string | null;
  standard: string | null;
  authority: string | null;
  validFrom: string | null;
  validTo: string | null;
  rowVersion: number;
}

export interface UpdateComplianceRequest extends ComplianceDocumentRequest {
  status: string;
}

// ---------------------------------------------------------------------------
// Packaging, relationships, applications (Sprint 9)
// ---------------------------------------------------------------------------

/** Innermost to outermost. Order is meaningful — a carton contains eaches. */
export const PACKAGING_LEVELS = ['EACH', 'INNER', 'CARTON', 'LAYER', 'PALLET'] as const;
export type PackagingLevel = (typeof PACKAGING_LEVELS)[number];

export interface PackagingUnit {
  id: number;
  level: PackagingLevel;
  parentLevel: PackagingLevel | null;
  containsQty: number;
  gtin: string | null;
  sscc: string | null;
  grossWeight: number | null;
  netWeight: number | null;
  lengthMm: number | null;
  widthMm: number | null;
  heightMm: number | null;
  isBaseUnit: boolean;
  dangerousGoods: boolean;
  unClass: string | null;
}

export interface SkuPackaging {
  skuCode: string;
  units: PackagingUnit[];
}

export interface ProductPackaging {
  productCode: string;
  rowVersion: number;
  skus: SkuPackaging[];
}

export interface AddPackagingRequest {
  skuCode: string;
  level: string;
  parentLevel: string | null;
  containsQty: number;
  gtin: string | null;
  sscc: string | null;
  grossWeight: number | null;
  netWeight: number | null;
  lengthMm: number | null;
  widthMm: number | null;
  heightMm: number | null;
  isBaseUnit: boolean;
  dangerousGoods: boolean;
  unClass: string | null;
  rowVersion: number;
}

export type UpdatePackagingRequest = Omit<AddPackagingRequest, 'skuCode' | 'level' | 'parentLevel'>;

export const RELATION_TYPES = [
  'ACCESSORY',
  'COMPATIBLE',
  'REPLACEMENT',
  'CROSS_SELL',
  'UP_SELL',
] as const;
export type RelationType = (typeof RELATION_TYPES)[number];

export interface ProductRelationship {
  relationType: RelationType;
  targetCode: string;
  targetName: string | null;
  targetWorkflowState: string;
  priority: number;
}

export interface RelationshipGroup {
  relationType: RelationType;
  items: ProductRelationship[];
}

export interface ProductRelationships {
  productCode: string;
  rowVersion: number;
  groups: RelationshipGroup[];
}

export interface AddRelationshipRequest {
  relationType: string;
  targetCode: string;
  priority: number;
  rowVersion: number;
}

export interface ApplicationOption {
  code: string;
  label: string;
  isAssigned: boolean;
}

export interface ApplicationGroup {
  groupCode: string;
  groupLabel: string;
  options: ApplicationOption[];
}

export interface ProductApplications {
  productCode: string;
  rowVersion: number;
  groups: ApplicationGroup[];
}

export interface SetApplicationsRequest {
  applicationCodes: string[];
  rowVersion: number;
}

// ---------------------------------------------------------------------------
// Activity (Sprint 10)
// ---------------------------------------------------------------------------

export interface ActivityEntry {
  id: number;
  occurredAt: string;
  /** Null until authentication ships — rendered as "Unknown", never invented. */
  actor: string | null;
  action: string;
  detail: string | null;
  source: string | null;
}

export interface ProductActivity {
  productCode: string;
  availableActions: string[];
  entries: PagedResult<ActivityEntry>;
}

export interface ActivityParams {
  page: number;
  pageSize: number;
  action?: string;
  search?: string;
}

// ---------------------------------------------------------------------------
// Digital assets and documents (Sprint 12)
// ---------------------------------------------------------------------------

export const ASSET_ROLES = [
  'MAIN',
  'GALLERY',
  'DATASHEET',
  'INSTALL',
  'DRAWING',
  'BIM',
  'VIDEO',
  'SPIN',
] as const;
export type AssetRole = (typeof ASSET_ROLES)[number];

export const ASSET_TYPES = ['IMAGE', 'CAD', 'BIM', 'DOCUMENT', 'VIDEO', 'SPIN_360'] as const;
export type AssetType = (typeof ASSET_TYPES)[number];

export interface ProductAsset {
  linkId: number;
  assetId: number;
  role: AssetRole;
  assetType: AssetType;
  storageProvider: string;
  fileName: string | null;
  mimeType: string | null;
  fileSize: number | null;
  /** Resolved server-side: the web rendition if one exists, else the raw key. */
  url: string;
  title: string | null;
  altText: string | null;
  isPrimary: boolean;
  sortOrder: number;
  /** Hosted on someone else's server, so a failed preview is expected, not a bug. */
  isExternal: boolean;
}

export interface ProductAssets {
  productCode: string;
  rowVersion: number;
  /** Types that can be rendered as a picture. */
  media: ProductAsset[];
  /** Everything else — datasheets, BIM, CAD. */
  documents: ProductAsset[];
}

export interface AddAssetRequest {
  storageKey: string;
  role: string;
  assetType: string | null;
  title: string | null;
  altText: string | null;
  rowVersion: number;
}

// --- Planning & BIM (Sprint 13) ----------------------------------------------

export interface PlanningParameter {
  code: string;
  label: string;
  value: string | null;
  unitSymbol: string | null;
  dataType: string;
}

export interface BimReference {
  schemeCode: string;
  schemeName: string;
  schemeVersion: string;
  classCode: string | null;
  classLabel: string | null;
}

export interface GeometryFile {
  linkId: number;
  role: string;
  assetType: string;
  fileName: string | null;
  fileSize: number | null;
  url: string;
  title: string | null;
  isExternal: boolean;
}

/**
 * Read-only by design: parameters are written on Attributes, references on
 * Classifications, files on Media & Documents. There is no rowVersion write
 * path here, only the version so the header can stay in step.
 */
export interface ProductPlanning {
  productCode: string;
  rowVersion: number;
  bimReady: boolean | null;
  parameterCount: number;
  parameterFilledCount: number;
  parameters: PlanningParameter[];
  bimReferences: BimReference[];
  geometryFiles: GeometryFile[];
}

// --- Localization (Sprint 13) ------------------------------------------------

export const TRANSLATION_STATUSES = ['pending', 'translated', 'approved'] as const;
export type TranslationStatus = (typeof TRANSLATION_STATUSES)[number];

export interface TranslationField {
  field: string;
  label: string;
  /** The master text, shown for reference. Never editable on this tab. */
  baseValue: string | null;
  value: string | null;
  isRequired: boolean;
  isMultiline: boolean;
  isAttribute: boolean;
}

export interface ProductLocale {
  localeCode: string;
  localeName: string;
  isBase: boolean;
  translationStatus: string;
  source: string | null;
  updatedAtUtc: string | null;
  fieldCount: number;
  filledCount: number;
  completeness: number;
  fields: TranslationField[];
}

export interface ProductLocalization {
  productCode: string;
  baseLocale: string;
  rowVersion: number;
  pendingLocaleCount: number;
  locales: ProductLocale[];
}

export interface TranslationValueInput {
  field: string;
  value: string | null;
}

export interface UpdateLocalizationRequest {
  values: TranslationValueInput[];
  translationStatus: string | null;
  rowVersion: number;
}
