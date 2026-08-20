import type { Audience } from './session';

/**
 * Which audience a route belongs to (ADR 0005 / ADR 0012 — an account is
 * bound to exactly one audience, `User.MatchesAudience`).
 *
 * Two selectors over one table so the guard and the "you are already
 * signed in" affordance can never drift apart:
 *
 * - {@link guardedRouteAudience} — the edge-middleware route guard.
 *   Dashboards only: a missing session cookie there means there is
 *   nothing to render, so a redirect is the whole answer.
 * - {@link routeAudience} — audience *ownership*, guard or not. The
 *   checkout/order flow is customer-only but deliberately stays out of
 *   the guard: those pages render their own maker / anonymous states
 *   (a maker bounced to /login can never satisfy it — their account
 *   cannot hold a customer JWT, which was the reported login loop).
 */
interface AudienceRoute {
  readonly prefix: string;
  readonly audience: Audience;
  /** True when the edge middleware redirects an unauthenticated request. */
  readonly guarded: boolean;
}

const AUDIENCE_ROUTES: readonly AudienceRoute[] = [
  { prefix: '/dashboard/zakaznik', audience: 'customer', guarded: true },
  { prefix: '/dashboard/maker', audience: 'maker', guarded: true },
  { prefix: '/dashboard/admin', audience: 'admin', guarded: true },
  { prefix: '/objednavka', audience: 'customer', guarded: false },
];

/**
 * Segment-aware prefix match. A bare `startsWith` would claim
 * `/objednavkovy-formular` for `/objednavka`; the boundary check keeps
 * ownership to the route itself, its children and its query string
 * (callers pass a full `href` as often as a `pathname`).
 */
function underPrefix(pathname: string, prefix: string): boolean {
  if (!pathname.startsWith(prefix)) return false;
  const boundary = pathname.charAt(prefix.length);
  return boundary === '' || boundary === '/' || boundary === '?';
}

/** Audience that owns `pathname`, or undefined for a public route. */
export function routeAudience(pathname: string): Audience | undefined {
  return AUDIENCE_ROUTES.find((route) => underPrefix(pathname, route.prefix))?.audience;
}

/** Audience that owns `pathname` *and* is enforced by the middleware guard. */
export function guardedRouteAudience(pathname: string): Audience | undefined {
  return AUDIENCE_ROUTES.find((route) => route.guarded && underPrefix(pathname, route.prefix))
    ?.audience;
}

/** Where an audience lands when no explicit redirect target applies. */
export function audienceHome(audience: Audience): string {
  switch (audience) {
    case 'maker':
      return '/dashboard/maker/objednavky';
    case 'admin':
      return '/dashboard/admin';
    default:
      return '/dashboard/zakaznik/objednavky';
  }
}

/**
 * Post-login destination for a session that already exists.
 *
 * A redirect target owned by *another* audience is dropped — sending a
 * maker to `/objednavka` or `/dashboard/zakaznik/*` only bounces them
 * back to /login. Public targets are kept: both audiences can browse
 * the catalog.
 */
export function continueHref(audience: Audience, redirect: string | null): string {
  if (redirect === null) return audienceHome(audience);
  const owner = routeAudience(redirect);
  return owner !== undefined && owner !== audience ? audienceHome(audience) : redirect;
}
