---
id: T-0046
title: Frontend /katalog page — maker list with category/city/rating filters + URL-driven pagination
status: ready
size: M
owner: frontend
created: 2026-05-30
updated: 2026-05-30
depends_on: [T-0043]
blocks: [T-0131]
user_stories: [US-customer-0007]
adrs: [0005, 0022]
phase: 3
---

# T-0046 — Frontend catalog page

## Context

T-0043 shipped the public catalog read-side: `GET /api/v1/catalog/makers` returns a `PagedData<MakerListItem>` with category/city/min-rating filters and rating-desc → orders-desc sort. This ticket lights up the customer-facing storefront page that consumes it. It is the first public list page; the choices made here (URL-state pagination, debounced submit, empty/error states) are the template the rest of Phase 3 (T-0047 profile, T-0048 product detail) and Phase 4 (order lists) will copy.

## Scope

### Generated client
- Run `npm run generate:api` against the running `Makables.Web.Public` host so `frontend/src/lib/api-client/public-api.v1.ts` is populated with `CatalogController` operations + DTOs (`MakerListItem`, `PagedData<MakerListItem>`). Commit the regenerated file plus the matching hash update in `.spec-hashes.json`.
- If categories aren't on a public endpoint yet, hard-code the 6 launch slugs from T-0040 (`cat-3d-tisk`, `cat-klasicky-tisk`, `cat-potisk-textilu`, `cat-laser-cnc`, `cat-velkoformat`, `cat-handmade`) in a Czech-labelled constant under `frontend/src/lib/catalog/categories.ts` and leave a `// TODO(T-0119): replace with /catalog/categories when admin CRUD ships` comment — no new backend work in this ticket.

### Helper
- `frontend/src/lib/api-client-helpers/catalog.ts` — thin wrapper around the generated `CatalogClient.getMakers` that returns `Result<PagedData<MakerListItem>, ApiError>` via `apiFetch`. Follows the `auth.ts` / `profile.ts` pattern.

### Page
- `frontend/src/app/(public)/katalog/page.tsx` — Server Component. Reads `searchParams` (`category`, `city`, `minRating`, `page`), calls the helper server-side, renders the layout shell (h1, filter sidebar slot, results grid, pagination). 24/page default. Renders empty/error/loading states inline (loading via `loading.tsx`).
- `frontend/src/app/(public)/katalog/filters-client.tsx` — `'use client'`. Form with: category select, city text input (debounced 300ms before push), min-rating radio (1–5 stars + "any"), submit button + "Vymazat filtry" reset. On change, updates URL search params via `useRouter().replace()` so back-button restores state. Submitting resets `page` to 1.
- `frontend/src/app/(public)/katalog/maker-card.tsx` — Server Component card per `MakerListItem`: company name, verified badge, city, rating stars (from `RatingAverageBp / 1000`), order count, bio truncate to 2 lines. Links to `/katalog/{slug}` (T-0047 target).
- `frontend/src/app/(public)/katalog/pagination.tsx` — Server Component. Renders prev/next + page numbers as `<Link>` to same path with updated `?page=`. `HasNext` / `HasPrevious` from `PagedData`.
- `frontend/src/app/(public)/katalog/loading.tsx` — skeleton grid.

### i18n
- Add catalog keys to `frontend/src/lib/i18n/cs-CZ.ts` under `catalog.*`: `title`, `subtitle`, `filter.category`, `filter.city`, `filter.cityPlaceholder`, `filter.minRating`, `filter.minRatingAny`, `filter.apply`, `filter.reset`, `empty.title`, `empty.description`, `error.title`, `error.retry`, `pagination.previous`, `pagination.next`, `pagination.pageOf` (`Stránka {0} z {1}`), `card.verified`, `card.orders`, `card.ratingNone`. Plus category-slug labels (`catalog.category.cat-3d-tisk`, ...).

## Out of scope
- Map-based "makers near you" (post-MVP per US-customer-0007).
- Free-text search.
- Sort options beyond the backend default.
- Categories CRUD endpoint / dynamic category list (T-0119).
- Maker profile page (T-0047) — link target only.
- SEO / sitemap (T-0131).

## Acceptance criteria
- **AC-1** Given the catalog page loads with no query params, when the request succeeds, then up to 24 maker cards render in the order returned by the API (rating desc → orders desc).
- **AC-2** Given the customer picks a category, when the form submits, then the URL gains `?category={slug}` and only matching makers render. Browser back returns to the prior URL state.
- **AC-3** Given the customer types a city and submits, then the URL gains `?city=`. The input is debounced — no fetch fires per keystroke; the URL only updates after 300ms of inactivity or explicit submit.
- **AC-4** Given the result set has more than 24 makers, when the page loads, then prev/next pagination renders. Clicking next pushes `?page=2`, preserves other filters, and the back button returns to page 1 with filters intact.
- **AC-5** Given the filtered set is empty, when the response returns `Items=[]`, then an empty-state card renders with a "Vymazat filtry" reset link.
- **AC-6** Given the backend returns an error, when the page renders, then an error state with a retry link renders. No raw error strings leak to the UI.
- **AC-7** No hardcoded Czech outside `cs-CZ.ts` (brand copy in `common.*` is OK). Lint passes.
- **AC-8** Server Components by default — `'use client'` only on `filters-client.tsx`. No `useEffect` for data fetching. No DB SDK imports. All API calls go through `lib/api-client/` + `lib/api-client-helpers/catalog.ts` + `apiFetch`.
- **AC-9** Generated `public-api.v1.ts` is regenerated and the hash recorded; the pre-commit hook passes.
- **AC-10** Responsive at 375 / 768 / 1280; filter sidebar collapses to a top sheet at <768px.

## Technical notes
- Backend contract is fully in place — see `backend/src/Makables.Core.Domain/Catalog/ICatalogQueries.cs` (`CatalogFilter`, `MakerListItem`, `PagedData<T>`) and `backend/src/Makables.Web.Public/Controllers/CatalogController.cs` (`GET /api/v1/catalog/makers`).
- `RatingAverageBp` is basis-points (0..50000). Convert to 0.0–5.0 stars by dividing by 1000 for display; round to one decimal.
- `PagedData<T>` exposes `Items`, `Page`, `PageSize`, `TotalCount`, `TotalPages`, `HasNext`, `HasPrevious` — use those directly; do not recompute.
- `pageSize` is capped server-side at 48; do not expose a page-size picker yet.
- City filter is partial + case-insensitive on the backend — no client normalisation needed.
- Unknown category slug returns an empty list (not 404) — surfaces as the AC-5 empty state, which is fine.
- Use the `(public)` route group so the page lives outside the auth-gated middleware matcher.

## Files touched (expected)
- `frontend/src/app/(public)/katalog/page.tsx`
- `frontend/src/app/(public)/katalog/filters-client.tsx`
- `frontend/src/app/(public)/katalog/maker-card.tsx`
- `frontend/src/app/(public)/katalog/pagination.tsx`
- `frontend/src/app/(public)/katalog/loading.tsx`
- `frontend/src/lib/api-client-helpers/catalog.ts`
- `frontend/src/lib/catalog/categories.ts`
- `frontend/src/lib/i18n/cs-CZ.ts`
- `frontend/src/lib/api-client/public-api.v1.ts` (regenerated)
- `frontend/src/lib/api-client/.spec-hashes.json` (regenerated)

## Status log
- 2026-05-30 `draft → ready` by PM
- 2026-05-30 done. `npx tsc --noEmit` clean, `npm run lint` clean. Security review CLEAR. Code-quality review BLOCKERs (Czech in `metadata`, dead-code `void`) + Mediums (inline `import()` type, `useEffect` deps escape hatches, breakpoint mismatch, structural cast for `labelKey`) all folded in the same commit.
  - **B1 — `metadata` i18n.** Converted the static `metadata` export to `generateMetadata()` so the title/description resolve via `t('catalog.title')` / `t('catalog.subtitle')` instead of inline Czech.
  - **B2 — dead code.** Dropped `void CATALOG_MAX_PAGE_SIZE;` + the unused `max?` param on `parsePositiveInt` + the orphan import. The contract anchor still lives in `catalog.ts`.
  - **M1.** Hoisted the inline `import('...').MakerListItem` to a top-level `import type`.
  - **M2.** Replaced both `useEffect`s in `filters-client.tsx` (which had `eslint-disable react-hooks/exhaustive-deps`) with direct handlers — selects push immediately on change; city debounces via a `useRef<NodeJS.Timeout>` inside the input's `onChange`. No more lint disables.
  - **M3.** Sidebar grid switched from `lg:` (1024) → `md:` (768) to match the AC-10 mobile-stack breakpoint.
  - **M4.** Typed `CatalogCategoryOption.labelKey: MessageKey` so `t(c.labelKey)` typechecks directly — the structural cast is gone and a stale i18n slug now breaks the build.
  - **M5 deferred.** The Public catalog controller is missing `[ProducesResponseType(typeof(PagedData<MakerListItem>), 200)]`, so the NSwag-generated `PublicApi.makers(...)` returns `Promise<void>`. The hand-written helper at `frontend/src/lib/api-client-helpers/catalog.ts` mirrors the DTOs as a workaround. PM to open a follow-up backend ticket; not blocking T-0046.
