/**
 * TypeScript counterpart of `Makables.Core.Domain.Common.BusinessResult<T>`
 * and the `ErrorType` enum.
 *
 * Every call into the .NET backend through the generated NSwag clients
 * eventually surfaces through {@link apiFetch}, which translates HTTP
 * responses into a `Result<T, ApiError>`. Consumers then narrow with
 * `.success` and either render the value or the user-facing error.
 *
 * Keeping the shape symmetric with the backend means: the same business
 * error code drives the same UI behaviour, and discriminated narrowing
 * works without casts.
 */

export type ErrorType =
  | 'Validation'
  | 'Unauthorized'
  | 'Forbidden'
  | 'NotFound'
  | 'Conflict'
  | 'Transient'
  | 'Permanent'
  | 'Configuration'
  | 'Unknown';

export interface ApiError {
  /** Stable machine-readable error code (e.g. `order.maker_not_verified`). */
  readonly code: string;
  /** Human-readable Czech message safe to display verbatim. */
  readonly message: string;
  /** Coarse category — drives status code mapping and i18n fallback. */
  readonly type: ErrorType;
  /**
   * Optional field-level details for validation errors. Keyed by field
   * name (PascalCase from FluentValidation; the form normalises to
   * camelCase). Each entry carries the row's <b>display</b> string —
   * the backend's <c>message</c> when present, falling back to the
   * <c>code</c>. The form renders these verbatim under the matching
   * input; the top-level <c>code</c> stays available for i18n-aware
   * consumers that prefer machine keys. (T-0049 Copilot review.)
   */
  readonly fields?: Readonly<Record<string, readonly string[]>>;
  /** Backend-issued correlation id (echoed via `x-correlation-id` response header). */
  readonly correlationId?: string;
}

export type Result<TValue, TError = ApiError> =
  | { readonly success: true; readonly value: TValue }
  | { readonly success: false; readonly error: TError };

export function ok<TValue, TError = ApiError>(value: TValue): Result<TValue, TError> {
  return { success: true, value };
}

export function err<TValue, TError = ApiError>(error: TError): Result<TValue, TError> {
  return { success: false, error };
}

/**
 * Narrowing helper for `.map`-style chains.
 *
 * @example
 *   const result = await fetchOrder(id);
 *   if (isOk(result)) return result.value.maker;
 */
export function isOk<TValue, TError>(
  result: Result<TValue, TError>,
): result is { success: true; value: TValue } {
  return result.success;
}

export function isErr<TValue, TError>(
  result: Result<TValue, TError>,
): result is { success: false; error: TError } {
  return !result.success;
}
