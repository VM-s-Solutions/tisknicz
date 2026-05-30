---
id: T-0047
title: Frontend /katalog/[slug] maker profile page
status: ready
size: M
owner: frontend
created: 2026-05-30
updated: 2026-05-30
depends_on: [T-0044, T-0046, T-0046b]
blocks: [T-0048]
user_stories: [US-customer-0008]
adrs: [0005, 0022]
phase: 3
---

# T-0047 — Frontend /katalog/[slug] maker profile page

## Context

The maker profile is the second step in the customer funnel: catalog list (T-0046) → **maker profile (this)** → product detail (T-0048) → order placement. It is the page that decides whether a customer trusts a maker enough to click through to a product. The backend `MakerProfile` contract is shipped and typed (T-0044, T-0046b); this ticket is pure presentation.

## Scope

- Server Component shell at `frontend/src/app/(public)/katalog/[slug]/page.tsx`. Next.js 16 — `params` is async (`await props.params`).
- Sibling `loading.tsx` for the suspense boundary.
- Profile header card: company name, verified badge if `IsVerified`, city, rating (stars rendered from `RatingAverageBp ÷ 1000`, 1-decimal display) + `RatingCount` + `TotalOrders`, bio paragraph, legal form, personal-pickup pill if `PersonalPickupEnabled`.
- Pickup-note section: render only if `PersonalPickupEnabled && PickupNote`. Plain text (let JSX escape; no `dangerouslySetInnerHTML`).
- Products grid: new `product-card.tsx` Server Component, keyed by `ProductId`, each wrapping a `next/link` to `/produkt/{productId}`. Card shows the primary image (`next/image` with explicit `width`/`height`), `Title`, formatted price.
- Reviews section: heading + empty-state copy. Forward-compatible for T-0050; render the heading even when `Reviews` is empty.
- Empty-products state: dedicated i18n empty state when `Products.length === 0`.
- 404 handling: page calls `notFound()` from `next/navigation` when the helper returns an `ApiError` of type `not_found`.
- `generateMetadata(props)` returning `{ title: companyName, description: bio?.slice(0,160) ?? <i18n fallback> }`.
- Responsive: products grid is `grid-cols-1 sm:grid-cols-2 lg:grid-cols-3`; header is single-column at every breakpoint.
- Extend `frontend/src/lib/api-client-helpers/catalog.ts` (created by T-0046) with `getMakerBySlug(slug): Promise<Result<MakerProfile, ApiError>>`. Generated client method is `PublicApi.makers2(slug)` (NSwag rename — see Technical notes).
- Add `frontend/src/lib/money/formatter.ts` if T-0046 hasn't already. Functions: `formatCzkPrice(amountMinor, currency, priceType)` returning `"1 234 Kč"` / `"od 1 234 Kč"` / i18n key for `OnRequest`. CZK strips haléře (integer division by 100 for display). Space thousands separator. Display-only — no business logic.
- All copy via i18n keys under `catalog.maker.*` and `catalog.product.*` (new keys, additive to `cs-CZ.ts`).

## Out of scope

- Reviews list rendering (T-0050 ships the data).
- Direct messaging from the profile (escrow model — only post-order, US-customer-0008 "Out of scope").
- "Follow this maker" / favorites.
- Pagination of the products grid — `MakerProfile.Products` is unpaginated by design (active products of one maker is a small set).
- Map / coordinates display.
- Product detail page (T-0048 — the `<Link>` href will 404 gracefully until T-0048 ships).

## Acceptance criteria

- **AC-1** Given a customer visits `/katalog/<active-maker-slug>`, when the page loads, then they see the company name, verified badge (if applicable), city, rating average (1 decimal) + count + total orders, bio, legal form, and the products grid. (US-customer-0008 AC-1)
- **AC-2** Given the maker has `PersonalPickupEnabled = true` and a non-empty `PickupNote`, when the page loads, then the pickup-note section renders as plain text (no HTML injection — JSX escaping). (US-customer-0008 AC-2)
- **AC-3** Given the backend returns 404 (inactive maker, unconfirmed email, or unknown slug), when the page renders, then the helper returns an `ApiError` of type `not_found` and the page calls `notFound()` from `next/navigation`. (US-customer-0008 AC-4)
- **AC-4** Given `MakerProfile.Products` is non-empty, when the grid renders, then each product card uses `next/image` with explicit `width` and `height` (no layout shift), src pointing to the Public-host image endpoint `/api/v1/files/products/{country}/{productId}/{filename}` derived from `PrimaryImageBlobPath`, alt text = product title.
- **AC-5** Given a product has `PriceType = "Fixed"`, the card displays `"1 234 Kč"`. Given `"From"`, displays `"od 1 234 Kč"`. Given `"OnRequest"`, displays the i18n string for "Na poptávku". Formatter lives in `frontend/src/lib/money/formatter.ts` (created here only if T-0046 didn't already create it).
- **AC-6** Given `MakerProfile.Products` is empty, when the grid would render, then an i18n empty-state block renders in its place (not an empty grid).
- **AC-7** Given `MakerProfile.Reviews` is empty (always, until T-0050), when the page loads, then a reviews section heading + i18n "no reviews yet" message renders. Forward-compatible: when reviews ship, only the body of the section changes.
- **AC-8** `generateMetadata(props)` returns the company name as `title` and the first 160 chars of `bio` (or an i18n fallback when bio is null/empty) as `description`. No hardcoded Czech in the metadata path.
- **AC-9** Layout responsive at 375 / 768 / 1280: products grid is 1 / 2 / 3 columns respectively; the header is single-column at every breakpoint.
- **AC-10** All user-facing copy comes from `lib/i18n/cs-CZ.ts`. No hardcoded Czech strings in the page, card, or helper. No `useEffect` for data fetching. No DB SDK imports. All API access through `lib/api-client-helpers/catalog.ts` + `apiFetch`.

## Technical notes

- Endpoint: `GET /api/v1/catalog/makers/{slug}` on the Public host (anonymous).
- Generated client method: `PublicApi.makers2(slug)` returns `Promise<MakerProfile>` and **throws** on 404. NSwag renamed the second `makers/*` GET to `makers2` because the first one is the listing — do not "fix" this name. The helper wraps the throw in `Result<T, ApiError>` so the page handles 404 via `notFound()`, not via `try/catch` in JSX.
- DTOs (do not redeclare; import from generated client via `lib/api-client/public-api.v1.ts`):
  - `MakerProfile` — `MakerId, Slug, CompanyName, Bio, LegalForm, City, IsVerified, PersonalPickupEnabled, PickupNote, RatingAverageBp, RatingCount, TotalOrders, Products, Reviews`.
  - `MakerProductItem` — `ProductId, Title, PriceAmountMinor, PriceCurrency, PriceType, PrimaryImageBlobPath`.
  - `MakerReviewItem` — `ReviewId, RatingStars, Comment, CreatedAt`.
- Image URL construction: `PrimaryImageBlobPath` is the storage-relative path (e.g. `cz/products/<id>/<filename>`). Prefix with the Public host base URL + `/api/v1/files/products/` for `next/image`. See `Makables.Web.Public/Controllers/ProductImageController.cs` for the route shape — `{country}/{productId}/{filename}` segments must already be in the blob path.
- Rating: `RatingAverageBp` is basis points (×1000). Display as `(RatingAverageBp / 1000).toFixed(1)`. Render star glyphs purely from the display value — no business logic, just a presentational `<Stars value={x} />` Server Component.
- Price formatting (frontend-side, display only): integer-divide `PriceAmountMinor` by 100, format with a space thousands separator, append ` Kč`. Half-up is irrelevant — this is display-side only, the backend already snapshotted the price.
- 404 handling lives in the **page**, not in the helper. The helper returns `Result<MakerProfile, ApiError>`; the page inspects `error.type === 'not_found'` and calls `notFound()`. This keeps the helper reusable for non-page contexts (e.g. a future preview pane).
- Do not edit `lib/api-client/public-api.v1.ts`. Pre-commit hook will block it. If the generated method shape is wrong, raise a ticket for NSwag regen — do not patch by hand.
- No client components unless interaction demands it. The whole page should be reachable without JS.

## Files touched (expected)

- `frontend/src/app/(public)/katalog/[slug]/page.tsx` (new)
- `frontend/src/app/(public)/katalog/[slug]/loading.tsx` (new)
- `frontend/src/app/(public)/katalog/[slug]/product-card.tsx` (new — Server Component)
- `frontend/src/app/(public)/katalog/[slug]/stars.tsx` (new — Server Component) *(optional; could inline)*
- `frontend/src/lib/api-client-helpers/catalog.ts` (extend — add `getMakerBySlug`)
- `frontend/src/lib/money/formatter.ts` (new if T-0046 didn't create it)
- `frontend/src/lib/i18n/cs-CZ.ts` (extend with `catalog.maker.*` + `catalog.product.*` keys)

## Status log

- 2026-05-30 `draft → ready` by PM. Backend (T-0044) and generated client (T-0046b) merged; T-0046 in flight but parallelizable (this ticket extends a helper file the other ticket creates — merge sequencing handled by the frontend agent).
- 2026-05-30 done. `npx tsc --noEmit` + `npm run lint` clean. Security review CLEAR (no exploit path; informational note that `next/image` host-anchoring + JSX escaping cover the user-controlled DB strings + the image URL). Code-quality review CLEAR (no BLOCKERs, no Mediums; DTO mirror in `catalog.ts` matches the C# records exactly; CZK formatter produces `1 234 Kč`; `notFound()` honored on `'NotFound'`).
  - **Rebase reconciliation.** T-0047 was authored against a tree that assumed T-0046 hadn't merged. After T-0046 landed first, the helper file (`lib/api-client-helpers/catalog.ts`) was reconciled by hand: kept T-0046's hand-mirrored DTO convention (matches `profile.ts`) and added T-0047's new functions (`getMakerBySlug`, `buildProductImageUrl`) + DTO types (`MakerProductItem`, `MakerReviewItem`, `MakerProfile`) on top. T-0047's `reviews-section.tsx` was treating `createdAt` as a `Date`; switched to `new Date(review.createdAt).toLocaleDateString('cs-CZ')` because the JSON wire shape is a string.
  - **Review nit #1 folded.** `generateMetadata` was returning the "Výrobce nenalezen" title for transient backend errors too — misleading for SEO. Now only branches the title on `error.type === 'NotFound'`; transient errors fall back to the bare brand title.
  - **Review nit #3 folded (defense-in-depth).** `buildProductImageUrl` now rejects blob paths containing `..` segments. Non-exploitable (next/image anchors on `remotePatterns.hostname`), but better to refuse a suspicious blob path than emit a URL the optimizer will normalize to a same-host 404.
  - **Deferred follow-up.** Duplicate `verified` i18n key (`catalog.card.verified` from T-0046 + `catalog.maker.verified` from T-0047) — informational only; consolidate when convenient.
