import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { apiFetch as ApiFetch } from './api-fetch';

/**
 * Pins the T-0154 browser-side 401 → refresh → retry-once contract:
 * an expired access cookie mid-session recovers through ONE refresh
 * round trip; auth endpoints and failed refreshes never loop.
 *
 * The module is re-imported per test (T-0190): api-fetch now remembers, at
 * module scope, which hosts the backend has declared dead, so a shared import
 * would make these tests order-dependent.
 */

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

describe('apiFetch 401 auth retry', () => {
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

  it('refreshes and retries once on 401, returning the retried result', async () => {
    fetchMock
      .mockResolvedValueOnce(jsonResponse(401, { code: 'auth.required', type: 'Unauthorized' }))
      .mockResolvedValueOnce(new Response(null, { status: 204 })) // refresh OK
      .mockResolvedValueOnce(jsonResponse(200, { hello: 'world' }));

    const result = await apiFetch<{ hello: string }>('customer', '/api/v1/orders', { method: 'GET' });

    expect(result.success).toBe(true);
    if (result.success) expect(result.value).toEqual({ hello: 'world' });
    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(String(fetchMock.mock.calls[1]?.[0])).toContain('/api/v1/auth/refresh');
  });

  it('returns the 401 error when the refresh is rejected (no retry loop)', async () => {
    fetchMock
      .mockResolvedValueOnce(jsonResponse(401, { code: 'auth.required', type: 'Unauthorized' }))
      .mockResolvedValueOnce(new Response(null, { status: 401 })); // refresh rejected

    const result = await apiFetch<unknown>('customer', '/api/v1/orders', { method: 'GET' });

    expect(result.success).toBe(false);
    if (!result.success) expect(result.error.type).toBe('Unauthorized');
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('does not attempt a refresh for auth endpoints', async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse(401, { code: 'auth.invalidCredentials', type: 'Validation' }),
    );

    const result = await apiFetch<unknown>('customer', '/api/v1/auth/login', {
      method: 'POST',
      json: { email: 'x@example.cz', password: 'nope' },
    });

    expect(result.success).toBe(false);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('retries only once even if the retried call also 401s', async () => {
    fetchMock
      .mockResolvedValueOnce(jsonResponse(401, { code: 'auth.required', type: 'Unauthorized' }))
      .mockResolvedValueOnce(new Response(null, { status: 204 })) // refresh "succeeds"
      .mockResolvedValueOnce(jsonResponse(401, { code: 'auth.required', type: 'Unauthorized' }));

    const result = await apiFetch<unknown>('customer', '/api/v1/orders', { method: 'GET' });

    expect(result.success).toBe(false);
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it('collapses concurrent 401s into a single refresh round trip', async () => {
    let refreshCalls = 0;
    fetchMock.mockImplementation((input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/api/v1/auth/refresh')) {
        refreshCalls++;
        return Promise.resolve(new Response(null, { status: 204 }));
      }
      // First hit per path 401s, retry succeeds.
      const firstCall = fetchMock.mock.calls.filter((c) => String(c[0]) === url).length <= 1;
      return Promise.resolve(
        firstCall
          ? jsonResponse(401, { code: 'auth.required', type: 'Unauthorized' })
          : jsonResponse(200, { ok: true }),
      );
    });

    const [a, b] = await Promise.all([
      apiFetch<unknown>('customer', '/api/v1/orders/1', { method: 'GET' }),
      apiFetch<unknown>('customer', '/api/v1/orders/2', { method: 'GET' }),
    ]);

    expect(a.success).toBe(true);
    expect(b.success).toBe(true);
    expect(refreshCalls).toBe(1);
  });
});
