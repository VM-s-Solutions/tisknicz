/**
 * Process-local TTL cache for the public category list (CLAUDE.md §5 —
 * "cache what is stable and hot … behind an interface, with explicit
 * invalidation").
 *
 * The list is anonymous admin-managed reference data that changes a few
 * times a year, yet it was re-fetched on every render of `/katalog`,
 * `/dashboard/maker/produkty/novy` and the product edit page. Next's own
 * Data Cache cannot help here: all three routes are `force-dynamic`,
 * which Next documents as equivalent to `fetchCache = 'force-no-store'`,
 * and `apiFetch` passes an `AbortSignal.timeout` on every call, which is
 * Next's documented opt-out from per-request fetch memoization
 * (`next/dist/server/lib/dedupe-fetch.js`). A plain module-level memo
 * sidesteps both and costs one map lookup.
 *
 * Scope is ONE Node process (one App Service instance), so the bound on
 * staleness after an admin edit is {@link CATEGORY_CACHE_TTL_MS} per
 * instance, not a global invalidation. {@link invalidateCatalogCategories}
 * exists for a same-process writer that wants the list refreshed sooner.
 *
 * Failure posture matches the callers': a failed or empty read is NOT
 * cached, and a failure falls back to the last good value when one is
 * still in memory (a backend blip must not blank the category filter).
 * When there is nothing to fall back to the result is an empty list and
 * the caller degrades to `CATALOG_CATEGORIES`.
 */
import { getCatalogCategories, type PublicCategoryItem } from '@/lib/api-client-helpers/catalog';

/** Staleness bound after an admin category edit, per Node process. */
export const CATEGORY_CACHE_TTL_MS = 5 * 60 * 1000;

interface CacheEntry {
  readonly items: readonly PublicCategoryItem[];
  readonly expiresAt: number;
}

const entries = new Map<string, CacheEntry>();
/** Stampede guard: concurrent renders on a cold cache share one round trip. */
const inflight = new Map<string, Promise<readonly PublicCategoryItem[]>>();

/**
 * Active categories for the country, served from the process memo when
 * fresh. Returns an empty list when the backend read fails and no
 * previous value is held — callers treat that as "use the static
 * fallback", exactly as they treat a failed `getCatalogCategories`.
 */
export async function getCachedCatalogCategories(
  country = 'CZ',
): Promise<readonly PublicCategoryItem[]> {
  const cached = entries.get(country);
  if (cached && cached.expiresAt > Date.now()) {
    return cached.items;
  }

  const existing = inflight.get(country);
  if (existing) return existing;

  const attempt = (async (): Promise<readonly PublicCategoryItem[]> => {
    const result = await getCatalogCategories(country);
    if (result.success && result.value.items.length > 0) {
      entries.set(country, {
        items: result.value.items,
        expiresAt: Date.now() + CATEGORY_CACHE_TTL_MS,
      });
      return result.value.items;
    }
    // Serve the expired-but-known list rather than blanking the filter
    // on a transient backend failure.
    return cached?.items ?? [];
  })();

  inflight.set(country, attempt);
  try {
    return await attempt;
  } finally {
    inflight.delete(country);
  }
}

/** Drop the memo so the next read goes to the backend. */
export function invalidateCatalogCategories(country?: string): void {
  if (country === undefined) {
    entries.clear();
    return;
  }
  entries.delete(country);
}
