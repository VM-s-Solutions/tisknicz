import nextConfig from '../../next.config';

/**
 * The next/image optimizer re-encodes every (image, device width) pair on
 * the frontend's own App Service instance, which shares one 2-vCPU Linux
 * plan with four .NET API hosts and Azure Functions.
 *
 * Measured 2026-08-22 on the standalone build, one 1600x1200 JPEG through
 * the full `deviceSizes` set with a cold `.next/cache/images`:
 *   AVIF 0.83 s of encode CPU  vs  WebP 0.36 s  (2.3x)
 * and at w=640 the AVIF was the LARGER file (78.6 kB vs 70.3 kB). Every
 * modern browser advertises `Accept: image/avif`, so re-adding it charges
 * that tax on the first view of every product photo and maker logo.
 *
 * If the frontend ever gets its own plan with spare cores, revisit — but
 * do it with a fresh measurement, not by deleting this test.
 */
describe('next.config image formats', () => {
  it('serves WebP only — AVIF encoding is too expensive for the shared plan', () => {
    expect(nextConfig.images?.formats).toEqual(['image/webp']);
  });

  it('keeps a long optimized-image cache so a restart is the only re-encode', () => {
    // 30 days. The container's disk cache is wiped on deploy, so anything
    // shorter multiplies the encode cost the test above is guarding.
    expect(nextConfig.images?.minimumCacheTTL).toBe(60 * 60 * 24 * 30);
  });
});
