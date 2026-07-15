/**
 * Hand-written wrappers around the authenticated admin category
 * endpoints on the .NET Admin host (T-0119 / US-admin-0013). Same
 * convention as <c>admin-ops-client.ts</c> (patterns.md B.16): we call
 * <see cref="apiFetch"/> directly because it returns
 * <c>Result&lt;T, ApiError&gt;</c>, while the NSwag-generated client
 * throws on every non-2xx.
 *
 * All endpoints require an authenticated admin session (audience-scoped
 * cookies; SSR forwards them per patterns.md B.14 / ADR 0024). The
 * profanity screen (<c>category.nameNotAllowed</c>) and the slug-
 * uniqueness gate (<c>category.slugAlreadyExists</c>) are backend
 * business logic — these helpers only post inputs and surface the typed
 * verdict.
 */

import { apiFetch } from '../runtime/api-fetch';
import { type ApiError, type Result, ok } from '../runtime/result';

const Base = '/api/v1/categories';

/** Mirror of <c>AdminCategoryItem</c> — includes deactivated rows. */
export interface AdminCategoryItem {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly icon: string | null;
  readonly description: string | null;
  readonly sortOrder: number;
  readonly countryCode: string;
  readonly isActive: boolean;
}

export interface CreateCategoryInput {
  readonly name: string;
  readonly slug?: string;
  readonly description?: string;
  readonly sortOrder: number;
  readonly countryCode: string;
  readonly notes?: string;
}

export interface UpdateCategoryInput {
  readonly name: string;
  readonly description?: string;
  readonly sortOrder: number;
  readonly notes?: string;
}

export async function getAdminCategories(): Promise<
  Result<{ readonly items: readonly AdminCategoryItem[] }, ApiError>
> {
  return apiFetch<{ readonly items: readonly AdminCategoryItem[] }>('admin', Base, {
    method: 'GET',
  });
}

export async function createCategory(
  input: CreateCategoryInput,
): Promise<Result<{ readonly id: string; readonly slug: string }, ApiError>> {
  return apiFetch<{ readonly id: string; readonly slug: string }>('admin', Base, {
    method: 'POST',
    json: {
      name: input.name,
      slug: input.slug || null,
      icon: null,
      description: input.description || null,
      sortOrder: input.sortOrder,
      countryCode: input.countryCode,
      notes: input.notes || null,
    },
  });
}

export async function updateCategory(
  categoryId: string,
  input: UpdateCategoryInput,
): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>('admin', `${Base}/${encodeURIComponent(categoryId)}`, {
    method: 'PUT',
    json: {
      name: input.name,
      icon: null,
      description: input.description || null,
      sortOrder: input.sortOrder,
      notes: input.notes || null,
    },
  });
  return result.success ? ok(undefined) : result;
}

export async function deactivateCategory(
  categoryId: string,
  notes?: string,
): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(
    'admin',
    `${Base}/${encodeURIComponent(categoryId)}/deactivate`,
    { method: 'POST', json: { notes: notes || null } },
  );
  return result.success ? ok(undefined) : result;
}
