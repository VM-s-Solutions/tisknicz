---
id: T-0116
title: Maker payout dashboard — list + drill-into-batch + fee-invoice download (/dashboard/maker/vyplaty)
status: ready
size: M
owner: frontend
created: 2026-06-13
updated: 2026-06-13
depends_on: [T-0103, T-0112, T-0112a]
blocks: []
user_stories: [US-maker-0012, US-maker-0013]
adrs: [0009, 0011, 0013, 0022, 0024]
phase: 4
manual_steps: ["QA pass on Vercel preview per docs/test-plans/T-0116.md (Playwright-style manual plan)"]
security_touching: false
layers: [frontend]
---

# T-0116 — Maker payout dashboard (`/dashboard/maker/vyplaty`)

## Context

T-0116 is the **frontend cap of the payout bundle** `feat/order-cleanup-bundle`. The backend ships first: **T-0103** (`MarkPayoutBatchCompleted` + `PayoutBatch.Complete(clock)` + per-maker payout-sent emails + the `BankReference` column), **T-0112** (maker-scoped `GetMakerPayouts` list + `GetMakerPayoutDetail` per-order breakdown queries), and **T-0112a** (maker fee-invoice PDF streaming endpoint). This ticket replaces the absent `/dashboard/maker/vyplaty` route with the real payout dashboard, satisfying **US-maker-0012 — View payouts** (list + drill-into-batch per-order breakdown) and the maker-facing half of **US-maker-0013 — Download fee invoice** (the PDF download button; the streaming endpoint itself is T-0112a).

The implementation precedent is the shipped **maker order dashboard** (`frontend/src/app/(maker)/dashboard/maker/objednavky/`, T-0087a list + T-0087b detail): Server Component pages, `dynamic = 'force-dynamic'`, URL-state pagination via `searchParams` + `<Link>`, hand-written `Result<T, ApiError>` helpers in `lib/api-client-helpers/` (patterns.md §B.16), mobile-cards/desktop-table responsive layout, `formatCzk(amountMinor, currency)` for money, Czech-short dates via `lib/utils/dates.ts`, and the **blob-download discipline** (`apiFetch` `parse: 'blob'` + `timeoutMs`, programmatic anchor) already proven by `downloadShippingLabel` / `downloadMakerOrderFile` + `triggerBlobDownload` in `order-actions.tsx`. T-0116 mirrors all of it on the new `vyplaty` route so the two maker dashboards stay structurally identical for review and maintenance.

Everything this page renders is on the wire after T-0103/T-0112/T-0112a. The list query returns paged batch rows (batch number, total paid to **this** maker, claimed-order count, state, completed/processed date); the detail query returns the per-order breakdown (order #, product price, platform fee, net payout) plus a `FeeInvoiceId` that, when non-null, the download button streams via T-0112a. The page is a pure presentation layer: no money math (the backend computes every figure — `formatCzk` only formats), no state-machine knowledge, no CSV anywhere.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 5 dimensions at the 2026-06-13 deliberation (Q1–Q5 + reversibility). T-0116 is the frontend surface of Q4 and Q5; the rest of the Q-set is backend (T-0103/T-0112/T-0112a) and is referenced here only where it shapes what the UI may render. The remainder is ADR/pattern-locked or PM-absorbed from the `objednavky` precedent.

### A. User-locked at deliberation (non-negotiable)

1. **T-0116 = list + drill-into-batch, NO CSV ever (Q4).** The maker dashboard shows two surfaces: a **paged list** of payout batches affecting them, and a **per-batch detail** with a per-order breakdown (order #, product price, platform fee, **net payout**) + the **fee-invoice PDF download**. The **CSV export is NEVER shown to makers** — it is the operator's bank file containing every maker's account number across the whole batch (cross-maker PII). The maker dashboard has no CSV affordance, no CSV link, and no endpoint call that could return one. **Rejected:** surfacing a per-maker CSV slice on the dashboard (still leaks the batch-level file's existence and tempts an operator-file reuse; the fee-invoice PDF is the maker's commercial document, the CSV is the bank's); a "download my payout statement" button wired to anything CSV-shaped (the per-order breakdown table + the fee invoice already give the maker everything their accountant needs).

2. **List = pagination only, no filters at MVP (Q5).** The payout list takes a single URL-state `page` param (T-0087a pagination precedent) and **no state/date filters**. Default sort is by batch `ProcessedAt`/`CompletedAt` **DESC** (most-recent payout first) — fixed server-side (T-0112), not a UI control. **Rejected:** state + date-range filters mirroring the order list (a maker has far fewer payout batches than orders — the filter chrome costs more than it saves at MVP; pagination alone covers the volume); a client-side sort toggle (sort is a backend concern surfaced once, DESC, and the maker's operative question is "what's my latest payout", which DESC answers without a control).

3. **Completion is financially terminal — the UI reflects forward-only state (reversibility lock).** There is **no un-complete**: once a batch is `Completed` the maker sees it as paid (`Vyplaceno`) and the row never returns to `Připravujeme`. The dashboard renders state as a read-only badge with no maker action to change it (errors are corrected forward by the operator via T-0105/T-0107, off this surface). **Rejected:** any maker-facing "dispute"/"reopen" affordance on a completed batch (financially terminal — immutable Fee invoices, executed transfer, sent emails; not a maker capability); rendering a mutable/optimistic state that could imply the payout is reversible.

### B. ADR + pattern-locked (no relitigation)

- **patterns.md §B.1 — Server Components by default.** Both `page.tsx` files have no `'use client'`. The list is fully server-rendered (`<Link>` pagination); the only client island is the fee-invoice **download button** (event-handler blob fetch — §C), mirroring how `objednavky` keeps `page.tsx` server-side and isolates interactivity.
- **patterns.md §B.4 + §B.16 — all data via `apiFetch` + a hand-written helper.** New `lib/api-client-helpers/payouts-client.ts` wraps the T-0112/T-0112a endpoints and returns `Result<T, ApiError>`. No raw `fetch`, no `useEffect` data fetching anywhere in the route. (Naming follows the `orders-client.ts` / `payments-client.ts` convention.)
- **patterns.md §B.14 + ADR 0024 — SSR auth cookie forwarding.** The Server Component render forwards the maker-audience cookie to the maker host. A customer JWT replayed against the maker host 401s at the backend (ADR 0013); the frontend adds no parallel auth logic. The IDOR shield is backend-side (T-0112 resolves `makerId` from the session, projection is maker-scoped) — the frontend never passes a `makerId`.
- **ADR 0022 — NSwag is the contract.** `frontend/src/lib/api-client/` is consumed, never hand-edited (pre-commit hook). The **maker-host client regen for T-0112 + T-0112a rides those backend tickets**, not this one; T-0116 consumes the already-regenerated `GetMakerPayouts` / `GetMakerPayoutDetail` types. **No backend change and no regen in T-0116.**
- **patterns.md §B.8 — URL-state pagination via `searchParams` + `<Link>`.** `page` lives in the URL; back/forward and deep links round-trip (T-0087a `Pagination` precedent — local copy or reuse per §C).
- **patterns.md §B.5 + §B.18 — Czech-only UI via i18n keys.** Zero hardcoded Czech outside `lib/i18n/cs-CZ.ts`. New keys under `dashboard.maker.payouts.*`. Plural-neutral phrasing for the order-count line.
- **patterns.md §B.10 — `formatCzk(amountMinor, currency)`** for every money figure (batch total, product price, platform fee, net payout). No arithmetic on the client — the backend already computed each figure.
- **patterns.md §B.9 — `generateMetadata` + `notFound()`.** The detail page calls `notFound()` for a missing/foreign `batchId` (T-0112 returns one shape — no IDOR oracle), mirroring `objednavky/[orderId]`.
- **No business logic client-side.** State→label mapping (`Processing → Připravujeme`, `Completed → Vyplaceno`) is presentation routing; `FeeInvoiceId != null → show download button` is a render condition, not a rule.

### C. PM-absorbed (no user input needed)

- **State enum mapping is two values only — `Processing` / `Completed` (no `Pending`).** T-0103 collapses the lifecycle to `Processing` ("připravujeme") and `Completed` ("paid"). This **overrides US-maker-0012 AC-2's stale `Pending | Processing` enum** — there is no `Pending` row on the maker dashboard. UI mapping: `Processing → "Připravujeme"` (badge `warning`/neutral, total is a preview — operator hasn't confirmed bank), `Completed → "Vyplaceno"` (badge `success`, terminal). Reuse the `Badge` primitive (`components/ui/badge`) per the order-state precedent.
- **Completed date column.** Rows show the batch's `CompletedAt` (Czech short format via the shared date formatter) when `Completed`; for `Processing` rows the date cell shows the `ProcessedAt` "preparing since" date (or an em-dash placeholder if the backend leaves it null) — keyed, never hardcoded.
- **Detail breakdown table — Q4 columns exactly.** Per order: **order number**, **product price**, **platform fee**, **net payout** — all via `formatCzk`. No `shippingPrice` column unless T-0112's detail DTO carries it (US-maker-0012 AC-3 mentions "shipping reimbursed (if any)"; render the column iff the field exists on the wire, omit it cleanly otherwise — implementer verifies against the regenerated type, does not invent a field). A **batch summary** block sits above the table: batch number, total paid to this maker, order count, state badge, completed/processed date.
- **Fee-invoice download button — visible iff `FeeInvoiceId != null`** (US-maker-0013 AC-1; T-0112a target). Null → the button is omitted entirely (no disabled stub). The download is a **blob fetch through the runtime layer** (`apiFetch` `parse: 'blob'`, `DOWNLOAD_TIMEOUT_MS = 120_000` per the checkout-fold streaming budget) + `triggerBlobDownload` programmatic anchor named `faktura-{batchNumber}.pdf` — the **identical mechanism** `downloadShippingLabel` / `FileDownloadButton` already use in `order-actions.tsx`. A plain `<a href>` is rejected (would not carry the audience-cookie discipline — Option G below). Errors surface as an inline i18n-keyed alert next to the button.
- **Mobile cards / desktop table** — single server-rendered markup with responsive Tailwind (cards `< md`, grid "table" `≥ md`), verified at 375/768/1280; the whole list row is the `<Link>` to `/dashboard/maker/vyplaty/[batchId]` (CSS-grid row, not `<tr>` — the `order-row.tsx` pattern). No table library.
- **Empty state** — informational, onboarding-flavoured ("Zatím nemáš žádné výplaty" — payouts appear here once orders complete and the operator runs a batch). Distinct key under `dashboard.maker.payouts.empty.*`.
- **Tykání copy.** All maker-facing strings are tykání (T form) per CLAUDE.md — pending the tone open question in `docs/questions/open.md`; if it resolves to vykání, the catalog flips without code changes. Noted in the PR (T-0087a/b precedent).
- **Conditional "Výplaty" nav entry.** The maker dashboard layout (`(maker)/layout.tsx`) is **still the Phase-1 skeleton** (`return <>{children}</>` — verified; its own comment lists T-0116 as the payout addition). T-0116 does **not** build the full maker sidebar (out of scope — separate layout ticket). It adds the route and its pages; **if** a nav exists at impl time, a "Výplaty" entry is added — otherwise the nav addition is logged as a follow-up and the route ships reachable via direct URL + the order-dashboard's eventual nav. The route is correct regardless of nav state.
- **URL-state pagination** mirrors T-0087a: `?page=` clamped (≥ 1) via the `parsePositiveInt` helper precedent; `page=1` dropped from canonical URLs (patterns.md §B.8). No `pageSize` control (backend default; not a maker concern at MVP).
- **Loading + error route segments** mirror the `objednavky` skeletons (`loading.tsx` pulse placeholders, `error.tsx` last-resort boundary) so the payout route has the same fallbacks.

## Scope

### Routes (new)

- **`frontend/src/app/(maker)/dashboard/maker/vyplaty/page.tsx`** — Server Component list. `dynamic = 'force-dynamic'`; `generateMetadata` from i18n keys. Reads `searchParams.page`, calls `getMakerPayouts({ page })` once, renders the batch list (rows: batch number, total paid via `formatCzk`, order count, state badge per §C, completed/processed date) + URL-state pagination, or the empty/error state. Unauthorized → redirect to `/login?redirect=...` (T-0087a precedent).
- **`frontend/src/app/(maker)/dashboard/maker/vyplaty/payout-row.tsx`** — server-rendered row/card (cards `< md`, grid `≥ md`), the whole row a `<Link>` to the detail route.
- **`frontend/src/app/(maker)/dashboard/maker/vyplaty/[batchId]/page.tsx`** — Server Component detail. Fetches `getMakerPayoutDetail(batchId)`; `NotFound` → `notFound()` (§B.9, no IDOR oracle). Renders the batch summary block + the per-order breakdown table (Q4 columns) + the fee-invoice download button (client island) when `FeeInvoiceId != null`. `dynamic = 'force-dynamic'`; `generateMetadata` per §B.9.
- **`frontend/src/app/(maker)/dashboard/maker/vyplaty/[batchId]/fee-invoice-download.tsx`** — `'use client'` island: blob fetch via the helper + `triggerBlobDownload` named `faktura-{batchNumber}.pdf`; pending/disabled handling; inline i18n error alert. (The only client code in the route.)
- **Pagination** + **`loading.tsx`** + **`error.tsx`** — reuse / mirror the `objednavky` resolution (local copy of the `Pagination` component pointed at the `vyplaty` base path; pulse skeleton; last-resort boundary).

### API helper

- **`frontend/src/lib/api-client-helpers/payouts-client.ts`** — NEW:
  - `getMakerPayouts({ page? }): Promise<Result<MakerPayoutsPage, ApiError>>` wrapping the generated `GetMakerPayouts` (envelope unwrapped to the inner `PagedData`; `page` emitted only when `> 1` per §B.8).
  - `getMakerPayoutDetail(batchId): Promise<Result<MakerPayoutDetail, ApiError>>` wrapping `GetMakerPayoutDetail` (404 → `ApiError.type === 'NotFound'`, single shape).
  - `downloadFeeInvoice(batchId | feeInvoiceId): Promise<Result<Blob, ApiError>>` — blob fetch against the T-0112a endpoint (`parse: 'blob'`, `timeoutMs: 120_000`); NOT any generated file method (NSwag `Promise<void>` file-response gap — same reason `downloadShippingLabel` is hand-written). The endpoint-path argument follows whatever T-0112a's backend-built relative path exposes (`feeInvoiceId` vs `batchId`) — implementer matches the regenerated contract.
  - Wire-shape overrides: timestamps (`completedAt`/`processedAt`) re-typed to `string` (ISO 8601) per the `maker-orders.ts` `apiFetch`-returns-raw-JSON rationale.

### i18n

- **`frontend/src/lib/i18n/cs-CZ.ts`** — NEW keys under `dashboard.maker.payouts.*`: metadata title/description, page title/subtitle, list column headers (batch number, total, order count, state, date), state labels (`state.processing` → "Připravujeme", `state.completed` → "Vyplaceno"), order-count line (plural-neutral), empty state (title/description), error title/body/retry, pagination strings (or reuse the order-list pagination keys if shared), detail section headings (summary, breakdown), breakdown column headers (order, productPrice, platformFee, netPayout, [shipping if present]), download button label + downloading label + error, `notFound` copy for the detail page. Nav label `dashboard.maker.nav.payouts` ("Výplaty") for the conditional nav entry.

### No backend change

No endpoint, DTO, or contract change in T-0116 → no NSwag regen, no `api-client` diff (the T-0112 + T-0112a regen ships on those tickets; a regen here would be blocked by the pre-commit hook anyway).

## Alternatives Considered

- **Option A — Surface a per-maker CSV "payout statement" on the dashboard.** *Rejected per A.1* — the CSV is the operator's bank file with every maker's account number across the whole batch (cross-maker PII). Even a per-maker slice tempts operator-file reuse and leaks the batch-file's existence. The per-order breakdown table + the fee-invoice PDF give the maker's accountant everything; the CSV stays operator-only.
- **Option B — State + date-range filters on the payout list (mirror the order list).** *Rejected per A.2* — a maker has far fewer payout batches than orders; the filter chrome costs more than it saves at MVP, and DESC sort already puts the latest payout first. Pagination alone covers the volume. (Filters are a clean post-MVP add if a high-volume maker asks.)
- **Option C — Client-side sort toggle on the list.** *Rejected per A.2* — sort is a backend concern surfaced once (DESC); the maker's operative question ("what's my latest payout") is answered without a control. A toggle turns a server list into client state for zero workflow gain.
- **Option D — A maker-facing "dispute / reopen" action on a completed batch.** *Rejected per the reversibility lock* — completion is financially terminal (immutable Fee invoices, executed transfer, sent payout emails). Corrections are operator-forward (T-0105/T-0107), never a maker capability. The state badge is read-only.
- **Option E — Render `Pending` as a third state (per US-maker-0012 AC-2).** *Rejected per §C* — T-0103 collapses the lifecycle to `Processing`/`Completed`; there is no `Pending` batch the maker can see. The story's AC-2 enum is stale; this ticket's two-value mapping supersedes it.
- **Option F — Use a generated client file method for the fee-invoice download.** *Rejected per §C* — NSwag types file responses as `Promise<void>` and discards the PDF body (the `label()` gap, T-0087b Option F). The blob helper through `apiFetch` is the only working path that preserves auth, the timeout budget, and RFC7807 parsing.
- **Option G — Plain `<a href>` to the T-0112a endpoint for the download.** *Rejected* — a bare anchor navigation does not carry the audience-cookie discipline the runtime fetch layer manages; the blob helper + programmatic anchor is the established maker-download mechanism (`order-actions.tsx`).
- **Option H — A generic shared `PayoutList`/`OrderList` mega-component.** *Rejected* — the two lists have different columns (payout total/order count/batch state vs payout/customer/order state), different empty states, and different routes; a parameterised component couples surfaces that share conventions but not shape. Mirror the conventions, don't share the code (T-0087a Option G precedent).
- **Option I — Distinguish "batch not found" from "not your batch" in the detail UI.** *Rejected per §B.9* — T-0112 returns one shape (no IDOR oracle); the frontend renders one `notFound()` page.

## Out of scope

- **T-0103 backend** (`MarkPayoutBatchCompleted`, `PayoutBatch.Complete(clock)`, per-maker payout-sent emails, `BankReference` column) — upstream backend ticket; this page only renders the resulting batch state.
- **T-0112 backend** (`GetMakerPayouts` list + `GetMakerPayoutDetail` queries, IDOR scoping, NSwag maker regen) — upstream; consumed here.
- **T-0112a backend** (maker fee-invoice PDF streaming endpoint, `IInvoiceRepository.ForMaker` scope, controller-direct streaming per T-0088) — upstream; the download button hits it.
- **CSV export / operator bank file** — operator-only, never a maker surface (A.1); not built here, not linked here.
- **Admin payout dashboard / batch-completion UI** (the operator's "mark completed" form capturing `BankReference` + `PaymentDate`) — admin host, separate ticket.
- **Full maker sidebar / dashboard nav shell** — the `(maker)/layout.tsx` skeleton is replaced by a dedicated layout ticket; T-0116 adds only the route + (if a nav already exists) one entry.
- **Payout state-change history / event audit trail** on the batch — out of MVP (US-maker-0017 territory for orders; no payout analogue at MVP).
- **Bank-reference / payment-date display to the maker** — the maker sees state + total + date + invoice; whether `BankReference` is surfaced to the maker is a product question (logged via PM if asked) — not rendered at MVP.
- **401 → refresh → retry in `api-fetch.ts`** — known platform gap; unauthenticated SSR falls back to the redirect-to-login path (T-0087a precedent). Not this ticket.

## Acceptance criteria

- **AC-1** Given a logged-in maker visiting `/dashboard/maker/vyplaty`, when the page renders, then it is a Server Component (`page.tsx` has no `'use client'`), `dynamic = 'force-dynamic'` is set, data comes from `getMakerPayouts` via `apiFetch` with SSR cookie forwarding, and no `useEffect` data fetching exists anywhere in the route folder.
- **AC-2** Given the maker has payout batches, when the list renders, then each row shows: batch number, total paid to this maker via `formatCzk(totalMinor, currency)`, order count, a state badge (`Processing → "Připravujeme"`, `Completed → "Vyplaceno"` — the two-value mapping; no `Pending`), and the completed/processed date in Czech short format. The list is sorted most-recent-first (server DESC) — verified by row order on the preview.
- **AC-3** Given `?page=2`, when the list renders, then `getMakerPayouts` is called with page 2 and pagination controls reflect `PagedData` totals; junk (`page=0`, `page=abc`) clamps to page 1 without an error page (backend Validator remains authoritative). `page=1` is absent from canonical URLs.
- **AC-4** Given zero payout batches, when the list renders, then the informational empty state shows (distinct `dashboard.maker.payouts.empty.*` copy) — not an error, not a blank page.
- **AC-5** Given the list API fails (network / 5xx), when rendered, then an `Alert variant="error"` with i18n title/body + a retry link renders (no blank page, no thrown render error) — `objednavky` error-state precedent.
- **AC-6** Given the maker clicks a batch row, when the detail page (`/dashboard/maker/vyplaty/{batchId}`) renders, then it is a Server Component fetching `getMakerPayoutDetail` with SSR cookie forwarding, showing the batch summary block (number, total, order count, state badge, date) and the per-order breakdown.
- **AC-7** Given the detail page, when the breakdown table renders, then each order row shows: order number, product price, platform fee, and **net payout** — all via `formatCzk`; the figures are rendered verbatim from the backend (no client-side arithmetic anywhere in the route — grep proof). Responsive: breakdown is cards `< md`, table `≥ md`.
- **AC-8** Given a `batchId` that does not exist OR belongs to another maker, when visited, then the Next.js `notFound()` page renders — one shape for both (no IDOR oracle); `generateMetadata` title branch per §B.9.
- **AC-9** Given the detail page and `FeeInvoiceId != null`, when "Stáhnout fakturu" is clicked, then the PDF downloads as `faktura-{batchNumber}.pdf` via the blob helper (`apiFetch` `parse: 'blob'`, `timeoutMs` 120 000 — NOT a generated file method); on failure an inline i18n-keyed alert renders and the button re-enables. Given `FeeInvoiceId == null`, the button does not render (no disabled stub).
- **AC-10** Given the whole route, when inspected, then **no CSV affordance, no CSV link, and no CSV-returning call exists anywhere** (A.1 — grep proof: no `csv` in route/helper code). The maker has no path to the operator bank file.
- **AC-11** Given viewports 375 / 768 / 1280, when the list and detail render, then rows display as cards below `md` and as a grid table at `md`+, no horizontal scroll at 375, and tap targets (row links, download button) remain reachable.
- **AC-12** Hygiene gate: zero `any`, zero `console.*`, zero hardcoded Czech outside `cs-CZ.ts` (new `dashboard.maker.payouts.*` keys; tykání tone noted pending in the PR), zero edits to `lib/api-client/` (pre-commit hook), `npm run lint` + `npm run build` clean, `node scripts/check-consistency.mjs` exit 0.

## Technical notes

### Why no CSV reaches the maker

The CSV is the operator's bank-upload file: one file per batch, every maker's account number and payout amount in it. It exists for the human who runs the SEPA/CZK bank transfer, not for any individual maker. The maker's commercial document is the **Fee invoice PDF** (platform → maker, the fee they expense), and their operational view is the **per-order breakdown table**. Putting any CSV affordance on the maker dashboard would, at best, expose a file shaped like the operator's and, at worst, leak cross-maker account data. A.1 closes the door: the route has no CSV code path at all, and AC-10 greps for its absence.

### Why the maker sees only two states (no `Pending`)

T-0103 collapses the payout lifecycle to `Processing` (the operator is preparing the batch — total is a preview until the bank transfer is confirmed) and `Completed` (paid, terminal). US-maker-0012 AC-2's older `Pending | Processing` enum predates that collapse; this ticket's mapping (`Processing → Připravujeme`, `Completed → Vyplaceno`) supersedes it. There is no maker-visible `Pending` row, so the UI needs exactly two badge variants and two labels.

### Why completion is terminal in the UI

Completing a batch executes a real bank transfer, freezes the Fee invoices (immutable commercial documents), and fires the per-maker payout-sent emails (T-0103). None of that is reversible from the maker's side — or anyone's, forward-only (the reversibility lock). The dashboard therefore renders state as a read-only badge with no maker action: a completed batch is a financial fact, not a mutable record. Operator corrections happen forward via T-0105/T-0107 on the admin host, never on this surface.

### Why the download is a blob helper, not a generated method

NSwag types schema-less file responses as `Promise<void>` and discards the body — the exact gap that forced `downloadShippingLabel` to be hand-written (T-0087b Option F). The fee-invoice download reuses that proven mechanism verbatim: `apiFetch` with `parse: 'blob'` + the 120 s streaming timeout (the checkout-fold budget for worst-case mobile downlinks), then `triggerBlobDownload` turns the Blob into a named, object-URL anchor download. The audience cookie, timeout, and RFC7807 parsing all ride along — a plain `<a href>` would carry none of them.

### Why pagination-only (no filters) is the right MVP shape

A maker accrues payout batches at the cadence the operator runs them (weekly/monthly), not per order — the list is short and grows slowly. DESC-by-date puts the latest payout first, which is the maker's near-universal question. Filter chrome (state tabs, date range) would add surface and i18n for a list that fits on a screen or two. Pagination is the one control that scales with time; everything else is post-MVP if a high-volume maker asks (logged, not built).

## Risk / mitigation

- **CSV slips onto the maker surface** (an implementer reaches for an operator export helper). *Mitigation:* A.1 lock + Option A rebuttal + AC-10's grep-for-absence proof; the `payouts-client.ts` helper exposes only list/detail/invoice-blob — no CSV function to call.
- **Stale `Pending` enum render** (older docs/DTO leftover). *Mitigation:* §C two-value mapping + Option E rebuttal; the state→label function is one greppable map with exactly `Processing`/`Completed`. An unexpected enum value renders a neutral fallback label rather than crashing (defensive default), flagged in QA.
- **Download silently "succeeds" while discarding the PDF** (generated file method used by habit). *Mitigation:* §C lock + Option F rebuttal + AC-9 names the blob helper explicitly; `downloadFeeInvoice` is the only export.
- **Detail field drift** (T-0112's breakdown DTO lacks/has the shipping column). *Mitigation:* §C renders the shipping column iff the field exists on the regenerated type; the four Q4 columns are guaranteed, shipping is conditional — verified against the contract at impl time, never invented.
- **Nav addition blocked by skeleton layout.** *Mitigation:* §C — the route ships reachable by URL regardless; the "Výplaty" entry is added only if a nav already exists, otherwise logged for the layout ticket. No hard dependency on the layout being built.
- **Tykání/vykání open question resolves late.** *Mitigation:* all copy behind i18n keys; a tone flip is a catalog-only change (T-0087a/b precedent).

## Test plan reference

`docs/test-plans/T-0116.md` (stub created with this ticket) — Playwright-style manual QA plan against the Vercel preview: list render (batch rows, state badges both values, DESC order, money formatting), pagination + deep links + junk-param clamp, empty + error states, drill-into-batch breakdown (Q4 columns, money formatting, no client math), `notFound` for foreign/missing `batchId`, fee-invoice download (success → `faktura-{n}.pdf`, failure alert, button absent when `FeeInvoiceId` null), CSV-absence grep, responsive passes at 375/768/1280. No backend tests (no backend change in T-0116).

## Files touched (expected)

### New
- `frontend/src/app/(maker)/dashboard/maker/vyplaty/page.tsx`
- `frontend/src/app/(maker)/dashboard/maker/vyplaty/payout-row.tsx`
- `frontend/src/app/(maker)/dashboard/maker/vyplaty/pagination.tsx` (local copy of the `objednavky` resolution, base path `/dashboard/maker/vyplaty`)
- `frontend/src/app/(maker)/dashboard/maker/vyplaty/loading.tsx`
- `frontend/src/app/(maker)/dashboard/maker/vyplaty/error.tsx`
- `frontend/src/app/(maker)/dashboard/maker/vyplaty/[batchId]/page.tsx`
- `frontend/src/app/(maker)/dashboard/maker/vyplaty/[batchId]/fee-invoice-download.tsx`
- `frontend/src/app/(maker)/dashboard/maker/vyplaty/[batchId]/not-found.tsx` (mirrors the `objednavky/[orderId]` not-found)
- `frontend/src/lib/api-client-helpers/payouts-client.ts`
- `docs/test-plans/T-0116.md`

### Modified
- `frontend/src/lib/i18n/cs-CZ.ts` — `dashboard.maker.payouts.*` + `dashboard.maker.nav.payouts` keys appended.
- `frontend/src/app/(maker)/layout.tsx` — **only if** a nav exists at impl time, add the conditional "Výplaty" entry; otherwise unchanged (route reachable by URL, nav logged for the layout ticket).

## Commits hint

1. **`feat(T-0116): maker payouts api helper + i18n keys`** — `payouts-client.ts` (list/detail/invoice-blob) + cs-CZ catalog additions.
2. **`feat(T-0116): maker payout list page with URL pagination`** — `vyplaty/page.tsx` + `payout-row.tsx` + pagination/loading/error; state-badge + money + DESC list.
3. **`feat(T-0116): payout batch detail — breakdown table + fee-invoice download`** — `[batchId]/page.tsx` + `fee-invoice-download.tsx` + not-found; blob download wired.
4. **`test(T-0116): manual QA plan + preview fixes`** — `docs/test-plans/T-0116.md` + any QA-pass fixes from the Vercel preview run.

## Status log

- 2026-06-13 `draft` by PM. Created as the frontend cap of `feat/order-cleanup-bundle` (backend: T-0103 completion + emails + `BankReference`; T-0112 maker list/detail queries + NSwag regen; T-0112a fee-invoice streaming endpoint). Precedents: `objednavky` maker dashboard (T-0087a list + T-0087b detail) for SSR + URL pagination + mobile-cards/desktop-table + blob downloads + `formatCzk` + tykání.
- 2026-06-13 `draft → ready` by PM. User locked 5 dimensions at the deliberation (frontend-relevant): **A.1** (Q4) T-0116 = list + drill-into-batch with the order/product-price/platform-fee/net-payout breakdown + fee-invoice PDF, **CSV never shown to makers** (operator bank file, cross-maker PII — rejected per-maker CSV slice + statement download); **A.2** (Q5) list = pagination only, no filters, DESC by date (rejected order-list-style filters + client sort toggle); **A.3** (reversibility) completion is financially terminal, read-only state badge, forward-only corrections (rejected maker dispute/reopen + mutable state). PM-absorbed §C: two-value state mapping superseding US-maker-0012 AC-2's stale `Pending` enum, Q4 breakdown columns, conditional fee-invoice download via the proven blob helper, mobile-cards/desktop-table, empty state, tykání-pending copy, conditional "Výplaty" nav (layout still skeleton), URL pagination, loading/error mirrors. No NSwag regen (read-only consumer of the T-0112/T-0112a contract). **Ready for frontend** — implemented after T-0103/T-0112/T-0112a on the bundle branch.

## Definition of Ready checklist

- [x] Linked user stories present (US-maker-0012 list/detail, US-maker-0013 download).
- [x] Acceptance criteria observable + numbered (AC-1 through AC-12).
- [x] Locked design decisions captured (§A user-locked Q4/Q5/reversibility, §B ADR+pattern-locked, §C PM-absorbed).
- [x] Alternatives Considered with ≥1 rebutted alternative per locked dimension (Options A–I).
- [x] Out of scope explicit (CSV operator-only, admin completion UI, full sidebar, bank-reference display deferred).
- [x] Risk / mitigation called out (CSV leak, stale Pending enum, blob-download habit, field drift, nav skeleton, tone).
- [x] Test plan reference (docs/test-plans/T-0116.md stub, Vercel preview QA).
- [x] Files touched listed (new + modified).
- [x] Layers / ADRs / dependencies in the frontmatter; depends on T-0103 + T-0112 + T-0112a (backend); no NSwag regen here (regen rides T-0112/T-0112a).
- [x] Security-touching: NO (IDOR/GDPR/CSV-exclusion shields are backend compile-time locks this page consumes; no new auth surface).
- [x] Size: M.
- [x] No business logic client-side (state→label + invoice-present are presentation conditions; backend computes every money figure).
