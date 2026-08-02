import { type ApiError, type ErrorType, type Result, err, ok } from './result';
import { ACCESS_COOKIE_PREFIX, REFRESH_COOKIE_PREFIX } from '../auth/session';

/**
 * Hostname identifiers for the four .NET Web hosts. The matching base URL
 * comes from `NEXT_PUBLIC_API_<HOST>_BASE_URL` env vars; in development
 * each host has a fixed port per ADR 0005 (Customer=5001, Maker=5002,
 * Admin=5003, Public=5104). Public moved off 5004 in T-0046b because the
 * default collided with an unrelated local project on dev machines —
 * launchSettings, nswag/config.json and this fallback all track 5104.
 *
 * Deployed environments (T-0153): `NEXT_PUBLIC_*` may instead be a
 * SAME-ORIGIN RELATIVE path (`/api-proxy/<host>`) that the Next server
 * rewrites to the real API host (see `next.config.ts`). That makes the
 * backend's `Set-Cookie` land first-party on the frontend origin — the
 * only way the ADR 0012 `SameSite=Strict` session cookies can work when
 * frontend and APIs sit on sibling `*.azurewebsites.net` hosts (a
 * public-suffix domain, so no shared parent cookie is possible). Server
 * renders can't use a relative path, so they resolve the sibling
 * `API_<HOST>_INTERNAL_BASE_URL` runtime env var instead.
 */
export type ApiHost = 'customer' | 'maker' | 'admin' | 'public';

const HOST_BASE_URLS: Record<ApiHost, string> = {
  customer: process.env.NEXT_PUBLIC_API_CUSTOMER_BASE_URL ?? 'http://localhost:5001',
  maker: process.env.NEXT_PUBLIC_API_MAKER_BASE_URL ?? 'http://localhost:5002',
  admin: process.env.NEXT_PUBLIC_API_ADMIN_BASE_URL ?? 'http://localhost:5003',
  public: process.env.NEXT_PUBLIC_API_PUBLIC_BASE_URL ?? 'http://localhost:5104',
};

/**
 * Server-side (SSR) absolute base URLs. Read lazily — NOT `NEXT_PUBLIC_`,
 * so they are never inlined into the client bundle and stay overridable
 * via App Service app settings at runtime (standalone output).
 */
function internalHostBaseUrl(host: ApiHost): string | undefined {
  const byHost: Record<ApiHost, string | undefined> = {
    customer: process.env.API_CUSTOMER_INTERNAL_BASE_URL,
    maker: process.env.API_MAKER_INTERNAL_BASE_URL,
    admin: process.env.API_ADMIN_INTERNAL_BASE_URL,
    public: process.env.API_PUBLIC_INTERNAL_BASE_URL,
  };
  return byHost[host];
}

/**
 * Resolves the base URL a fetch from the CURRENT runtime should use:
 * the browser gets the public base (absolute, or proxy-relative which
 * the browser resolves against the page origin), the server prefers the
 * internal absolute URL and refuses to fetch a relative base (Node
 * `fetch` has no origin to resolve it against).
 */
function resolveBaseUrl(host: ApiHost): string {
  const publicBase = HOST_BASE_URLS[host];
  if (typeof window !== 'undefined') {
    return publicBase;
  }
  const internal = internalHostBaseUrl(host);
  if (internal) {
    return internal;
  }
  if (publicBase.startsWith('/')) {
    throw new Error(
      `apiFetch(${host}): NEXT_PUBLIC base is the proxy-relative path "${publicBase}" but ` +
        `API_${host.toUpperCase()}_INTERNAL_BASE_URL is not set — server renders need an absolute URL.`,
    );
  }
  return publicBase;
}

/**
 * Exposes a host's BROWSER-FACING absolute base URL to helper modules
 * that need to build an absolute backend URL themselves (T-0026: the
 * Google OAuth `redirectUri` — under the same-origin proxy that URL is
 * `<frontend origin>/api-proxy/<host>/...`, which the rewrite forwards
 * to the backend's own `google/callback` route so the resulting
 * `Set-Cookie` still lands first-party). Client-only concern; callers
 * are Client Components. Kept as a thin accessor rather than exporting
 * `HOST_BASE_URLS` directly so call sites can't mutate the map.
 */
export function apiHostBaseUrl(host: ApiHost): string {
  const base = HOST_BASE_URLS[host];
  if (base.startsWith('/') && typeof window !== 'undefined') {
    return `${window.location.origin}${base}`;
  }
  return base;
}

export interface ApiFetchOptions extends Omit<RequestInit, 'body' | 'headers'> {
  /** JSON body — serialized by the wrapper (set `body` to a string to skip). */
  readonly json?: unknown;
  /** Raw body if you need full control (multipart upload etc.). */
  readonly body?: BodyInit;
  /** Extra request headers. `Content-Type` is set automatically for `json`. */
  readonly headers?: Record<string, string>;
  /**
   * Bearer token to send. Phase-1 contract: callers pass this explicitly
   * after reading the audience-scoped access cookie themselves (see
   * `lib/auth/accessCookieName(audience)`). T-0027 will move the cookie →
   * Bearer bridge into this wrapper once real JWT refresh exists; until
   * then a missing token simply means the request goes unauthenticated.
   */
  readonly accessToken?: string;
  /**
   * Provide your own AbortSignal. The wrapper composes it with the
   * timeout via `AbortSignal.any`, so the request aborts on whichever
   * fires first.
   */
  readonly signal?: AbortSignal;
  /**
   * Per-call timeout budget in milliseconds. Defaults to
   * {@link DEFAULT_TIMEOUT_MS} (8 s) — the long-standing ceiling for
   * every JSON round trip. Only long-running transfers (e.g. multipart
   * attachment uploads, checkout-flow review B-1) should override it;
   * a caller-supplied `signal` can still abort earlier.
   */
  readonly timeoutMs?: number;
  /**
   * Success-body parsing mode. The default (`'auto'`) keeps the
   * long-standing JSON/text detection; `'blob'` returns the raw body as
   * a `Blob` for authenticated binary downloads (T-0086b attachments +
   * invoice PDFs — the streaming endpoints need the audience cookie, so
   * a plain `<a href>` can't be used and `response.text()` would corrupt
   * the bytes). Error responses still flow through
   * {@link parseErrorResponse} unchanged.
   */
  readonly parse?: 'auto' | 'blob';
  /**
   * Override the transient-retry decision. Defaults to true for methods
   * that are idempotent by HTTP contract (GET/HEAD/OPTIONS) and false for
   * everything else. Set it to true on a POST only when the endpoint is
   * genuinely idempotent (server-side dedupe / idempotency key); set it
   * to false on a GET whose side effects make a replay undesirable.
   */
  readonly retryOnTransient?: boolean;
}

/** Default request timeout — unchanged behaviour for every JSON call site (T-0015 reviewer M1). */
const DEFAULT_TIMEOUT_MS = 8000;

/**
 * Transient-failure retry budget.
 *
 * Azure App Service + Postgres Flexible Server produce a steady trickle of
 * one-off failures that a plain repeat of the same request clears: gateway
 * 502/503/504s while an instance recycles, and connection resets. Without
 * a retry here the customer sees a red banner and re-clicks — the
 * "some actions have to be done multiple times before we get a 200"
 * symptom.
 *
 * ONLY methods that are idempotent by HTTP contract are retried. A POST
 * may have committed server-side before the response was lost, so
 * replaying it could double-charge, double-order, or double-message.
 * Callers that know a specific POST is safe opt in via `retryOnTransient`.
 *
 * Retries share ONE overall deadline: the caller's own budget plus a
 * single {@link DEFAULT_TIMEOUT_MS} allowance. Each attempt gets whatever
 * is left, and once it is spent the last error is returned. The allowance
 * is a fixed amount rather than a multiplier on purpose — the long-timeout
 * call sites are the 120 s blob downloads, and a multiplier would let one
 * of those hang for four minutes. That costs nothing in the case this
 * exists for: gateway 5xx and connection resets come back in
 * milliseconds, so every attempt still gets its full budget.
 */
const MAX_TRANSIENT_ATTEMPTS = 3;
/** Backoff before retry N. Sized to MAX_TRANSIENT_ATTEMPTS - 1 entries. */
const RETRY_BACKOFF_MS = [200, 600] as const;
const IDEMPOTENT_METHODS = new Set(['GET', 'HEAD', 'OPTIONS']);
const TRANSIENT_STATUSES = new Set([502, 503, 504]);

const delay = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

/**
 * The single entry point for calling the .NET backend from the frontend.
 *
 * NSwag-generated clients live in `lib/api-client/*-api.v1.ts`. A thin
 * helper layer in `lib/api-client-helpers/` (created per call site) wraps
 * each client and routes its HTTP through this function so:
 *   - the JWT is attached uniformly,
 *   - the backend's `BusinessResult` / `ProblemDetails` shapes are folded
 *     into the frontend's {@link Result<T, ApiError>},
 *   - the `x-correlation-id` echoed by the backend is preserved on errors,
 *   - timeouts and network failures become `Transient` errors instead of
 *     thrown exceptions.
 *
 * Per ADR 0022 (NSwag pipeline) the generated client is the contract
 * boundary; per ADR 0005 each host has its own base URL.
 */
export async function apiFetch<TValue>(
  host: ApiHost,
  path: string,
  options: ApiFetchOptions = {},
): Promise<Result<TValue, ApiError>> {
  return apiFetchCore<TValue>(host, path, options, /* allowAuthRetry */ true);
}

/**
 * In-flight client-side refresh de-dupe (T-0154). Refresh-token reuse
 * detection revokes the whole token family (ADR 0012), so several
 * concurrently-401ing calls (an order page fires multiple requests)
 * must collapse into ONE refresh round trip per host.
 */
const clientRefreshInflight = new Map<ApiHost, Promise<boolean>>();

function refreshClientSession(host: ApiHost): Promise<boolean> {
  const existing = clientRefreshInflight.get(host);
  if (existing) return existing;

  const attempt = (async () => {
    try {
      const response = await fetch(`${resolveBaseUrl(host)}/api/v1/auth/refresh`, {
        method: 'POST',
        credentials: 'include',
        signal: AbortSignal.timeout(DEFAULT_TIMEOUT_MS),
      });
      return response.ok;
    } catch {
      return false;
    }
  })();

  clientRefreshInflight.set(host, attempt);
  void attempt.finally(() => clientRefreshInflight.delete(host));
  return attempt;
}

/** Auth endpoints never trigger the 401 → refresh → retry loop (retrying login/refresh is meaningless). */
function isAuthPath(path: string): boolean {
  return path.replace(/^\/+/, '').startsWith('api/v1/auth/');
}

async function apiFetchCore<TValue>(
  host: ApiHost,
  path: string,
  options: ApiFetchOptions,
  allowAuthRetry: boolean,
): Promise<Result<TValue, ApiError>> {
  const baseUrl = resolveBaseUrl(host);
  // Belt-and-braces: helper modules pass paths starting with "/" (and
  // a Copilot reviewer would catch one that doesn't), but a missing
  // slash on either side would silently glue host + path into
  // `http://localhost:5001api/v1/...`. Strip trailing slashes off the
  // base and leading slashes off the path, then join with exactly one.
  // T-0036 Copilot review.
  const url = path.startsWith('http')
    ? path
    : `${baseUrl.replace(/\/+$/, '')}/${path.replace(/^\/+/, '')}`;

  const headers: Record<string, string> = {
    Accept: 'application/json',
    ...(options.headers ?? {}),
  };

  let body = options.body;
  if (options.json !== undefined) {
    body = JSON.stringify(options.json);
    headers['Content-Type'] ??= 'application/json';
  }

  if (options.accessToken) {
    headers['Authorization'] = `Bearer ${options.accessToken}`;
  }

  // SSR cookie forwarding (T-0049 review B1).
  //
  // `credentials: 'include'` below tells the browser to ride along with
  // the cookie jar on cross-origin requests, but Server Components run
  // on the Node runtime and have no implicit cookie jar — they need the
  // session cookie attached explicitly via the `Cookie` request header.
  // Read the audience-scoped cookies (`makables_access_<host>` +
  // `makables_refresh_<host>`) from `next/headers` and forward only
  // those that belong to this host's audience, so a server render on
  // /dashboard/maker/* doesn't also leak the customer session cookie to
  // the Maker host.
  //
  // Public host stays anonymous — never forwarded. A caller-supplied
  // Cookie header wins (test rigs / hand-rolled callers stay in
  // control). The casing-agnostic check matters because HTTP header
  // names are case-insensitive but `Record<string, string>` is not;
  // sending both `cookie` and `Cookie` is RFC 6265 ill-defined and
  // some servers will reject or silently merge them.
  // T-0049 Copilot review M1.
  const callerSetCookie = Object.keys(headers).some((h) => h.toLowerCase() === 'cookie');
  if (host !== 'public' && typeof window === 'undefined' && !callerSetCookie) {
    const cookieHeader = await readAudienceCookieHeader(host);
    if (cookieHeader) {
      headers['Cookie'] = cookieHeader;
    }
  }

  // Compose any caller-supplied signal with the timeout budget so the
  // request aborts on whichever fires first (T-0015 reviewer M1). The
  // budget defaults to 8 s; uploads opt into a longer one via
  // `timeoutMs` (checkout-flow review B-1). A fresh timeout signal is
  // minted per attempt so a retry isn't handed an already-elapsed budget.
  const timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  const method = (options.method ?? 'GET').toUpperCase();
  const retryable = options.retryOnTransient ?? IDEMPOTENT_METHODS.has(method);
  const deadline = Date.now() + timeoutMs + (retryable ? DEFAULT_TIMEOUT_MS : 0);

  let response: Response | undefined;
  let lastNetworkError: ApiError | undefined;

  for (let attempt = 0; attempt < (retryable ? MAX_TRANSIENT_ATTEMPTS : 1); attempt++) {
    if (attempt > 0) {
      // The caller pulled the plug (component unmounted, navigation) —
      // never burn a retry on an abort we were told to honour.
      if (options.signal?.aborted) break;
      // Index is always in range (attempt < MAX_TRANSIENT_ATTEMPTS); the
      // `?? 0` satisfies the compiler without an unsafe assertion.
      await delay(RETRY_BACKOFF_MS[attempt - 1] ?? 0);
    }

    const remaining = attempt === 0 ? timeoutMs : Math.min(timeoutMs, deadline - Date.now());
    if (remaining <= 0) break;

    const timeoutSignal = AbortSignal.timeout(remaining);
    const signal = options.signal
      ? AbortSignal.any([options.signal, timeoutSignal])
      : timeoutSignal;

    try {
      response = await fetch(url, {
        ...options,
        // Include cookies on cross-origin requests so the audience-scoped
        // session cookies (set by the .NET AuthController per ADR 0012)
        // ride along. T-0035 cq reviewer m5: this overrides the spread so
        // a caller passing `credentials: undefined` still gets 'include',
        // and an explicit 'omit'/'same-origin' is honoured.
        credentials: options.credentials ?? 'include',
        body,
        headers,
        signal,
      });
    } catch (cause) {
      response = undefined;
      lastNetworkError = transientError(cause);
      // A caller-driven abort is a deliberate cancellation, not a blip.
      if (options.signal?.aborted) break;
      continue;
    }

    if (!TRANSIENT_STATUSES.has(response.status)) break;
  }

  if (!response) {
    return err(lastNetworkError ?? transientError(new Error('fetch produced no response')));
  }

  const correlationId = response.headers.get('x-correlation-id') ?? undefined;

  // 401 → refresh → retry-once (T-0154), browser only: the 15-minute
  // access cookie can expire under a long-lived page (order-detail
  // message polling is the platform's only polling surface); one
  // refresh + one retry recovers it. Server renders don't need this —
  // the edge middleware refreshes the cookie before the render, and a
  // fetch here couldn't persist rotated cookies anyway. Public host has
  // no session; auth endpoints are excluded to prevent loops.
  if (
    response.status === 401 &&
    allowAuthRetry &&
    typeof window !== 'undefined' &&
    host !== 'public' &&
    !isAuthPath(path)
  ) {
    const refreshed = await refreshClientSession(host);
    if (refreshed) {
      return apiFetchCore<TValue>(host, path, options, /* allowAuthRetry */ false);
    }
  }

  if (response.ok) {
    if (response.status === 204) {
      return ok(undefined as TValue);
    }
    if (options.parse === 'blob') {
      const blob = (await response.blob()) as unknown as TValue;
      return ok(blob);
    }
    const contentType = response.headers.get('content-type') ?? '';
    if (contentType.includes('application/json')) {
      const value = (await response.json()) as TValue;
      return ok(value);
    }
    const text = (await response.text()) as unknown as TValue;
    return ok(text);
  }

  return err(await parseErrorResponse(response, correlationId));
}

interface BackendValidationDetail {
  readonly field?: string;
  readonly code?: string;
  readonly message?: string;
}

async function parseErrorResponse(
  response: Response,
  correlationId: string | undefined,
): Promise<ApiError> {
  const contentType = response.headers.get('content-type') ?? '';
  // ASP.NET's ProblemDetails / ValidationProblemDetails responses use
  // `application/problem+json` per RFC 7807. Without this branch the
  // model-binding 400s (and any other framework-level errors that
  // bypass our pipeline) would fall through to the text fallback and
  // we'd lose the `title` / `detail` fields. T-0049 Copilot review M1.
  if (contentType.includes('application/json') || contentType.includes('application/problem+json')) {
    try {
      // The wrapper accepts both Makables-native `Error` (field + code +
      // type + details) and RFC 7807 ProblemDetails (title + detail).
      // The native shape always wins when present; ProblemDetails covers
      // ASP.NET framework errors (model-binding, 404, etc.) that bypass
      // our pipeline.
      //
      // The backend ships validation errors in two flavours:
      //   1. Multi-field via FluentValidation → `details:
      //      IReadOnlyList<ValidationDetail>` (one row per failing field;
      //      the top-level `field`/`code` mirror the first row).
      //   2. Single-field via `Error.Validation(field, code)` → the top-
      //      level `field`/`code` are the entire payload and `details`
      //      is null.
      //
      // We collapse both into `fields: Record<string, readonly string[]>`
      // (display copy) so the form can render inline errors keyed by
      // field name without caring which shape arrived. Field names from
      // FluentValidation are PascalCase; the caller normalises to
      // camelCase if it needs to match its state keys (see
      // product-form.tsx). T-0049 Copilot review H1+M2.
      const payload = (await response.json()) as Partial<{
        code: string;
        message: string;
        field: string;
        type: ErrorType;
        details: unknown;
        title: string;
        detail: string;
      }>;

      const code = payload.code ?? `http.${response.status}`;
      const message = payload.message ?? payload.detail ?? payload.title ?? defaultMessage(response.status);
      const type = payload.type ?? mapStatusToErrorType(response.status);

      return {
        code,
        message,
        type,
        // Pass the RAW backend message — not the substituted-with-
        // defaultMessage version above — so collectValidationFields'
        // pickDisplay can fall through to the code when the backend
        // didn't ship a message. Otherwise the inline field error
        // would read "Server je momentálně nedostupný…" for a vanilla
        // `Error.Validation(field, code)`. T-0049 Copilot review M1.
        fields: collectValidationFields(payload.details, payload.field, code, payload.message, type),
        correlationId,
      };
    } catch {
      // fall through to text branch
    }
  }

  return {
    code: `http.${response.status}`,
    message: defaultMessage(response.status),
    type: mapStatusToErrorType(response.status),
    correlationId,
  };
}

/**
 * Collapse the backend's two validation-error shapes (multi-field
 * <c>details: ValidationDetail[]</c> + single-field top-level
 * <c>field</c>+<c>code</c>) into a unified
 * <c>Record&lt;field, display strings&gt;</c>. Returns <c>undefined</c>
 * when neither shape is present so non-validation paths stay clean
 * (their <c>details</c> can carry opaque transient/permanent
 * payloads).
 *
 * <para>
 * Each entry carries the row's <c>message</c> when present, falling
 * back to the <c>code</c> — Copilot review M2: the form renders these
 * strings verbatim under inputs, so user-readable text wins over
 * machine keys. A future i18n-aware form can still <c>t()</c> the
 * code; the top-level <c>ApiError.code</c> is always present.
 * </para>
 *
 * <para>
 * The single-field fallback only kicks in for
 * <c>type === 'Validation'</c> when <c>details</c> didn't supply
 * anything — a <c>NotFound</c> with a top-level <c>field</c> is the
 * entity name, not a form input, and shouldn't surface inline.
 * </para>
 */
function collectValidationFields(
  details: unknown,
  topField: string | undefined,
  topCode: string,
  topMessage: string | undefined,
  type: ErrorType,
): Record<string, readonly string[]> | undefined {
  const grouped: Record<string, string[]> = {};
  let matched = 0;

  if (Array.isArray(details) && details.length > 0) {
    for (const raw of details as readonly unknown[]) {
      if (raw === null || typeof raw !== 'object') continue;
      const detail = raw as BackendValidationDetail;
      if (typeof detail.field !== 'string') continue;
      const text = pickDisplay(detail.message, detail.code);
      if (text === null) continue;
      const bucket = grouped[detail.field] ?? (grouped[detail.field] = []);
      bucket.push(text);
      matched++;
    }
  }

  if (matched === 0 && type === 'Validation' && typeof topField === 'string' && topField !== '') {
    const text = pickDisplay(topMessage, topCode);
    if (text !== null) {
      grouped[topField] = [text];
      matched++;
    }
  }

  return matched > 0 ? grouped : undefined;
}

function pickDisplay(message: string | undefined, code: string | undefined): string | null {
  if (typeof message === 'string' && message.trim() !== '') return message;
  if (typeof code === 'string' && code !== '') return code;
  return null;
}

/**
 * Server-only cookie reader for SSR auth forwarding. `cookies()` is
 * dynamic-imported so this module stays consumable from Client
 * Components (which would never call apiFetch directly today, but the
 * import shape stays portable). The audience-scoped pair
 * (`makables_access_<host>` + `makables_refresh_<host>`) is forwarded
 * verbatim; any other cookie in the store stays put so the Maker host
 * doesn't see the Customer session.
 */
async function readAudienceCookieHeader(host: ApiHost): Promise<string | null> {
  try {
    const { cookies } = await import('next/headers');
    const store = await cookies();
    const accessName = `${ACCESS_COOKIE_PREFIX}${host}`;
    const refreshName = `${REFRESH_COOKIE_PREFIX}${host}`;
    const parts: string[] = [];
    const access = store.get(accessName);
    if (access) parts.push(`${accessName}=${access.value}`);
    const refresh = store.get(refreshName);
    if (refresh) parts.push(`${refreshName}=${refresh.value}`);
    return parts.length > 0 ? parts.join('; ') : null;
  } catch {
    // Outside a request scope (e.g. unit tests, build-time prefetch) the
    // import throws — that's fine; the request goes unauthenticated and
    // the backend returns 401 the helper folds to a typed error.
    return null;
  }
}

function transientError(cause: unknown): ApiError {
  const isAbort = cause instanceof DOMException && cause.name === 'AbortError';
  return {
    code: isAbort ? 'network.timeout' : 'network.unreachable',
    message: isAbort
      ? 'Server neodpověděl včas. Zkuste to prosím znovu.'
      : 'Server je momentálně nedostupný. Zkuste to prosím znovu.',
    type: 'Transient',
  };
}

function mapStatusToErrorType(status: number): ErrorType {
  if (status === 400 || status === 422) return 'Validation';
  if (status === 401) return 'Unauthorized';
  if (status === 403) return 'Forbidden';
  if (status === 404) return 'NotFound';
  if (status === 409) return 'Conflict';
  if (status === 429 || status >= 500) return 'Transient';
  return 'Unknown';
}

function defaultMessage(status: number): string {
  switch (status) {
    case 400: return 'Neplatný požadavek.';
    case 401: return 'Pro pokračování se prosím přihlaste.';
    case 403: return 'K této akci nemáte oprávnění.';
    case 404: return 'Požadovaný záznam nebyl nalezen.';
    case 409: return 'Tato akce není v aktuálním stavu povolena.';
    case 422: return 'Některé údaje neprošly validací.';
    case 429: return 'Příliš mnoho požadavků. Zkuste to za chvíli znovu.';
    default:
      return status >= 500
        ? 'Na serveru došlo k neočekávané chybě. Zkuste to prosím znovu.'
        : 'Něco se pokazilo.';
  }
}
