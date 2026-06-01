---
id: T-0049
title: Frontend /dashboard/maker/produkty — product CRUD dashboard with image manager
status: ready
size: L
owner: frontend
created: 2026-06-01
updated: 2026-06-01
depends_on: [T-0041, T-0042, T-0049a, T-0049b]
blocks: []
user_stories: [US-maker-0004]
adrs: [0005, 0011, 0022]
phase: 3
---

# T-0049 — Frontend maker product CRUD dashboard

## Context

T-0046 + T-0047 + T-0048 shipped the customer storefront (`/katalog`, `/katalog/{slug}`, `/produkt/{id}`). T-0049a + T-0049b shipped the Maker host backend prep: the read queries (`GetMyProducts`, `GetMyProductById`), `[ProducesResponseType]` on all six endpoints, and a typed regenerated `maker-api.v1.ts` with a string-union `priceType` on writes. This ticket lights up the maker's own dashboard for managing their catalog: list, create, edit, delete, image upload + remove. It is the biggest frontend ticket of Phase 3 — the maker's first authenticated authoring surface — and it shares no UI with the public storefront. US-maker-0004 happy path closes here.

## Scope

### Helper module (new)
- `frontend/src/lib/api-client-helpers/maker-products.ts` — hand-written wrappers over `apiFetch('maker', ...)` returning `Result<T, ApiError>`, same convention as `profile.ts` / `catalog.ts`. Re-exports the generated DTO `I…` interfaces as readonly type aliases so route code never imports from `lib/api-client/`. Functions: `getMyProducts({page?, pageSize?})`, `getMyProductById(productId)`, `createProduct(input)`, `updateProduct(productId, input)`, `deleteProduct(productId)`, `uploadProductImage(productId, file: File)`, `removeProductImage(productId, imageId)`. Multipart upload builds a `FormData`, calls `apiFetch` with `body: formData` and **no `Content-Type` header** (browser sets the multipart boundary automatically; `apiFetch` only injects `application/json` when `json` is used).

### Index page
- `frontend/src/app/(maker)/dashboard/maker/produkty/page.tsx` — Server Component. Reads `searchParams` (`page`, `pageSize`), NaN-safe clamps per the T-0046 URL-state pagination convention, calls `getMyProducts`. Renders a **responsive card grid** (1 col mobile / 2 sm / 3 lg / 4 xl) of `MakerProductCard` Server Components; each card carries thumbnail (first image via `buildProductImageUrl` or placeholder), title, category label (look up via `CATALOG_CATEGORIES` from `lib/catalog/categories.ts`), price (formatted with the same `priceType` branching as T-0047/0048 — `formatCzk` for `Fixed`, `od {price}` for `From`, `Na poptávku` for `OnRequest`), weight (reuse the shared `formatWeight` helper at `lib/format/weight.ts` — promoted out of T-0048 inline as part of this ticket), image count, active/inactive badge, created date (Czech short format via `lib/utils/dates.ts`'s `formatDate`), actions row with "Edit" `<Link>` to `/dashboard/maker/produkty/{id}` and a "Delete" Client Component. The spec originally called for a table; we shipped a card grid because the audience needs to thumbnail-skim on mobile and a table-with-mobile-collapse would have been worse than a single responsive primitive. The data points the AC requires (every column in the table) all render in the card.
- The list **includes drafts and soft-deleted products** (backend `GetMyProducts` returns them) so the maker sees their full surface. Inactive rows get an i18n badge (`dashboard.maker.products.badge.inactive`) and a muted row style.
- Empty state: "Zatím jste nepřidali žádné produkty" + CTA to `/dashboard/maker/produkty/novy`. (Plural-neutral wording — singular maker.)
- "Přidat produkt" CTA at the top → `/dashboard/maker/produkty/novy`.
- Pagination component identical pattern to T-0046 (Server Component, prev/next + page numbers).
- Sibling `loading.tsx` (skeleton table) and `error.tsx` (Czech retry copy via i18n).

### Create page
- `frontend/src/app/(maker)/dashboard/maker/produkty/novy/page.tsx` — Server Component shell. Renders the shared `product-form.tsx` Client Component in `mode="create"`.
- `frontend/src/app/(maker)/dashboard/maker/produkty/_components/product-form.tsx` (`'use client'`) — single form used by both create + edit. Props: `mode: 'create' | 'edit'`, `initial?: MakerProductDetail`. Fields: title (text), description (textarea), category (`<select>` from `CATALOG_CATEGORIES`), priceType (`<select>`: Fixed / From / OnRequest with i18n labels), priceAmountKc (number input in Kč; multiplied by 100 at submit for the wire), weightGrams (number input). On submit: `mode==='create'` → `createProduct`, on success `router.push('/dashboard/maker/produkty/{id}')` (so the maker can immediately upload images); `mode==='edit'` → `updateProduct`, on success `router.refresh()` + toast. On `ApiError.type === 'Validation'` with `fields`, surface field-level errors inline (`fields[fieldName][0]`); on other error types, show a top-of-form alert.
- No image upload on the create page — the upload endpoint requires `productId`, so images live on the edit page only.

### Edit page
- `frontend/src/app/(maker)/dashboard/maker/produkty/[productId]/page.tsx` — Server Component. Awaits `params` (Next.js 16), calls `getMyProductById`, 404s via `notFound()` on `error.type === 'NotFound'` (covers both "doesn't exist" and "belongs to another maker" — the backend collapses to 404 for IDOR shielding).
- Renders `product-form.tsx` in `mode="edit"` with `initial`.
- If `initial.isActive === false`, renders an i18n banner at the top: "Tento produkt je neaktivní a není viditelný na vašem veřejném profilu."
- Renders `image-manager.tsx` (`'use client'`) below the form: grid of existing images (each `<Image>` with explicit dimensions + a "Odebrat" button that calls `removeProductImage` then `router.refresh()`), plus a single `<input type="file" accept="image/jpeg,image/png,image/webp">` upload control. On change, calls `uploadProductImage(productId, file)`; success → `router.refresh()`; error → inline i18n message keyed off `ApiError.code` (covers `file.invalid` per T-0041 AC-2 for oversized / wrong type).
- Renders `delete-product-button.tsx` (`'use client'`) — opens a confirm modal; on confirm calls `deleteProduct` then `router.push('/dashboard/maker/produkty')`.
- Sibling `loading.tsx` and `not-found.tsx`.

### i18n
- New namespace `dashboard.maker.products.*` in `frontend/src/lib/i18n/cs-CZ.ts`: `title`, `subtitle`, `cta.create`, `empty.title`, `empty.description`, `table.col.{thumbnail,title,category,price,weight,images,status,created,actions}`, `badge.active`, `badge.inactive`, `inactiveBanner`, `actions.{edit,delete}`, `form.field.{title,description,category,priceType,price,weight}`, `form.priceType.{Fixed,From,OnRequest}`, `form.submit.{create,update,saving}`, `form.error.generic`, `images.title`, `images.upload`, `images.remove`, `images.error.{fileTooLarge,fileTypeUnsupported,uploadFailed}`, `delete.confirm.{title,body,confirm,cancel}`. Reuse `catalog.product.price.{from,on_request}` from T-0048 for the price display branches.

## Out of scope

- Image reordering — backend has no reorder endpoint; the existing order is sorted ascending on the wire and that's what the manager renders.
- Bulk operations (multi-select delete / activate) — one product at a time.
- Client-side image preview before upload — upload-and-display only.
- Drag-and-drop image upload — the `<input type="file">` is the only entry point.
- Re-activating soft-deleted products — backend has no re-activate endpoint at present; the inactive badge is informational. (Track as a follow-up if the maker workflow demands it.)
- Fixing the `imagesPOST(productId, file: FileParameter | undefined)` optional-file typing on the generated client — that's T-0049c. The helper always passes a file, so the union doesn't bite at runtime; the typing nuisance is contained to the helper module.
- Image upload progress UI — fire-and-await is sufficient at this scale.

## Acceptance criteria

- **AC-1** Given the maker visits `/dashboard/maker/produkty`, when the page loads, then they see a responsive card grid of their products (active + inactive) with thumbnail, title, category label (resolved from `CATALOG_CATEGORIES`), price (per-`priceType` formatting matching T-0047/0048), weight, image count, active/inactive badge, created date, and Edit/Delete actions. Pagination is URL-driven (`?page=N`); NaN/<1 clamps to 1 in the Server Component (T-0046 convention). (Original spec said "table"; reconciled to "responsive card grid" to keep one primitive across breakpoints — the AC's data points all render.)
- **AC-2** Given the maker has at least one soft-deleted product in the list, when the row renders, then it shows the `dashboard.maker.products.badge.inactive` badge and a muted row style. The list endpoint surfaces these (per T-0049a `GetMyProducts`); the dashboard does not filter them out.
- **AC-3** Given the maker has zero products, when the index page renders, then the empty state shows the i18n title + body + a primary CTA linking to `/dashboard/maker/produkty/novy`.
- **AC-4** Given the maker visits `/dashboard/maker/produkty/novy` and fills the form with valid input, when they submit, then `createProduct` is called with `priceAmountMinor = priceKc * 100` and on success the page navigates to `/dashboard/maker/produkty/{newId}` so the maker can upload images.
- **AC-5** Given the maker submits the create form with input the backend rejects, when the response carries `ApiError.type === 'Validation'` with a `fields` map, then per-field error messages render inline next to each invalid field (read from `fields[fieldName][0]`). Other error types show a single top-of-form alert. No client-side validation duplicates the backend rules — the form submits and lets the backend speak.
- **AC-6** Given the maker visits `/dashboard/maker/produkty/{id}` for a product they own, when the page loads, then the form prefills from `MakerProductDetail` and the image manager shows the existing images in `sortOrder` ascending. Editing a field and submitting calls `updateProduct`; on success the page calls `router.refresh()` so the gallery and form are consistent with the new server state.
- **AC-7** Given the maker visits `/dashboard/maker/produkty/{id}` for a product owned by another maker (or one that doesn't exist), when the backend responds 404, then the helper returns `ApiError.type === 'NotFound'` and the page calls `notFound()`. The sibling `not-found.tsx` renders Czech copy + a link back to the index. No oracle leakage between "doesn't exist" and "belongs to someone else".
- **AC-8** Given the maker clicks "Delete" on the index row or the edit page, when they confirm the modal, then `deleteProduct` is called; on success the index page is shown (soft-deleted row now badged inactive). On error, an i18n alert renders and the modal stays open.
- **AC-9** Given the maker uploads a single valid image (jpeg/png/webp ≤5 MB) on the edit page, when the upload succeeds, then `router.refresh()` reloads the Server Component shell and the image appears in the manager. The `<input type="file">` only accepts `image/jpeg,image/png,image/webp`.
- **AC-10** Given the maker uploads an oversized or wrong-type image, when the backend responds with `ApiError` (`code === 'file.invalid'` per T-0041 AC-2), then an inline i18n message renders next to the upload control. The helper passes the multipart body through `apiFetch` with `body: formData` and no `Content-Type` header so the browser sets the multipart boundary itself.
- **AC-11** Given a product is `isActive === false`, when the edit page renders, then a banner at the top of the page reads "Tento produkt je neaktivní a není viditelný na vašem veřejném profilu." (i18n key `dashboard.maker.products.inactiveBanner`).
- **AC-12** All user-facing copy comes from `lib/i18n/cs-CZ.ts` under the new `dashboard.maker.products.*` namespace (price labels reuse existing `catalog.product.price.*` keys). All money display goes through `formatCzk(amountMinor, 'CZK')`. The outermost wrapper of every page in this ticket is `<section>` — the root `<main>` already lives in `app/layout.tsx`. No `useEffect` for data fetching; Server Components fetch on render, the form/image-manager/delete-modal Client Components call the helper in event handlers. No direct imports from `lib/api-client/` in any route file — only `lib/api-client-helpers/maker-products.ts`.

## Technical notes

- **Endpoints** (all on the Maker host; authenticated via the cookie that rides on `apiFetch` defaults):
  - `GET    /api/v1/products?page=&pageSize=` → `PagedData<MakerProductListItem>`
  - `GET    /api/v1/products/{productId}`     → `MakerProductDetail` (404 for IDOR / unknown)
  - `POST   /api/v1/products` (`CreateProductRequest`) → `CreateProductResponse { id }`
  - `PUT    /api/v1/products/{productId}` (`UpdateProductRequest`) → 204
  - `DELETE /api/v1/products/{productId}` → 204 (soft delete)
  - `POST   /api/v1/products/{productId}/images` (multipart, `file`) → `UploadProductImageResponse { imageId }`
  - `DELETE /api/v1/products/{productId}/images/{imageId}` → 204
- **Generated client gotcha (T-0049c).** The generated `imagesPOST(productId, file: FileParameter | undefined)` types the file as optional even though the backend requires it. T-0049c will land an `IOperationFilter` that sets `required: true` on the multipart parameter so NSwag emits `file: FileParameter`. Until then the helper signature is `uploadProductImage(productId: string, file: File): Promise<Result<...>>` — the public surface is honest; the generated-client union is the helper's problem.
- **Multipart through `apiFetch`.** `apiFetch` only sets `Content-Type: application/json` when `options.json` is provided. When `options.body` is a `FormData`, the browser sets the multipart `Content-Type` (boundary included) automatically — pass `body: formData` and **do not** override `Content-Type`. Cookies still ride along via the default `credentials: 'include'`.
- **DTO re-export pattern.** Mirror `profile.ts` / `catalog.ts`: import the `I…` interfaces from `lib/api-client/maker-api.v1.ts` and re-export them as `readonly` type aliases (route code imports only from `lib/api-client-helpers/maker-products.ts`). Don't extend or wrap them — the generated shape is the contract.
- **Money on the wire.** Backend stores `priceAmountMinor` as `long`; UI displays Kč. Form input is in Kč (number); submit multiplies by 100. Display uses `formatCzk(amountMinor, 'CZK')` from `lib/money/formatter.ts`.
- **Weight format.** Reuse the `formatWeight` rule from T-0048 (`<1000` → `"NNN g"`, `≥1000` → `"X,Y kg"` via `Intl.NumberFormat('cs-CZ')`). If T-0048 inlined it on the product detail page rather than promoting it to `lib/format/weight.ts`, do the promotion in this ticket — it's about to have two callers.
- **Category labels.** `MakerProductListItem.categoryId` is the slug (T-0041 stores the slug); look it up in `CATALOG_CATEGORIES` (`lib/catalog/categories.ts`) for the i18n label key. If the lookup misses (admin added a category after launch), fall back to the raw slug.
- **URL-state pagination.** Same convention as T-0046: read `searchParams.page` in the Server Component, `Number.parseInt(..., 10)`, clamp `Number.isNaN` or `<1` to `1`; clamp `pageSize` to a sensible cap.
- **`<section>` not `<main>`.** Root `<main>` already lives in `app/layout.tsx`; route pages wrap in `<section>` (T-0047 convention).
- **No direct generated-client imports in route files.** Pre-commit hook does not block this (it only blocks edits inside `lib/api-client/`), so it is on the agent to honor. Route files import only from `lib/api-client-helpers/maker-products.ts`.
- **i18n plural-neutral convention.** Counts use the `"Label: N"` shape (`cs-CZ.ts` line ~192). Applies to the image count column and any future per-row count.
- **Auth.** The cookie set by `AuthController.login` on the Maker audience rides on `apiFetch`'s `credentials: 'include'` default — no token plumbing in the helper.

## Files touched (expected)

- `frontend/src/lib/api-client-helpers/maker-products.ts` (new)
- `frontend/src/app/(maker)/dashboard/maker/produkty/page.tsx` (new — Server Component index)
- `frontend/src/app/(maker)/dashboard/maker/produkty/loading.tsx` (new)
- `frontend/src/app/(maker)/dashboard/maker/produkty/error.tsx` (new)
- `frontend/src/app/(maker)/dashboard/maker/produkty/pagination.tsx` (new — Server Component, mirror of T-0046)
- `frontend/src/app/(maker)/dashboard/maker/produkty/novy/page.tsx` (new — Server Component shell)
- `frontend/src/app/(maker)/dashboard/maker/produkty/[productId]/page.tsx` (new — Server Component shell)
- `frontend/src/app/(maker)/dashboard/maker/produkty/[productId]/loading.tsx` (new)
- `frontend/src/app/(maker)/dashboard/maker/produkty/[productId]/not-found.tsx` (new)
- `frontend/src/app/(maker)/dashboard/maker/produkty/_components/product-form.tsx` (new — `'use client'`)
- `frontend/src/app/(maker)/dashboard/maker/produkty/_components/image-manager.tsx` (new — `'use client'`)
- `frontend/src/app/(maker)/dashboard/maker/produkty/_components/delete-product-button.tsx` (new — `'use client'`)
- `frontend/src/lib/i18n/cs-CZ.ts` (extend with `dashboard.maker.products.*`)
- `frontend/src/lib/format/weight.ts` (new or promote if T-0048 inlined — shared between this ticket and `/produkt/[id]`)

## Test plan reference

`docs/test-plans/T-0049.md` (to be created by QA on PR open)

## Status log

- 2026-06-01 `draft → ready` by PM. Backend (T-0041, T-0049a, T-0049b) merged; typed `maker-api.v1.ts` available; helper conventions (`profile.ts` / `catalog.ts`), `formatCzk`, `buildProductImageUrl`, URL-state pagination pattern, and `<section>` rule all established. Owner: `frontend`.
- 2026-06-01 done. `npx tsc --noEmit` + `npm run lint` clean. Security review surfaced one BLOCKER + one informational; code-quality review surfaced two BLOCKERs + five Mediums. All resolved in this commit.
  - **Security B1 / SSR auth (BLOCKER).** `getMyProducts` / `getMyProductById` run inside Server Components. `apiFetch`'s `credentials: 'include'` is browser-only — the Node runtime has no cookie jar, so every server render would have hit the Maker host unauthenticated. This is the platform's first authenticated SSR page; the precedent of `'use client' + useEffect` didn't apply. Extended `apiFetch` to detect the server runtime and forward the audience-scoped cookie pair (`makables_access_<host>` + `makables_refresh_<host>`) via `next/headers`'s `cookies()`. Only the cookie that belongs to the host's audience is forwarded — no Customer-cookie bleed into the Maker host. Public host stays anonymous. The `cookies()` import is dynamic so the module stays consumable from client environments; outside a request scope the import throws and the helper swallows it (the request goes unauthenticated and the backend's 401 folds to a typed `Unauthorized` error). Captured the convention in **ADR 0024** — every future authenticated SSR page works automatically by calling its helper.
  - **Code-quality B1 / `createdOn` Date type lie (BLOCKER).** The helper re-exported `MakerProductListItem` as `Omit<…, 'createdOn'> & { createdOn: Date }`, but `apiFetch` returns `await response.json()` as the value type without running NSwag's `init`. At runtime `createdOn` was a string; the card's `Intl.DateTimeFormat.format()` call would have thrown `RangeError` for every product. Changed the helper to keep `createdOn` as wire-shape `string`. Replaced the card's inline `formatDate(date: Date)` with the existing `lib/utils/dates.ts` `formatDate(date: string | Date)` (it already handled both). Documented the contract in the helper's DTO docstring so the next mirror doesn't reintroduce the lie.
  - **Code-quality B2 / AC-1 deviation (BLOCKER).** The original AC and Scope said "table"; the agent shipped a responsive card grid (1 col mobile / 2 sm / 3 lg / 4 xl). Every data point the AC required (thumbnail, title, category, price, weight, image count, active/inactive badge, created date, actions) renders in the card. Updated AC-1 and the Scope §1 paragraph to reflect "responsive card grid" with the rationale: a table-on-mobile collapse would have been worse than one responsive primitive across breakpoints. The agent's choice stands; the spec catches up.
  - **M1 / missing-category fallback.** Card was rendering `t('dashboard.maker.products.card.category_unknown')` ("Bez kategorie") when `CATALOG_CATEGORIES.find(...)` missed. The ticket Technical notes said "fall back to the raw slug". Switched to `item.categoryId` raw so a post-launch admin category is visible to the maker, not hidden behind a placeholder.
  - **M2 / OnRequest payload + misleading help copy.** Verified the backend rule (`Product.Create`): `amount == 0` requires `OnRequest`; `OnRequest + amount > 0` is allowed (informational "from" price). The form's disable-and-submit-zero flow is correct. Replaced the misleading help text ("je nutné vyplnit nulu") with copy that explains the semantics ("U 'Na poptávku' je pole nepovinné — odešle se 0 Kč jako informační údaj, finální cenu doladíte se zákazníkem.") and added a code comment at the payload site so the next reader doesn't think it's a bug.
  - **M3 / `description: undefined` intent.** Added a comment at the payload site documenting why `description: undefined` is intentional (JSON.stringify drops it; the backend's optional-string contract is "absent or non-empty", not "absent or null"). A future refactor that wraps the object can't silently change semantics without seeing the rationale.
  - **M4 / saved-flash fragility.** Acknowledged. The flash renders today; aligning with a real toast primitive (none exists yet in `components/ui/`) is deferred — not a regression.
  - **M5 / PascalCase→camelCase narrow.** Acknowledged. Top-level fields work; nested validators surface via the top-of-form summary. T-0049's validators are all top-level; deferred until FluentValidation grows nested rules.
