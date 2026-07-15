/**
 * Static fallback for the public catalog category list.
 *
 * Since T-0119 the authoritative list is data-driven — the anonymous
 * `GET /api/v1/catalog/categories` endpoint (see
 * `getCatalogCategories` in `lib/api-client-helpers/catalog.ts`)
 * returns the active rows the admin manages. This module keeps the six
 * launch categories seeded in T-0040 as the degrade-gracefully fallback
 * when that read fails (same resilience posture as the sitemap's
 * static-only fallback in T-0131) and as the display-label lookup for
 * surfaces that only have a `categoryId`.
 *
 * NOTE `id` vs `slug`: the seeded row ids are `cat-3d-tisk`-style; the
 * seeded URL slugs are `3d-tisk`-style. The catalog filter query param
 * carries the SLUG; the product form posts the ID. (The pre-T-0119
 * version of this file conflated the two, which silently broke the
 * category filter — the backend matched on slug and never found
 * `cat-*`.)
 */
import type { MessageKey } from '@/lib/i18n/cs-CZ';

export interface CatalogCategoryOption {
  /** Primary key (`categories.id`) — what `Product.CategoryId` references. */
  readonly id: string;
  /** URL slug used in the catalog query (`?category=...`) and the backend filter. */
  readonly slug: string;
  /** Czech display label (i18n key `catalog.category.<id>`). */
  readonly labelKey: MessageKey;
}

export const CATALOG_CATEGORIES: readonly CatalogCategoryOption[] = [
  { id: 'cat-3d-tisk', slug: '3d-tisk', labelKey: 'catalog.category.cat-3d-tisk' },
  { id: 'cat-klasicky-tisk', slug: 'klasicky-tisk', labelKey: 'catalog.category.cat-klasicky-tisk' },
  { id: 'cat-potisk-textilu', slug: 'potisk-textilu', labelKey: 'catalog.category.cat-potisk-textilu' },
  { id: 'cat-laser-cnc', slug: 'laser-cnc', labelKey: 'catalog.category.cat-laser-cnc' },
  { id: 'cat-velkoformat', slug: 'velkoformat', labelKey: 'catalog.category.cat-velkoformat' },
  { id: 'cat-handmade', slug: 'handmade', labelKey: 'catalog.category.cat-handmade' },
] as const;
