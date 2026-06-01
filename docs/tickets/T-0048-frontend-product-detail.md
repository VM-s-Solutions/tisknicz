---
id: T-0048
title: Frontend /produkt/[productId] product detail page
status: ready
size: M
owner: frontend
created: 2026-05-31
updated: 2026-05-31
depends_on: [T-0045, T-0047]
blocks: []
user_stories: [US-customer-0009]
adrs: [0005, 0022]
phase: 3
---

# T-0048 — Frontend /produkt/[productId] product detail page

## Context

Third step in the customer funnel: catalog list (T-0046) → maker profile (T-0047) → **product detail (this)** → order placement (T-0084). The customer clicked a `ProductCard` and now needs enough information — images, description, weight, price-type variant, by-maker link — to decide whether to place an order. The backend `ProductDetail` contract is shipped and typed (T-0045); this ticket is pure presentation plus one interactive image gallery.

## Scope

- Server Component shell at `frontend/src/app/(public)/produkt/[productId]/page.tsx`. Next.js 16 — `params` is async (`const { productId } = await props.params`).
- Sibling `loading.tsx` (skeleton: image placeholder, title/price shimmer, description lines).
- Sibling `not-found.tsx` (Czech "produkt nenalezen" + link back to `/katalog`). Matches T-0047 pattern.
- One Client Component `product-gallery.tsx` — takes `images: readonly ProductImageItem[]` + `title` (used for the primary `<Image alt>`), renders the primary image as `next/image` with explicit `width`/`height`, and thumbnails below. Clicking a thumbnail swaps the primary view via local `useState`. Keyboard-accessible: thumbnails are `<button type="button">` with `aria-label` per i18n. The Server Component shell stays presentational and passes the already-sorted image list.
- Title block: product `Title` (H1), price formatted per variant:
  - `Fixed` → `formatCzk(priceAmountMinor, priceCurrency)` → `"1 234 Kč"`
  - `From` → i18n `catalog.product.price.from` with `{price: formatCzk(...)}` → `"od 1 234 Kč"`
  - `OnRequest` → i18n `catalog.product.price.on_request` → `"Na poptávku"`
  - Non-CZK fallback: route to "Na poptávku" copy at the card boundary (same as T-0047 `ProductPrice`); never call `formatCzk` on a non-CZK currency.
- By-maker link: company name + verified badge if `makerIsVerified`, linked to `/katalog/{makerSlug}`. Reuse the verified-badge visual from T-0047.
- Description: render as plain text. Backend stores plaintext per T-0041 — no HTML, no `dangerouslySetInnerHTML`. Preserve newlines via Tailwind `whitespace-pre-line`.
- Weight: display-only helper `formatWeight(grams)`: `≥1000` → `"1,5 kg"` (one decimal, Czech comma via `Intl.NumberFormat('cs-CZ')`); `<1000` → `"650 g"` (integer). Lives in this ticket (small enough to inline; could move to `lib/format/weight.ts` if the agent prefers).
- CTA placeholder: "Objednat" button linking to `/objednavka?productId={productId}`. This route 404s until T-0084 ships — same forward-compat pattern T-0046/T-0047 used. Do not add client-side disabling; the link is honest.
- 404 handling: helper returns `Result<ProductDetail, ApiError>`; page inspects `error.type === 'NotFound'` and calls `notFound()` from `next/navigation`.
- `generateMetadata(props)`: `title = "{ProductTitle} — {MakerCompanyName} — Makables"`; `description = Description.slice(0, 160)` with i18n fallback when description is null/empty. Only branch the not-found title on `error.type === 'NotFound'` (T-0047 nit #1 convention) — transient errors fall back to the bare brand title.
- Responsive: gallery left, info right at `lg`; stacked at `md` and below.
- Extend `frontend/src/lib/api-client-helpers/catalog.ts`:
  - `getProductById(productId): Promise<Result<ProductDetail, ApiError>>` calling `apiFetch('public', '/api/v1/catalog/products/{productId}', ...)` with `encodeURIComponent`.
  - Hand-mirrored `ProductDetail` + `ProductImageItem` interfaces. Mirror the C# records exactly (same convention as `MakerProfile` etc.). Reuse `buildProductImageUrl(blobPath)` — already exported.
- All copy via i18n keys under `catalog.product_detail.*` (and reuse existing `catalog.product.*`). Follow the plural-neutral `"Label: N"` convention documented in `cs-CZ.ts` line ~192.

## Out of scope

- Order placement form — T-0084. The CTA link is a forward-compat dead end until then.
- Real ratings on the product itself — `ProductDetail` has no rating; maker rating is shown on T-0047 only, not duplicated here.
- Lightbox / fullscreen image viewer.
- Image zoom on hover.
- Add-to-favorites / share.
- SSG / ISR — every render is dynamic so a newly-deactivated product 404s immediately.

## Acceptance criteria

- **AC-1** Given a customer visits `/produkt/<active-product-id>`, when the page loads, then they see the title, the by-maker link (company name + verified badge if applicable + `/katalog/{slug}` href), the primary image, secondary thumbnails (if any), the formatted price per variant, the weight (formatted via `formatWeight`), the description (plaintext, newlines preserved), and the "Objednat" CTA. (US-customer-0009 happy path)
- **AC-2** Given the product has ≥2 images, when the customer clicks a thumbnail in `product-gallery.tsx`, then the primary image swaps to that image via local Client-Component state. The Server Component shell does not re-render. Thumbnails are `<button>` with `aria-label`, keyboard-focusable, no `useEffect` involved.
- **AC-3** Given `PriceType = "Fixed"`, the price renders as `formatCzk(priceAmountMinor, priceCurrency)`. Given `"From"`, renders `catalog.product.price.from` with `{price}` substituted. Given `"OnRequest"`, renders `catalog.product.price.on_request`. Given `priceCurrency !== "CZK"` for any variant, the page routes around `formatCzk` and renders the `on_request` copy — formatter is never invoked with non-CZK (matches T-0047 `ProductPrice` convention).
- **AC-4** Given the backend returns 404 (product inactive OR owning maker not publicly-listable OR unknown id), when the page renders, then the helper returns `ApiError` of type `NotFound` (matches the `ErrorType` union in `lib/runtime/result.ts`) and the page calls `notFound()`. The sibling `not-found.tsx` renders Czech copy + a link back to `/katalog`.
- **AC-5** Given `Description` contains newlines, when the description block renders, then newlines are preserved (Tailwind `whitespace-pre-line`). Given the description contains HTML-like text (`<b>` etc.), then it renders as literal text (JSX escaping; no `dangerouslySetInnerHTML`).
- **AC-6** Given `WeightGrams >= 1000`, then weight renders as `"X,Y kg"` (one decimal, Czech comma separator via `Intl.NumberFormat('cs-CZ')`). Given `WeightGrams < 1000`, then weight renders as `"NNN g"` (integer).
- **AC-7** `generateMetadata(props)` returns `title = "{title} — {companyName} — Makables"` and `description` set to the first 160 chars of `Description` (i18n fallback when null/empty). On `error.type === 'NotFound'` only, title becomes the i18n not-found title; transient errors fall back to the bare brand title.
- **AC-8** Layout responsive at 375 / 768 / 1280: gallery and info stack at `<lg`, side-by-side at `lg` and above. Primary image uses `next/image` with explicit `width`/`height` (no CLS). Existing `next.config.ts` `remotePatterns` covers the image host — no config change.
- **AC-9** All user-facing copy comes from `lib/i18n/cs-CZ.ts`. No hardcoded Czech in the page, the gallery, the helper, or the metadata path. New keys follow the plural-neutral `"Label: N"` convention (see `cs-CZ.ts` line ~192).
- **AC-10** No `useEffect` for data fetching. No DB SDK imports. All API access through `lib/api-client-helpers/catalog.ts` + `apiFetch`. Only one Client Component on the page (`product-gallery.tsx`); the shell, info block, by-maker link, and CTA stay Server Components. The outermost wrapper of `page.tsx`, `loading.tsx`, and `not-found.tsx` is `<section>` (root `<main>` already lives in `app/layout.tsx` — T-0047 convention).

## Technical notes

- Endpoint: `GET /api/v1/catalog/products/{productId}` on the Public host (anonymous).
- DTO `ProductDetail` (in `backend/src/Makables.Core.Domain/Catalog/ICatalogQueries.cs`):
  - Product: `productId`, `title`, `description`, `priceAmountMinor`, `priceCurrency`, `priceType` (`"Fixed" | "From" | "OnRequest"`), `weightGrams`, `categoryId`.
  - Owning-maker display: `makerId`, `makerSlug`, `makerCompanyName`, `makerIsVerified`.
  - `images`: `IReadOnlyList<ProductImageItem>` with `imageId`, `blobPath`, `sortOrder` (ordered by sort ascending).
- 404 gate is the same as T-0044/T-0045: product inactive OR owning maker not publicly-listable. No oracle leakage — the page cannot differentiate cases, and shouldn't try.
- Helper convention: do **not** import the NSwag-generated client directly (`PublicApi.products(productId)` throws on non-2xx). Mirror the DTOs by hand in `catalog.ts` and call `apiFetch('public', ..., { method: 'GET' })` — same convention as `getMakerBySlug` / `getPagedMakers`.
- Image URLs: reuse `buildProductImageUrl(blobPath)` — already in `catalog.ts` from T-0047. Do not duplicate. The blob path on `ProductImageItem` carries `{country}/products/{productId}/{filename}`; the helper strips the duplicated `products/` segment.
- Sort order: the backend already orders `images` by `sortOrder` ascending. The Server Component shell passes the list as-is to `product-gallery.tsx`; initial index = 0.
- Money: `formatCzk(amountMinor, currency)` now takes the currency as the second arg (T-0047 fold of Copilot Medium #2). Pass `priceCurrency` explicitly. Do not call `formatCzk` on non-CZK — route to `on_request` copy at the card boundary, same as T-0047 `ProductPrice`.
- i18n plural-neutral convention: keys with counts use `"Label: N"` shape (see `cs-CZ.ts` line ~192 comment). Not directly relevant on this page (no `{count}` strings) but mirror the rule if a count crops up.
- 404 handling lives in the page, not the helper (same as T-0047). Helper stays reusable for non-page contexts.
- Do not edit `lib/api-client/public-api.v1.ts` — pre-commit hook will block it.
- `RATING_BP_PER_STAR` is irrelevant here (no rating on product detail).

## Files touched (expected)

- `frontend/src/app/(public)/produkt/[productId]/page.tsx` (new — Server Component)
- `frontend/src/app/(public)/produkt/[productId]/loading.tsx` (new)
- `frontend/src/app/(public)/produkt/[productId]/not-found.tsx` (new)
- `frontend/src/app/(public)/produkt/[productId]/product-gallery.tsx` (new — `'use client'`)
- `frontend/src/lib/api-client-helpers/catalog.ts` (extend — add `ProductDetail`, `ProductImageItem`, `getProductById`)
- `frontend/src/lib/i18n/cs-CZ.ts` (extend with `catalog.product_detail.*` keys)

## Test plan reference

`docs/test-plans/T-0048.md` (to be created by QA on PR open)

## Status log

- 2026-05-31 `draft → ready` by PM. Backend (T-0045) merged; helper file + `formatCzk` + `buildProductImageUrl` + i18n scaffold already in place from T-0047. Owner: `frontend`.
- 2026-05-31 done. `npx tsc --noEmit` + `npm run lint` clean. Security review CLEAR (XSS-safe via JSX escaping; `buildProductImageUrl` carries forward T-0047's `..`-rejection + host anchoring; `notFound()` honors backend's collapsed 404 with no oracle leak; Client gallery has no server env vars or sensitive state). Code-quality review CLEAR after one Medium fix.
  - **M1 — nested `<section>` in description block.** The description block used `<section>` for the heading + body, but it doesn't warrant its own landmark and the outer `<section>` already wraps the page. Changed to `<div>` so the document outline stays clean (only the outer route wrapper is a landmark, per the T-0047 convention).
  - All 10 ACs covered: gallery interactivity, three price variants, 404 via `notFound()`, plaintext description with `whitespace-pre-line`, weight formatter (`<1000` → "g", `>=1000` → "kg" with Czech comma decimal), metadata with NotFound vs transient branching, two-column layout at `lg:` stacking below, full i18n, `<section>` (not `<main>`) outermost wrapper.
  - Reused `catalog.maker.back_to_catalog` (the neutral key T-0047 promoted) for both the page footer and the not-found return link — no namespace-misleading variant created.
- 2026-05-31 Copilot review folded — five spec-vs-shipped drifts, all in this ticket file. Implementation was correct; the spec text drifted.
  - **Gallery props.** The Scope claimed `product-gallery.tsx` takes `images` + an `initialIndex`. Shipped props are `images` + `title` (title is used for the primary `<Image alt>`). Corrected.
  - **Weight separator (Scope).** Example was `"1.5 kg"` with a dot; the formatter uses `Intl.NumberFormat('cs-CZ')` which emits a Czech comma. Corrected to `"1,5 kg"` and called out the locale.
  - **i18n namespace.** Scope + Files-touched both said `catalog.product.detail.*`. Shipped keys live under `catalog.product_detail.*`. Corrected both mentions.
  - **AC-4 ErrorType casing.** AC said `not_found` but the `ErrorType` union in `lib/runtime/result.ts` is `'NotFound'` (PascalCase) and the page matches `error.type === 'NotFound'`. Corrected.
  - **AC-6 weight separator.** Same dot/comma drift as the Scope. Corrected to `"X,Y kg"`.
- 2026-05-31 second Copilot review folded — three code findings.
  - **Thumbnail dim drift (Medium).** `THUMB_WIDTH/HEIGHT` constants were `96` but the button's Tailwind class is `h-20 w-20` (5rem = 80 px). `next/image` would request a larger source than the layout uses. Aligned the constants to `80` and added a comment tying them to the Tailwind size so the next person doesn't drift them again.
  - **Decorative thumbnail alt (Medium).** Each thumbnail `<Image alt={product title}>` duplicated the wrapping `<button aria-label>` and the same alt repeated across every thumbnail — screen readers announce the title N+1 times. The button's localised `aria-label` ("Náhled N") already carries the role + identity, so the thumbnail image is decorative; switched to `alt=""`. Primary image keeps its descriptive alt.
  - **Duplicate `truncateForMeta` (Low).** Same helper lived in both T-0047 `/katalog/[slug]/page.tsx` and T-0048 `/produkt/[productId]/page.tsx` — two metadata truncation behaviours that could drift. Extracted to `lib/seo/truncate-for-meta.ts` with a docstring explaining the heuristic. Both pages now import the shared util; future SEO-fed pages get the same contract for free.
- 2026-06-01 third Copilot review folded — one finding on the extracted util.
  - **Hard-coded `lastSpace > 80` threshold (Medium).** The helper accepts an arbitrary `max` but the cutoff was the literal `80`, which only encodes "roughly half" at the default `max = 160`. For any other limit (e.g. an OG-description path passing `max = 70` or a longer-form `max = 240`) the half-budget promise in the docstring would no longer hold — it would either cut too aggressively or never trigger. Replaced with `Math.floor(max / 2)` so the threshold scales with the caller's budget and the docstring stays honest. Updated the docstring to reference the formula.
