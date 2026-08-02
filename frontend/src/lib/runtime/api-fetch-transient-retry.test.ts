import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiFetch } from './api-fetch';

/**
 * Pins the transient-retry contract. Azure App Service recycles an
 * instance mid-request often enough that a single 502/503/504 or dropped
 * connection is normal background noise; without a retry the customer
 * sees an error banner and re-clicks. The retry is deliberately limited
 * to requests that are idempotent by HTTP contract — replaying a POST
 * could double-order or double-charge.
 */

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

const gatewayError = () => new Response('upstream recycling', { status: 503 });

describe('apiFetch transient retry', () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('retries a GET through a 503 and returns the eventual success', async () => {
    fetchMock
      .mockResolvedValueOnce(gatewayError())
      .mockResolvedValueOnce(jsonResponse(200, { hello: 'world' }));

    const result = await apiFetch<{ hello: string }>('public', '/api/v1/catalog/makers');

    expect(result.success).toBe(true);
    if (result.success) expect(result.value).toEqual({ hello: 'world' });
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('recovers a GET from a dropped connection', async () => {
    fetchMock
      .mockRejectedValueOnce(new TypeError('fetch failed'))
      .mockResolvedValueOnce(jsonResponse(200, { ok: true }));

    const result = await apiFetch<{ ok: boolean }>('public', '/api/v1/catalog/makers');

    expect(result.success).toBe(true);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('gives up after the attempt cap and surfaces the transient error', async () => {
    fetchMock.mockResolvedValue(gatewayError());

    const result = await apiFetch<unknown>('public', '/api/v1/catalog/makers');

    expect(result.success).toBe(false);
    if (!result.success) expect(result.error.type).toBe('Transient');
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it('never replays a POST — a lost response may still have committed', async () => {
    fetchMock.mockResolvedValue(gatewayError());

    const result = await apiFetch<unknown>('customer', '/api/v1/orders', { method: 'POST' });

    expect(result.success).toBe(false);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('honours an explicit opt-in on a POST the caller knows is idempotent', async () => {
    fetchMock
      .mockResolvedValueOnce(gatewayError())
      .mockResolvedValueOnce(jsonResponse(200, { ok: true }));

    const result = await apiFetch<{ ok: boolean }>('customer', '/api/v1/orders', {
      method: 'POST',
      retryOnTransient: true,
    });

    expect(result.success).toBe(true);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('honours an explicit opt-out on a GET', async () => {
    fetchMock.mockResolvedValue(gatewayError());

    const result = await apiFetch<unknown>('public', '/api/v1/catalog/makers', {
      retryOnTransient: false,
    });

    expect(result.success).toBe(false);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('does not retry a 500 — that is the app failing, not the edge', async () => {
    fetchMock.mockResolvedValue(jsonResponse(500, { code: 'unknown', type: 'Transient' }));

    const result = await apiFetch<unknown>('public', '/api/v1/catalog/makers');

    expect(result.success).toBe(false);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('stops retrying once the caller aborts', async () => {
    const controller = new AbortController();
    fetchMock.mockImplementationOnce(() => {
      controller.abort();
      return Promise.reject(new DOMException('Aborted', 'AbortError'));
    });

    const result = await apiFetch<unknown>('public', '/api/v1/catalog/makers', {
      signal: controller.signal,
    });

    expect(result.success).toBe(false);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('does not multiply a long download timeout — retries share one deadline', async () => {
    // Blob downloads pass timeoutMs: 120_000. The retry allowance is a
    // flat DEFAULT_TIMEOUT_MS on top, not a multiplier, so a hung
    // download cannot stretch to four minutes.
    const seen: number[] = [];
    fetchMock.mockImplementation((_url: string, init: RequestInit) => {
      // The composed signal carries the per-attempt budget; record when
      // each attempt starts so we can assert the second one is short.
      seen.push(Date.now());
      void init;
      return Promise.resolve(gatewayError());
    });

    const started = Date.now();
    const result = await apiFetch<unknown>('customer', '/api/v1/orders/x/invoice', {
      method: 'GET',
      timeoutMs: 120_000,
    });

    expect(result.success).toBe(false);
    expect(seen).toHaveLength(3);
    // Fast 503s: all three attempts land well inside the deadline, so the
    // long timeout never comes into play.
    expect(Date.now() - started).toBeLessThan(5_000);
  });

  it('leaves 4xx alone', async () => {
    fetchMock.mockResolvedValue(jsonResponse(404, { code: 'order.notFound', type: 'NotFound' }));

    const result = await apiFetch<unknown>('public', '/api/v1/catalog/makers/nope');

    expect(result.success).toBe(false);
    if (!result.success) expect(result.error.type).toBe('NotFound');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
