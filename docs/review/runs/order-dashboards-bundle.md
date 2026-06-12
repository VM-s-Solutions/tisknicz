# Order-dashboards bundle (T-0088 + T-0089 + T-0086a/b + T-0087a/b) — Reviewer FINAL verdict

> Final PR-open pass. Supersedes `order-dashboards-bundle-draft.md`. Incorporates the Gate 9 + QA-plan audit (`order-dashboards-bundle-gate9-and-qa.md`). Sources verified against `feat/order-dashboards-bundle` head `dd50c47` on 2026-06-12.

**Commits reviewed:** `55a0f68..dd50c47` (7 commits, diff vs `master`: 48 files, +5182/−60).

## Verdict

**APPROVE WITH FOLD CONDITIONS** — all five pre-flight HIGHs are resolved in the diff, all six tickets' user-locked dimensions are honoured, the full verification matrix is green (build 0/0, 1408 unit + 181 integration, tsc, lint, next build, consistency clean 118), and no checklist row hard-fails. Approval is conditional on the fold list below (one MEDIUM test gap on T-0088 AC-7, one MEDIUM robustness fix in the thread poller, three LOWs, plus committing the QA-authored T-0087a/b plans) and on **Gate 3 SecOps sign-off for T-0088, which has not been produced yet** (`security_touching: true`; the prior two bundles each shipped a `*-gate3-security.md`). My own line-by-line IDOR trace passes (see HIGH-1) — the SecOps pass is a process requirement, not an open technical doubt.

## Pre-flight HIGHs — confirmed / refuted against the diff

| Draft finding | Disposition | Evidence |
|---|---|---|
| HIGH-1 invoice IDOR chain | **RESOLVED** | Both actions run session → ownership-scoped read-only order load → unscoped `GetByOrderIdAsync` (with the required "safe ONLY after the ownership-scoped order load above" comment) → blob (`Web.Customer/Controllers/OrdersController.cs:459-499`; `Web.Maker/Controllers/OrdersController.cs:221-265`). Maker host resolves the maker BEFORE the order load; user-without-maker-row → 404 with `Received(0)` on the order repo (maker unit test 1). `Received(0)` pins on `IInvoiceRepository.GetByOrderIdAsync` AND `IBlobStorageClient.DownloadAsync` for the not-owned path (customer test 2, maker test 2). Cache/disposition headers are set only AFTER blob success, so no cache headers exist on any 404 (pinned explicitly in maker test 3). Cross-tenant and unknown-id 404s carry the identical `order.notFound` code (`OrderInvoiceDownloadTests.GET_invoice_404_paths_are_oracle_free` arms a/c). `private, no-store` + `faktura-{InvoiceNumber}.pdf` + byte-equality pinned in both unit happy paths + integration test 1 on both hosts. |
| HIGH-2 thread reuse trap | **RESOLVED** | `components/shared/order-message-thread.tsx` contains zero audience knowledge — no host/isMaker prop, no helper imports; the three callbacks + `canPost` are injected. Customer wrapper injects `getOrderMessages`/`postOrderMessage`/`markOrderMessagesRead` (`(customer)/objednavka/[id]/order-thread-client.tsx:35-51`); maker wrapper injects the maker trio (`(maker)/.../[orderId]/order-thread-client.tsx:36-53`); both are `useCallback`-stable keyed on `orderId`. **Both** pages pass `canPost={detail.state !== OrderState.PendingPayment}` (customer page.tsx:249, maker page.tsx:241) — HIGH-2c closed. `isMine` comes from each host's DTO field via the per-route `thread-mapping.ts`. |
| HIGH-3 poller discipline | **RESOLVED (one robustness fold — NEW-2)** | Single effect: named `POLL_INTERVAL_MS = 30_000` with the Q5/Q6 lock comment + JSDoc sanction referencing B.1; `refreshInFlightRef` overlap guard; `visibilitychange` genuinely stops the timer (clearInterval, not skip); immediate refetch + resume on visible; cleanup clears interval AND removes the listener (`order-message-thread.tsx:95-161`). Mark-read on mount (`refresh(true)`), re-fired after polls delivering counterparty news. `router.refresh()` cannot double-arm: callbacks are useCallback-stable and the cleanup-first effect contract is documented in the component header. The platform now has exactly two sanctioned polling surfaces (T-0085 + this). |
| HIGH-4 binary downloads vs 8 s timeout | **RESOLVED** | One `parse: 'blob'` variant added inside `apiFetch` itself, sharing auth/timeout/RFC7807 machinery — no forked fetch path (`lib/runtime/api-fetch.ts:50-59,172-175`); default `'auto'` behaviour unchanged for every existing call site. `DOWNLOAD_TIMEOUT_MS = 120_000` with B-1 source comments on all binary helpers: `downloadOrderFile` (orders-client.ts), `downloadShippingLabel` + `downloadMakerOrderFile` (maker-orders.ts). Label filename `stitek-{orderNumber}.pdf`; 503 → `shipping.carrierUnavailable`. |
| HIGH-5 generated `Promise<void>` file methods | **RESOLVED** | Regen confirms `invoice(orderId): Promise<void>` on both hosts (body discarded — exactly the predicted NSwag gap). Grep proof: zero call sites of `.invoice(` or `.label(` anywhere in `frontend/src` outside the generated client (one doc-comment hit only). Every binary goes through the blob helper against the backend-provided URL string. |

## Draft MEDIUM/LOW dispositions

- **M-1 single request per tab:** PASS — `tabToState` maps each tab to ≤1 state; one `getMakerOrders` call per render (maker page.tsx:116-124); tabs are plain `<Link>`s; dynamic-route prefetch does not execute the page render.
- **M-2 SameSite anchors:** PASS — every attachment/invoice/label download is blob + programmatic anchor; no plain `<a href>` to API-host URLs anywhere in the diff (tracking links are external carrier URLs; `tel:` only on the contact card).
- **M-3 i18n parity:** PASS — `order.notFound` + `order.invalidTransition` added (cs-CZ.ts:454-456); `customer.orders.markDeliveredButton` added; `order.notPayableYet` correctly NOT added; all error rendering via `resolveErrorMessage` (no `t(code as MessageKey)` casts).
- **M-4 helper duplication:** PASS via justified deviation — implementer extended `orders-client.ts` instead of creating the ticket's `customer-orders.ts`; `getCustomerOrderDetail` reused, zero duplicate wrappers.
- **M-5 T-0084b regression:** PASS — PendingPayment branch preserves `OrderBreakdown` + `PayButtonClient` + `AttachmentManagerClient` + the `?attachmentsFailed` alert byte-for-byte; the placeholder banner path is cleanly deleted (its two i18n keys remain live via `not-found.tsx` + `potvrzeni/page.tsx` — not dead).
- **M-6 regen leak:** PASS — the only `lib/api-client/` hunks are the two `invoice()` methods + `.spec-hashes.json`.
- **M-7 T-0089 scope:** PASS — production diff zero; one integration test mirroring the maker twin (`IncrementUnreadFor(Maker)` ×2 → wire 2; untouched order → 0, never null).
- **M-8 phantom placeholders:** confirmed net-new routes, handled as such. **M-9 maker `number | undefined`:** PASS — `UnreadBadge({ count }: { count: number | undefined })`, no `!`.
- **LOW-1:** adequate — 404 arms (a)/(c) assert the same `order.notFound` code in the body. **LOW-2:** confirmed — `timeline.tsx` / `order-timeline.tsx` are near-identical (2nd timeline); harvest on the 3rd.
- **INFO-2 recurring-finding watch:** "ticket claims i18n parity / key reserved, catalog disagrees" is at **count = 2** (checkout HIGH-5; this bundle's `order.notFound`/`order.invalidTransition`/`markDeliveredButton`/`notPayableYet`). Below the harvest threshold (3) — next occurrence triggers the `recurring-findings.md` row + Architect ping. A mechanical BusinessErrorMessage ↔ cs-CZ parity check remains the obvious codification.

## New findings (this pass)

- **NEW-1 (MEDIUM, fold):** **T-0088 AC-7 (If-None-Match → 304) has no test anywhere in the diff** — no unit arm, no integration arm, no manual-plan row (grep: zero `IfNoneMatch`/`NotModified` hits in the new test files; the only 304 pin in the repo is `OrderAttachmentDownloadTests.cs:363` for attachments). The controller arm exists and byte-mirrors the tested T-0064 `ETagMatches` path, so risk is low, but AC-7 currently has no verifiable proof. Fold: one 304 test (unit per host asserting `StatusCodes.Status304NotModified` + disposed stream, or an integration mirror of the attachment test) before merge.
- **NEW-2 (MEDIUM, fold):** `order-message-thread.tsx:109-121` — `counterpartyNews` is assigned inside the `setMessages` updater. React state updaters must be pure and are not guaranteed to run synchronously at dispatch (the eager-state fast path is an implementation detail; StrictMode double-invokes updaters). If the updater defers, the mark-read re-fire after a counterparty-news poll is silently skipped → stale badge while the thread is open. Fold: compute the dedupe/fresh-set outside the updater (e.g., a `knownIdsRef`) and decide `counterpartyNews` deterministically before calling `setMessages`.
- **NEW-3 (LOW, fold):** T-0086a AC-5 partial — the list error states render generic copy (`error.transient` at customer page.tsx:224; `dashboard.maker.orders.error.body` on the maker list) instead of "Czech copy mapped from the error code". A backend 400 (inverted date range, reachable from the date inputs) reads as a server outage. Fold: pass `result.error` into the error component and render `resolveErrorMessage(error)`.
- **NEW-4 (LOW):** maker `filters-client.tsx` `pushFilters` drops a non-default `pageSize` from the URL on any filter change (page.tsx preserves it for tab/pagination links). Canonicalization inconsistency only; backend unaffected.
- **NEW-5 (INFO, fold):** `docs/test-plans/T-0087a.md` + `T-0087b.md` exist but are **untracked** (QA's deviation-6 remediation). The fold commit must add them, plus the four Gate-9/QA open items listed in `order-dashboards-bundle-gate9-and-qa.md §4`.
- **NEW-6 (INFO):** `potvrzeni/page.tsx:125` still renders `order.page.banner.detailComing` ("detail připravujeme") — the detail now exists. Stale copy, T-0085 surface, out of bundle scope; log for PM/l10n.

## Deviations judged (tree evidence)

1. **Baseline re-key (`--update-baseline`)** — LEGITIMATE: diff is exactly one line pair (`OrdersController.cs:284→286`, same T5), count 118→118, no new/removed entries (independently re-verified; matches the QA audit).
2. **NSwag regen as a bundle-level commit** (859f42a) instead of a T-0088 chore commit — fine; regen is in the same PR per AC-8.
3. **Customer helpers consolidated into `orders-client.ts`** (no new `customer-orders.ts`) — JUSTIFIED (draft M-4: one source of truth; ticket file-name was written pre-checkout-merge).
4. **Tab switch resets `page`** — JUSTIFIED: AC-3's testable behaviour (deep link + back/forward land on tab+page) is intact; resetting page on tab change avoids out-of-range empty states (filters precedent). Documented in `order-tabs.tsx` header.
5. **Wire-honest `typeof` guards** (`hasUrl`/`hasTimestamp`/`hasValue`/`hasCarrierRef`) — CORRECT: generated optionals type `string | undefined` while ASP.NET serialises `null`; `typeof v === 'string' && v !== ''` covers null/undefined/empty without unsafe casts. Approved pattern; candidate for a shared util if a 5th copy appears.
6. **T-0087 test-plan stubs not created by the implementer** — noted, NOT blocking: QA authored both plans in parallel (gate9-and-qa doc §3); fold commits them (NEW-5).
7. **Thread component at `components/shared/`** (tickets said `components/dashboard/` / `components/orders/` — the tickets disagreed with each other) — fine; one shared location is the point of Q6.
8. **SSR thread-prefetch failure degrades to an empty initial page** with the first poll recovering — acceptable: transient degrade, commented, not a mock.
9. **`/login` redirect target** (tickets' `/auth/login` prose) — tree-over-prose per the checkout precedent; QA discrepancy A2 should align the plans.
10. **`OrderStateValue` template-literal widening** in `state-labels.ts` — JUSTIFIED and well-made: string-literal cases preserve `never`-default exhaustiveness; both nominal host enums are assignable into the union; a 10th backend state fails tsc.
11. **`orderMessages.*` audience-neutral namespace** for the shared thread — fine (one component, one copy set; PendingPayment note phrased audience-neutrally).
12. **`OrderBreakdown` split into `OrderBreakdown` + `OrderPriceCards`** — needed for tracking-page composition; PendingPayment surface unchanged (M-5).
13. **`canPost` computed on the customer page** even though `PendingPayment` never reaches `TrackingDetail` — deliberate contract-carrying per ticket §C; correct.

## 61-AC traceability — verdict

| Ticket | ACs | Verdict |
|---|---|---|
| T-0088 (8) | AC-1..6, AC-8 **PASS** (code + unit + integration + green matrix). **AC-7 PARTIAL** — 304 arm implemented, untested (NEW-1). | 7/8 + 1 fold |
| T-0089 (4) | AC-1..4 **PASS** (field/projection/pins verified on tree; new wire pin asserts 2 and 0-not-null; test-only diff; consistency 0). | 4/4 |
| T-0086a (12) | AC-1..4, 6..12 **PASS** in code (manual-plan execution pending per process). **AC-5 PARTIAL** — error alert present, copy not code-mapped (NEW-3). | 11/12 + 1 fold |
| T-0086b (13) | AC-1..13 **PASS** in code: state branch, timeline + cancelled branch, breakdown via `formatCzk` + `BASIS_POINTS_PER_PERCENT`, tracking link rel/target, Shipped-only deliver + refresh, 409 alert, blob attachments/invoice, mark-read + newest-first + load-older (deduped), post + 2000 mirror (`ORDER_MESSAGE_MAX_LENGTH`), poll matrix, notFound/PendingPayment regression, hygiene. NEW-2 hardening applies to AC-9's re-fire path. | 13/13 |
| T-0087a (11) | AC-1..11 **PASS** in code: SSR/force-dynamic/no-useEffect, one request per tab w/ correct map + default Nové, Link tabs + deep links, GDPR row (name only, no email/mailto — grep clean), badge `>0` only, clamps 1/20/50, date/sort passthrough + junk-drop, 3 distinct empty states, cards/table, error alert + retry, hygiene. | 11/11 |
| T-0087b (13) | AC-1..13 **PASS** in code: SSR detail, one notFound shape, pure State×ShippingMethod matrix (Paid→accept; Accepted×Zásilkovna→ship w/ confirm; Accepted×PersonalPickup→handover; else none incl. PendingPayment), Cancel-issues-no-request dialog (Esc + focus-trap sentinels + backdrop), Conflict→alert+refresh, label via blob helper gated on Shipped+Zásilkovna+carrierRef, contact card name+tel only, attachments/invoice via downloadUrl, payout-prominent breakdown, thread reuse w/ maker trio + canPost, hygiene. | 13/13 |

**60/61 fully traceable in the diff; T-0088 AC-7 needs its test (fold).** Manual-plan execution on the preview is the QA step, tracked in the four plans.

## Gates 1–7 (+8/9)

| Gate | Result | Notes |
|---|---|---|
| 1 — Build | **PASS** | `dotnet build` exit 0, 0 warnings surfaced; `next build` exit 0, both new route trees emitted (`(customer)/dashboard/zakaznik/objednavky`, `(maker)/dashboard/maker/objednavky[/orderId]`). |
| 2 — Type/lint | **PASS** | `tsc --noEmit` 0 errors; `eslint` clean. Zero new `any` (generated client only), zero `console.*`, zero `!` assertions in the diff. |
| 3 — SecOps | **OUTSTANDING** | T-0088 is `security_touching: true`; no `order-dashboards-bundle-gate3-security.md` exists. Reviewer trace of the IDOR chain passes (HIGH-1) — PM must obtain the SecOps pass before merge. |
| 4 — Architecture | **PASS** | Controller-direct per ADR 0014; ADR 0013 ownership scoping; ADR 0025 read-only variants; no `SaveChangesAsync`; no country branching; frontend has zero business logic (display maps only); extension points untouched. RDD parity: no new aggregate/VO/domain service; the one repository-surface delta is documented in `IOrderRepository` XML doc + `docs/architecture/roles/invoice.md` T-0088 section (Gate 7 note shipped). |
| 5 — Tests | **PASS w/ fold** | No new must-cover pure logic backend-side (streaming controller actions); 8 unit + 3 integration shipped alongside implementation; suites green (1408 + 181). Frontend: no harness (unchanged); pure-logic candidates (tab map, action matrix, timeline) pinned by the four manual plans (T-0086a/b in-diff; T-0087a/b QA-authored, to be committed — NEW-5). No after-the-fact pure-logic tests detected. AC-7 test gap = NEW-1 fold. |
| 6 — Contract parity | **PASS** | Regen committed same PR, both hosts + `.spec-hashes.json`; zero manual client edits; only invoice methods changed. PR description must flag the contract change. |
| 7 — Docs | **PASS** | `roles/invoice.md` gains the T-0088 read-surface section; INDEX flips are PM post-merge. |
| 8 — Optimizer | **N/A (recorded)** | T-0088 is a single-resource passthrough read: 3 sequential indexed lookups + 1 blob stream, no loops, no N+1, CT propagated, AsNoTracking. No algorithmic surface to simplify. |
| 9 — Mechanical | **PASS** | `check-consistency` exit 0, clean (118 tracked); baseline re-key audited legitimate (one line pair, no smuggled entries). T7: the sanctioned poll carries its JSDoc justification; zero other interval adopters. |

## Fold list (ordered)

1. **NEW-1** — add the AC-7 304 test (T-0088, one per host or one integration mirror of `OrderAttachmentDownloadTests.cs:363`).
2. **NEW-2** — move the `counterpartyNews` computation out of the `setMessages` updater in `order-message-thread.tsx` (deterministic mark-read re-fire).
3. **NEW-3** — map list-error copy from the error code (`resolveErrorMessage`) on both list pages (T-0086a AC-5 / T-0087a AC-10 polish).
4. **NEW-5** — commit `docs/test-plans/T-0087a.md` + `T-0087b.md`; apply QA open items (T-0086a hygiene row + `/login` plan alignment; T-0086b TC-9 badge-appears leg; TC AC-13 hygiene row).
5. **NEW-4** — (optional polish) preserve non-default `pageSize` in the maker filter bar URL pushes.
6. PM: obtain **Gate 3 SecOps** sign-off for T-0088; note NEW-6 (stale potvrzeni copy) for the backlog; recurring-finding counter "i18n-parity prose vs catalog" now at 2/3.
