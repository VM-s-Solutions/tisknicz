import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * SEO predicate tests (T-0133 / Q-0031 retroactive unblock of T-0131).
 *
 * `canonicalUrl` and `SITE_URL` resolution are pure presentational
 * predicates the SEO ticket (T-0131) left unpinned because the frontend
 * had no test harness. Now that the vitest harness exists they get
 * pinned: the leading-slash join, the no-double-slash guard, the bare
 * origin for `/`, and the env-driven default resolution.
 *
 * `SITE_URL` is resolved once at module-load time from
 * `NEXT_PUBLIC_SITE_URL` / `NODE_ENV`, so the default-resolution cases
 * use `vi.resetModules()` + `vi.stubEnv()` and re-import the module to
 * observe a fresh evaluation. The join cases assert against the
 * module's own resolved `SITE_URL` so they hold regardless of the
 * ambient env the harness runs under.
 */
describe('canonicalUrl', () => {
  it('returns the bare origin (no trailing slash) for "/"', async () => {
    const { canonicalUrl, SITE_URL } = await import('../site-url');
    expect(canonicalUrl('/')).toBe(SITE_URL);
  });

  it('returns the bare origin for the empty string', async () => {
    const { canonicalUrl, SITE_URL } = await import('../site-url');
    expect(canonicalUrl('')).toBe(SITE_URL);
  });

  it('joins a leading-slash path onto the origin', async () => {
    const { canonicalUrl, SITE_URL } = await import('../site-url');
    expect(canonicalUrl('/katalog')).toBe(`${SITE_URL}/katalog`);
  });

  it('normalises a path without a leading slash (no double slash, no missing slash)', async () => {
    const { canonicalUrl, SITE_URL } = await import('../site-url');
    expect(canonicalUrl('produkt/abc')).toBe(`${SITE_URL}/produkt/abc`);
  });

  it('does not double a leading slash already present', async () => {
    const { canonicalUrl, SITE_URL } = await import('../site-url');
    expect(canonicalUrl('/katalog/alfa')).toBe(`${SITE_URL}/katalog/alfa`);
    expect(canonicalUrl('/katalog/alfa')).not.toContain('//katalog');
  });
});

describe('SITE_URL default resolution', () => {
  beforeEach(() => {
    vi.resetModules();
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it('uses NEXT_PUBLIC_SITE_URL when set, stripping any trailing slash', async () => {
    vi.stubEnv('NEXT_PUBLIC_SITE_URL', 'https://staging.makables.cz/');
    const { SITE_URL, canonicalUrl } = await import('../site-url');
    expect(SITE_URL).toBe('https://staging.makables.cz');
    expect(canonicalUrl('/katalog')).toBe('https://staging.makables.cz/katalog');
  });

  it('defaults to the production host when the env var is unset in production', async () => {
    vi.stubEnv('NEXT_PUBLIC_SITE_URL', '');
    vi.stubEnv('NODE_ENV', 'production');
    const { SITE_URL } = await import('../site-url');
    expect(SITE_URL).toBe('https://makables.cz');
  });

  it('falls back to localhost only when unset AND not production', async () => {
    vi.stubEnv('NEXT_PUBLIC_SITE_URL', '');
    vi.stubEnv('NODE_ENV', 'development');
    const { SITE_URL } = await import('../site-url');
    expect(SITE_URL).toBe('http://localhost:3000');
  });
});
