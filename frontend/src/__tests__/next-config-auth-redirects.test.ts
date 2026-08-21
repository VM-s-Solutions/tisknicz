import { existsSync } from 'node:fs';
import { join } from 'node:path';
import nextConfig from '../../next.config';

/**
 * T-0166 (audit AUTH-H1): transactional emails historically carried
 * "/auth/*" links while the real `(auth)` pages live at /verify, /magic
 * and /reset (the route group adds no URL segment). The config must keep
 * permanent redirects so every already-delivered email link still lands
 * on the live route — including the /auth/confirm → /verify leaf rename.
 */
describe('next.config legacy auth redirects', () => {
  it('maps every legacy /auth/* email path to its live route, permanently', async () => {
    const redirects = await nextConfig.redirects?.();

    expect(redirects).toBeDefined();
    const bySource = new Map(redirects!.map((r) => [r.source, r]));

    for (const [source, destination] of [
      ['/auth/confirm', '/verify'],
      ['/auth/verify', '/verify'],
      ['/auth/magic', '/magic'],
      ['/auth/reset', '/reset'],
    ] as const) {
      const rule = bySource.get(source);
      // Object shape keeps the failing `source` visible in the assertion diff.
      expect({ source, rule }).toMatchObject({ source, rule: { destination, permanent: true } });
    }
  });

  // Review gate for T-0166: pinned path strings alone would stay green if
  // someone renamed an (auth) page folder — the exact refactor that would
  // recreate AUTH-H1. Assert the redirect targets (which are also the
  // PublicAppUrlsOptions email-link targets) are real routes on disk.
  it('every redirect target is a real (auth) route on disk', () => {
    for (const leaf of ['verify', 'magic', 'reset'] as const) {
      const page = join(process.cwd(), 'src', 'app', '(auth)', leaf, 'page.tsx');
      expect({ page, exists: existsSync(page) }).toEqual({ page, exists: true });
    }
  });
});
