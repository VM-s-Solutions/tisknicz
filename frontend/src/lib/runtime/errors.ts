/**
 * Shared ApiError → display-copy resolution (patterns.md B.5).
 *
 * Backend `BusinessErrorMessage` codes have 1:1 i18n keys in
 * `lib/i18n/cs-CZ.ts`; when an error's code is present in the catalog
 * we render `t(code)`. Codes without a catalog entry (framework
 * `http.*` codes, `network.*` from the fetch wrapper, future codes that
 * haven't landed in the catalog yet) fall back to the generic
 * per-`ErrorType` copy — never the raw `error.message`, so no backend
 * string leaks to the UI (T-0084a AC-10).
 */

import { type MessageKey, messages, t } from '../i18n';
import type { ApiError, ErrorType } from './result';

/** Narrow an arbitrary backend error code to a known catalog key. */
export function isMessageKey(code: string): code is MessageKey {
  return Object.prototype.hasOwnProperty.call(messages, code);
}

const TYPE_FALLBACK_KEY: Record<ErrorType, MessageKey> = {
  Validation: 'error.validation',
  Unauthorized: 'error.unauthorized',
  Forbidden: 'error.forbidden',
  NotFound: 'error.not_found',
  Conflict: 'error.conflict',
  Transient: 'error.transient',
  Permanent: 'error.permanent',
  Configuration: 'error.configuration',
  Unknown: 'error.unknown',
};

/**
 * Resolve the user-facing Czech message for an {@link ApiError}:
 * catalog entry for the code when one exists, otherwise the generic
 * copy for the error's type.
 */
export function resolveErrorMessage(error: ApiError): string {
  return isMessageKey(error.code) ? t(error.code) : t(TYPE_FALLBACK_KEY[error.type]);
}
