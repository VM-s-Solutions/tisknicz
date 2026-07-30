/**
 * Hand-written wrappers around the authenticated profile endpoints in
 * .NET <c>ProfileController</c> at <c>/api/v1/me/*</c>. Sits alongside
 * the NSwag-generated client per the api-client README; NSwag regen is
 * tracked as a follow-up.
 *
 * All endpoints require an authenticated session. The audience-scoped
 * cookies set by <c>AuthController.login</c> ride along automatically
 * thanks to <c>apiFetch</c>'s default <c>credentials: 'include'</c>.
 */

import { type ApiHost, apiFetch } from '../runtime/api-fetch';
import { type ApiError, type Result, ok } from '../runtime/result';

// Leading slash matters: apiFetch concatenates `${baseUrl}${path}`
// against host URLs that have no trailing slash (e.g. http://localhost:5001),
// so an unrooted "api/v1/me" would produce http://localhost:5001api/v1/me.
// T-0036 Copilot review.
const Base = '/api/v1/me';

// ---- User profile ----

export type UserRole = 'Customer' | 'Maker' | 'Admin';

export interface MyProfile {
  userId: string;
  email: string;
  fullName: string;
  phone: string | null;
  countryCodePrimary: string;
  role: UserRole;
  emailConfirmed: boolean;
  preferredLanguage: string | null;
}

export async function getMyProfile(host: ApiHost): Promise<Result<MyProfile, ApiError>> {
  return apiFetch<MyProfile>(host, Base, { method: 'GET' });
}

export interface UpdateProfileInput {
  fullName: string;
  phone: string | null;
}

export async function updateMyProfile(host: ApiHost, input: UpdateProfileInput): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(host, Base, { method: 'PUT', json: input });
  return result.success ? ok(undefined) : result;
}

export interface ChangePasswordInput {
  currentPassword: string;
  newPassword: string;
}

export async function changePassword(host: ApiHost, input: ChangePasswordInput): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(host, `${Base}/change-password`, { method: 'POST', json: input });
  return result.success ? ok(undefined) : result;
}

export interface DeleteMyAccountInput {
  confirmedEmail: string;
}

/**
 * Self-service GDPR account deletion (soft delete + logout-all). The
 * backend requires the caller to retype their own email; on success it
 * clears the session cookies, so the caller must be redirected out of
 * the authenticated area immediately.
 */
export async function deleteMyAccount(host: ApiHost, input: DeleteMyAccountInput): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(host, `${Base}/delete`, { method: 'POST', json: input });
  return result.success ? ok(undefined) : result;
}

// ---- Maker profile ----

export interface MyMakerProfile {
  makerId: string;
  registrationNumber: string;
  vatId: string | null;
  companyName: string;
  legalForm: string | null;
  registeredAddressId: string;
  isActiveInRegistry: boolean;
  isVerified: boolean;
  snapshotIsStale: boolean;
  snapshotFetchedAt: string;
  bio: string | null;
  bankAccount: string | null;
  personalPickupEnabled: boolean;
  pickupNote: string | null;
}

export async function getMyMakerProfile(host: ApiHost): Promise<Result<MyMakerProfile, ApiError>> {
  return apiFetch<MyMakerProfile>(host, `${Base}/maker`, { method: 'GET' });
}

export interface UpdateMakerProfileInput {
  bio: string | null;
  bankAccount: string | null;
  personalPickupEnabled: boolean | null;
  pickupNote: string | null;
}

export async function updateMyMakerProfile(host: ApiHost, input: UpdateMakerProfileInput): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(host, `${Base}/maker`, { method: 'PUT', json: input });
  return result.success ? ok(undefined) : result;
}
