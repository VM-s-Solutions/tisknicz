---
id: T-0127
title: Admin read gaps — country-config GET, admin order-detail, stalled-outbox + payout-batch LIST + 3 frontend re-wires
status: ready
size: M
owner: dotnet-backend
created: 2026-06-15
updated: 2026-06-15
depends_on: [T-0108, T-0111, T-0118b, T-0118c, T-0126]
blocks: []
user_stories: [US-admin-0006, US-admin-0007, US-admin-0009, US-admin-0014, US-admin-0016]
adrs: [0013, 0022, 0023, 0025]
phase: 5
manual_steps: []
security_touching: true
layers: [domain, appservices, infra-database, web-admin, frontend, config]
---

# T-0127 — Admin read gaps (country-config GET + order-detail + stalled-outbox/payout LIST + 3 FE re-wires)

## Context

T-0127 **closes Q-0029 (all four reads) + Q-0024 (admin order-detail)** and **completes the admin read-side**: it ships the four thin admin-host reads the T-0118 frontend slices logged against themselves, then re-wires the three T-0118 surfaces that shipped degraded because the contracts did not yet exist. The whole bundle is one cross-stack PR.

The load-bearing item is **GetCountryConfiguration GET** — it **removes the PR-2 full-replace fence**. T-0118c's country-config form starts blank because there is no GET; the `PUT /country-configurations/{code}` is a full-replace, so a blank-form save silently overwrites the whole config. T-0118c fenced this with a prominent warning banner. The GET lets the form **pre-fill** from the current row, downgrading the fence to a brief info note and — critically — letting the provider retype modal gate on an **actual provider-code change** (diff the form value against the loaded config), which finally meets T-0118c AC-4/AC-5.

The four reads **mirror the T-0111 `IAdminQueries` precedent** (AsNoTracking, `Unscoped()`, paged where listy, globally-unique Response, `[Authorize]` admin audience, two-round-trip `PagedData<T>` for the LISTs). No mutation, no migration, no new outbox events. **Zero new `BusinessErrorMessage` codes expected:** the country-config GET 404 reuses the existing `CountryConfigurationNotFound = "countryConfiguration.notFound"` (already keyed in cs-CZ — verified, used by T-0118c). The stalled-outbox LIST reuses the **exact** T-0126/T-0109 stalled predicate. T8/T9 gates are LIVE: baseline `check-consistency.mjs` is clean (147 tracked); this bundle adds no new code and no new unique index → expected zero new T8/T9 entries.

This bundle depends on the contracts/surfaces it extends: **T-0111** (the `IAdminQueries` + admin-controller precedent these reads mirror), **T-0126** (the sibling admin read-followup bundle — same admin-host read shape, shares the NSwag regen pattern), **T-0108** (the `UpdateCountryConfiguration` Response field set the GET must echo + the `CountInFlightByCountryAsync` precedent), **T-0118b** (the order-detail header that re-wires onto `GetAdminOrderDetail`), **T-0118c** (the outbox/payout/country-config/delete-user surfaces that re-wire onto the new LISTs + GET).

## Locked design decisions (§A)

Captured per `docs/process/deliberation.md`. Q-0029 resolved **option a** for all four reads (build them now); Q-0024 resolved **option a** (a real `GetAdminOrderDetail` DTO). The rest is ADR-locked or PM-absorbed from T-0111/T-0108/T-0126.

### A. Locked (non-negotiable)

1. **Country-config GET pre-fill + diff-based modal (Q-0029 GetCountryConfiguration — PRIORITY).** `GET /api/v1/country-configurations/{countryCode}` returns the current config via the existing `ICountryConfigurationRepository.GetByCodeAsync` (AsNoTracking) with **the SAME field set `UpdateCountryConfiguration`'s Response echoes** — `StandardVatRateBp`, `ReducedVatRateBp`, `InvoicingMode`, `PlatformFeeRateBp`, `DefaultShippingPriceMinor`, `DefaultPaymentProvider`, `DefaultShippingCarrier`, `DefaultRegistry`, `DefaultEmailProvider` (read `UpdateCountryConfiguration.cs` and mirror it exactly so the form round-trips). 404 → reuse `CountryConfigurationNotFound` (no new code). The FE form pre-fills SSR → the provider retype modal fires **only when a `Default*Provider` form value differs from the loaded config** (diff, not "any provider field present"). **Rejected:** a new `countryConfiguration.notFound` code (it already exists); returning a partial/extended field set (the form is a full-replace round-trip — the GET must return exactly what the PUT echoes or the form drifts); keeping the warning banner as-is (the pre-fill removes the silent-overwrite hazard).

2. **GetAdminOrderDetail = a real `AdminOrderDetailDto` (Q-0024 option a).** `GET /api/v1/admin-orders/{orderId}` returns the **full order header** — order number, state, all amounts/breakdown, country, maker (id + name), `customerEmail`, contact snapshot, lifecycle timestamps — composed over the existing `IOrderRepository.GetByIdUnscopedAsync` (already shipped for T-0105/T-0107), AsNoTracking + Unscoped, projected to a globally-unique `AdminOrderDetailDto`. **Admin is privileged → NO GDPR redaction** (carries `customerEmail` + full contact snapshot, mirroring the T-0111 list-DTO divergence from the maker surface). This replaces T-0118b's list-row-scan header. 404 (unknown / inactive id) reuses the existing `OrderNotFound` (no new code). **Rejected:** reusing the T-0082 customer/maker detail DTOs (owner-scoped, loaded via `GetByIdForCustomerAsync`/`ForMakerAsync` — not reachable on the admin host); leaving T-0118b on the bounded list-row composition (Q-0024 chose the real DTO); adding line items / message thread (out of scope — the header + the existing audit-log trail cover US-admin-0009 AC-2; the detail is the privileged header, not the full aggregate).

3. **Per-user in-flight signal = a filterable `GET /api/v1/admin-orders` query (Q-0024, the delete-user pre-disable).** **Decision: add a `customerUserId` (and `makerId`) filter to a thin admin-orders read** rather than folding a boolean into a user read — the delete-user screen needs "does this user have any order in `[PendingPayment, Paid, Accepted, Shipped, Disputed]`", and a filtered orders read is the cleanest single seam (it reuses the same `Unscoped()` projection, needs no new user-read shape, and the FE already paginates orders). The delete-user panel calls it filtered to the target user + the in-flight states; a non-empty result pre-disables the destructive button with `user.cannotDeleteWithInFlightOrders` **proactively** (the backend T-0110 gate stays authoritative). The states list matches T-0110's interlock plus `Disputed`. **Rejected:** folding a `HasInFlightOrders` boolean into a `GetUserForErase` read (a second narrow read shape for one boolean; the orders filter generalises + serves the order list too); a dedicated `/admin-orders/in-flight-count` endpoint (over-specific; the filtered LIST's emptiness IS the signal).

4. **Stalled-outbox LIST reuses the EXACT T-0126/T-0109 stalled predicate (Q-0029).** `GET /api/v1/outbox-events/stalled` (paged) → list of `{ id, eventType, aggregateId, lastErrorCode, retryCount, createdAt }` using **`ProcessedAt == null && NextRetryAt == null && LastErrorKind != OutboxErrorKind.None`** — the same WHERE as `IOutboxConsumerRepository.CountStalledAsync` (T-0126). Reuse/mirror that method's predicate verbatim (a new `GetStalledPagedAsync` on the same repo, AsNoTracking). The triage page browses + retries by **visible id** instead of count + blind by-id. **Rejected:** a looser `NextRetryAt IS NULL` alone (counts freshly-processed rows — the T-0126 §A.2 rejection); re-deriving the predicate (it must stay byte-identical to the count so the list and the tile agree).

5. **Payout-batch LIST is the GET on the existing route (Q-0029).** `GET /api/v1/payout-batches` (paged) → `{ batchId, batchNumber, state, totalAmountMinor, orderCount, makerCount, createdAt, completedAt }`, AsNoTracking + Unscoped. The existing `POST /payout-batches` is `CreatePayoutBatch` (T-0102a) — the **GET is the list (different verb, same route — fine)**. The payout page browses Processing/Completed batches + complete/CSV by **visible id**. **Rejected:** a new `/payout-batches/list` route (the verb already disambiguates; REST-correct to GET the collection); reusing the maker `IPayoutQueries` (T-0112 is per-maker scoped — the admin list is cross-maker `Unscoped()`).

### B. ADR-locked (no relitigation)

- **ADR 0013 (per-audience JWT + Unscoped admin reads).** All four reads run under the `Web.Admin` host audience; a customer/maker JWT cannot replay (cross-host 401 pinned in integration). The order-detail + payout LIST use `Unscoped()`; the Reviewer rejects any of these reachable from a non-admin host. `security_touching: true`.
- **ADR 0022 (NSwag is the contract).** Four new admin-host methods → **one NSwag regen (admin host)** in the same PR; `frontend/src/lib/api-client/` is not hand-edited (pre-commit hook). The FE re-wires are read-only consumers of the regenerated client. Re-enable the parity check via `npm run check:api`.
- **ADR 0023 (read-side queries split from write-side repositories).** These are query features mirroring T-0111's `IAdminQueries` (extend it OR add sibling one-file features under `Features/Admin/` — implementer judges by precedent). The country-config GET reads through the existing `ICountryConfigurationRepository.GetByCodeAsync`. Controller-direct is N/A (these are query features, not file streams).
- **ADR 0025 (read-only repository variants).** AsNoTracking on all four reads (the GET, the order-detail projection, both LISTs). If a read-only unscoped order lookup variant does not already exist for the detail projection, mirror the T-0111 / T-0126 read-only pattern.

### C. PM-absorbed (no user input needed)

- **Globally-unique Response names** (PR #38 NSwag convention): `GetCountryConfigurationResponse`, `GetAdminOrderDetailResponse`, `GetStalledOutboxEventsResponse`, `GetPayoutBatchesResponse`.
- **Paged where listy** (`PagedData<T>`, page clamp `[1,50]`, default 20, `CreatedAt DESC` + `Id` tie-break, two round-trips) on the stalled-outbox LIST, the payout-batch LIST, and the filtered admin-orders read; the country-config GET and the single-order detail are non-paged single-shape reads.
- **No new error codes / migrations / outbox events / i18n keys expected** — the GET 404 reuses `CountryConfigurationNotFound`, the detail 404 reuses `OrderNotFound`, all reads are pure GETs (empty LIST → `PagedData` with `TotalCount = 0`, never 404). Recurring-finding #3 (T9): **no new unique index** expected.
- **`[Authorize]` (admin scheme)** on all four endpoints; admin audience per ADR 0013.
- **Integration fixtures `MarkCreated`** per the recurring-finding convention.
- **NSwag regen — admin host only**, one commit. **`npm run check:api` re-enabled** (the regen gate).

## Scope (checklist)

### Backend (4 thin admin-host reads)

- [ ] **(1) GetCountryConfiguration GET** (PRIORITY) — `GET /api/v1/country-configurations/{countryCode}` → `GetCountryConfigurationResponse` with the **exact** `UpdateCountryConfiguration` Response field set (§A.1). Via `ICountryConfigurationRepository.GetByCodeAsync` (AsNoTracking). 404 → reuse `CountryConfigurationNotFound`. One-file query feature; `[Authorize]` admin.
- [ ] **(2) GetAdminOrderDetail** — `GET /api/v1/admin-orders/{orderId}` → `AdminOrderDetailDto` (full privileged header, §A.2) over `GetByIdUnscopedAsync`, AsNoTracking + Unscoped, no redaction. 404 → reuse `OrderNotFound`. One-file query feature; `[Authorize]` admin.
- [ ] **(2b) Per-user in-flight signal** — add `customerUserId` + `makerId` filters to a thin paged `GET /api/v1/admin-orders` read (§A.3); the delete-user panel filters to the user + in-flight states. `[Authorize]` admin.
- [ ] **(3) Stalled-outbox LIST** — `GET /api/v1/outbox-events/stalled` (paged) → `GetStalledOutboxEventsResponse` (`id, eventType, aggregateId, lastErrorCode, retryCount, createdAt`) reusing the **exact** stalled predicate (§A.4), new `GetStalledPagedAsync` on `IOutboxConsumerRepository` (AsNoTracking). `[Authorize]` admin.
- [ ] **(4) Payout-batch LIST** — `GET /api/v1/payout-batches` (paged) → `GetPayoutBatchesResponse` (`batchId, batchNumber, state, totalAmountMinor, orderCount, makerCount, createdAt, completedAt`), AsNoTracking + Unscoped (§A.5). `[Authorize]` admin.
- [ ] **NSwag regen — admin host only**, one commit (4 new methods). Re-enable `npm run check:api`.

### Frontend (3 re-wires of the T-0118 surfaces)

- [ ] **(5) Country-config form (T-0118c) re-wire** — `[code]/page.tsx` fetches GetCountryConfiguration SSR → pre-fills `country-config-form.tsx`; downgrade the warning banner to an info note ("save replaces the full config" — the PUT is still full-replace, but pre-fill removes the silent-overwrite hazard); **gate the provider retype modal on a diff** of the form value vs the loaded config (T-0118c AC-4/AC-5 now met). 404 from the GET → the blank-form + warning-banner fallback (graceful).
- [ ] **(6) Order-detail (T-0118b) re-wire** — the `[orderId]` page header uses `GetAdminOrderDetail` (real DTO) instead of the list-scan; keep the audit trail from the audit-log filter (unchanged).
- [ ] **(7) Delete-user (T-0118c) proactive in-flight pre-disable** — use the per-user in-flight signal (§A.3) to disable the delete button **PRE-call** when the user has in-flight orders (T-0118c AC-12, the proactive version replacing the reactive post-submit verdict). Backend gate stays authoritative; this is the proactive UX layer.
- [ ] **(8) Outbox + payout pages (T-0118c) re-wire** — browsable paged lists from the new LIST reads (replace the count + by-id surfaces); URL-state pagination per T-0087a.

## Acceptance criteria

### Backend reads

- **AC-1** Given a CZ config row, when `GET /api/v1/country-configurations/CZ` is called with an admin JWT, then `200` with a body carrying **exactly** `StandardVatRateBp, ReducedVatRateBp, InvoicingMode, PlatformFeeRateBp, DefaultShippingPriceMinor, DefaultPaymentProvider, DefaultShippingCarrier, DefaultRegistry, DefaultEmailProvider` (the `UpdateCountryConfiguration` Response field set, byte-round-trippable). AsNoTracking confirmed.
- **AC-2** Given an unknown `countryCode`, when the GET is called, then `404 countryConfiguration.notFound` (reused code — **no new code**).
- **AC-3** Given an order, when `GET /api/v1/admin-orders/{orderId}` is called with an admin JWT, then `200` with the full privileged `AdminOrderDetailDto` (number, state, amounts/breakdown, country, maker id+name, `customerEmail` non-empty, contact snapshot, timestamps); Unscoped (cross-tenant); unknown/inactive id → `404 OrderNotFound`. **No GDPR redaction** (admin is privileged).
- **AC-4** Given orders for a user, when `GET /api/v1/admin-orders?customerUserId=X` is called with the in-flight states, then only that user's matching orders return; an empty result is the "no in-flight" signal (`200`, `TotalCount = 0`).
- **AC-5** Given a mix of outbox rows, when `GET /api/v1/outbox-events/stalled` is called with an admin JWT, then `200` with a `PagedData` of only the **stalled** set (`ProcessedAt == null && NextRetryAt == null && LastErrorKind != None`) — the SAME rows `CountStalledAsync` counts; each row carries `id, eventType, aggregateId, lastErrorCode, retryCount, createdAt`.
- **AC-6** Given Processing + Completed batches, when `GET /api/v1/payout-batches` is called with an admin JWT, then `200` with a `PagedData` of `{ batchId, batchNumber, state, totalAmountMinor, orderCount, makerCount, createdAt, completedAt }`, Unscoped (cross-maker), `CreatedAt DESC`.
- **AC-7** Given an anonymous request OR a customer/maker JWT (`aud != admin`), when any of the four reads is called, then `401`/`403` (ADR 0013); no Unscoped read reachable from a non-admin host (cross-host probe pinned in integration).

### Frontend re-wires

- **AC-8** Given `/dashboard/admin/countries/{code}`, when it renders, then the form **pre-fills** from GetCountryConfiguration SSR; the warning banner is downgraded to an info note; the provider retype modal fires **only when a `Default*Provider` value differs from the loaded config** (T-0118c AC-4/AC-5 met); a `404` GET → the blank-form + warning-banner fallback.
- **AC-9** Given `/dashboard/admin/orders/[orderId]`, when it renders, then the header consumes `GetAdminOrderDetail` (real DTO, not the list-scan); the audit trail still reads from the audit-log filter.
- **AC-10** Given a user with any in-flight order (`PendingPayment / Paid / Accepted / Shipped / Disputed`), when the delete-user screen renders, then the destructive button is disabled **PRE-call** with the `user.cannotDeleteWithInFlightOrders` reason inline (proactive — T-0118c AC-12); the backend gate stays authoritative.
- **AC-11** Given the outbox + payout pages, when they render, then they show browsable paged lists from the new LIST reads (replacing count + by-id) with URL-state pagination (T-0087a).

### Cross-stack

- **AC-12** Build clean. `node scripts/check-consistency.mjs` exit 0 (no new T1–T9 vs the 147-tracked baseline; **zero** new `BusinessErrorMessage` codes; **zero** new unique indexes). NSwag regen committed in the same PR (admin host — 4 new methods); `frontend/src/lib/api-client/admin-api.v1.ts` types all four; no manual api-client edits (pre-commit hook); `npm run check:api` re-enabled and green.

## Test plan (stub)

Inline; no separate `docs/test-plans/T-0127.md`.

- **Unit (~8):** country-config GET happy field-set parity + 404 reuse; admin order-detail projection field-set + 404 reuse + no-redaction (`customerEmail` present); admin-orders `customerUserId` filter pass-through; stalled-outbox predicate (stalled in; processed/acknowledged/due excluded — the load-bearing predicate assertion, byte-identical to `CountStalledAsync`); payout-batch list projection.
- **Integration (~6):** (1) seeded CZ config → GET round-trips the PUT field set; unknown code → 404. (2) seeded cross-tenant order → admin detail returns the privileged header; cross-host customer JWT → 401. (3) seed orders for user X in in-flight + terminal states → `?customerUserId=X` returns only the in-flight matches. (4) seed K stalled + assorted non-stalled outbox rows (fixtures **MarkCreated**) → stalled LIST returns exactly K. (5) seed Processing + Completed batches → list returns both, Unscoped. (6) cross-host 401 on the payout + outbox LISTs.
- **Frontend:** manual QA on the admin Vercel preview — country-config pre-fill + diff-modal (provider change fires modal, VAT-only save skips it, 404 → blank fallback); order-detail header from the real DTO; delete-user proactive pre-disable with an in-flight order; outbox/payout browsable paged lists + URL-state pagination.

## Files touched (expected)

### New (backend)
- `backend/src/Makables.Core.AppServices/Features/Admin/GetCountryConfiguration.cs` (or `Features/CountryConfigurations/`)
- `backend/src/Makables.Core.AppServices/Features/Admin/GetAdminOrderDetail.cs` (+ `AdminOrderDetailDto`)
- `backend/src/Makables.Core.AppServices/Features/Admin/GetStalledOutboxEvents.cs` (+ list-item DTO)
- `backend/src/Makables.Core.AppServices/Features/Admin/GetPayoutBatches.cs` (+ list-item DTO)
- tests: `Makables.Tests/.../Admin/AdminReadGaps*Tests.cs` + `Makables.IntegrationTests/Admin/AdminReadGapsIntegrationTests.cs`

### Modified (backend)
- `backend/src/Makables.Core.Domain/Outbox/IOutboxConsumerRepository.cs` + `Infra.Database/.../OutboxConsumerRepository.cs` — add `GetStalledPagedAsync` (reuses the `CountStalledAsync` predicate)
- `backend/src/Makables.Core.Domain/Admin/IAdminQueries.cs` + `Infra.Database/Admin/AdminQueries.cs` — order-detail projection + the `customerUserId`/`makerId` admin-orders filter + payout-batch list (judge by T-0111 precedent)
- `backend/src/Makables.Web.Admin/Controllers/AdminQueriesController.cs` (+ a country-config / payout / outbox read action — judge placement by precedent)
- `frontend/src/lib/api-client/admin-api.v1.ts` + `.spec-hashes.json` — NSwag-regenerated (admin host)

### Modified (frontend)
- `frontend/src/app/(admin)/dashboard/admin/countries/[code]/page.tsx` + `country-config-form.tsx` — pre-fill + diff-modal + banner downgrade
- `frontend/src/app/(admin)/dashboard/admin/orders/[orderId]/page.tsx` — header from `GetAdminOrderDetail`
- `frontend/src/app/(admin)/dashboard/admin/users/delete-user-panel.tsx` (+ its page) — proactive in-flight pre-disable
- `frontend/src/app/(admin)/dashboard/admin/outbox/page.tsx` + `vyplaty/page.tsx` — browsable paged LISTs + URL-state pagination
- `frontend/src/lib/api-client-helpers/admin-ops-client.ts` (+ `admin-orders.ts`) — wrap the 4 new reads

## Commits hint

1. `feat(T-0127): country-config GET + admin order-detail + stalled-outbox/payout-batch LIST reads (admin)`
2. `test(T-0127): read field-set + stalled-predicate + filter + cross-host unit + integration coverage`
3. `chore(T-0127): NSwag regen (admin host — 4 new methods); re-enable check:api`
4. `feat(T-0127): re-wire country-config pre-fill + diff-modal, order-detail header, delete-user pre-disable, outbox/payout lists`

## Out of scope

- **Q-0028 (audit admin invoice-PDF reads)** — open, separate forensic-trail decision; not touched here.
- **Order line items / message thread on the admin detail** — the privileged header + the existing audit-log trail cover US-admin-0009 AC-2; the full aggregate is not added.
- **New error codes / migrations / outbox events / i18n keys** — none (reads reuse `CountryConfigurationNotFound` + `OrderNotFound`).
- **Mutating outbox / payout / config state** — read-only; the existing T-0108/0109/0103 commands own writes.

## Status log

- 2026-06-15 `draft → ready` by PM. Groomed as a single **M cross-stack** bundle closing **Q-0029 (all four admin reads)** + **Q-0024 (admin order-detail)**, both resolved **option a**, user-locked 2026-06-15. Mirrors the T-0111 `IAdminQueries` precedent (AsNoTracking, `Unscoped()`, paged-where-listy, globally-unique Response, `[Authorize]` admin audience) and the T-0126 admin-read-followup shape. §A locked: (1) country-config GET pre-fill with the **exact** `UpdateCountryConfiguration` Response field set + the diff-based provider modal — **removes the PR-2 full-replace fence**, reuses `CountryConfigurationNotFound` (no new code); (2) `GetAdminOrderDetail` = real privileged `AdminOrderDetailDto` over `GetByIdUnscopedAsync`, no GDPR redaction, 404 reuses `OrderNotFound`; (3) per-user in-flight signal = `customerUserId`/`makerId` filter on a thin admin-orders read (cleanest seam — judged over a `HasInFlightOrders` boolean), drives the delete-user proactive pre-disable; (4) stalled-outbox LIST reuses the **exact** T-0126/T-0109 predicate `ProcessedAt==null && NextRetryAt==null && LastErrorKind!=None`; (5) payout-batch LIST = the GET on the existing `/payout-batches` route (verb disambiguates the CreatePayoutBatch POST). FE re-wires items 5–8 consume the regenerated admin client (read-only). `security_touching: YES` (admin Unscoped reads + the delete-user pre-disable consumes a per-user signal). depends_on T-0108 (the Response field set + in-flight precedent), T-0111 (the admin-query precedent), T-0118b (order-detail header) + T-0118c (the surfaces re-wired), T-0126 (sibling admin read-followup + the shared stalled predicate). T8/T9 gates LIVE — baseline `check-consistency.mjs` clean (147 tracked); expected **zero** new codes, **zero** new unique indexes. NSwag regen admin host (4 new methods); `npm run check:api` re-enabled. **Ready for dotnet-backend** (4 reads + regen first; FE re-wires follow in the same PR).

## Definition of Ready

- [x] **not-duplicate** — confirmed against INDEX.md (no existing country-config GET, admin order-detail, stalled-outbox LIST, or payout-batch LIST; T-0126 ships the *counts* not the LISTs; T-0111 ships the order *list* not the *detail*; T-0118b/c logged these exact gaps as Q-0024/Q-0029) and recent ADRs.
- [x] **observable G/W/T AC** — AC-1…AC-12 are field-set-parity / status-code / predicate-equality / pre-disable proofs.
- [x] **sized M** — 4 thin reads + 1 regen + 3 FE re-wires, cross-stack, no migration, no domain mutation. M (4–16h).
- [x] **depends_on done or unblocker** — T-0108/T-0111/T-0126 (ready/landing in the admin bundles — read precedents + the shared predicate); T-0118b/T-0118c (the surfaces re-wired, ready). No chain-waiting blocker; the reads can land ahead of the FE re-wires within the PR.
- [x] **manual_steps populated** — none (read-only; no deployment / migration / webhook beyond standard QA). `manual_steps: []`.
- [x] **security_touching set** — `security_touching: yes` (admin Unscoped reads of cross-tenant order/payout data + the delete-user pre-disable consumes a per-user in-flight signal; Gate 3 secops applies).
- [x] **layers populated** — `domain, appservices, infra-database, web-admin, frontend, config`.
