import type { MetadataRoute } from 'next';
import { getPagedMakers, CATALOG_MAX_PAGE_SIZE } from '@/lib/api-client-helpers/catalog';
import { canonicalUrl } from '@/lib/seo/site-url';

/**
 * App-Router `sitemap.ts` (T-0131). Next serializes the returned array
 * to `/sitemap.xml`. This is a Next.js `MetadataRoute` convention — NOT
 * hand-rolled XML.
 *
 * Enumeration decision (A.4):
 *   - Static public routes are ALWAYS listed.
 *   - Maker profiles (`/katalog/{slug}`) are enumerated dynamically via
 *     a capped paged walk of the existing anonymous `getPagedMakers`
 *     read. A transient catalog failure falls back to static-only and
 *     never throws (a backend blip must not 500 the sitemap).
 *   - Product detail (`/produkt/{productId}`) enumeration is DEFERRED:
 *     there is no bulk product-id read at MVP (the only product read is
 *     `getProductById(id)`, which needs an id you don't have yet;
 *     products are reachable only through a maker profile, so listing
 *     them would mean an N+1 walk of every maker's products at
 *     generation time — disproportionate for a frontend polish ticket).
 *     The clean fix is a backend bulk-id feed (deferred, out of scope).
 *
 * `revalidate = 3600` regenerates hourly so a freshly-listed maker
 * appears within an hour without a redeploy.
 */

export const revalidate = 3600;

// Safety cap on the maker walk so a large catalog can't make sitemap
// generation unbounded. 20 pages × 48 = up to 960 maker URLs at MVP,
// comfortably under the 50k single-sitemap limit.
const MAX_MAKER_PAGES = 20;

const STATIC_ROUTES: ReadonlyArray<{
  readonly path: string;
  readonly changeFrequency: MetadataRoute.Sitemap[number]['changeFrequency'];
  readonly priority: number;
}> = [
  { path: '/', changeFrequency: 'weekly', priority: 1.0 },
  { path: '/katalog', changeFrequency: 'daily', priority: 0.8 },
  { path: '/jak-to-funguje', changeFrequency: 'monthly', priority: 0.8 },
  { path: '/pro-makery', changeFrequency: 'monthly', priority: 0.8 },
  { path: '/vop', changeFrequency: 'monthly', priority: 0.3 },
  { path: '/gdpr', changeFrequency: 'monthly', priority: 0.3 },
];

/**
 * Walk the public catalog page-by-page collecting maker slugs. Stops at
 * the last page, at the {@link MAX_MAKER_PAGES} cap, or on the first
 * failed read (returning whatever was collected so far — the caller
 * still emits the static routes). Never throws.
 */
async function collectMakerSlugs(): Promise<string[]> {
  const slugs: string[] = [];
  for (let page = 1; page <= MAX_MAKER_PAGES; page++) {
    const result = await getPagedMakers({
      country: 'CZ',
      page,
      pageSize: CATALOG_MAX_PAGE_SIZE,
    });
    if (!result.success) {
      // Transient backend error — fall back to whatever we have. The
      // sitemap must not 500 on a catalog blip (AC-3).
      break;
    }
    for (const maker of result.value.items) {
      slugs.push(maker.slug);
    }
    if (!result.value.hasNext) break;
  }
  return slugs;
}

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const lastModified = new Date();

  const staticEntries: MetadataRoute.Sitemap = STATIC_ROUTES.map((route) => ({
    url: canonicalUrl(route.path),
    lastModified,
    changeFrequency: route.changeFrequency,
    priority: route.priority,
  }));

  const slugs = await collectMakerSlugs();
  const makerEntries: MetadataRoute.Sitemap = slugs.map((slug) => ({
    url: canonicalUrl(`/katalog/${slug}`),
    lastModified,
    changeFrequency: 'weekly',
    priority: 0.6,
  }));

  return [...staticEntries, ...makerEntries];
}
