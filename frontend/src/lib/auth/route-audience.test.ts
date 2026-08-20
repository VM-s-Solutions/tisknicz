import { describe, expect, it } from 'vitest';
import {
  audienceHome,
  continueHref,
  guardedRouteAudience,
  routeAudience,
} from './route-audience';

/**
 * Route ownership + the post-login continue target.
 *
 * The bug these pin: a signed-in maker who pressed "Objednat" was sent
 * to /login?redirect=/objednavka…, logged in again (their account can
 * only mint a maker JWT), was pushed back to /objednavka and bounced to
 * /login once more. `continueHref` must never hand a session a target
 * owned by a different audience.
 */

describe('routeAudience', () => {
  it('maps the dashboards to their audience', () => {
    expect(routeAudience('/dashboard/zakaznik/objednavky')).toBe('customer');
    expect(routeAudience('/dashboard/maker/objednavky')).toBe('maker');
    expect(routeAudience('/dashboard/admin')).toBe('admin');
  });

  it('claims the whole checkout/order flow for the customer audience', () => {
    expect(routeAudience('/objednavka')).toBe('customer');
    expect(routeAudience('/objednavka/ord-1')).toBe('customer');
    expect(routeAudience('/objednavka/ord-1/potvrzeni')).toBe('customer');
  });

  it('leaves public routes unowned', () => {
    expect(routeAudience('/')).toBeUndefined();
    expect(routeAudience('/katalog')).toBeUndefined();
    expect(routeAudience('/produkt/p1')).toBeUndefined();
    expect(routeAudience('/login')).toBeUndefined();
  });

  it('does not claim a route that merely shares a prefix', () => {
    expect(routeAudience('/objednavkovy-formular')).toBeUndefined();
    expect(routeAudience('/dashboard/makers-guide')).toBeUndefined();
  });
});

describe('guardedRouteAudience', () => {
  it('covers the dashboards', () => {
    expect(guardedRouteAudience('/dashboard/zakaznik/profile')).toBe('customer');
    expect(guardedRouteAudience('/dashboard/maker/produkty')).toBe('maker');
    expect(guardedRouteAudience('/dashboard/admin/faktury')).toBe('admin');
  });

  it('leaves the checkout flow to the page guards', () => {
    // The middleware must not redirect here: the page renders the maker
    // explanation state instead of an unsatisfiable login screen.
    expect(guardedRouteAudience('/objednavka')).toBeUndefined();
    expect(guardedRouteAudience('/objednavka/ord-1')).toBeUndefined();
  });
});

describe('audienceHome', () => {
  it('routes each audience to its own landing area', () => {
    expect(audienceHome('customer')).toBe('/dashboard/zakaznik/objednavky');
    expect(audienceHome('maker')).toBe('/dashboard/maker/objednavky');
    expect(audienceHome('admin')).toBe('/dashboard/admin');
  });
});

describe('continueHref', () => {
  it('falls back to the audience home without a redirect target', () => {
    expect(continueHref('maker', null)).toBe('/dashboard/maker/objednavky');
    expect(continueHref('customer', null)).toBe('/dashboard/zakaznik/objednavky');
  });

  it('keeps a target the session audience owns', () => {
    expect(continueHref('maker', '/dashboard/maker/vyplaty')).toBe('/dashboard/maker/vyplaty');
    expect(continueHref('customer', '/objednavka?productId=p1')).toBe('/objednavka?productId=p1');
  });

  it('keeps public targets for every audience', () => {
    expect(continueHref('maker', '/katalog')).toBe('/katalog');
    expect(continueHref('customer', '/produkt/p1')).toBe('/produkt/p1');
  });

  it('drops a target owned by another audience — the login loop', () => {
    expect(continueHref('maker', '/objednavka?productId=p1')).toBe('/dashboard/maker/objednavky');
    expect(continueHref('maker', '/dashboard/zakaznik/objednavky')).toBe(
      '/dashboard/maker/objednavky',
    );
    expect(continueHref('customer', '/dashboard/maker/objednavky')).toBe(
      '/dashboard/zakaznik/objednavky',
    );
  });
});
