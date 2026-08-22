---
id: T-0171
title: "Public surface: error boundaries, contextual back-navigation, honest counts, audience-gated CTA"
status: in_review
size: M
owner:
created: 2026-08-21
updated: 2026-08-21
depends_on: []
blocks: []
user_stories: [US-customer-0007, US-customer-0008, US-customer-0009]
adrs: [0024]
phase: 8
manual_steps: []
security_touching: false
layers: [frontend, l10n]
---

# T-0171 — Public error surfaces + navigation context

## Context
Audit findings [PUB-M1, PUB-M2, PUB-M4, PUB-M5, PUB-M6, PUB-L1–L6, PUB-L8](../review/ux-functional-audit-2026-08-21.md).
The public surface — the only one anonymous visitors see — is the only route group with **no**
error boundary (render errors show Next's raw English screen); back-links throw away browsing
context; the reviews badge contradicts the rating count on the same screen; hardcoded landing
tiles can silently point at dead categories; and a signed-in admin still gets the customer
"Objednat" CTA that ends in an unsatisfiable login loop (the exact trap fixed for makers in 49b3637).

## Scope
- `error.tsx` for `(public)` and the app root: Czech copy, retry (`reset()`), public nav; root
  `not-found.tsx` gains navbar/footer (PUB-L1); `orders/[orderId]`-style misdirects avoided.
- Back-navigation: product page back-link targets the owning maker profile; catalog back-links and
  both `not-found` pages preserve the prior query (breadcrumb Katalog → maker → produkt).
- Maker/product transient-error surfaces gain a retry beside "Zpět na katalog" (PUB-L4).
- Reviews: label the list "posledních 5 recenzí", badge carries the true `ratingCount` (PUB-M4).
- Landing category tiles read from `getCachedCatalogCategories`; a dropped/unknown category slug in
  the catalog URL renders a dismissible "kategorie už neexistuje" notice instead of a silent
  unfiltered list (PUB-M5).
- Logout failure shows inline copy in the account menu (PUB-M6); menu closes on Escape with focus
  returned to the trigger (PUB-L5).
- "Objednat" gates treat **any** signed-in non-customer audience like the maker case — product page
  CTA and `/objednavka` guard both (PUB-L6).
- Skeleton parity for maker profile + product detail; ScrollToTop on the maker profile; `/produkt/*`
  marks the catalog nav item active (PUB-L2, L3, L8).
- jest-axe stays clean on touched pages; Chrome + WebKit at 375/768/1280.

## Alternatives Considered
- **One global error.tsx only** — rejected: the public group needs its own boundary to keep the
  public chrome; root boundary is the fallback.
- **Hiding "Objednat" entirely for non-customers** — rejected: show the CTA with an explanatory
  disabled/notice state so the situation is legible (mirror the maker treatment shipped in 49b3637).

## Out of scope
- Reviews pagination beyond the labeled cap (needs a paged read — backlog note).
- Katalog filter-state mechanics (T-0170).

## Acceptance criteria
- **AC-1** Given a thrown render error on any public route, when it surfaces, then the user sees a
  Czech error page with working retry inside the public chrome (forced-throw test + manual).
- **AC-2** Given a user on catalog page 3 with filters who opens maker → product, when they use the
  in-page back affordances, then they return maker profile → filtered catalog page 3.
- **AC-3** Given a maker with 37 ratings, when the profile renders, then the reviews section reads
  "posledních 5 recenzí" and the count shown is 37.
- **AC-4** Given an admin session, when viewing a product, then "Objednat" does not lead to a login
  loop (both gates; vitest mirror of the maker test).
- **AC-5** Given a deactivated category slug in the URL, when the catalog renders, then a notice
  explains it and the sidebar shows no phantom active filter.

## Technical notes
Files per audit: `public-navbar.tsx:63-160`, `app/page.tsx:24-31,130-147`, `produkt/[productId]/page.tsx:117-126`,
`katalog/[slug]/{page,loading,reviews-section}.tsx`, `app/not-found.tsx`, `objednavka/page.tsx:73-81`.

## Files touched (expected)
- `frontend/src/app/{error.tsx,not-found.tsx}` (new/updated), `frontend/src/app/(public)/error.tsx` (new)
- `frontend/src/app/(public)/**` (katalog, [slug], produkt, landing)
- `frontend/src/components/shared/public-navbar.tsx`
- `frontend/src/app/(customer)/objednavka/page.tsx`
- `frontend/src/lib/i18n/cs-CZ.ts`

## Test plan reference
`docs/test-plans/T-0171.md`

## Status log
- 2026-08-21 `draft → ready` (Phase 8 UX sweep plan; DoR checklist passed)
- 2026-08-22 `ready → in_progress`, branch `feat/T-0171-public-error-navigation`
- 2026-08-22 `in_progress → in_review` — tsc clean, vitest 225/225 (+3 new); stale-category notice
  SSR-verified on the running app; PUB-L1 + PUB-L2 deferred with rationale in the
  [test plan](../test-plans/T-0171.md); see [review run](../review/runs/T-0171.md)
