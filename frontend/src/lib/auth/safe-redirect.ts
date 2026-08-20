/**
 * Open-redirect guard for the `?redirect=` post-login target.
 *
 * Accepts only path-only targets: exactly one leading slash, never
 * protocol-relative `//host` (nor `/\host`, which WHATWG URL parsing
 * normalises to `//host`). Anything else — absolute URLs, `javascript:`,
 * an empty value — collapses to `null` so the caller falls back to its
 * own default. Extracted from the login forms (checkout-flow Gate 3 F1)
 * so every consumer of the parameter applies the same rule.
 */
const PATH_ONLY = /^\/(?![/\\])/;

export function safeRedirectTarget(raw: string | null | undefined): string | null {
  return typeof raw === 'string' && PATH_ONLY.test(raw) ? raw : null;
}
