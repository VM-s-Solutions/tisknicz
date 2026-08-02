/**
 * Hand-written wrappers around the authenticated admin maker endpoints
 * on the .NET Admin host (T-0119b — list/detail reads + the T-0034
 * judgment-call actions verify / deactivate / refresh-ARES; the T-0140
 * fee override lives in <c>admin-ops-client.ts</c>). Same convention as
 * <c>admin-categories.ts</c> (patterns.md B.16): <see cref="apiFetch"/>
 * directly for the <c>Result&lt;T, ApiError&gt;</c> flow.
 *
 * All endpoints require an authenticated admin session (audience-scoped
 * cookies; SSR forwards them per patterns.md B.14 / ADR 0024). The
 * detail read is PII-audited server-side (T-0137 `maker.detail.view`).
 */

import { apiFetch } from '../runtime/api-fetch';
import { type ApiError, type Result, ok } from '../runtime/result';

const Base = '/api/v1/makers';

/** Mirror of <c>AdminMakerListItemDto</c> — includes deactivated makers. */
export interface AdminMakerListItem {
  readonly makerId: string;
  readonly companyName: string;
  readonly registrationNumber: string;
  readonly city: string;
  readonly userEmail: string;
  readonly isVerified: boolean;
  readonly isActive: boolean;
  readonly feeRateOverrideBp: number | null;
  readonly ratingAverageBp: number;
  readonly totalOrders: number;
  readonly createdAt: string;
}

/** Mirror of <c>AdminMakerDetailDto</c>. */
export interface AdminMakerDetail {
  readonly makerId: string;
  readonly userId: string;
  readonly userEmail: string;
  readonly companyName: string;
  readonly registrationNumber: string;
  readonly vatId: string | null;
  readonly legalForm: string | null;
  readonly slug: string;
  readonly city: string;
  readonly isVerified: boolean;
  readonly isActive: boolean;
  readonly isActiveInRegistry: boolean;
  readonly snapshotIsStale: boolean;
  readonly snapshotFetchedAt: string;
  readonly feeRateOverrideBp: number | null;
  readonly ratingAverageBp: number;
  readonly ratingCount: number;
  readonly totalOrders: number;
  readonly personalPickupEnabled: boolean;
  readonly isRetainedForLegal: boolean;
  readonly createdAt: string;
}

/**
 * Mirror of <c>PagedData&lt;AdminMakerListItemDto&gt;</c>. The two
 * booleans carry the <c>Page</c> suffix because that is the wire name of
 * the C# computed properties (<c>HasNextPage</c> / <c>HasPreviousPage</c>) —
 * declaring them without it types as <c>boolean</c> but resolves to
 * <c>undefined</c>, silently disabling the prev/next controls.
 */
export interface AdminMakerPage {
  readonly items: readonly AdminMakerListItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
  readonly hasNextPage: boolean;
  readonly hasPreviousPage: boolean;
}

export const ADMIN_MAKERS_DEFAULT_PAGE_SIZE = 20;

export async function getAdminMakers(input: {
  readonly page?: number;
  readonly search?: string;
  readonly isVerified?: boolean;
}): Promise<Result<{ readonly makers: AdminMakerPage }, ApiError>> {
  const params = new URLSearchParams();
  params.set('page', String(input.page ?? 1));
  params.set('pageSize', String(ADMIN_MAKERS_DEFAULT_PAGE_SIZE));
  if (input.search) params.set('search', input.search);
  if (input.isVerified !== undefined) params.set('isVerified', String(input.isVerified));
  return apiFetch<{ readonly makers: AdminMakerPage }>('admin', `${Base}?${params.toString()}`, {
    method: 'GET',
  });
}

export async function getAdminMakerDetail(
  makerId: string,
): Promise<Result<{ readonly maker: AdminMakerDetail }, ApiError>> {
  return apiFetch<{ readonly maker: AdminMakerDetail }>(
    'admin',
    `${Base}/${encodeURIComponent(makerId)}`,
    { method: 'GET' },
  );
}

async function postAction(makerId: string, action: string, notes?: string): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(
    'admin',
    `${Base}/${encodeURIComponent(makerId)}/${action}`,
    { method: 'POST', json: { notes: notes || null } },
  );
  return result.success ? ok(undefined) : result;
}

export async function verifyMaker(makerId: string, notes?: string): Promise<Result<void, ApiError>> {
  return postAction(makerId, 'verify', notes);
}

export async function deactivateMaker(makerId: string, notes?: string): Promise<Result<void, ApiError>> {
  return postAction(makerId, 'deactivate', notes);
}

export async function refreshMakerFromAres(
  makerId: string,
  notes?: string,
): Promise<Result<{ readonly snapshotIsStale: boolean }, ApiError>> {
  return apiFetch<{ readonly snapshotIsStale: boolean }>(
    'admin',
    `${Base}/${encodeURIComponent(makerId)}/refresh-ares`,
    { method: 'POST', json: { notes: notes || null } },
  );
}
