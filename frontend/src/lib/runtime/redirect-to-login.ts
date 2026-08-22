'use client';

/**
 * Terminal-401 navigation for client components (T-0169, audit AUTH-M5).
 *
 * `apiFetch` already refreshes once and retries; when THAT fails the
 * caller gets an `Unauthorized` result whose default copy is "Pro
 * pokračování se prosím přihlaste." — advice with no way to follow it.
 * SSR pages redirect with a returnUrl, and checkout pushes to login, but
 * every other client-side mutation just printed the sentence. This is
 * the shared way to act on it, so the behaviour stops being per-callsite.
 *
 * Kept as a plain function (not a hook) so it can be called from inside
 * event handlers and `catch` blocks without restructuring the caller.
 */
export function buildLoginRedirect(currentUrl: string): string {
  return `/login?redirect=${encodeURIComponent(currentUrl)}`;
}

/**
 * Send the browser to the login page, preserving where it was. Uses the
 * caller's router when supplied so the client-side transition is kept;
 * falls back to a full navigation for callers without one.
 */
export function redirectToLogin(router?: { readonly push: (href: string) => void }): void {
  const current = `${window.location.pathname}${window.location.search}`;
  const href = buildLoginRedirect(current);
  if (router) {
    router.push(href);
    return;
  }
  window.location.assign(href);
}
