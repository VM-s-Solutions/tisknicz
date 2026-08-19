import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { apiFetch } from './api-fetch';
import { resolveErrorMessage } from './errors';
import { messages } from '../i18n';

/**
 * Pins the diagnosis a rate-limited read produces.
 *
 * The Public host serves the catalog JSON and every image byte from one
 * per-IP envelope, so ordinary browsing exhausted it and the next server
 * render of /katalog got a 429. The page still returned HTTP 200, and
 * every failure resolved to `error.transient` — "Server je momentálně
 * nedostupný" — so the screen accused a server that had answered in
 * milliseconds. These tests pin that a 429 now reads as a 429, and that
 * a genuinely unreachable server still reads as unreachable.
 */
describe('rate-limited and transport error copy', () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('surfaces a bodyless 429 as http.429, not as a dead server', async () => {
    // Exactly what ASP.NET's rate limiter emits: no body, no
    // content-type, a Retry-After and the correlation id.
    fetchMock.mockResolvedValue(
      new Response(null, {
        status: 429,
        headers: { 'retry-after': '60', 'x-correlation-id': 'abc:001' },
      }),
    );

    const result = await apiFetch('public', '/api/v1/catalog/makers', {
      retryOnTransient: false,
    });

    expect(result.success).toBe(false);
    if (result.success) return;
    expect(result.error.code).toBe('http.429');
    expect(result.error.correlationId).toBe('abc:001');
    expect(resolveErrorMessage(result.error)).toBe(messages['http.429']);
    // The regression: this used to be the transient copy.
    expect(resolveErrorMessage(result.error)).not.toBe(messages['error.transient']);
  });

  it('still tells the truth when the server really is unreachable', async () => {
    fetchMock.mockRejectedValue(new TypeError('fetch failed'));

    const result = await apiFetch('public', '/api/v1/catalog/makers', {
      retryOnTransient: false,
    });

    expect(result.success).toBe(false);
    if (result.success) return;
    expect(result.error.code).toBe('network.unreachable');
    expect(resolveErrorMessage(result.error)).toBe(messages['network.unreachable']);
  });

  it('distinguishes a timeout from an unreachable host', async () => {
    fetchMock.mockRejectedValue(new DOMException('aborted', 'AbortError'));

    const result = await apiFetch('public', '/api/v1/catalog/makers', {
      retryOnTransient: false,
    });

    expect(result.success).toBe(false);
    if (result.success) return;
    expect(result.error.code).toBe('network.timeout');
    expect(resolveErrorMessage(result.error)).toBe(messages['network.timeout']);
    expect(resolveErrorMessage(result.error)).not.toBe(messages['network.unreachable']);
  });

  it('does not retry a 429 — the window outlives the request budget', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 429 }));

    // A GET is retryable by default; 429 must NOT be in that set, or
    // three attempts each burn a permit and deepen the window.
    await apiFetch('public', '/api/v1/catalog/makers');

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
