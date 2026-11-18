import { FormEvent, useEffect, useState } from 'react';
import { ApiError } from '../../api/client';
import { Button } from '../../components/Button';
import { Drawer } from '../../components/Drawer';
import { SelectField, TextField } from '../../components/Field';
import { createProduct, listBrands, listTaxonomyNodes } from './productsApi';
import type { BrandLookup, Product, TaxonomyNodeLookup } from './types';

export interface CreateProductDrawerProps {
  open: boolean;
  onClose: () => void;
  onCreated: (product: Product) => void;
}

/**
 * v4.2 `openProductWizard`: Brand, Product Family, Category, SAP Number,
 * Product Name (base-locale master), Owner, Base Language. Creates a Draft;
 * every other field is enriched later on the detail screen.
 *
 * Owner is a display-name select until an identity service exists
 * (decision 2026-08-14) — the names match the prototype's stewards.
 */
const OWNERS = ['Anna Becker', 'Markus Klein', 'Lena Vogt', 'Sophie Adler'];

const BASE_LOCALES = [
  { value: 'de-DE', label: 'German (DE)' },
  { value: 'en-GB', label: 'English (UK)' },
];

export function CreateProductDrawer({ open, onClose, onCreated }: CreateProductDrawerProps) {
  const [brands, setBrands] = useState<BrandLookup[]>([]);
  const [families, setFamilies] = useState<TaxonomyNodeLookup[]>([]);
  const [categories, setCategories] = useState<TaxonomyNodeLookup[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Lookups load when the drawer first opens, not on every mount of the grid.
  useEffect(() => {
    if (!open) {
      return;
    }

    let active = true;
    setError(null);

    Promise.all([listBrands(), listTaxonomyNodes('FAMILY'), listTaxonomyNodes('CATEGORY')])
      .then(([brandList, familyList, categoryList]) => {
        if (!active) {
          return;
        }
        setBrands(brandList);
        setFamilies(familyList);
        setCategories(categoryList);
      })
      .catch(() => {
        if (active) {
          setError('Could not load reference data. Is the catalog API running?');
        }
      });

    return () => {
      active = false;
    };
  }, [open]);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    setSubmitting(true);
    setError(null);

    try {
      const categoryValue = String(form.get('categoryId') ?? '');
      const category = categories.find((c) => String(c.id) === categoryValue);

      const product = await createProduct({
        code: String(form.get('code') ?? '').trim(),
        primaryNodeId: Number(categoryValue),
        baseLocale: String(form.get('baseLocale') ?? ''),
        name: String(form.get('name') ?? '').trim(),
        brandId: form.get('brandId') ? Number(form.get('brandId')) : null,
        // The category node carries the attribute set that drives completeness.
        attributeSetId: category?.attributeSetId ?? null,
        gtin: null,
        sapMaterialNo: String(form.get('sapMaterialNo') ?? '').trim() || null,
        ownerDisplayName: String(form.get('owner') ?? '') || null,
      });

      onCreated(product);
    } catch (caught: unknown) {
      setError(
        caught instanceof ApiError
          ? caught.message
          : 'Could not create the product. Please try again.',
      );
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Drawer
      open={open}
      title="New Product"
      onClose={onClose}
      footer={
        <>
          <Button onClick={onClose} disabled={submitting}>
            Cancel
          </Button>
          <Button
            variant="primary"
            type="submit"
            form="create-product-form"
            disabled={submitting}
          >
            {submitting ? 'Creating…' : 'Create product'}
          </Button>
        </>
      }
    >
      <form id="create-product-form" onSubmit={onSubmit}>
        <div className="fgrid">
          <SelectField
            label="Brand"
            name="brandId"
            options={[
              { value: '', label: '— Select —' },
              ...brands.map((b) => ({ value: String(b.id), label: b.name })),
            ]}
          />

          <SelectField
            label="Product Family"
            name="familyId"
            options={[
              { value: '', label: '— Select —' },
              ...families.map((f) => ({ value: String(f.id), label: f.name })),
            ]}
          />

          <SelectField
            label="Category"
            name="categoryId"
            required
            hint="Determines the attribute set, and therefore completeness."
            options={[
              { value: '', label: '— Select —' },
              ...categories.map((c) => ({ value: String(c.id), label: c.name })),
            ]}
          />

          <TextField label="Product code" name="code" required placeholder="FD-90-GS-1000" />

          <TextField label="SAP Number" name="sapMaterialNo" placeholder="10000012345" />

          <SelectField
            label="Owner"
            name="owner"
            options={[
              { value: '', label: '— Unassigned —' },
              ...OWNERS.map((owner) => ({ value: owner, label: owner })),
            ]}
          />

          <SelectField label="Base Language" name="baseLocale" required options={BASE_LOCALES} />

          <div className="full">
            <TextField
              label="Product Name (master language)"
              name="name"
              required
              placeholder="Fire Door 90 Min"
            />
          </div>
        </div>

        {error && (
          <div className="alert err" role="alert">
            {error}
          </div>
        )}
      </form>
    </Drawer>
  );
}
