import { type ApiError, type ErrorType, type Result, err, ok } from './result';
import { ACCESS_COOKIE_PREFIX, REFRESH_COOKIE_PREFIX } from '../auth/session';

/**
 * Hostname identifiers for the four .NET Web hosts. The matching base URL
 * comes from `NEXT_PUBLIC_API_<HOST>_BASE_URL` env vars; in development
 * each host has a fixed port per ADR 0005 (Customer=5001, Maker=5002,
 * Admin=5003, Public=5104). Public moved off 5004 in T-0046b because the
 * default collided with an unrelated local project on dev machines —
 * launchSettings, nswag/config.json and this fallback all track 5104.
 */
export type ApiHost = 'customer' | 'maker' | 'admin' | 'public';

const HOST_BASE_URLS: Record<ApiHost, string> = {
  customer: process.env.NEXT_PUBLIC_API_CUSTOMER_BASE_URL ?? 'http://localhost:5001',
  maker: process.env.NEXT_PUBLIC_API_MAKER_BASE_URL ?? 'http://localhost:5002',
  admin: process.env.NEXT_PUBLIC_API_ADMIN_BASE_URL ?? 'http://localhost:5003',
  public: process.env.NEXT_PUBLIC_API_PUBLIC_BASE_URL ?? 'http://localhost:5104',
};

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
   * Provide your own AbortSignal. The wrapper composes it with an 8 s
   * timeout via `AbortSignal.any`, so the request aborts on whichever
   * fires first.
   */
  readonly signal?: AbortSignal;
}

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
  const baseUrl = HOST_BASE_URLS[host];
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

  // Compose any caller-supplied signal with our 8 s timeout so the request
  // aborts on whichever fires first (T-0015 reviewer M1).
  const timeoutSignal = AbortSignal.timeout(8000);
  const signal = options.signal
    ? AbortSignal.any([options.signal, timeoutSignal])
    : timeoutSignal;

  let response: Response;
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
    return err(transientError(cause));
  }

  const correlationId = response.headers.get('x-correlation-id') ?? undefined;

  if (response.ok) {
    if (response.status === 204) {
      return ok(undefined as TValue);
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
  if (contentType.includes('application/json')) {
    try {
      // The wrapper accepts both Makables-native `Error` (field + code +
      // type + details) and RFC 7807 ProblemDetails (title + detail).
      // The native shape always wins when present; ProblemDetails covers
      // ASP.NET framework errors (model-binding, 404, etc.) that bypass
      // our pipeline.
      //
      // Native `details` is normally
      // `IReadOnlyList<ValidationDetail>` (one row per failing field)
      // when `type === 'Validation'`, but it can be `null`, an opaque
      // object, or absent. We flatten it into the typed
      // `fields: Record<string, readonly string[]>` shape that callers
      // (forms in particular) consume, so a form can render inline
      // errors keyed by field name. Field names from FluentValidation
      // are PascalCase; we let the caller normalise to camelCase if it
      // needs to match its state keys (see product-form.tsx). T-0049
      // Copilot review H1.
      const payload = (await response.json()) as Partial<{
        code: string;
        message: string;
        type: ErrorType;
        details: unknown;
        title: string;
        detail: string;
      }>;

      return {
        code: payload.code ?? `http.${response.status}`,
        message: payload.message ?? payload.detail ?? payload.title ?? defaultMessage(response.status),
        type: payload.type ?? mapStatusToErrorType(response.status),
        fields: flattenValidationDetails(payload.details),
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
 * Fold the backend's <c>ValidationDetail[]</c> shape into a
 * map of field-name → ordered error codes. Returns <c>undefined</c>
 * when the payload's <c>details</c> isn't a recognisable validation
 * list — keeps the helper honest for the non-validation paths where
 * <c>details</c> carries opaque transient/permanent payloads.
 */
function flattenValidationDetails(details: unknown): Record<string, readonly string[]> | undefined {
  if (!Array.isArray(details) || details.length === 0) {
    return undefined;
  }
  const grouped: Record<string, string[]> = {};
  let matched = 0;
  for (const raw of details as readonly unknown[]) {
    if (raw === null || typeof raw !== 'object') continue;
    const detail = raw as BackendValidationDetail;
    if (typeof detail.field !== 'string' || typeof detail.code !== 'string') continue;
    const bucket = grouped[detail.field] ?? (grouped[detail.field] = []);
    bucket.push(detail.code);
    matched++;
  }
  return matched > 0 ? grouped : undefined;
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
