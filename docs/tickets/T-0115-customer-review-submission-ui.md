---
id: T-0115
title: Customer review-submission UI (inline CTA + form on the order-detail page)
status: ready
size: S
owner: frontend
created: 2026-06-14
updated: 2026-06-14
depends_on: [T-0100, T-0086b, T-0043]
blocks: [T-0050]
user_stories: [US-customer-0015]
adrs: [0022, 0024]
phase: 4
manual_steps:
  - "Manual QA pass on the Vercel preview per the inline test plan (375/768/1280, keyboard-operable star picker, char-counter mirror, submit→read-only re-sync, existing-review read-only render, maker-reply display)"
security_touching: false
layers: [frontend, frontend-i18n]
---

# T-0115 — Customer review-submission UI (inline CTA + form on the order-detail page)

## Context

T-0115 is the **frontend slice of the order-cleanup review bundle** (`feat/order-cleanup-bundle`). T-0100 ships the backend: the `Review` aggregate (ULID PK, `Core.Domain/Reviews/`), the `SubmitReview` command on the **Web.Customer** host, the `RespondToReview` command on the **Web.Maker** host, the recompute-from-rows rating mutation against `Maker.SetCatalogStats(ratingAverageBp, ratingCount, totalOrders)`, the new `IReviewQueries` read seam, the five new `BusinessErrorMessage` codes, and the NSwag regen for both hosts. **This ticket consumes that contract** and adds the customer-facing surface: an inline review-submission CTA + form that hangs at the bottom of the order-detail page when the order is `Delivered`/`Completed` and the caller has not yet reviewed it.

The page already exists and is shipped: **`frontend/src/app/(customer)/objednavka/[id]/page.tsx`** (T-0086b) is the canonical order URL for the order's whole life (T-0067/T-0076 emails pre-bake `/objednavka/{id}`). Its `TrackingDetail` server component composes the post-payment surface — header, timeline, price breakdown, shipping block, attachments, invoice link, and the `OrderThreadClient` island. **T-0115 adds one more inline block to that composition**, mirroring exactly how `MarkDeliveredButton` (rendered only when `state === Shipped`) and the message thread already hang inline as conditional islands. No new route.

The review **grain is per delivered order** (locked Q1, 2026-06-14): one review per order, eligibility = the caller owns an order in `Delivered` or `Completed` with no active review, no time limit (Q3). The customer review is **immutable after submit** (Q4) — there is no edit/delete affordance — while the maker's reply is overwritable on the maker side (out of scope here, but its presence is rendered read-only). Rating is **1–5 stars REQUIRED**; the comment body is **OPTIONAL, ≤1000 chars** (Q2). The maker's denormalized `RatingAverageBp`/`RatingCount` go **live on the first review** (Q5) — but that is a backend recompute concern; this surface only submits.

**This is a pure presentation slice.** Eligibility is decided by the backend: the order-detail DTO carries the signals the page reads to choose between three render states (CTA-eligible / already-reviewed-read-only / no-review-block-at-all). The 1000-char counter is a UX mirror; the backend `ReviewBodyTooLong` rule stays authoritative. No business logic, no rating math, no state machine ships client-side.

**Public review LIST is NOT this ticket** — it is deferred to T-0050 (public-host query + T-0047 profile binding). The maker-profile DTO already carries an empty `Reviews` placeholder (T-0043); star NUMBERS are already public (T-0043 `RatingAverageBp`/`RatingCount`). T-0115 ships only the customer's own submit + read-back surface on the order page.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 5 dimensions at the 2026-06-14 review-bundle grooming session (Q1–Q5); the frontend-relevant consequences are below. PM-absorbed decisions follow from the T-0086b page composition, the T-0100 contract, and the §B pattern locks.

### A. User-locked at grooming (non-negotiable, frontend-relevant)

1. **Review grain = per delivered order; immutable after submit (Q1 + Q4).** The CTA + form render iff the order is `Delivered`/`Completed` AND no active review exists for it. After a successful submit, the surface flips to the **read-only submitted review** (stars + comment) — **no edit, no delete, no re-submit affordance is ever surfaced** (Q4 customer-side immutability). A second visit to an already-reviewed order renders the same read-only block (never the form). **Rejected:** an "edit my review" affordance (Q4 locks customer-side immutability — the backend has no update path; surfacing an edit button would be a dead control); a per-product or per-maker review grain (Q1 locks per-order — one order, one review, enforced by the unique partial index).

2. **1–5 stars REQUIRED, comment OPTIONAL ≤1000 chars (Q2).** The submit button stays disabled until a star value ∈ [1,5] is picked. The comment textarea is optional; an empty body submits a valid star-only review. A live char counter + a `maxLength={1000}` UX mirror guard the body; the backend `ReviewBodyTooLong` rule remains authoritative for any bypass. **Rejected:** star-only reviews with no comment field at all (US-customer-0015 wants the optional comment); required comment (Q2 locks comment optional — forcing prose suppresses response rate).

3. **Maker reply rendered read-only when present (Q4).** If the submitted/existing review carries a non-null maker reply (overwritable on the maker side, ≤500 chars, out of scope here), the read-only block renders it beneath the customer's review as a distinct "Odpověď výrobce" panel. Absent reply → no reply panel. **Rejected:** hiding the reply on the customer order page (the customer is exactly who the reply is for; the catalog list — T-0050 — is the public surface, this is the private one).

### B. ADR + pattern-locked (no relitigation)

- **patterns.md §B.1** — Server Components by default; `'use client'` only for the interactive island (the star picker + comment form + submit). The eligibility branch and the read-only render path are server-rendered. Validation mirrors (char cap, star-required) are UX-only duplicates; backend authoritative.
- **patterns.md §B.4 + §B.16 (ADR 0022)** — the new endpoints get hand-written `Result<T, ApiError>` wrappers in a `reviews-client.ts` helper; route + component code never imports the generated client; `lib/api-client/` is never edited manually (pre-commit hook).
- **patterns.md §B.14 (ADR 0024)** — the order-detail SSR fetch already authenticates via cookie forwarding; the review eligibility/read-back signals ride on the existing `CustomerOrderDetail` payload (no extra SSR fetch if T-0100 folds the fields into the detail DTO — see §C). The submit mutation runs in a client event handler via `apiFetch`; on success `router.refresh()` re-syncs the server tree (Q5 page-wide lock from T-0086b).
- **patterns.md §B.5 + §B.18** — all strings from `cs-CZ.ts`; every backend error code renders via its parity i18n key (`reviewAlreadyExists`, `reviewOrderNotDelivered`, `reviewRatingOutOfRange`, `reviewBodyTooLong`, `reviewReplyTooLong` — the last is maker-side but the parity key ships here for catalog completeness). **Vykání** (V-form) throughout — this is the customer surface (per CLAUDE.md i18n tone).
- **patterns.md §B.7 + §B.10** — `<section>`/`<Card>` wrappers consistent with the existing detail page; cs-CZ date-time formatting for the review's `createdAt`.
- **T-0082/T-0086b §C action stance** — the page conditionally renders the review block by inspecting the backend-provided signals. No client-side eligibility re-derivation beyond reading the DTO fields.

### C. PM-absorbed (no user input needed)

- **Three render states, decided by backend signals on `CustomerOrderDetail`** (T-0100 folds these into the existing detail DTO — confirmed in the bundle, so this ticket adds **no new SSR fetch**):
  - `canReview === true` (order in `Delivered`/`Completed`, no active review) → render the interactive `ReviewFormClient` island.
  - `review !== null` (a review exists) → render the read-only `SubmittedReview` block (stars + optional comment + Czech timestamp + maker reply panel if present).
  - neither → render **nothing** (no CTA, no empty block) — e.g. order still `Shipped`, or a custom/edge order with no eligibility.
  - If T-0100 instead exposes the review via a separate `IReviewQueries`-backed read endpoint, the page SSR-fetches it once alongside the detail (same cookie, ADR 0024) and a fetch failure degrades to "no review block" (loudly recoverable on next `router.refresh()`, no mock). The implementer confirms the shape against the merged T-0100 DTO before wiring.
- **`StarRating` UI primitive (NEW, `components/ui/star-rating.tsx`)** — two variants in one component, no star library:
  - **interactive** (`onChange` provided): five keyboard-operable buttons (`role="radiogroup"`, arrow-key + click selection), filled `star` / outline `starOutline` icons (both already in `components/ui/icon.tsx` — verified), hover/focus preview, the selected value mirrored to an `aria-label`. Disabled while the parent submit is in flight.
  - **display** (`onChange` omitted): non-interactive filled/outline render for the read-only block. `size` prop (sm/md). Half-star rendering NOT required at MVP (whole-star reviews only — `Rating` is `SMALLINT(1..5)`).
- **`ReviewFormClient` island (NEW, `'use client'`, colocated with the page)** — composes `StarRating` (interactive) + a `Textarea` (optional, `maxLength={1000}`, live `{count}/1000` counter) + a submit `Button`. Submit disabled until a star is chosen or while in flight. On submit → `submitReview(orderId, { rating, body })` helper. On success: `router.refresh()` (the server tree re-renders into the read-only `SubmittedReview` state — Q5 lock). On failure: the mapped Czech error renders inline via `resolveErrorMessage` (`reviewAlreadyExists` covers the race where another tab/device submitted first; `reviewOrderNotDelivered` covers a state regression; `reviewRatingOutOfRange`/`reviewBodyTooLong` cover bypassed mirrors). Mirrors the `MarkDeliveredButton` in-flight/`useRef` re-entrancy guard pattern already in `order-actions-client.tsx`.
- **`SubmittedReview` block (server-safe presentational)** — `StarRating` (display) + the optional comment (rendered only when non-empty) + the Czech-formatted `createdAt` + a maker-reply panel (heading "Odpověď výrobce" + reply text) rendered only when `makerReply` is non-null. No interactivity. Wrapped in the same `<Card padding="md">` idiom as the surrounding detail blocks.
- **`reviews-client.ts` helper (NEW, `lib/api-client-helpers/reviews-client.ts`)** — `submitReview(orderId, { rating, body })` → `apiFetch` POST against the T-0100 customer endpoint, returning `Result<…, ApiError>`; DTO types re-exported from the regenerated customer client (route code never imports `lib/api-client/`). Naming + structure mirror `orders-client.ts`. **Only the customer submit wrapper ships here** — the maker-reply wrapper belongs to the maker-dashboard ticket, not this one.
- **i18n: new `customer.review.*` keys** (vykání) — section heading, CTA/prompt copy, star-picker `aria` labels (1–5), comment placeholder + optional hint + `{count}/1000` counter, submit button label + in-flight label, submitted-review heading, maker-reply heading, and the five `reviewAlreadyExists`/`reviewOrderNotDelivered`/`reviewRatingOutOfRange`/`reviewBodyTooLong`/`reviewReplyTooLong` parity error keys. Plural-neutral per §B.18.
- **Placement in the page** — the review block renders inside `TrackingDetail`, after the message-thread `<Card>` (the conversation is the active coordination surface; the review is a terminal, post-delivery action — it belongs last). One conditional branch reading the backend signals; no change to the `PendingPayment` surface or any pre-`Delivered` state.
- **No backend changes, no NSwag regen in THIS ticket** — the contract (endpoint, DTO fields, error codes) ships in T-0100; the regen is committed there. T-0115 consumes the regenerated client as-is. If the implementer finds the detail DTO does not yet carry `canReview`/`review`, that is a T-0100 gap to escalate, not a T-0115 backend edit.

## Scope

- **`frontend/src/components/ui/star-rating.tsx`** — NEW. `StarRating` primitive with interactive + display variants (per §C). Keyboard-operable radiogroup for the interactive variant; filled/outline icon render for both. No third-party star library.
- **`frontend/src/app/(customer)/objednavka/[id]/review-form-client.tsx`** — NEW `'use client'` island: interactive `StarRating` + optional `Textarea` (counter + 1000-char mirror) + submit. `submitReview` on click → `router.refresh()` on success; mapped Czech error inline on failure. Re-entrancy guard per the `MarkDeliveredButton` pattern.
- **`frontend/src/app/(customer)/objednavka/[id]/submitted-review.tsx`** — NEW server-safe presentational block: display `StarRating` + optional comment + Czech timestamp + maker-reply panel (when present).
- **`frontend/src/app/(customer)/objednavka/[id]/page.tsx`** — MODIFY: in `TrackingDetail`, after the thread `<Card>`, add the three-state branch (`canReview` → `ReviewFormClient`; `review` present → `SubmittedReview`; else nothing) reading the T-0100 detail-DTO signals.
- **`frontend/src/lib/api-client-helpers/reviews-client.ts`** — NEW: `submitReview(orderId, { rating, body })` `apiFetch` wrapper + customer-review DTO re-exports.
- **`frontend/src/lib/i18n/cs-CZ.ts`** — `customer.review.*` keys + the five review error parity keys.

## Alternatives Considered

- **Option A — Do nothing (no review surface at MVP).** *Rejected* — US-customer-0015 is an MVP capability and T-0100 ships the whole backend; leaving the customer with no way to submit would strand a built endpoint and the maker's public rating would never populate (Q5 live-on-first-review would have no first review). The capability is the point of the bundle.
- **Option B — A standalone `/objednavka/[id]/hodnoceni` review route.** *Rejected per A.1 + T-0086b precedent* — the order has ONE canonical URL (emails pre-bake it); a second route forks the resource, buries the review behind an extra click, and duplicates the SSR detail fetch. The inline block mirrors how the deliver button and thread already hang on the one page.
- **Option C — Surface an "edit / delete my review" affordance.** *Rejected per A.1 (Q4)* — customer-side reviews are immutable; the backend exposes no update/delete path. A visible edit control would be a dead button that 404s or confuses. Immutability is rendered honestly: once submitted, read-only forever.
- **Option D — Make the comment required (no star-only reviews).** *Rejected per A.2 (Q2)* — forcing prose depresses response rate; a star-only signal is still a useful rating. The form submits a valid review with an empty body; the textarea is explicitly optional.
- **Option E — Pull in a third-party star-rating npm package.** *Rejected* — a five-button keyboard-operable radiogroup over the two existing `star`/`starOutline` icons is ~40 lines, fully styleable with Tailwind, and avoids a dependency (bundle weight, a11y unknowns, version churn) for a trivially small primitive. The `components/ui/` catalog is hand-built by house convention.
- **Option F — Client-fetch the review state via `useEffect` after mount.** *Rejected per §B.1/§B.14* — the eligibility + read-back signals ride on the SSR-forwarded `CustomerOrderDetail` payload (or a single SSR sibling fetch); a client `useEffect` data fetch is banned and would flash an empty block before hydration. First paint is data-complete.
- **Option G — Optimistic render of the submitted review without `router.refresh()`.** *Rejected per the page-wide Q5 lock (T-0086b)* — the server is authoritative for the review's ID, timestamp, and any concurrently-set maker reply; `router.refresh()` re-renders the canonical read-only block in one cheap round-trip and keeps the page's single re-sync mechanism consistent. Optimistic local state invites drift with the unique-index race.
- **Option H — Fold the public review LIST onto this page too.** *Rejected* — the public list is deferred to T-0050 (public-host query + T-0047 binding); the maker-profile DTO carries only an empty placeholder today. T-0115 is the private submit/read-back surface; mixing in the public catalog list would couple two independently-scheduled tickets.

## Out of scope

- **Backend: `Review` aggregate, `SubmitReview`/`RespondToReview` commands, rating recompute, `IReviewQueries`, the five `BusinessErrorMessage` codes, the migration, NSwag regen** — all T-0100. This ticket consumes the contract.
- **Maker-side reply UI** (`RespondToReview`, the overwritable ≤500-char reply form on the maker dashboard) — separate maker-dashboard ticket; T-0115 renders an existing reply read-only but never authors one.
- **Public review LIST** (catalog/profile binding, populating the maker-profile `Reviews` placeholder) — **T-0050** (deferred); depends on the public-host query + T-0047. Star NUMBERS are already public via T-0043.
- **Editing / deleting a customer review** — Q4 immutability; no path exists, no affordance shipped.
- **Admin review moderation UI** — out of MVP; the soft-delete hook exists on the backend for a later admin command (T-0100 note), no frontend surface here.
- **Maker-notification email on new review** — synchronous-only at MVP per the bundle; the notification email is a documented fast-follow (no outbox/email surface in this UI ticket).
- **Half-star / decimal-star display** — whole-star reviews only (`Rating` is `SMALLINT(1..5)`); the display variant renders filled/outline whole stars. The maker's `RatingAverageBp` decimal is a T-0043/T-0050 catalog-display concern, not this page.
- **Review the order from the dashboard LIST row** — the CTA lives on the order-detail page only (one place to act); the list (T-0086a) shows state, not the review form.

## Acceptance criteria

- **AC-1** Given a customer's own order in `Delivered` or `Completed` with no existing review, when `/objednavka/{id}` is server-rendered, then a review block appears below the message thread with the section heading, the interactive star picker (1–5), and the optional comment field — fetched SSR via the forwarded audience cookie (no client fetch for the initial paint).
- **AC-2** Given the review form is rendered, when the customer has not yet picked a star, then the submit button is disabled; when they pick any star ∈ [1,5] (by click OR keyboard arrow + enter), the button enables. The star picker is keyboard-operable and exposes the selected value via `aria-label`.
- **AC-3** Given a chosen rating and an EMPTY comment, when the customer submits, then the review posts successfully (star-only review valid — comment optional) and the page re-renders into the read-only submitted-review state.
- **AC-4** Given the comment field, when the customer types, then a live `{n}/1000` counter updates and input is capped at 1000 chars (UX mirror); a forced backend `reviewBodyTooLong` 400 renders its Czech parity copy inline without a crash.
- **AC-5** Given a valid submit succeeds, when `router.refresh()` re-renders, then the form is GONE and the read-only block shows the chosen stars (display variant), the comment (if any), and the Czech-formatted submission date. No edit, delete, or re-submit control is present anywhere.
- **AC-6** Given an order that ALREADY has a review, when the page is server-rendered, then the read-only submitted-review block renders directly (never the form), and if a maker reply exists it shows beneath the review under an "Odpověď výrobce" heading; absent reply → no reply panel.
- **AC-7** Given an order NOT in `Delivered`/`Completed` (e.g. `Shipped`) with no review, when the page renders, then NO review block, CTA, or empty placeholder appears at all (the block is fully conditional on backend signals).
- **AC-8** Given a submit fails with `reviewAlreadyExists` (race: another tab submitted first) or `reviewOrderNotDelivered` (state regression), when the error returns, then the mapped Czech copy renders inline, the page stays usable, and no raw error text or crash appears.
- **AC-9** Build, lint, typecheck clean. Zero `any`, `console.*`, client-store imports, or `useEffect` data fetching. All strings via `customer.review.*` i18n keys (vykání); error-code parity present for all five review codes. `lib/api-client/` untouched (consumed as regenerated by T-0100). Responsive + operable at 375/768/1280 per the manual QA plan; the star picker is focus-visible and keyboard-driven.

## Technical notes

### Why the review block hangs inline on the existing order page (not a new route)

The order has exactly one canonical URL for its whole life — T-0067/T-0076 emails pre-bake `/objednavka/{id}`, and T-0086b already established that every post-payment action (confirm delivery, message the maker) hangs as a conditional inline island on that single page. The review is the terminal post-delivery action; a second route would fork the resource, demand a duplicate SSR detail fetch, and bury the CTA behind a click. One page, one more conditional branch — the same shape as `MarkDeliveredButton` (which the server renders only on `Shipped`) and the thread.

### Why eligibility is read from backend signals (not derived client-side)

The "can this caller review this order" decision is a backend invariant: the unique partial index on `(order_id) WHERE is_active` plus the `Delivered`/`Completed` + caller-owns-order predicate (T-0100). The page must not re-implement that — it reads `canReview` / `review` off the SSR-forwarded `CustomerOrderDetail` and picks one of three renders. The backend stays the single source of truth; a client-side guess could disagree with the SQL and flash a form for an order that 400s on submit (`reviewAlreadyExists`/`reviewOrderNotDelivered`), which the inline error path also covers as a defensive backstop.

### Why immutability is rendered as "no controls" (not a disabled edit button)

Q4 locks customer-side reviews immutable; T-0100 ships no update or delete path. Rendering a disabled or hidden edit control would imply a capability that does not exist and invites support tickets ("the edit button doesn't work"). The honest render is: before submit, a form; after submit (and on every later visit), a read-only block with zero mutation affordances. The only thing that ever changes on that block afterward is the maker's reply appearing — authored entirely on the maker side.

### Why a hand-built `StarRating` primitive (not a library)

The interactive picker is a five-button `role="radiogroup"` over the two `star`/`starOutline` icons already in `components/ui/icon.tsx`, with arrow-key selection and a focus-visible ring — roughly 40 lines, fully Tailwind-styled, zero new dependencies. The `components/ui/` catalog is hand-built by house convention (Button, Badge, Textarea, etc.); a star library would add bundle weight and unaudited a11y for a primitive this small. The same component's display variant (no `onChange`) serves the read-only block, so one file covers both the submit form and the read-back.

### Why `router.refresh()` after submit (not optimistic local state)

The page's single re-sync mechanism (Q5 lock, T-0086b) is `router.refresh()`: the server re-renders the canonical state. After a successful submit the server now returns `review !== null`, so the same tree paints the read-only block with the backend's authoritative ID, timestamp, and any concurrently-set maker reply — no client-held copy to drift. The cost is one cheap round-trip; the gain is that the form-to-read-only transition uses the exact code path a fresh page load uses, so the two can never diverge.

## Risk / mitigation

- **Detail DTO doesn't yet carry `canReview`/`review`** (T-0100 exposes the review via a separate query instead). *Mitigation:* §C confirms the shape against the merged T-0100 DTO before wiring; the fallback is a single SSR sibling fetch under the same cookie, degrading to "no review block" on failure (loudly recoverable, no mock). If neither path exists, escalate as a T-0100 gap — do NOT add a backend edit in this ticket.
- **Double-submit race** (customer clicks submit twice, or two tabs). *Mitigation:* the `useRef` in-flight guard (mirrored from `MarkDeliveredButton`) blocks the second client click; the backend unique partial index is the authoritative backstop and surfaces `reviewAlreadyExists`, which AC-8 renders inline.
- **Bypassed char/star mirrors** (crafted request past the 1000-char or 1–5 client guards). *Mitigation:* the backend `reviewBodyTooLong`/`reviewRatingOutOfRange` rules are authoritative; their parity Czech keys render inline (AC-4/AC-8). The mirror never replaces the backend rule.
- **Star picker not keyboard-operable** (a11y regression). *Mitigation:* `role="radiogroup"` + arrow-key handling + focus-visible ring; AC-2 + the manual QA plan pin keyboard operation explicitly.
- **Missing parity i18n keys for the five review codes.** *Mitigation:* this ticket adds all five keys (`reviewAlreadyExists`/`reviewOrderNotDelivered`/`reviewRatingOutOfRange`/`reviewBodyTooLong`/`reviewReplyTooLong`); the i18n parity gate verifies every backend code resolves.

## Test plan reference

Inline manual QA against the Vercel preview (no automated frontend harness at MVP): three-state walk (eligible→form / submit→read-only / already-reviewed→read-only / not-delivered→nothing), keyboard star selection, char-counter + cap, star-only submit, forced-error inline render (`reviewBodyTooLong`, `reviewAlreadyExists`), maker-reply panel presence/absence, 375/768/1280 sweep. The plan is the verification artifact; no separate `docs/test-plans/T-0115.md`.

## Files touched (expected)

### New
- `frontend/src/components/ui/star-rating.tsx` (interactive + display variants)
- `frontend/src/app/(customer)/objednavka/[id]/review-form-client.tsx`
- `frontend/src/app/(customer)/objednavka/[id]/submitted-review.tsx`
- `frontend/src/lib/api-client-helpers/reviews-client.ts`

### Modified
- `frontend/src/app/(customer)/objednavka/[id]/page.tsx` — three-state review branch in `TrackingDetail` (after the thread `Card`).
- `frontend/src/lib/i18n/cs-CZ.ts` — `customer.review.*` keys + five review error parity keys.
- `docs/tickets/INDEX.md` — PM flips T-0115 to `**done**` post-merge.

## Commits hint

1. **`feat(T-0115): StarRating UI primitive + reviews-client helper + customer.review i18n keys`** — the hand-built star primitive (interactive + display), the `submitReview` wrapper, the i18n catalog + five error parity keys.
2. **`feat(T-0115): inline review form + read-only submitted-review block on the order page`** — `ReviewFormClient` island + `SubmittedReview` presentational block + the three-state branch in `TrackingDetail`.
3. **`chore(T-0115): responsive + a11y polish (keyboard star picker, 375/768/1280)`** — focus-visible ring, keyboard selection, breakpoint sweep.

## Status log

- 2026-06-14 `draft → ready` by PM. Created as the frontend slice of the order-cleanup review bundle (`feat/order-cleanup-bundle`); backend ships in T-0100 (Review aggregate, SubmitReview/RespondToReview, recompute-from-rows rating against `Maker.SetCatalogStats`, IReviewQueries, five BusinessErrorMessage codes, both-host NSwag regen). User locked 5 dimensions at the 2026-06-14 grooming session — frontend-relevant: Q1 per-delivered-order grain, Q2 1–5 stars required / comment optional ≤1000, Q3 no time limit, Q4 customer review immutable (maker reply overwritable, rendered read-only), Q5 rating live on first review. Public review LIST deferred to T-0050 (NOT this bundle); star numbers already public via T-0043. Slice scope: inline CTA + form on the existing `/objednavka/[id]` order-detail page (state Delivered/Completed + no review), new `StarRating` primitive, `reviews-client.ts` submit helper, `customer.review.*` i18n keys, three render states (eligible→form / submitted→read-only / already-reviewed→read-only / ineligible→nothing). No backend changes, no NSwag regen (consumed from T-0100). **Ready for frontend.** Implement after T-0100 merges (shares the regenerated customer client + the detail-DTO review signals); blocks nothing downstream except the public-list T-0050 which is independently scheduled.

## Definition of Ready checklist

- [x] Linked user story present (US-customer-0015).
- [x] Acceptance criteria observable + numbered (AC-1 through AC-9).
- [x] Locked design decisions captured (§A user-locked Q1/Q2/Q4 frontend consequences, §B pattern/ADR-locked, §C PM-absorbed).
- [x] Alternatives Considered with ≥1 rebutted alternative per dimension (Options A–H, incl. "do nothing" as Option A).
- [x] Out of scope explicit (T-0100 backend, maker reply UI, T-0050 public list, edit/delete, admin moderation, email, half-stars).
- [x] Risk / mitigation called out (DTO shape, double-submit race, bypassed mirrors, a11y, parity keys).
- [x] Test plan referenced (inline manual Vercel-preview QA incl. keyboard + three-state walk).
- [x] Files touched listed (new + modified).
- [x] Layers / ADRs / dependencies in the frontmatter (depends_on T-0100, T-0086b, T-0043; blocks T-0050).
- [x] Security-touching: NO (consumes the IDOR-shielded, per-audience-split T-0100 endpoint; eligibility + immutability enforced server-side).
- [x] Size: S.
- [x] No NSwag regen in this ticket (contract + regen ship in T-0100; consumed as-is).
- [x] No business logic client-side; star/char mirrors UX-only; backend authoritative for eligibility, rating math, and validation.
