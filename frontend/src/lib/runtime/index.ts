/**
 * `lib/runtime/` — cross-cutting helpers that every page and Server Action
 * uses. Kept dependency-free so it can be imported from both Server and
 * Client components. Per ADR 0022.
 */
export {
  type ApiError,
  type ErrorType,
  type Result,
  ok,
  err,
  isOk,
  isErr,
} from './result';

export {
  type ApiHost,
  type ApiFetchOptions,
  apiFetch,
} from './api-fetch';
