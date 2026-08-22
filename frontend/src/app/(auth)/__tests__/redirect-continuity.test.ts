import { describe, expect, it } from 'vitest';
import {
  audienceHome,
  continueHref,
  hostToAudience,
  loginHrefWithRedirect,
} from '@/lib/auth/route-audience';
import { buildLoginRedirect } from '@/lib/runtime/redirect-to-login';
import { safeRedirectTarget } from '@/lib/auth/safe-redirect';

/**
 * T-0169 (audit AUTH-M3/M5/L2, PUB-L7, CUST-M6): the `?redirect=`
 * contract leaked at every hand-off — the query string was dropped, a
 * wrong-audience target bounced a fresh session onto the
 * "already signed in" panel, and the register/verify/magic funnels lost
 * the destination entirely.
 */
describe('redirect continuity', () => {
  it('drops a redirect owned by another audience instead of bouncing the user', () => {
    // The AUTH-M3 case: customer logs in with a maker destination.
    expect(continueHref('customer', '/dashboard/maker/objednavky')).toBe(
      audienceHome('customer'),
    );
    expect(continueHref('maker', '/dashboard/zakaznik/objednavky')).toBe(audienceHome('maker'));
  });

  it('keeps a redirect the audience actually owns, query string included', () => {
    expect(continueHref('customer', '/dashboard/zakaznik/objednavky?state=Shipped&page=3')).toBe(
      '/dashboard/zakaznik/objednavky?state=Shipped&page=3',
    );
  });

  it('keeps public targets for both audiences', () => {
    expect(continueHref('maker', '/katalog?city=Brno')).toBe('/katalog?city=Brno');
    expect(continueHref('customer', '/katalog?city=Brno')).toBe('/katalog?city=Brno');
  });

  it('falls back to the audience home when there is no redirect', () => {
    expect(continueHref('maker', null)).toBe('/dashboard/maker/objednavky');
  });

  it('maps API hosts to audiences for the login hand-off', () => {
    expect(hostToAudience('maker')).toBe('maker');
    expect(hostToAudience('admin')).toBe('admin');
    expect(hostToAudience('customer')).toBe('customer');
    expect(hostToAudience('public')).toBe('customer');
  });

  it('builds login links that preserve the destination', () => {
    expect(loginHrefWithRedirect('/dashboard/zakaznik/objednavky?page=3')).toBe(
      '/login?redirect=%2Fdashboard%2Fzakaznik%2Fobjednavky%3Fpage%3D3',
    );
    expect(loginHrefWithRedirect(null)).toBe('/login');
    expect(buildLoginRedirect('/objednavka/o-1')).toBe('/login?redirect=%2Fobjednavka%2Fo-1');
  });

  it('still rejects absolute and protocol-relative targets (open-redirect guard)', () => {
    expect(safeRedirectTarget('https://evil.example/x')).toBeNull();
    expect(safeRedirectTarget('//evil.example/x')).toBeNull();
    expect(safeRedirectTarget('/dashboard/zakaznik/objednavky?page=2')).toBe(
      '/dashboard/zakaznik/objednavky?page=2',
    );
  });
});
