/**
 * Public catalog category list.
 *
 * Hard-coded against the 6 launch slugs seeded in T-0040 (see backend
 * `CategoriesSeed`). The catalog page (T-0046) renders these in the
 * filter dropdown; the maker dashboard category picker (T-0040) uses
 * its own data source.
 *
 * TODO(T-0119): replace with /catalog/categories when admin CRUD ships
 *               so the list is data-driven and supports add/remove.
 */
import type { MessageKey } from '@/lib/i18n/cs-CZ';

export interface CatalogCategoryOption {
  /** Stable slug used in URL query (`?category=...`) and the backend filter. */
  readonly slug: string;
  /** Czech display label (i18n key `catalog.category.<slug>`). */
  readonly labelKey: MessageKey;
}

export const CATALOG_CATEGORIES: readonly CatalogCategoryOption[] = [
  { slug: 'cat-3d-tisk', labelKey: 'catalog.category.cat-3d-tisk' },
  { slug: 'cat-klasicky-tisk', labelKey: 'catalog.category.cat-klasicky-tisk' },
  { slug: 'cat-potisk-textilu', labelKey: 'catalog.category.cat-potisk-textilu' },
  { slug: 'cat-laser-cnc', labelKey: 'catalog.category.cat-laser-cnc' },
  { slug: 'cat-velkoformat', labelKey: 'catalog.category.cat-velkoformat' },
  { slug: 'cat-handmade', labelKey: 'catalog.category.cat-handmade' },
] as const;
