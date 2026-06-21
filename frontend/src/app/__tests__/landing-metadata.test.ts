import { describe, expect, it } from 'vitest';
import { t } from '@/lib/i18n';
import { canonicalUrl } from '@/lib/seo/site-url';
import { generateMetadata } from '../page';

/**
 * SEO predicate test (T-0133 / Q-0031 retroactive unblock of T-0131).
 *
 * The landing page's `generateMetadata` is a pure function (it reads
 * i18n + the SITE_URL helper, no network), so it is unit-testable
 * directly. Pins T-0131 AC-5: canonical + OG + Twitter summary card on
 * `/`, with title/description from the new `home.metadata.*` i18n keys
 * (not a hardcoded literal).
 */
describe('landing generateMetadata', () => {
  it('emits canonical, OpenGraph and a Twitter summary card from i18n keys', () => {
    const meta = generateMetadata();
    const expectedTitle = t('home.metadata.title');
    const expectedDescription = t('home.metadata.description');
    const expectedUrl = canonicalUrl('/');

    expect(meta.title).toBe(expectedTitle);
    expect(meta.description).toBe(expectedDescription);
    expect(meta.alternates?.canonical).toBe(expectedUrl);

    expect(meta.openGraph).toMatchObject({
      title: expectedTitle,
      description: expectedDescription,
      url: expectedUrl,
      type: 'website',
    });

    expect(meta.twitter).toMatchObject({
      card: 'summary',
      title: expectedTitle,
      description: expectedDescription,
    });
  });

  it('does not use a hardcoded literal title (keys resolve to non-empty Czech copy)', () => {
    const meta = generateMetadata();
    expect(typeof meta.title).toBe('string');
    expect(meta.title).not.toBe('home.metadata.title');
    expect((meta.title as string).length).toBeGreaterThan(0);
  });
});
