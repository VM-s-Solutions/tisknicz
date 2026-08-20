import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PublicCategoryItem } from '@/lib/api-client-helpers/catalog';
import type { ApiError, Result } from '@/lib/runtime/result';

/**
 * Pins the behaviour of the process-local category memo: a hot list is
 * read once per TTL window, concurrent cold renders share ONE backend
 * round trip, and a backend blip degrades to the last good value (or an
 * empty list, which the callers turn into the static fallback) rather
 * than poisoning the cache.
 */

const getCatalogCategories = vi.fn<
  (country?: string) => Promise<Result<{ readonly items: readonly PublicCategoryItem[] }, ApiError>>
>();

vi.mock('@/lib/api-client-helpers/catalog', () => ({
  getCatalogCategories: (country?: string) => getCatalogCategories(country),
}));

function category(id: string): PublicCategoryItem {
  return { id, name: id, slug: id, icon: null, description: null, sortOrder: 0 };
}

function ok(items: readonly PublicCategoryItem[]) {
  return { success: true as const, value: { items } };
}

function transientFailure() {
  return {
    success: false as const,
    error: { code: 'network.timeout', message: 'timeout', type: 'Transient' } as ApiError,
  };
}

async function loadModule() {
  // Fresh module registry per test — the memo is module state by design.
  vi.resetModules();
  return import('../category-cache');
}

describe('getCachedCatalogCategories', () => {
  beforeEach(() => {
    getCatalogCategories.mockReset();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('hits the backend once and serves the memo for the rest of the TTL window', async () => {
    const { getCachedCatalogCategories, CATEGORY_CACHE_TTL_MS } = await loadModule();
    getCatalogCategories.mockResolvedValue(ok([category('cat-3d-tisk')]));

    expect(await getCachedCatalogCategories()).toHaveLength(1);
    vi.advanceTimersByTime(CATEGORY_CACHE_TTL_MS - 1);
    expect(await getCachedCatalogCategories()).toHaveLength(1);

    expect(getCatalogCategories).toHaveBeenCalledTimes(1);
  });

  it('re-reads once the TTL window has elapsed', async () => {
    const { getCachedCatalogCategories, CATEGORY_CACHE_TTL_MS } = await loadModule();
    getCatalogCategories.mockResolvedValue(ok([category('cat-3d-tisk')]));

    await getCachedCatalogCategories();
    vi.advanceTimersByTime(CATEGORY_CACHE_TTL_MS + 1);
    await getCachedCatalogCategories();

    expect(getCatalogCategories).toHaveBeenCalledTimes(2);
  });

  it('collapses concurrent cold reads into one backend round trip', async () => {
    const { getCachedCatalogCategories } = await loadModule();
    let release: (() => void) | undefined;
    getCatalogCategories.mockImplementation(
      () =>
        new Promise((resolve) => {
          release = () => resolve(ok([category('cat-handmade')]));
        }),
    );

    const all = Promise.all([
      getCachedCatalogCategories(),
      getCachedCatalogCategories(),
      getCachedCatalogCategories(),
    ]);
    await vi.waitFor(() => expect(release).toBeDefined());
    release?.();

    expect(await all).toEqual([
      [category('cat-handmade')],
      [category('cat-handmade')],
      [category('cat-handmade')],
    ]);
    expect(getCatalogCategories).toHaveBeenCalledTimes(1);
  });

  it('serves the last good list when a later read fails', async () => {
    const { getCachedCatalogCategories, CATEGORY_CACHE_TTL_MS } = await loadModule();
    getCatalogCategories.mockResolvedValueOnce(ok([category('cat-laser-cnc')]));
    await getCachedCatalogCategories();

    getCatalogCategories.mockResolvedValue(transientFailure());
    vi.advanceTimersByTime(CATEGORY_CACHE_TTL_MS + 1);

    expect(await getCachedCatalogCategories()).toEqual([category('cat-laser-cnc')]);
  });

  it('returns an empty list (caller falls back) when the first read fails, and does not cache it', async () => {
    const { getCachedCatalogCategories } = await loadModule();
    getCatalogCategories.mockResolvedValueOnce(transientFailure());
    expect(await getCachedCatalogCategories()).toEqual([]);

    getCatalogCategories.mockResolvedValueOnce(ok([category('cat-velkoformat')]));
    expect(await getCachedCatalogCategories()).toEqual([category('cat-velkoformat')]);
    expect(getCatalogCategories).toHaveBeenCalledTimes(2);
  });

  it('does not cache an empty success — an unseeded backend must not pin an empty picker', async () => {
    const { getCachedCatalogCategories } = await loadModule();
    getCatalogCategories.mockResolvedValueOnce(ok([]));
    expect(await getCachedCatalogCategories()).toEqual([]);

    getCatalogCategories.mockResolvedValueOnce(ok([category('cat-klasicky-tisk')]));
    expect(await getCachedCatalogCategories()).toEqual([category('cat-klasicky-tisk')]);
  });

  it('caches per country and invalidates the named country only', async () => {
    const { getCachedCatalogCategories, invalidateCatalogCategories } = await loadModule();
    getCatalogCategories.mockImplementation(async (country?: string) => ok([category(`cat-${country}`)]));

    await getCachedCatalogCategories('CZ');
    await getCachedCatalogCategories('SK');
    expect(getCatalogCategories).toHaveBeenCalledTimes(2);

    invalidateCatalogCategories('CZ');
    await getCachedCatalogCategories('CZ');
    await getCachedCatalogCategories('SK');

    expect(getCatalogCategories).toHaveBeenCalledTimes(3);
  });
});
