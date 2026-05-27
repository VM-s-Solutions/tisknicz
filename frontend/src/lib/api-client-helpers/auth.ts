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

import { type ApiHost, apiFetch } from '../runtime/api-fetch';
import { type ApiError, type Result, ok } from '../runtime/result';

const Base = 'api/v1/auth';
const PublicBase = 'api/v1/makers';

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
