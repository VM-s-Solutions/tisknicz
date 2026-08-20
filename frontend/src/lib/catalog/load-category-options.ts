/**
 * Server-side loader for the product-form category picker (T-0119).
 * Data-driven from the anonymous categories endpoint (admin-created
 * categories appear without a deploy); degrades to the static launch
 * list when the read fails. `value` is the category ID —
 * `Product.CategoryId` references it.
 */
import { getCachedCatalogCategories } from './category-cache';
import { t } from '@/lib/i18n';
import { CATALOG_CATEGORIES } from './categories';

export interface CategorySelectOption {
  value: string;
  label: string;
}

export async function loadProductCategoryOptions(): Promise<readonly CategorySelectOption[]> {
  const items = await getCachedCatalogCategories();
  if (items.length > 0) {
    return items.map((c) => ({ value: c.id, label: c.name }));
  }
  return CATALOG_CATEGORIES.map((c) => ({ value: c.id, label: t(c.labelKey) }));
}
