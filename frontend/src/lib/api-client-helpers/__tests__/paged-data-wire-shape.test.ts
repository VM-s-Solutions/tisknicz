import { describe, expect, it } from 'vitest';
import { PagedDataOfMakerListItem } from '@/lib/api-client/public-api.v1';
import type { IPagedDataOfAdminMakerListItemDto } from '@/lib/api-client/admin-api.v1';
import type { IPagedDataOfMakerListItem } from '@/lib/api-client/public-api.v1';
import type { AdminMakerPage } from '../admin-makers';
import type { MakerListItem, PagedData } from '../catalog';

/**
 * Wire-shape pin for the paginated-list contract.
 *
 * Most list endpoints are consumed through the NSwag-generated client,
 * but a few surfaces (public catalog, admin maker ops) call `apiFetch`
 * directly against HAND-WRITTEN DTO mirrors. Those mirrors drifted: both
 * declared `hasNext` / `hasPrevious` while `PagedData<T>` in
 * `Makables.Core.Domain.Common` computes `HasNextPage` / `HasPreviousPage`.
 *
 * The drift was invisible to `tsc` — the mirror declared the fields as
 * required `boolean`, so every read typechecked while resolving to
 * `undefined` at runtime. Fallout: catalog and admin-maker prev/next
 * controls rendered permanently disabled (falsy → the disabled `<span>`
 * branch), and `sitemap.ts` broke out of its paging loop after the first
 * page, so only the first 48 makers were ever listed.
 *
 * These assertions are the guard the type system couldn't provide on its
 * own. The `type` assertions below fail the `tsc --noEmit` gate — not
 * just the test run — if either mirror renames a paging field again.
 */

/** Compile-time assertion: `Expect<true>` compiles; `Expect<false>` is a type error. */
type Expect<T extends true> = T;

/** True when every member of `Keys` is a key of `T`. */
type DeclaresAll<T, Keys extends string> = Exclude<Keys, keyof T> extends never ? true : false;

/**
 * The paging field names the backend actually serializes. Sourced from
 * the C# computed properties on `PagedData<T>`; the two booleans carry
 * the `Page` suffix.
 */
type PagingContract = 'totalPages' | 'hasNextPage' | 'hasPreviousPage';

/* eslint-disable @typescript-eslint/no-unused-vars -- compile-time assertions: `tsc` evaluates them at the declaration, nothing reads them at runtime. */

// The contract is what the generated clients declare — if NSwag ever
// emits different names, these break first and the mirrors below follow.
type _GeneratedPublic = Expect<DeclaresAll<IPagedDataOfMakerListItem, PagingContract>>;
type _GeneratedAdmin = Expect<DeclaresAll<IPagedDataOfAdminMakerListItemDto, PagingContract>>;

// ...and the hand-written mirrors must declare the same names.
type _CatalogMirror = Expect<DeclaresAll<PagedData<MakerListItem>, PagingContract>>;
type _AdminMakerMirror = Expect<DeclaresAll<AdminMakerPage, PagingContract>>;

/* eslint-enable @typescript-eslint/no-unused-vars */

describe('PagedData wire shape', () => {
  it('deserializes the paging flags the backend sends', () => {
    const parsed = PagedDataOfMakerListItem.fromJS({
      items: [],
      page: 2,
      pageSize: 24,
      totalCount: 100,
      totalPages: 5,
      hasNextPage: true,
      hasPreviousPage: true,
    });

    expect(parsed.totalPages).toBe(5);
    expect(parsed.hasNextPage).toBe(true);
    expect(parsed.hasPreviousPage).toBe(true);
  });

  it('lets the hand-written catalog mirror read a raw wire payload', () => {
    // Typed as the mirror, populated with the wire's own key names. If
    // the mirror renames a field, this stops compiling.
    const wire: PagedData<MakerListItem> = {
      items: [],
      page: 2,
      pageSize: 24,
      totalCount: 100,
      totalPages: 5,
      hasNextPage: true,
      hasPreviousPage: false,
    };

    expect(wire.hasNextPage).toBe(true);
    expect(wire.hasPreviousPage).toBe(false);
  });

  it('lets the hand-written admin-maker mirror read a raw wire payload', () => {
    const wire: AdminMakerPage = {
      items: [],
      page: 1,
      pageSize: 20,
      totalCount: 40,
      totalPages: 2,
      hasNextPage: true,
      hasPreviousPage: false,
    };

    expect(wire.hasNextPage).toBe(true);
    expect(wire.hasPreviousPage).toBe(false);
  });
});
