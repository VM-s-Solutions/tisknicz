---
id: T-0126
title: Admin read follow-ups — invoice-PDF download + overview count endpoints
status: ready
size: S
owner: dotnet-backend
created: 2026-06-15
updated: 2026-06-15
depends_on: [T-0088, T-0111, T-0102, T-0109]
blocks: []
user_stories: [US-admin-0002, US-admin-0012]
adrs: [0013, 0022, 0023, 0025]
phase: 5
manual_steps: []
security_touching: true
layers: [domain, appservices, infra-database, web-admin]
---

# T-0126 — Admin read follow-ups — invoice-PDF download + overview count endpoints

## Context

T-0126 closes the two backend gaps T-0118a's admin shell logged against itself: **Q-0026** (admin invoice-PDF download endpoint — the faktury "Stáhnout fakturu" button ships disabled-with-tooltip until it exists) and **Q-0027** (overview KPI count reads — the Processing-payouts + stalled-outbox tiles render "—" placeholders until a count source exists). Both are admin-host, read-only follow-ups; neither needs a migration, an outbox event, or (expected) a new error code.

This is a **backend-only** bundle. It SHIPS the three endpoints + one NSwag regen of the admin host and notes that the **T-0118a frontend re-enable is unblocked** — re-enabling the disabled invoice-download button and wiring the count tiles is a T-0118a frontend follow-up (rides slice-c or a tiny FE follow-up), NOT in scope here.

The endpoints unblock the **degraded surfaces** on T-0118a (US-admin-0012 AC-2 invoice download; US-admin-0002 AC-2 stalled-outbox banner) and serve **T-0118c**'s ops control-plane (the payout + outbox surfaces consume the same reads). Precedents are all on master:

- **Invoice download** — T-0088 shipped controller-direct invoice streaming on the customer + maker hosts (`OrdersController.DownloadInvoice`: ownership-scoped read-only order load → `IInvoiceRepository.GetByOrderId…` → blob stream from `BlobContainer.Invoices` with `private, no-store` + ETag/304 + `faktura-{InvoiceNumber}.pdf` disposition). T-0126 mirrors that shape on Web.Admin with **one swap**: the lookup is **Unscoped by invoice id** (admin sees ANY invoice — no owner scoping, unlike T-0088's `GetByOrderId`/`GetForMaker` ownership chains). `IInvoiceRepository.Unscoped()` / `GetByIdUnscopedAsync` are the documented admin-only escape hatch (ADR 0013); the read-only mirror is added here per ADR 0025.
- **Count reads** — T-0111 set the admin-host paged-query precedent (`AdminQueriesController`, `[Authorize]` admin audience, one-liner Mediator dispatch, AsNoTracking + `Unscoped()`). T-0102 owns `PayoutBatch` + `IPayoutBatchRepository` + `PayoutBatchState { Processing, Completed }`; T-0109/T-0029 own `OutboxEvent` + `IOutboxConsumerRepository` + the stalled-set predicate. T-0126 adds two thin count methods on those repositories + two thin admin-host count features.

No new `BusinessErrorMessage` codes expected: the invoice 404 reuses **`InvoiceNotYetRendered`** (T-0069 — "no Invoice row OR `PdfBlobPath` still null"; already has the `invoice.notYetRendered` cs-CZ key), exactly as T-0088. The count endpoints are pure reads (no failure mode beyond a clamp-free GET; empty set → `{ count: 0 }`, never 404). T8/T9 consistency gates are LIVE: this ticket is expected to add **zero** new codes and **zero** new unique indexes, so it surfaces no new T9 translator entries; if a 404 path were to need a code lacking a cs-CZ key, add the key OR reuse an existing code (no new code without parity).

## Locked design decisions (§A)

Captured per `docs/process/deliberation.md`. Both Q-items resolved **option a** (build the thin endpoints now); the rest is ADR-locked or PM-absorbed from the T-0088 / T-0111 precedents.

### A. Locked (non-negotiable)

1. **Admin invoice download is controller-direct + Unscoped-by-id (Q-0026 option a).** `GET /api/v1/admin-invoices/{invoiceId}/pdf` on Web.Admin, **controller-direct streaming** (NOT a MediatR feature — matches the T-0088 lock: a passthrough read with no validation rule and no transaction does not justify handler ceremony). Lookup: resolve the invoice by id via a **read-only Unscoped** repository read → null OR `PdfBlobPath` null → `404 invoice.notYetRendered` → stream the blob from `BlobContainer.Invoices` (path = `Invoice.PdfBlobPath` verbatim, the `IEmailSendService` precedent). Admin sees **any** invoice — no owner predicate (ADR 0013 §"Unscoped escape hatch is admin-host only"). **Rejected:** owner-scoped reads (admin is the privileged actor — there is no order/maker owner to scope to on the admin host); a MediatR `GetAdminInvoicePdf` feature (handler ceremony for a single passthrough read — T-0088 lock).

2. **The exact stalled-outbox predicate (Q-0027, matches T-0109's stalled set).** A stalled `OutboxEvent` is one the retry ladder has exhausted (admin intervention is the only legal next state): **`ProcessedAt == null AND NextRetryAt == null AND LastErrorKind != OutboxErrorKind.None`**. This is the inverse of the `ProcessOutboxFunction` due-predicate (`processed_at IS NULL AND (next_retry_at IS NULL OR next_retry_at <= now)`) narrowed to the failed-and-not-rescheduled set, and matches `OutboxEvent.ParkPendingConsumer`'s own "refuses to park a stalled row" guard (`NextRetryAt is null && LastErrorKind != None`). An **acknowledged** row has `ProcessedAt` set (`Acknowledge` sets both `ProcessedAt` + `NextRetryAt = null`), so `ProcessedAt == null` already excludes it — `AcknowledgedAt == null` is implied, not a separate clause. **Rejected:** counting by `RetryCount >= MaxTransientAttempts` (misses Permanent/Configuration/Unknown errors that stall immediately at `RetryCount == 1`); a looser `next_retry_at IS NULL` alone (would count freshly-processed rows whose `NextRetryAt` is cleared on success).

3. **Count endpoint shapes (Q-0027 option a).** Two thin admin-host count endpoints, each returning a single-field response (globally-unique Response name per the PR #38 NSwag convention):
   - `GET /api/v1/payout-batches/count?state=Processing` → `{ count }` — counts `PayoutBatch` rows in the given `PayoutBatchState`. Backed by a new `IPayoutBatchRepository.CountByStateAsync(state, ct)` (AsNoTracking, Unscoped — admin-only repo).
   - `GET /api/v1/outbox-events/stalled/count` → `{ count }` — counts the stalled set per §A.2. Backed by a new `IOutboxConsumerRepository.CountStalledAsync(ct)` (AsNoTracking).
   The overview (T-0118a) consumes these to replace the "—" placeholder tiles + drive the US-admin-0002 AC-2 stalled-outbox banner. **Rejected:** folding the counts into the existing T-0111 list responses (the overview needs a count, not a page; over-fetching a page to read `TotalCount` is the over-fetch A.2 rejection); deferring the tiles to slice-c lists entirely (the overview is the at-a-glance triage surface — it wants the counts at PR-1, not a deep-link only).

### B. ADR-locked (no relitigation)

- **ADR 0013 (per-audience JWT + Unscoped admin reads).** All three endpoints run under the `Web.Admin` host audience; a customer/maker JWT cannot replay (the cross-host 401 is pinned in the invoice integration test). The invoice read uses `IInvoiceRepository.Unscoped()`/`GetByIdUnscoped…`; the payout count uses the admin-only `IPayoutBatchRepository`. The Reviewer rejects any of these reachable from a non-admin host.
- **ADR 0022 (NSwag is the contract).** All three endpoints are new on the admin host → one NSwag regen (admin host) in the same PR; `frontend/src/lib/api-client/` is not hand-edited (pre-commit hook). The invoice endpoint ADDS a contract method (a file-download method); the two count endpoints add typed `{ count }` responses.
- **ADR 0023 (read-side queries split from write-side repositories).** The count endpoints CAN be thin query features (the T-0111 admin-query precedent) OR fold into the existing admin queries controller — implementer judges by precedent (the cleanest fit is two one-file query features under `Features/Admin/` or `Features/Payouts`/`Features/Outbox`, dispatched from a thin admin controller). The invoice download is controller-direct (§A.1), not a query feature.
- **ADR 0025 (read-only repository variants).** The invoice read only inspects `PdfBlobPath` + `InvoiceNumber` after resolving the row — use a read-only (`AsNoTracking`) Unscoped variant. Add `IInvoiceRepository.GetByIdUnscopedReadOnlyAsync` if a read-only unscoped variant does not already exist (mirror the existing `GetByIdUnscopedAsync` tracked variant + the `GetByOrderIdReadOnlyAsync` read-only mirror precedent — same `.IgnoreQueryFilters()` so admin reconciliation sees soft-deleted/anonymised rows, plus `.AsNoTracking()`). The count repository methods are AsNoTracking by construction.

### C. PM-absorbed (no user input needed)

- **Invoice 404 reuses `InvoiceNotYetRendered`** (T-0069; `invoice.notYetRendered` cs-CZ key exists). No new code — null-row and null-`PdfBlobPath` and blob-purged-race all map to it, exactly as T-0088. No `OrderNotFound` path here (no order in the lookup — the admin reads by invoice id directly).
- **Invoice headers mirror T-0088 / T-0064 exactly:** `Content-Disposition: attachment; filename="faktura-{InvoiceNumber}.pdf"` (run through the existing `EscapeFilenameForHeader` helper), `Content-Type: application/pdf`, `Cache-Control: private, no-store` + ETag/`If-None-Match` → 304 (T-0064/T-0088 PII policy — invoices carry recipient name/address/tax-ids; a logged-out request must miss every cache and 401). `File(stream, "application/pdf", enableRangeProcessing: false)`.
- **Count responses:** `GetProcessingPayoutsCountResponse(int Count)` + `GetStalledOutboxCountResponse(int Count)` (globally-unique names). The payout count GET binds `state` as `PayoutBatchState` (`[FromQuery] PayoutBatchState state = PayoutBatchState.Processing`); the stalled-outbox count takes no params.
- **No clamps / no Validator failure modes on the count endpoints** (pure GET; empty → `{ count: 0 }`). The invoice endpoint's only "validation" is the existence/render check → 404.
- **DI:** no new registration if the count methods land on already-registered repositories (`IPayoutBatchRepository`, `IOutboxConsumerRepository` both registered by T-0102/T-0029). The invoice controller resolves `IInvoiceRepository` (registered by T-0068a) + `IBlobStorageClient`.
- **`[Authorize]` (admin scheme)** on all three endpoints; admin audience per ADR 0013.
- **NSwag regen — admin host only**, one commit.
- **Q-0011 (rate-limit) TOUCHED not closed** — these are admin-JWT-gated reads (2 trusted users); flagged for secops Gate 3 re-confirmation, no scope expansion.

## Scope (checklist)

### Item 1 — Q-0026: admin invoice-PDF download (controller-direct, Unscoped)

- [ ] **`Core.Domain/Invoices/IInvoiceRepository.cs`** — add `GetByIdUnscopedReadOnlyAsync(string invoiceId, CancellationToken ct)` IF a read-only unscoped variant does not exist (read-only mirror of `GetByIdUnscopedAsync`: `.IgnoreQueryFilters()` + `.AsNoTracking()`; null for unknown ids; admin host only per ADR 0013/0025). XML doc mirrors the existing tracked variant + cites T-0126.
- [ ] **`Infra.Database/Invoices/InvoiceRepository.cs`** — implement it (copy `GetByIdUnscopedAsync` body, add `.AsNoTracking()`).
- [ ] **`Web.Admin/Controllers/` — invoice download action** (controller-direct, NO MediatR; place on a new `AdminInvoicesController` OR beside the existing admin queries surface — implementer judges by precedent):
  - `[HttpGet("api/v{version:apiVersion}/admin-invoices/{invoiceId}/pdf")]`, `[Authorize]` (admin audience), `[ProducesResponseType(200)]` + `[ProducesResponseType(304)]` + typed `Error` 401/404.
  - Body per §A.1 + §C headers; reuse the file-local `EscapeFilenameForHeader` + `ETagMatches` helpers (copy from T-0088's controller if not shared).

### Item 2 — Q-0027: overview count reads (Processing payouts + stalled outbox)

- [ ] **`Core.Domain/Payouts/IPayoutBatchRepository.cs`** — add `Task<int> CountByStateAsync(PayoutBatchState state, CancellationToken ct)` (AsNoTracking, Unscoped — admin-only repo). Implement in `Infra.Database/Payouts/PayoutBatchRepository.cs`.
- [ ] **`Core.Domain/Outbox/IOutboxConsumerRepository.cs`** — add `Task<int> CountStalledAsync(CancellationToken ct)` with the §A.2 predicate (`ProcessedAt == null && NextRetryAt == null && LastErrorKind != OutboxErrorKind.None`). Implement in `Infra.Database/Outbox/OutboxConsumerRepository.cs` (AsNoTracking).
- [ ] **Two thin admin-host count features OR controller actions** (judge by T-0111 precedent):
  - `GET /api/v1/payout-batches/count?state=Processing` → `GetProcessingPayoutsCountResponse(int Count)`.
  - `GET /api/v1/outbox-events/stalled/count` → `GetStalledOutboxCountResponse(int Count)`.
  - Each `[Authorize]` (admin audience), one-liner Mediator dispatch (if a feature) or thin controller read; globally-unique Response names; NO new error codes (read-only).

### Cross-cutting

- [ ] **NSwag regen — admin host only**, one commit (all three endpoints are new on the admin host).
- [ ] **Docs:** `docs/architecture/roles/invoice.md` (admin Unscoped download surface), `roles/payout-batch.md` + `roles/outbox.md` (the count reads); `docs/tickets/INDEX.md` flipped to `**done**` post-merge.

## Acceptance criteria

### Q-0026 — invoice download

- **AC-1** Given an invoice with `PdfBlobPath` set, when `GET /api/v1/admin-invoices/{invoiceId}/pdf` is called on Web.Admin with a valid admin JWT, then `200 OK`, body byte-equal to the blob at `BlobContainer.Invoices/{PdfBlobPath}`, `Content-Type: application/pdf`, `Content-Disposition: attachment; filename="faktura-{InvoiceNumber}.pdf"`, `Cache-Control: private, no-store`. **Unscoped — the customer/maker ownership chains do not apply; admin reads any invoice by id.**
- **AC-2** Given an unknown invoiceId, OR an invoice whose `PdfBlobPath` is null, OR a blob-download failure (purged-blob race), when the endpoint is called, then `404 invoice.notYetRendered`. No `Cache-Control` on the 404. **No new error code** (reuses T-0069's `InvoiceNotYetRendered`).
- **AC-3** Given a repeat request carrying `If-None-Match` matching the blob ETag, when called, then `304 Not Modified` with no body (T-0064/T-0088 conditional-GET parity).
- **AC-4** Given an anonymous request OR a customer/maker JWT (`aud != admin`), when the endpoint is called, then `401`/`403` — admin audience enforcement per ADR 0013. No unscoped invoice read is reachable from a non-admin host (cross-host probe pinned in integration).

### Q-0027 — count reads

- **AC-5** Given N `PayoutBatch` rows in `Processing` (and M in `Completed`), when `GET /api/v1/payout-batches/count?state=Processing` is called with an admin JWT, then `200 OK` with `{ count: N }` (the Completed rows excluded). Empty set → `{ count: 0 }`, never 404.
- **AC-6** Given a mix of outbox rows — some due (`next_retry_at <= now`), some processed, some acknowledged, and K **stalled** (`processed_at = null AND next_retry_at = null AND last_error_kind != None`) — when `GET /api/v1/outbox-events/stalled/count` is called with an admin JWT, then `200 OK` with `{ count: K }`. Acknowledged rows (which have `processed_at` set) are excluded; due/retrying rows (non-null `next_retry_at`) are excluded.
- **AC-7** Given an anonymous request OR a customer/maker JWT, when either count endpoint is called, then `401`/`403` (admin audience per ADR 0013).

### Cross-stack

- **AC-8** Build clean. Unit tests: baseline + ~5 invoice-download + ~4 count (handler/predicate). Integration: baseline + ~1 invoice (seeded invoice + blob stub → stream; cross-host 401) + ~2 count (seed N Processing + K stalled → assert counts; admin-only). `node scripts/check-consistency.mjs` exit 0 (no new T1–T9 violations vs baseline; **zero** new `BusinessErrorMessage` codes, **zero** new unique indexes). NSwag regen committed in the same PR (admin host); `frontend/src/lib/api-client/admin-api.v1.ts` types all three endpoints; no manual api-client edits (pre-commit hook). No migration, no outbox event, no email, no i18n key.

## Test plan (stub)

Inline; no separate `docs/test-plans/T-0126.md`.

- **Unit (~9):** invoice controller — happy stream (byte-equal + headers), 404 no-invoice, 404 null-`PdfBlobPath`, ETag/304, admin-only audience; count — `CountByStateAsync` counts only the given state, `CountStalledAsync` predicate (stalled counted; processed/acknowledged/due excluded — the load-bearing predicate assertion), the two count handlers/actions pass through.
- **Integration (~3):** (1) seeded invoice + `FakeBlobStorageClient` bytes → admin endpoint streams byte-equal with `faktura-{InvoiceNumber}.pdf` disposition + `private, no-store`; cross-host customer/maker JWT → 401. (2) seed N `Processing` + M `Completed` batches → `?state=Processing` returns N. (3) seed K stalled + assorted non-stalled outbox rows (integration fixtures **MarkCreated** per recurring-finding convention) → stalled count returns K; admin-only.

## Files touched (expected)

### New
- `backend/src/Makables.Web.Admin/Controllers/AdminInvoicesController.cs` (or invoice action beside the admin queries surface — implementer judges)
- Two count features OR a `Web.Admin` count controller (`Features/Admin/GetProcessingPayoutsCount.cs` + `GetStalledOutboxCount.cs`, OR controller actions — judge by T-0111 precedent)
- `backend/src/Makables.Tests/Web/Admin/Controllers/AdminInvoiceDownloadTests.cs`
- `backend/src/Makables.Tests/...Count*Tests.cs` (count predicate + handler/action)
- `backend/src/Makables.IntegrationTests/Admin/AdminInvoiceDownloadIntegrationTests.cs` + `AdminOverviewCountsIntegrationTests.cs`

### Modified
- `backend/src/Makables.Core.Domain/Invoices/IInvoiceRepository.cs` — add `GetByIdUnscopedReadOnlyAsync` (if absent)
- `backend/src/Makables.Infra.Database/Invoices/InvoiceRepository.cs` — implement it
- `backend/src/Makables.Core.Domain/Payouts/IPayoutBatchRepository.cs` — add `CountByStateAsync`
- `backend/src/Makables.Infra.Database/Payouts/PayoutBatchRepository.cs` — implement it
- `backend/src/Makables.Core.Domain/Outbox/IOutboxConsumerRepository.cs` — add `CountStalledAsync`
- `backend/src/Makables.Infra.Database/Outbox/OutboxConsumerRepository.cs` — implement it
- `frontend/src/lib/api-client/*` — NSwag-regenerated (admin host); committed same PR
- `docs/architecture/roles/invoice.md`, `roles/payout-batch.md`, `roles/outbox.md` — read-surface notes

## Commits hint

1. `feat(T-0126): admin invoice-PDF download (Unscoped controller-direct) + read-only unscoped invoice lookup`
2. `feat(T-0126): Processing-payouts + stalled-outbox count endpoints (admin)`
3. `test(T-0126): invoice-download + count unit + integration coverage`
4. `chore(T-0126): NSwag regen (admin host — 3 new endpoints)`

## Out of scope

- **T-0118a frontend re-enable** — re-enabling the disabled "Stáhnout fakturu" button + wiring the count tiles is a T-0118a frontend follow-up (rides slice-c or a tiny FE follow-up). This bundle is **backend-only**; it ships the endpoints and notes the FE re-enable is now unblocked.
- **Admin invoice search / list** — T-0111 already ships `GET /api/v1/admin-invoices` (the list); T-0126 adds only the per-invoice PDF download.
- **A stalled-outbox LIST / triage view** — T-0118c's outbox surface (the count here drives the overview tile + banner; the list is separate).
- **New error codes / migrations / outbox events / i18n keys** — none ship (read-only; reuses `InvoiceNotYetRendered`).
- **Resetting / mutating outbox or payout state** — read-only; the count is a pure aggregate.

## Status log

- 2026-06-15 `draft → ready` by PM. Groomed as a single S backend follow-up closing **Q-0026** (admin invoice-PDF download) + **Q-0027** (overview count reads), both resolved **option a**. Precedents read + locked: T-0088 controller-direct invoice streaming (mirrored with an Unscoped-by-id swap — admin sees any invoice), T-0111 admin-query controller + `[Authorize]` admin-audience convention, T-0102 `IPayoutBatchRepository` + `PayoutBatchState`, T-0109/T-0029 `OutboxEvent` stalled-set predicate. §A locked: (1) controller-direct Unscoped invoice download reusing `InvoiceNotYetRendered` + T-0064/T-0088 PII headers (`private, no-store` + ETag/304 + `faktura-{InvoiceNumber}.pdf`); (2) exact stalled predicate `ProcessedAt == null AND NextRetryAt == null AND LastErrorKind != None` (matches `ParkPendingConsumer`'s stall guard; acknowledged rows excluded by `ProcessedAt == null`); (3) two thin `{ count }` endpoints with globally-unique Response names. `security_touching: YES` (admin file streaming + Unscoped reads). depends_on: T-0088 (streaming precedent), T-0111 (admin query precedent), T-0102 + T-0109 (the counted entities). No migration, no new code, no new index expected → T8/T9 gates clean (doc-grooming verified `check-consistency` exit 0 at baseline). NSwag regen admin host (all 3 endpoints new on it). **Backend-only — T-0118a FE re-enable unblocked, not in scope.** **Ready for dotnet-backend.**

## Definition of Ready

- [x] **not-duplicate** — confirmed against INDEX.md (no existing admin invoice-download or count endpoint; T-0111 ships the invoice LIST not the per-id PDF; T-0109 mutates outbox but adds no count) and recent ADRs.
- [x] **observable G/W/T AC** — AC-1…AC-8 are byte-equality / status-code / `{ count: N }` / header proofs.
- [x] **sized S** — three thin read endpoints + 3 repo read methods + ~12 tests + one regen; no migration, no domain mutation. S (<4h).
- [x] **depends_on done or unblocker** — T-0088 (done), T-0111 / T-0102 / T-0109 (ready/landing in the admin bundle PR); all are read-precedent or counted-entity owners, no chain-waiting blocker.
- [x] **manual_steps populated** — none (no deployment / migration / webhook / manual verification beyond standard QA). `manual_steps: []`.
- [x] **security_touching set** — `security_touching: yes` (admin file streaming of PII-bearing invoices + Unscoped admin reads; Gate 3 secops mandatory; Q-0011 flagged for re-confirmation).
- [x] **layers populated** — `domain, appservices, infra-database, web-admin`.
