import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { apiFetch as ApiFetch } from './api-fetch';

/**
 * T-0190 — the client-side half of the stale-cookie tax.
 *
 * The 401 → refresh → retry path (T-0154) treated "the backend rejected this
 * token" and "the backend is unreachable" as the same boolean, so once a
 * refresh token was definitively dead EVERY later call re-ran the same doomed
 * `401 → refresh → 401` and paid two extra round trips for it. The middleware
 * fix (T-0189) expires the cookies, but only on a document request — a
 * long-lived client page kept paying until the next navigation.
 *
 * The browser cannot expire an HttpOnly cookie itself, so api-fetch remembers
 * the rejection for the tab instead. These tests pin both halves: it must stop
 * asking after a rejection, and it must NOT stop asking after a blip.
 */

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

const UNAUTHORIZED = { code: 'auth.required', type: 'Unauthorized' };

function isRefresh(input: unknown): boolean {
  return String(input).includes('/api/v1/auth/refresh');
}

describe('apiFetch after the backend rejects a refresh token', () => {
  const fetchMock = vi.fn();
  let apiFetch: typeof ApiFetch;

  beforeEach(async () => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
    vi.resetModules();
    ({ apiFetch } = await import('./api-fetch'));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('stops attempting the refresh once the backend has rejected it', async () => {
    let refreshCalls = 0;
    fetchMock.mockImplementation((input: RequestInfo | URL) => {
      if (isRefresh(input)) {
        refreshCalls++;
        return Promise.resolve(new Response(null, { status: 401 }));
      }
      return Promise.resolve(jsonResponse(401, UNAUTHORIZED));
    });

    for (let i = 0; i < 4; i++) {
      const r = await apiFetch<unknown>('customer', `/api/v1/orders/${i}`, { method: 'GET' });
      expect(r.success).toBe(false);
    }

    // One refresh for the first 401; the other three calls skip it entirely.
    expect(refreshCalls).toBe(1);
    // 4 calls + 1 refresh, not 4 + 4.
    expect(fetchMock).toHaveBeenCalledTimes(5);
  });

  it('keeps trying when the refresh merely failed to reach the backend', async () => {
    let refreshCalls = 0;
    fetchMock.mockImplementation((input: RequestInfo | URL) => {
      if (isRefresh(input)) {
        refreshCalls++;
        return Promise.reject(new Error('network down'));
      }
      return Promise.resolve(jsonResponse(401, UNAUTHORIZED));
    });

    for (let i = 0; i < 3; i++) {
      await apiFetch<unknown>('customer', `/api/v1/orders/${i}`, { method: 'GET' });
    }

    // A blip says nothing about the token — refusing to retry would strand a
    // visitor whose session is actually fine.
    expect(refreshCalls).toBe(3);
  });

  it('keeps trying when the refresh endpoint answers 5xx', async () => {
    let refreshCalls = 0;
    fetchMock.mockImplementation((input: RequestInfo | URL) => {
      if (isRefresh(input)) {
        refreshCalls++;
        return Promise.resolve(new Response(null, { status: 503 }));
      }
      return Promise.resolve(jsonResponse(401, UNAUTHORIZED));
    });

    for (let i = 0; i < 3; i++) {
      await apiFetch<unknown>('customer', `/api/v1/orders/${i}`, { method: 'GET' });
    }

    expect(refreshCalls).toBe(3);
  });

  it('forgets the rejection as soon as a call to that host succeeds again', async () => {
    let refreshCalls = 0;
    let sessionAlive = false;
    fetchMock.mockImplementation((input: RequestInfo | URL) => {
      if (isRefresh(input)) {
        refreshCalls++;
        return Promise.resolve(new Response(null, { status: 401 }));
      }
      return Promise.resolve(
        sessionAlive ? jsonResponse(200, { ok: true }) : jsonResponse(401, UNAUTHORIZED),
      );
    });

    await apiFetch<unknown>('customer', '/api/v1/orders', { method: 'GET' });
    expect(refreshCalls).toBe(1);

    // The visitor logs in (or another tab rotates the shared cookies).
    sessionAlive = true;
    const loggedIn = await apiFetch<unknown>('customer', '/api/v1/orders', { method: 'GET' });
    expect(loggedIn.success).toBe(true);

    // The session expires again later — this must be recovered, not treated
    // as permanently dead.
    sessionAlive = false;
    await apiFetch<unknown>('customer', '/api/v1/orders', { method: 'GET' });
    expect(refreshCalls).toBe(2);
  });

  it('remembers per host — a dead maker session does not mute the customer host', async () => {
    const refreshCalls: string[] = [];
    fetchMock.mockImplementation((input: RequestInfo | URL) => {
      if (isRefresh(input)) {
        refreshCalls.push(String(input));
        return Promise.resolve(new Response(null, { status: 401 }));
      }
      return Promise.resolve(jsonResponse(401, UNAUTHORIZED));
    });

    await apiFetch<unknown>('maker', '/api/v1/products', { method: 'GET' });
    await apiFetch<unknown>('maker', '/api/v1/products', { method: 'GET' });
    await apiFetch<unknown>('customer', '/api/v1/orders', { method: 'GET' });

    // One per host: maker's second call is muted, customer's first is not.
    expect(refreshCalls).toHaveLength(2);
  });
});
