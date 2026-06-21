import { readdirSync, readFileSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * Route-group link-hygiene regression (T-0135 bug bash).
 *
 * Next.js App Router route groups — `(public)`, `(auth)`, `(customer)`,
 * `(maker)`, `(admin)` — add NO URL segment. A `<Link href="/auth/login">`
 * therefore 404s: the real route is `/login`. This bug class has shipped
 * TWICE (Bundle A: a dead `/auth/register/maker` maker CTA; T-0135: five dead
 * `/auth/*` CTAs on the homepage + login + register pages), so it gets a
 * static guard.
 *
 * The check scans every app source file for an internal navigation target
 * (`href` / `router.push` / `redirect` / `permanentRedirect`) whose path
 * begins with a ROUTE-GROUP segment that the router strips. Only `/auth/` and
 * `/public/` are unambiguous group leaks — `(auth)` and `(public)` are never
 * real path segments. (`/admin/login`, `/dashboard/admin`, `/maker/...` ARE
 * real URL paths — the `(admin)`/`(maker)` GROUPS are stripped but those same
 * words appear as genuine segments elsewhere, so they are NOT flagged here to
 * avoid false positives; `/auth` and `/public` have no such collision.)
 */

const FORBIDDEN_PREFIXES = ['/auth/', '/public/'] as const;
// A bare group leak with no trailing path, e.g. href="/auth" or "/public".
const FORBIDDEN_EXACT = ['/auth', '/public'] as const;

// Matches the path string inside href="...", href={'...'}, router.push('...'),
// redirect('...'), permanentRedirect('...'), and template literals that start
// with a literal path segment.
const NAV_TARGET = /(?:href|push|replace|redirect|permanentRedirect)\s*[=(]\s*[{]?\s*[`'"](\/[^`'"$]*)/g;

function collectSourceFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    const st = statSync(full);
    if (st.isDirectory()) {
      // Skip the generated NSwag client (never hand-edited) + test dirs.
      if (entry === 'api-client' || entry === '__tests__') continue;
      collectSourceFiles(full, acc);
    } else if (/\.(tsx|ts)$/.test(entry) && !/\.(test|spec)\.tsx?$/.test(entry)) {
      acc.push(full);
    }
  }
  return acc;
}

const SRC_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..');

describe('route-group link hygiene', () => {
  const files = collectSourceFiles(SRC_ROOT);

  it('finds app source files to scan (sanity)', () => {
    expect(files.length).toBeGreaterThan(0);
  });

  it('no internal navigation target leaks a stripped route-group segment', () => {
    const offenders: string[] = [];

    for (const file of files) {
      const text = readFileSync(file, 'utf8');
      for (const match of text.matchAll(NAV_TARGET)) {
        const target = match[1];
        const leaks =
          FORBIDDEN_PREFIXES.some((p) => target.startsWith(p)) ||
          FORBIDDEN_EXACT.includes(target as (typeof FORBIDDEN_EXACT)[number]);
        if (leaks) {
          offenders.push(`${file.replace(SRC_ROOT, 'src')} → "${target}"`);
        }
      }
    }

    expect(
      offenders,
      `Route-group segments (auth)/(public) are stripped from URLs — these targets 404:\n${offenders.join('\n')}`,
    ).toEqual([]);
  });
});
