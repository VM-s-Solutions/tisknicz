/**
 * Canonical site-origin helper for the SEO surface (T-0131).
 *
 * Every absolute URL the frontend emits for search engines / social
 * crawlers — sitemap entries, `openGraph.url`, `<link rel="canonical">`,
 * the `robots.txt` sitemap reference, and the root-layout
 * `metadataBase` — must point at the **user-facing site origin**
 * (`makables.cz`), NOT the API host (`NEXT_PUBLIC_API_PUBLIC_BASE_URL`,
 * which is the Public API). Conflating the two would emit a canonical
 * that points at the backend. This module is the single source of truth
 * for that origin — mirrors the centralisation of
 * `buildProductImageUrl` in `lib/api-client-helpers/catalog.ts`.
 *
 * `NEXT_PUBLIC_SITE_URL` overrides the origin (set it in prod/staging).
 * When unset it defaults to the production host; only when unset AND
 * running outside production does it fall back to localhost so the dev
 * build doesn't bake a production origin into local OG/canonical tags.
 */

const PRODUCTION_SITE_URL = 'https://makables.cz';
const DEV_SITE_URL = 'http://localhost:3000';

function resolveSiteUrl(): string {
  const fromEnv = process.env.NEXT_PUBLIC_SITE_URL?.trim();
  if (fromEnv) {
    return fromEnv.replace(/\/+$/, '');
  }
  if (process.env.NODE_ENV !== 'production') {
    return DEV_SITE_URL;
  }
  return PRODUCTION_SITE_URL;
}

/**
 * The absolute site origin, without a trailing slash
 * (e.g. `https://makables.cz`).
 */
export const SITE_URL = resolveSiteUrl();

/**
 * Join {@link SITE_URL} with a leading-slash path into an absolute URL.
 * `'/'` returns the bare origin (no trailing slash); any other path is
 * appended verbatim after normalising a single leading slash so callers
 * can't accidentally double it.
 *
 * @example
 *   canonicalUrl('/')         // 'https://makables.cz'
 *   canonicalUrl('/katalog')  // 'https://makables.cz/katalog'
 */
export function canonicalUrl(path: string): string {
  if (path === '/' || path === '') return SITE_URL;
  const normalised = path.startsWith('/') ? path : `/${path}`;
  return `${SITE_URL}${normalised}`;
}
