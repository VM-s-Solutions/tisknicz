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
      expect(rule, `missing redirect for ${source}`).toBeDefined();
      expect(rule!.destination).toBe(destination);
      expect(rule!.permanent).toBe(true);
    }
  });
});
