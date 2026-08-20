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
/**
 * A fetch that never answers: it settles only when the composed signal
 * aborts, and rejects with that signal's own reason — exactly what the
 * platform does. Mocking a bare rejection instead is what let the
 * timeout/unreachable copy sit swapped behind a green suite.
 */
function hangUntilAborted(_url: string, init: RequestInit): Promise<Response> {
  return new Promise((_resolve, reject) => {
    init.signal?.addEventListener('abort', () => reject(init.signal?.reason));
  });
}

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

  it('reports an expired request budget as a timeout, not an unreachable host', async () => {
    // A real fetch rejects with the signal's OWN reason, and
    // `AbortSignal.timeout()` makes that a `TimeoutError` — never the
    // `AbortError` this test used to invent, which is why the swapped
    // copy survived a green suite. Let the budget actually expire.
    fetchMock.mockImplementation(hangUntilAborted);

    const result = await apiFetch('public', '/api/v1/catalog/makers', {
      retryOnTransient: false,
      timeoutMs: 20,
    });

    expect(result.success).toBe(false);
    if (result.success) return;
    expect(result.error.code).toBe('network.timeout');
    expect(resolveErrorMessage(result.error)).toBe(messages['network.timeout']);
    expect(resolveErrorMessage(result.error)).not.toBe(messages['network.unreachable']);
  });

  it('does not dress a caller-cancelled request up as a timeout', async () => {
    // The other half of the swap: only the budget expiring is a timeout.
    // A component unmounting mid-flight aborts with an `AbortError`, and
    // that must not claim the server was slow.
    const controller = new AbortController();
    fetchMock.mockImplementation(hangUntilAborted);
    setTimeout(() => controller.abort(), 10);

    const result = await apiFetch('public', '/api/v1/catalog/makers', {
      retryOnTransient: false,
      timeoutMs: 5_000,
      signal: controller.signal,
    });

    expect(result.success).toBe(false);
    if (result.success) return;
    expect(result.error.code).toBe('network.unreachable');
    expect(resolveErrorMessage(result.error)).not.toBe(messages['network.timeout']);
  });

  it('does not retry a 429 — the window outlives the request budget', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 429 }));

    // A GET is retryable by default; 429 must NOT be in that set, or
    // three attempts each burn a permit and deepen the window.
    await apiFetch('public', '/api/v1/catalog/makers');

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
