/**
 * Hand-written wrappers around the auth endpoints exposed by the .NET
 * backend's <c>AuthController</c> (per host) and the
 * <c>RegisterMakerController</c> (Public host). Sits alongside the
 * NSwag-generated client per the {@link ../api-client/README.md}
 * contract — manual edits in <c>api-client/</c> are forbidden, hand-
 * written helpers live HERE.
 *
 * Every helper returns a {@link Result<TValue, ApiError>} so call sites
 * can narrow uniformly. Session-issuing endpoints (login, refresh,
 * consume-magic-link) return `Result<void, ApiError>` because the
 * backend sets the access/refresh tokens as HttpOnly cookies — the JS
 * layer never sees the raw tokens.
 *
 * Cookie credentials must accompany every request, hence `credentials:
 * 'include'` via `apiFetch`. The wrapper-level credential mode is
 * inherited from the apiFetch defaults.
 */

import { type ApiHost, apiFetch, apiHostBaseUrl } from '../runtime/api-fetch';
import { type ApiError, type Result, ok } from '../runtime/result';

// Leading slash matters: apiFetch concatenates `${baseUrl}${path}`
// against host URLs that have no trailing slash (e.g. http://localhost:5001),
// so an unrooted "api/v1/auth" would produce http://localhost:5001api/v1/auth.
// T-0036 Copilot review (same fix applied to profile.ts).
const Base = '/api/v1/auth';
const PublicBase = '/api/v1/makers';

// ---- Register (customer) ----

export interface RegisterCustomerInput {
  email: string;
  password: string;
  fullName: string;
  countryCodePrimary: string;
}

export async function registerCustomer(host: ApiHost, input: RegisterCustomerInput): Promise<Result<{ userId: string }, ApiError>> {
  return apiFetch<{ userId: string }>(host, `${Base}/register`, { method: 'POST', json: input });
}

// ---- Login ----

export interface LoginInput {
  email: string;
  password: string;
}

/**
 * On success the backend sets the audience-scoped cookies via
 * `Set-Cookie`. The frontend never reads the access token directly.
 */
export async function login(host: ApiHost, input: LoginInput): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(host, `${Base}/login`, { method: 'POST', json: input });
  return result.success ? ok(undefined) : result;
}

// ---- Logout ----

export async function logout(host: ApiHost): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(host, `${Base}/logout`, { method: 'POST', json: {} });
  return result.success ? ok(undefined) : result;
}

// ---- Refresh ----

export async function refresh(host: ApiHost): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(host, `${Base}/refresh`, { method: 'POST', json: {} });
  return result.success ? ok(undefined) : result;
}

// ---- Confirm email ----

export interface ConfirmEmailInput {
  token: string;
}

export async function confirmEmail(host: ApiHost, input: ConfirmEmailInput): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(host, `${Base}/confirm-email`, { method: 'POST', json: input });
  return result.success ? ok(undefined) : result;
}

// ---- Password reset ----

export interface RequestPasswordResetInput {
  email: string;
}

export async function requestPasswordReset(host: ApiHost, input: RequestPasswordResetInput): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(host, `${Base}/request-password-reset`, { method: 'POST', json: input });
  return result.success ? ok(undefined) : result;
}

export interface ConfirmPasswordResetInput {
  token: string;
  newPassword: string;
}

export async function confirmPasswordReset(host: ApiHost, input: ConfirmPasswordResetInput): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(host, `${Base}/confirm-password-reset`, { method: 'POST', json: input });
  return result.success ? ok(undefined) : result;
}

// ---- Magic link ----

export interface RequestMagicLinkInput {
  email: string;
}

export async function requestMagicLink(host: ApiHost, input: RequestMagicLinkInput): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(host, `${Base}/request-magic-link`, { method: 'POST', json: input });
  return result.success ? ok(undefined) : result;
}

export interface ConsumeMagicLinkInput {
  token: string;
}

export async function consumeMagicLink(host: ApiHost, input: ConsumeMagicLinkInput): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(host, `${Base}/consume-magic-link`, { method: 'POST', json: input });
  return result.success ? ok(undefined) : result;
}

// ---- Google OAuth (T-0026) ----

export interface StartGoogleOAuthOutput {
  authorizationUrl: string;
}

/**
 * Begins the "Sign in with Google" flow. The `redirectUri` bound into the
 * signed state must be the backend's own `google/callback` route — Google
 * GET-redirects the browser straight back to the .NET host, it never
 * passes through a Next.js route — so this helper derives it from the
 * host's configured base URL rather than taking it as a caller parameter.
 * The backend sets the OAuth anti-CSRF cookie on this response and
 * returns the authorization URL to redirect the browser to. Rejected for
 * the admin audience server-side — surface the resulting `ApiError` via
 * the caller's i18n mapping same as any other auth error.
 */
export async function startGoogleOAuth(host: ApiHost): Promise<Result<StartGoogleOAuthOutput, ApiError>> {
  const redirectUri = `${apiHostBaseUrl(host)}/api/v1/auth/google/callback`;
  const params = new URLSearchParams({ redirectUri });
  return apiFetch<StartGoogleOAuthOutput>(host, `${Base}/google/start?${params.toString()}`, { method: 'GET' });
}

// ---- Register maker (Public host only) ----

export interface RegisterMakerInput {
  email: string;
  password: string;
  fullName: string;
  countryCodePrimary: string;
  registrationNumber: string;
}

export interface RegisterMakerOutput {
  userId: string;
  makerId: string;
  snapshotIsStale: boolean;
}

export async function registerMaker(input: RegisterMakerInput): Promise<Result<RegisterMakerOutput, ApiError>> {
  return apiFetch<RegisterMakerOutput>('public', `${PublicBase}/register`, { method: 'POST', json: input });
}

// ---- Company preview by IČO (T-0159, business decision Q4) ----

/** Display slice of the ARES record the registration form shows for confirmation. */
export interface CompanyPreview {
  registrationNumber: string;
  companyName: string;
  legalForm?: string | null;
  vatId?: string | null;
  street: string;
  houseNumber: string;
  city: string;
  zip: string;
  isActiveInRegistry: boolean;
  isStale: boolean;
}

/**
 * Anonymous IČO → company preview (Public host). UX helper only — the
 * registration submit re-runs the authoritative lookup server-side, so
 * a failed preview must never block the form.
 */
export async function lookupCompanyPreview(ico: string): Promise<Result<CompanyPreview, ApiError>> {
  const params = new URLSearchParams({ registrationNumber: ico, countryCode: 'CZ' });
  return apiFetch<CompanyPreview>(
    'public',
    `${PublicBase}/registry-preview?${params.toString()}`,
    { method: 'GET' },
  );
}
