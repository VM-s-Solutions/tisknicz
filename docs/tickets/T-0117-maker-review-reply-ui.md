---
id: T-0117
title: Maker review-reply dashboard — received reviews list + public reply form (/dashboard/maker/recenze)
status: ready
size: S
owner: frontend
created: 2026-06-14
updated: 2026-06-14
depends_on: [T-0100]
blocks: []
user_stories: [US-maker-0014]
adrs: [0013, 0022, 0024]
phase: 5
manual_steps: ["QA pass on Vercel preview per the inline test plan (list render, reply submit/overwrite, empty/error, responsive)"]
security_touching: false
layers: [frontend]
---

# T-0117 — Maker review-reply dashboard (`/dashboard/maker/recenze`)

## Context

T-0117 is the **maker-facing frontend cap of the review bundle**, the parallel of T-0115 (the customer review-submission UI on `/dashboard/zakaznik`). The backend ships first under **T-0100** (`Review` entity + `IReviewRepository` + `SubmitReview` on Web.Customer + `RespondToReview` on Web.Maker + the `GetMakerReceivedReviewsPaged` read query on `IReviewQueries` + the atomic `Maker.SetCatalogStats` rating recompute). This ticket replaces the absent `/dashboard/maker/recenze` route with the real review dashboard, satisfying **US-maker-0014 — Respond to a review** in full: a maker sees every review they received and can attach (or overwrite) one public reply per review.

The implementation precedent is the **shipped maker payout dashboard** (`frontend/src/app/(maker)/dashboard/maker/vyplaty/`, T-0116): a Server Component `page.tsx` with `dynamic = 'force-dynamic'`, URL-state pagination via `searchParams` + a local `<Pagination>` `<Link>` component, a hand-written `Result<T, ApiError>` helper in `lib/api-client-helpers/`, mobile-cards/desktop-grid responsive layout, Czech-short dates via `lib/utils/dates.ts`, an informational empty state, and an `Alert variant="error"` failure state. T-0117 mirrors that route structure on the new `recenze` path so the maker dashboards stay structurally identical. The one new element is a **client island for the reply form** — the established maker mutation pattern from `objednavky/[orderId]/order-actions.tsx` (`'use client'`, POST via the helper, `router.refresh()` on success, inline i18n error alert). Star ratings render **read-only** via the existing `Stars` display component (`(public)/katalog/[slug]/stars.tsx`, T-0047) — the maker replies, they do not rate, so no rating input picker is needed (that picker is T-0115's customer concern).

Everything this page renders is on the wire after T-0100. The list query returns paged received-review rows (review id, rating stars, customer first name, order number, comment, created date, existing maker reply + reply timestamp if any); the reply command overwrites the single reply per review (Q4 lock). The page is a pure presentation layer: no rating math (the backend recomputes `Maker.RatingAverageBp` atomically in the same UoW as the review insert — T-0100), no eligibility logic, no state machine on the client.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 5 dimensions at the 2026-06-14 deliberation (Q1–Q5). T-0117 is the frontend surface of **Q4** (maker reply OVERWRITABLE, one reply per review, ≤500 chars) and consumes the rating fields produced by **Q5** (live `rating_avg`); the rest of the Q-set is backend (T-0100) and is referenced only where it shapes what the UI may render. The public review LIST is deferred to **T-0050** (NOT this bundle). The remainder is ADR/pattern-locked or PM-absorbed from the `vyplaty` precedent.

### A. User-locked at deliberation (non-negotiable)

1. **One overwritable reply per review (Q4).** The maker reply is a single `MakerReply` field (≤500 chars) on the review. Re-submitting **overwrites** the existing reply — there is no reply thread, no reply history, no per-reply timestamps beyond the one `MakerReplyAt`. The form pre-fills with the current reply (edit-in-place); on submit the row shows the updated reply after `router.refresh()`. **Rejected:** a multi-comment reply thread (Q4 locks one reply per review — a thread is a different product, post-MVP); append-only replies (the maker should be able to fix a typo, not stack corrections).

2. **The customer review is immutable on this surface (Q4).** The maker dashboard renders the customer's rating + comment as **read-only** — no edit, no delete, no moderation affordance for the review content itself. The maker can only attach their own reply. **Rejected:** any maker-facing "flag / dispute / hide" control on the review (admin-handled via support at MVP per US-maker-0014 out-of-scope; the soft-delete hook exists backend-side for a later admin command, never a maker capability); rendering the customer review in an editable control.

3. **Live aggregate header, no threshold (Q5).** The page header shows the maker's current average stars + review count read **directly from the maker's rating fields** (`RatingAverageBp` → /10000 for the 0–5 display, `RatingCount`) — already live from the first review (Q5: no N-threshold). No client-side averaging. **Rejected:** computing the average on the client from the listed reviews (wrong — the list is paginated; the backend's recompute-over-all-active-rows is authoritative); hiding the header until N reviews (Q5 removed the threshold).

### B. ADR + pattern-locked (no relitigation)

- **patterns.md §B.1 — Server Components by default.** The list `page.tsx` has no `'use client'`. The only client island is the **reply form** (event-handler POST + `router.refresh()` — §C), mirroring how `vyplaty` keeps `page.tsx` server-side and `objednavky/[orderId]` isolates the action buttons.
- **patterns.md §B.4 + §B.16 — all data via `apiFetch` + a hand-written helper.** New (or extended) `lib/api-client-helpers/reviews-client.ts` wraps the T-0100 maker endpoints and returns `Result<T, ApiError>`. No raw `fetch`, no `useEffect` data fetching anywhere in the route. (If T-0115 already created `reviews-client.ts` for the customer side, T-0117 **extends** it with `getMakerReviews` + `respondToReview`; otherwise it creates the file.)
- **patterns.md §B.14 + ADR 0024 — SSR auth cookie forwarding.** The Server Component render forwards the maker-audience cookie to the maker host. A customer JWT replayed against the maker host 401s at the backend (ADR 0013); the frontend adds no parallel auth logic. The IDOR shield is backend-side (T-0100's `RespondToReview` runs the maker-owns-review predicate; cross-tenant → 404) — the frontend never passes a `makerId`.
- **ADR 0022 — NSwag is the contract.** `frontend/src/lib/api-client/` is consumed, never hand-edited (pre-commit hook). The **maker-host client regen for T-0100 rides that backend ticket**, not this one; T-0117 consumes the already-regenerated `GetMakerReceivedReviewsPaged` + `RespondToReview` types. **No backend change and no regen in T-0117.**
- **patterns.md §B.8 — URL-state pagination via `searchParams` + `<Link>`.** `page` lives in the URL; `page=1` is dropped from canonical URLs; junk (`page=0`, `page=abc`) clamps to 1 (backend Validator authoritative). Local `<Pagination>` pointed at `/dashboard/maker/recenze` (the `vyplaty` resolution).
- **patterns.md §B.5 + §B.18 — Czech-only UI via i18n keys.** Zero hardcoded Czech outside `lib/i18n/cs-CZ.ts`. New keys under `dashboard.maker.reviews.*`. Plural-neutral phrasing for the review-count line.
- **No business logic client-side.** `RatingAverageBp / 10000 → 0–5 display` is a presentation conversion (the existing `Stars` precedent does the same bp→display conversion); `MakerReply == null → show form, else show reply + edit` is a render condition, not a rule.

## Scope

### Route (new)

- **`frontend/src/app/(maker)/dashboard/maker/recenze/page.tsx`** — Server Component list. `dynamic = 'force-dynamic'`; `generateMetadata` from i18n keys. Reads `searchParams.page`, calls `getMakerReviews({ page })` once, renders the **aggregate header** (avg stars via `Stars` + count, from the maker's rating fields on the response envelope), the **review list**, and URL-state pagination — or the empty/error state. Unauthorized → redirect to `/login?redirect=...` (`vyplaty` precedent).
- **`frontend/src/app/(maker)/dashboard/maker/recenze/review-card.tsx`** — server-rendered card per review (stacked cards `< md`, roomier card `≥ md` — reviews are richer than payout rows, so a card list, not a grid table). Shows: `Stars` (read-only rating), customer first name + order number, the comment (or a muted "bez komentáře" line if null), the Czech-short created date, and **either** the existing maker reply block (reply text + `MakerReplyAt` date + an "edit" affordance) **or** the reply form when no reply exists. The form itself is the client island below.
- **`frontend/src/app/(maker)/dashboard/maker/recenze/reply-form.tsx`** — `'use client'` island: a `Textarea` (≤500 chars, pre-filled with the existing reply when editing) + submit button, wired to `respondToReview(reviewId, body)`; on success `router.refresh()` (the SSR list re-renders with the new reply); on failure an inline i18n-keyed `Alert`. Disabled/pending handling during the request. Mirrors `order-actions.tsx`. (The only client code in the route.)
- **`recenze/pagination.tsx`** + **`recenze/loading.tsx`** + **`recenze/error.tsx`** — local copy / mirror of the `vyplaty` resolution (pagination pointed at the `recenze` base path; pulse skeleton; last-resort boundary).

### API helper

- **`frontend/src/lib/api-client-helpers/reviews-client.ts`** — extend (or create):
  - `getMakerReviews({ page? }): Promise<Result<MakerReviewsPage, ApiError>>` wrapping the generated `GetMakerReceivedReviewsPaged` (envelope unwrapped to the inner `PagedData`; `page` emitted only when `> 1` per §B.8). The page envelope also carries the maker's aggregate (`ratingAverageBp`, `ratingCount`) for the header — re-typed `createdAt` / `makerReplyAt` to wire-shape `string | undefined` per the `maker-orders.ts`/`payouts-client.ts` raw-JSON rationale.
  - `respondToReview(reviewId, body): Promise<Result<void, ApiError>>` POSTing to the maker `RespondToReview` endpoint; `404` (foreign/unknown review) → `ApiError.type === 'NotFound'`; `body.length > 500` is caught by the backend `ReviewReplyTooLong` validator (the frontend mirrors a `maxLength={500}` on the textarea for UX, backend stays authoritative).
  - Re-export any string enums consumed (none expected beyond the DTOs).

### i18n

- **`frontend/src/lib/i18n/cs-CZ.ts`** — NEW keys under `dashboard.maker.reviews.*` (**tykání** tone per CLAUDE.md, pending the tone open question — a flip is catalog-only): metadata title/description, page title/subtitle, aggregate header (avg label, count plural-neutral line), review-card labels (customer/order/date, "bez komentáře" for a null comment), reply block (heading, "odpovězeno" date prefix, edit button label), reply form (textarea label/placeholder, char hint `≤ 500`, submit label, submitting label, success-toast/inline confirmation, error labels keyed to the backend `ReviewReplyTooLong` / generic), empty state (title + onboarding-flavoured description — "Zatím nemáš žádné recenze"), error title/body/retry, pagination strings (reuse the shared keys if present). Nav label `dashboard.maker.nav.reviews` ("Recenze") for the conditional nav entry.

### No backend change

No endpoint, DTO, or contract change in T-0117 → no NSwag regen, no `api-client` diff (the T-0100 regen ships on that ticket; a regen here would be blocked by the pre-commit hook anyway).

## Alternatives Considered

- **Option A — A reply thread / multi-comment conversation on each review.** *Rejected per A.1* — Q4 locks one overwritable reply per review (`MakerReply` is a single ≤500-char field). A thread is a distinct product (back-and-forth, notifications, ordering) and not what the backend models. The edit-in-place form covers the real need ("fix my reply"), nothing more.
- **Option B — A maker-facing "flag / dispute / hide review" control.** *Rejected per A.2* — review moderation is admin-handled via support at MVP (US-maker-0014 out-of-scope). The soft-delete hook lives backend-side for a future admin command; surfacing any hide/flag affordance to the maker would imply a capability that does not exist and tempts review suppression.
- **Option C — Render the customer review in an editable control.** *Rejected per A.2* — the customer review is immutable after submit (Q4); only the maker's own reply is editable. The card renders rating + comment read-only and gates editing to the reply textarea alone.
- **Option D — Average the displayed reviews on the client for the header.** *Rejected per A.3* — the list is paginated, so client-side averaging would be wrong on page 2+. The header reads the maker's authoritative `RatingAverageBp` / `RatingCount` (backend recompute-over-all-active-rows, T-0100). No client math.
- **Option E — A grid "table" layout like `vyplaty` for the review list.** *Rejected per §Scope* — a review row is richer (multi-line comment + a reply block / form) than a payout row (scalar columns). A card list reads better at every breakpoint and avoids a cramped grid; the responsive split is cards-everywhere, not cards-then-table.
- **Option F — A generated `MakerApi` method for the reply POST.** *Rejected per §B* — the generated client throws on non-2xx; the route needs `Result<T, ApiError>` to render the inline error path (e.g. `ReviewReplyTooLong`, `NotFound`). The hand-written helper through `apiFetch` is the established convention (`payouts-client.ts`).
- **Option G — A dedicated rating-input `StarRating` picker on this surface.** *Rejected per §Context* — the maker replies, they do not rate. The interactive rating picker is T-0115's customer concern; T-0117 displays ratings read-only via the existing `Stars` component. Adding an input picker here would be dead UI.
- **Option H — Optimistic reply update (mutate the DOM before the server confirms).** *Rejected per §B.1* — the route mutates via the helper + `router.refresh()` (the `order-actions.tsx` precedent), which re-renders the SSR list with the authoritative reply. Optimistic state would re-introduce client state the server owns, for no perceptible latency win on a single short POST.

## Out of scope

- **T-0100 backend** (`Review` entity, `IReviewRepository`, `SubmitReview` on Web.Customer, `RespondToReview` on Web.Maker, `GetMakerReceivedReviewsPaged` on `IReviewQueries`, the atomic `Maker` rating recompute, the new `BusinessErrorMessage` codes, NSwag maker regen) — upstream backend ticket; this page only renders/POSTs against it.
- **T-0115 customer review-submission UI** (`/dashboard/zakaznik` — the 1–5 star input + optional comment, eligibility = a Delivered/Completed order with no active review) — sibling frontend ticket; the customer side of the bundle.
- **T-0050 public review LIST** (the public-host query populating `MakerProfile.Reviews` + the T-0047 profile binding) — deferred, separate ticket. The star **numbers** are already public via T-0043; the review **bodies** are not surfaced publicly until T-0050. T-0117 is the maker's private dashboard, not the public profile.
- **Admin review moderation UI** (hide / restore via the soft-delete hook) — admin host, later ticket; no maker affordance here (Option B).
- **Maker-notification email on a new review** — fast-follow (the backend is synchronous at MVP, no outbox/email on review submit per T-0100). No maker-facing notification surface in this ticket.
- **Full maker sidebar / dashboard nav shell** — the `(maker)/layout.tsx` is still the Phase-1 skeleton (`return <>{children}</>` — verified; its comment lists T-0116 as a nav addition). T-0117 adds only the route + (if a nav already exists at impl time) one "Recenze" entry; otherwise the nav addition is logged for the layout ticket and the route ships reachable by direct URL.
- **Reply edit history / audit trail** — Q4 keeps one overwritable reply; no version history at MVP.

## Acceptance criteria

- **AC-1** Given a logged-in maker visiting `/dashboard/maker/recenze`, when the page renders, then it is a Server Component (`page.tsx` has no `'use client'`), `dynamic = 'force-dynamic'` is set, data comes from `getMakerReviews` via `apiFetch` with SSR cookie forwarding, and no `useEffect` data fetching exists anywhere in the route folder.
- **AC-2** Given the maker has received reviews, when the list renders, then each card shows: the rating as read-only stars (`Stars`, bp→0–5 conversion), the customer first name + order number, the comment (or a muted "bez komentáře" line when null), and the created date in Czech short format. Reviews are most-recent-first (server sort — verified by card order on the preview).
- **AC-3** Given a review with no maker reply, when the card renders, then the reply form (`Textarea` ≤ 500 chars + submit) is shown; given a review that already has a reply, the existing reply text + `MakerReplyAt` date render with an edit affordance that opens the form pre-filled with the current reply.
- **AC-4** Given the maker submits a reply ≤ 500 chars, when the POST to `respondToReview` succeeds, then `router.refresh()` runs and the card re-renders showing the new reply (no full navigation, no client-side optimistic mutation). Given a re-submit on a review that already had a reply, the displayed reply is **overwritten** (Q4 — one reply per review), not appended.
- **AC-5** Given the reply POST fails (e.g. `ReviewReplyTooLong`, `NotFound`, network/5xx), when it returns, then an inline i18n-keyed `Alert` renders next to the form, the submit button re-enables, and no card is lost — the SSR list stays rendered.
- **AC-6** Given the page header, when it renders, then it shows the maker's average rating (stars + the 1-decimal numeric) and the review count read from the maker's rating fields on the response envelope (`RatingAverageBp` / `RatingCount`) — **not** averaged from the listed page (Q5 live aggregate, no client math; grep proof: no division over `items` in the header).
- **AC-7** Given zero received reviews, when the list renders, then the informational empty state shows (distinct `dashboard.maker.reviews.empty.*` copy) — not an error, not a blank page; the header is hidden or shows "zatím bez hodnocení" per the empty key.
- **AC-8** Given `?page=2`, when the list renders, then `getMakerReviews` is called with page 2 and pagination controls reflect `PagedData` totals; junk (`page=0`, `page=abc`) clamps to page 1 without an error page; `page=1` is absent from canonical URLs. Given the list API fails, an `Alert variant="error"` with i18n title/body + retry link renders (no blank page).
- **AC-9** Given viewports 375 / 768 / 1280, when the list renders, then review cards stack with no horizontal scroll at 375 and the textarea + submit remain reachable. Hygiene gate: zero `any`, zero `console.*`, zero hardcoded Czech outside `cs-CZ.ts` (new `dashboard.maker.reviews.*` keys; tykání tone noted pending in the PR), zero edits to `lib/api-client/` (pre-commit hook), `npm run lint` + `npm run build` clean, `node scripts/check-consistency.mjs` exit 0.

## Technical notes

### Why the maker reply is overwritable, not a thread

Q4 locks one reply per review (`MakerReply`, ≤500 chars, single `MakerReplyAt`). The product intent is "the maker gets one public say in response to a review", and the realistic edit need is fixing a typo or softening a defensive first draft — both satisfied by overwrite-in-place. A thread would imply a conversation the data model does not carry and the public profile (T-0050) is not designed to render. The form pre-fills with the current reply so editing is one keystroke away; the backend `RespondToReview` simply sets the field (T-0100), and `router.refresh()` shows the result.

### Why the aggregate header is read from the maker, not computed client-side

The list is paginated; on page 2 the visible reviews are a window, not the whole set, so averaging them would mislead. Q5 makes `Maker.RatingAverageBp` live from the first review (no N-threshold) and T-0100 recomputes it over all active reviews in the same UoW as each submit (self-healing under soft-delete). The header therefore reads that authoritative field straight off the response envelope and converts bp→0–5 for display the same way the existing `Stars` precedent does — no client arithmetic over `items`.

### Why a card list, not a grid table

A review carries a multi-line customer comment plus the maker's reply (or a reply form) — far more vertical content than a payout row's scalar columns. The `vyplaty` grid "table" works because each payout row is five short fields; forcing a review into that grid would clip the comment and crush the textarea. A card list reads cleanly at 375/768/1280 and keeps the reply form a natural-width block.

### Why the route is reachable even without the nav

The `(maker)/layout.tsx` is still the Phase-1 skeleton (`return <>{children}</>`). T-0117 does not build the maker sidebar (separate layout ticket). The route ships correct and reachable by URL regardless; the "Recenze" nav entry is added only if a nav already exists at impl time, otherwise logged as a follow-up — the same posture T-0116 took for "Výplaty".

## Risk / mitigation

- **Header averages the page instead of the maker field.** *Mitigation:* A.3 lock + Option D rebuttal + AC-6's grep-for-absence of any division over `items`; the header binds to `ratingAverageBp` / `ratingCount` on the envelope.
- **Reply appended instead of overwritten** (an implementer treats it as a comment list). *Mitigation:* Q4 lock + Option A rebuttal + AC-4; `respondToReview` is a single POST that sets one field, and the card renders exactly one reply.
- **Generated client used for the POST, swallowing the error path.** *Mitigation:* Option F rebuttal + the `respondToReview` helper returns `Result<void, ApiError>`; the inline error alert (AC-5) depends on it.
- **Customer review rendered editable / a moderation control slips in.** *Mitigation:* A.2 lock + Options B/C rebuttals; only the reply textarea is interactive, the review content is read-only DOM.
- **Nav addition blocked by the skeleton layout.** *Mitigation:* §Out-of-scope — route ships reachable by URL; the "Recenze" entry is conditional, otherwise logged for the layout ticket.
- **Tykání/vykání open question resolves late.** *Mitigation:* all copy behind `dashboard.maker.reviews.*` i18n keys; a tone flip is a catalog-only change (T-0116 precedent).

## Test plan reference

Inline (manual QA against the Vercel preview): list render (review cards, read-only stars, customer first name + order number, comment + null-comment line, dates, most-recent-first order), aggregate header (avg stars + count from the maker field, not the page), reply submit on a no-reply review (success → `router.refresh()` shows the reply), reply overwrite on a replied review (Q4 — single reply, not appended), reply error paths (`ReviewReplyTooLong` via a forced >500 body, `NotFound`, network), empty + error states, pagination + deep links + junk-param clamp, responsive passes at 375/768/1280, hygiene grep (no client math over `items` in the header). No backend tests (no backend change in T-0117).

## Files touched (expected)

### New
- `frontend/src/app/(maker)/dashboard/maker/recenze/page.tsx`
- `frontend/src/app/(maker)/dashboard/maker/recenze/review-card.tsx`
- `frontend/src/app/(maker)/dashboard/maker/recenze/reply-form.tsx`
- `frontend/src/app/(maker)/dashboard/maker/recenze/pagination.tsx` (local copy of the `vyplaty` resolution, base path `/dashboard/maker/recenze`)
- `frontend/src/app/(maker)/dashboard/maker/recenze/loading.tsx`
- `frontend/src/app/(maker)/dashboard/maker/recenze/error.tsx`

### New or modified
- `frontend/src/lib/api-client-helpers/reviews-client.ts` — `getMakerReviews` + `respondToReview` (extends the file if T-0115 created it for the customer side; otherwise new).

### Modified
- `frontend/src/lib/i18n/cs-CZ.ts` — `dashboard.maker.reviews.*` + `dashboard.maker.nav.reviews` keys appended.
- `frontend/src/app/(maker)/layout.tsx` — **only if** a nav exists at impl time, add the conditional "Recenze" entry; otherwise unchanged (route reachable by URL, nav logged for the layout ticket).

### Reused (not modified)
- `frontend/src/app/(public)/katalog/[slug]/stars.tsx` — the `Stars` read-only display component (bp→0–5). If it must move to `components/ui/` to be shared across route groups, that relocation is a clean refactor noted in the PR; otherwise imported in place.

## Commits hint

1. **`feat(T-0117): maker reviews api helper + i18n keys`** — `reviews-client.ts` (`getMakerReviews` + `respondToReview`) + `dashboard.maker.reviews.*` catalog additions.
2. **`feat(T-0117): maker received-reviews list page with aggregate header`** — `recenze/page.tsx` + `review-card.tsx` + pagination/loading/error; read-only stars, header from the maker rating field, empty/error states.
3. **`feat(T-0117): review reply form island`** — `reply-form.tsx`; POST + `router.refresh()` overwrite, inline error alert.

## Status log

- 2026-06-14 `draft` by PM. Created as the maker-facing frontend cap of the review bundle (backend: T-0100 `Review` entity + `SubmitReview`/`RespondToReview` + `GetMakerReceivedReviewsPaged` + atomic rating recompute). Sibling: T-0115 (customer review-submission UI). Precedents: `vyplaty` maker dashboard (T-0116) for SSR + URL pagination + empty/error states + tykání; `objednavky/[orderId]/order-actions.tsx` for the POST + `router.refresh()` mutation island; the existing `Stars` display component (T-0047) for read-only ratings.
- 2026-06-14 `draft → ready` by PM. User locked 5 dimensions at the deliberation; frontend-relevant: **Q4** maker reply OVERWRITABLE, one reply per review, ≤500 chars (rejected reply thread + append-only); customer review IMMUTABLE on this surface (rejected maker flag/hide/edit controls — Options B/C); **Q5** live `rating_avg`, no threshold → aggregate header reads the maker rating field, not the page (rejected client-side averaging — Option D). Public review LIST deferred to T-0050 (NOT this bundle; star numbers already public via T-0043). PM-absorbed: card list (not grid) for the richer review rows; read-only `Stars` (no rating picker — Option G is T-0115's concern); reply via the hand-written `Result`-returning helper (Option F); conditional "Recenze" nav (layout still skeleton). No NSwag regen (read-only consumer of the T-0100 contract). **Ready for frontend** — implemented after T-0100 on the bundle branch.

## Definition of Ready checklist

- [x] Linked user story present (US-maker-0014 — Respond to a review).
- [x] Acceptance criteria observable + numbered (AC-1 through AC-9).
- [x] Locked design decisions captured (§A user-locked Q4/Q5, §B ADR+pattern-locked, §C in-line PM-absorbed).
- [x] Alternatives Considered with ≥1 rebutted alternative per locked dimension (Options A–H).
- [x] Out of scope explicit (T-0100 backend, T-0115 customer UI, T-0050 public list, admin moderation, notification email, full sidebar, reply history).
- [x] Risk / mitigation called out (header averaging, append-vs-overwrite, generated-client error swallow, editable review, nav skeleton, tone).
- [x] Test plan reference (inline manual QA, Vercel preview).
- [x] Files touched listed (new + new-or-modified + modified + reused).
- [x] Layers / ADRs / dependencies in the frontmatter; depends on T-0100 (backend); no NSwag regen here (regen rides T-0100).
- [x] Security-touching: NO (IDOR shield is backend compile-time per-audience; no new auth surface).
- [x] Size: S.
- [x] No business logic client-side (bp→0–5 conversion + reply-present are presentation conditions; backend recomputes the rating average).
