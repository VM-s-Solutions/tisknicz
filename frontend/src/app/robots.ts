import type { MetadataRoute } from 'next';
import { SITE_URL, canonicalUrl } from '@/lib/seo/site-url';

/**
 * App-Router `robots.ts` (T-0131). Next serializes this to
 * `/robots.txt` — a `MetadataRoute` convention, not a hand-written
 * file.
 *
 * Allow-all at MVP: the public surface is meant to be fully indexable.
 * The authenticated dashboards (`(customer)` / `(maker)` / `(admin)`)
 * are NOT listed in the sitemap and are protected by JWT auth (a
 * crawler gets 401, never content), so no `Disallow` is needed — and a
 * `Disallow: /admin` would only *advertise* the admin path to scrapers.
 * `robots.txt` is advisory, not a security boundary.
 */

export default function robots(): MetadataRoute.Robots {
  return {
    rules: [{ userAgent: '*', allow: '/' }],
    sitemap: canonicalUrl('/sitemap.xml'),
    host: SITE_URL,
  };
}
