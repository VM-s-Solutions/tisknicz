/**
 * Shared URL-state parsing for the admin lists (T-0175, audit ADM-L5).
 * Three lists honoured `?pageSize=`, three ignored it, and `page` was
 * unclamped — `?page=9007199254740991` rode straight through to the
 * backend. One parser, applied everywhere.
 */

/** Hard ceiling mirroring the backend's MaxPageSize across admin reads. */
export const ADMIN_MAX_PAGE_SIZE = 100;
/** Nothing deep-links past this; keeps a hand-typed page from hitting the API. */
export const ADMIN_MAX_PAGE = 10_000;

export function readParam(value: string | string[] | undefined): string {
  if (Array.isArray(value)) return value[0] ?? '';
  return value ?? '';
}

/** 1-based page, clamped to [1, ADMIN_MAX_PAGE]; junk falls back to 1. */
export function parsePage(raw: string | string[] | undefined): number {
  const parsed = Number.parseInt(readParam(raw), 10);
  if (!Number.isFinite(parsed) || parsed < 1) return 1;
  return Math.min(parsed, ADMIN_MAX_PAGE);
}

/** Page size clamped to [1, ADMIN_MAX_PAGE_SIZE]; junk falls back to the caller's default. */
export function parsePageSize(
  raw: string | string[] | undefined,
  fallback: number,
  max: number = ADMIN_MAX_PAGE_SIZE,
): number {
  const parsed = Number.parseInt(readParam(raw), 10);
  if (!Number.isFinite(parsed) || parsed < 1) return fallback;
  return Math.min(parsed, max);
}

/**
 * Rebuild the current URL for an error-state retry (audit ADM-M3): the
 * inline "Zkusit znovu" links pointed at the bare route, silently
 * dropping every active filter and the page the admin was on.
 */
export function retryHref(
  routePath: string,
  baseParams: Readonly<Record<string, string>>,
  page: number,
): string {
  const params = new URLSearchParams(baseParams);
  if (page > 1) params.set('page', String(page));
  const query = params.toString();
  return query ? `${routePath}?${query}` : routePath;
}
