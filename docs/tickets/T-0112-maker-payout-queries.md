---
id: T-0112
title: Maker payout queries — paged list + per-order detail + order outbox-events
status: ready
size: M
owner: dotnet-backend
created: 2026-06-13
updated: 2026-06-13
depends_on: [T-0081, T-0101, T-0102b, T-0103]
blocks: [T-0112a, T-0116]
user_stories: [US-maker-0012, US-maker-0013, US-maker-0017]
adrs: [0009, 0013, 0014, 0020, 0023]
phase: 4
manual_steps: []
security_touching: true
layers: [domain, appservices, infra-database, web-maker]
---

# T-0112 — Maker payout queries (paged list + per-order detail + order outbox-events)

## Context

T-0112 is the **read-side of the maker money surface**: it ships the three projection queries the maker dashboard's "Výplaty" (payouts) page and the per-order "události" (events) drawer consume. It is the direct read mirror of T-0081 (`GetMakerOrders`) one layer up the money chain — same `IDOR-twice` discipline, same `AsNoTracking` + `IgnoreAutoIncludes` projection convention, same globally-unique response naming (post-PR #38 NSwag collision fix), same `PagedData<T>` envelope, same fixed ULID-stable sort. Where T-0081 projects the maker's orders, T-0112 projects the maker's **payout batches** (the weekly `PayoutBatch` rows that paid them), the **per-order breakdown inside one batch**, and the **outbox audit trail for one of the maker's orders**.

The three queries satisfy three stories:

- **US-maker-0012 (View payouts)** — the paged batch list (AC-1: batch number, processed/completed date, total paid to *this* maker, order count, link to fee invoice) + the drill-into-batch per-order breakdown (AC-3: order number, product price, platform fee deducted, net payout).
- **US-maker-0013 (Download fee invoice)** — the list + detail surface the `FeeInvoiceId` for *this* maker's Fee invoice on the batch; the actual streaming download is **T-0112a** (controller-direct stream per T-0088, reusing `IInvoiceRepository.ForMaker` IDOR scope). T-0112 only exposes the id.
- **US-maker-0017 (Track outbox events for my orders)** — the maker-scoped, read-only outbox-events query for one order: event type + delivery status + timestamp only. **No payload internals leaked** (the `PayloadJson` carries customer email, addresses, provider refs — none of it surfaces). No maker retry — admin-only per US-maker-0017 AC-2 + the out-of-scope note.

This is a **read-only ticket**: no migration, no new `BusinessErrorMessage` code, no outbox event, no entity mutation. The write-side completion that *produces* the data this ticket reads (`PayoutBatch.Complete()`, `Order.Complete(clock)`, the per-maker payout-sent email) ships in **T-0103** (the immediate sibling in the payout-completion bundle, also user-locked at the 2026-06-13 deliberation). T-0103's locks are recorded in `docs/questions/open.md` and summarized in §B below for the reader's context, but **T-0112 implements none of them** — it consumes the columns T-0103 fills (`PayoutBatch.CompletedAt`, the `BankReference` column, `Order.State == Completed`).

A note on the **US-maker-0012 AC-2 staleness**: that AC references a `Pending` / `Processing` "připravujeme" badge and a stale 3-value enum. The `PayoutBatchState` enum on master has **exactly two values** — `Processing` and `Completed` (per the T-0101 lock A.4: the batch is born directly in `Processing`, no observable `Pending`). T-0112 surfaces the live two-state enum; T-0116 (the frontend) renders `Processing → "připravujeme"` and `Completed → "vyplaceno"`. AC-2's three-value enum is **superseded** by the shipped domain; this ticket does not resurrect `Pending`.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked the payout-completion bundle (T-0103 + T-0112 + T-0112a + T-0116) at the **2026-06-13 deliberation** (Q1–Q5 + reversibility). The locks below are the subset that shapes T-0112's read surface; T-0103's write-side locks are in §B for context only.

### A. User-locked at the 2026-06-13 deliberation (non-negotiable for T-0112)

1. **(Q4) Maker payout detail = list + drill-into-batch with per-order breakdown + fee-invoice PDF download.** The detail query returns, per claimed order of *this* maker in *this* batch: order number, product price, shipping price, platform fee, net payout — plus the maker's Fee-invoice id for the download link. **The operator CSV is NEVER shown to makers.** The CSV is the bank-transfer file containing *every* maker's account number and payout — cross-maker PII (other makers' IBANs + amounts). It is an admin-only artifact (T-0102b); the maker surface exposes only the Fee-invoice PDF (the maker's own commercial document) and the per-order breakdown. **Rejected:** surfacing the CSV row to the maker (catastrophic cross-maker PII leak — one maker would see every other maker's bank account); surfacing a "your CSV line" extract (still leaks the operator-file shape + invites a "show me the rest" support loop — the Fee invoice + per-order breakdown already give the maker everything they legally need).

2. **(Q5, PM default) Maker payout list = pagination only, no state/date filters at MVP.** Default sort `CompletedAt DESC` then `BatchNumber DESC` (ULID-stable). A maker has a handful of batches; a filter set is speculative complexity. **Rejected:** state filter (`?state=Completed`) — at MVP the maker wants "show me my payouts, newest first"; the two-state enum doesn't justify a filter UI; add when a real workflow needs it (mirrors T-0081 §A.3's "no pseudo-state" stance); date-range filter — same speculative-complexity rejection as the state filter.

3. **(reversibility) No un-complete; completion is financially terminal.** This is a **T-0103 write-side lock** but it shapes T-0112's contract: the list/detail surface NEVER exposes an "un-complete" or "reopen" affordance, and the `Completed` state is presented as terminal. Completion is irreversible because it issues immutable Fee invoices, executes the bank transfer, sends the payout-sent emails, and gates the refund-acknowledgement — there is no clean rollback. Errors are corrected **forward** via T-0105 (refund) / T-0107 (manual state change). T-0112's read surface reflects this: no mutation method, no reopen field, `Completed` rows are immutable history. **Rejected:** an "un-complete" maker-visible affordance (there is no write-side un-complete — exposing one would be a dead button); a "dispute this payout" field on the row (disputes are an admin support channel, not a payout-row affordance at MVP).

### B. T-0103 write-side locks (context only — T-0112 implements NONE of these)

Recorded so the reader understands the columns T-0112 reads. T-0103 ships them; T-0112 consumes them.

- **(Q1)** `MarkPayoutBatchCompleted` captures `BankReference` (string, stored on a **new `PayoutBatch.BankReference` column** T-0103 migrates) + `PaymentDate` (`DateOnly`, optional → `CompletedAt` defaults to `clock.UtcNow` if omitted). Both prompted in the admin UI. T-0112 surfaces `CompletedAt` (already on master per T-0101) on the list/detail; `BankReference` is **NOT** surfaced to the maker at MVP (operator-internal reconciliation field — out of scope §below).
- **(Q2)** Completion materializes the batch's claimed order ids and loops `Order.Complete(clock)` directly in the ONE handler under one UoW — **no per-order `mediator.Send`** (the Q-0008 MARS lesson: orders are already in batch scope, the transition is a pure entity method, fan-out via mediator would re-open the `MultipleActiveResultSets` hazard). `PayoutBatch.Complete()` **does not exist yet** — T-0103 ships it with its only caller (no dead code).
- **(Q3, PM default)** One payout-sent email **per maker per batch** (group claimed orders by `MakerId` in the same completion loop): summarizes batch number, total paid to that maker, order count, fee-invoice link. Reuses the `PayoutFeeInvoiceMakerEmailPayload` precedent (PascalCase JSON, invoice id looked up at send time). Email subject uses **double-brace** interpolation tokens per the Q-0017 lesson.
- **(idempotency, PM-absorbed)** T-0103 is **Silent-Success on already-`Completed` re-call** (no re-transition, no re-emit) — mirrors the webhook-idempotency convention. Forward-only; no un-complete.

### C. ADR-locked (no relitigation)

- **ADR 0009 (payouts).** The batch total a maker sees is the **sum of *this* maker's `Order.MakerPayoutAmountMinor`** across the batch — NOT `PayoutBatch.TotalAmountMinor` (which is the operator's whole-batch wire total across *all* makers). A multi-maker batch pays N makers; each maker sees only their slice. The list query computes the per-maker slice with a `GROUP BY`/`SUM` over the batch's claimed orders filtered to `o.MakerId == makerId`.
- **ADR 0013 (per-audience JWT + scoped repo split).** All three endpoints run under the `Web.Maker` host audience; a customer JWT cannot replay against the maker host. The read interface follows the `ForMaker` scoping convention — every method takes a non-optional `makerId` resolved from the session **before** dispatch. The IDOR shield is enforced **twice** (handler resolves `makerId` from session via `IMakerRepository.GetByUserIdAsync`; the projection's `Where` re-filters), exactly as T-0081 + the T-0081-verbatim convention require.
- **ADR 0014 (UoW pipeline).** Read queries → no `UnitOfWorkPipelineBehavior` write. `ValidationPipelineBehavior` still runs (clamps `Page >= 1`, `PageSize ∈ [1, 50]`). No `SaveChangesAsync` (none would be called — the pipeline is the only writer and it skips queries).
- **ADR 0020 (outbox).** The outbox-events query is **read-only + maker-scoped**: it projects `EventType` + a derived delivery-status enum + `CreatedAt` from `OutboxEvent` rows whose `AggregateId` is one of the maker's orders. It surfaces NO `PayloadJson`, NO `LastErrorCode`, NO retry affordance — admin owns retry (US-maker-0017 AC-2). The status is derived: `Processed` (`ProcessedAt != null` and not acknowledged-stalled) / `Scheduled` (`NextRetryAt != null`, `ProcessedAt == null`, kind transient) / `Stalled` (`NextRetryAt == null`, `ProcessedAt == null`, kind `Permanent`/`Configuration`).
- **ADR 0023 (read-side query interface separation).** A **new `IPayoutQueries` interface** in `Core.Domain/Payouts/` holds all three methods (the payout read surface has no existing queries interface; unlike T-0081 which extended `IOrderQueries`, the outbox-events method co-locates here because it is part of the same maker-money read bundle and shares the `makerId`-scoping discipline). `IPayoutBatchRepository` + `IOrderRepository` + `IOutbox` remain write/admin-scoped — untouched.
- **One-file feature shape.** Three feature files in `Core.AppServices/Features/Payouts/`: `GetMakerPayouts.cs`, `GetMakerPayoutDetail.cs`, `GetMakerOutboxEventsForOrder.cs`. Each contains nested `Query`, `Validator`, `Handler`, and a globally-unique `…Response` record.
- **`BusinessResult<T>` for expected failures.** No-maker-row → `NotFound(MakerNotFound)` (existing, T-0049a). Unknown/cross-maker batch id on detail → `null` projection → `NotFound` (reuse existing — see §C below, no new code). Unknown/cross-maker order id on outbox-events → empty page (paged shape, not an oracle). Validation → 400 via pipeline.
- **`.AsNoTracking()` + `.IgnoreAutoIncludes()` on every read.** T-0081-verbatim.

### D. PM-absorbed (no user input needed)

- **`IDOR shield enforced twice`** on all three queries (T-0081-verbatim): handler resolves `makerId` from session; projection `Where` re-filters. The list filters `PayoutBatch` rows to those containing ≥1 order of this maker; the detail filters to a batch containing an order of this maker AND projects only this maker's orders; the outbox-events query filters to events whose `AggregateId` is an order owned by this maker.
- **Globally-unique response names** (T-0081-verbatim, post-PR #38): `GetMakerPayoutsResponse`, `GetMakerPayoutDetailResponse`, `GetMakerOutboxEventsResponse`. Avoids the NSwag TS class collision.
- **Per-maker batch total derivation:** `MakerTotalPaidMinor` = `SUM(o.MakerPayoutAmountMinor) WHERE o.PayoutBatchId == batchId AND o.MakerId == makerId`; `OrderCount` = the matching row count. Computed in the projection (no denormalized column — `PayoutBatch.TotalAmountMinor` is the cross-maker total and is **never** surfaced to a single maker).
- **`FeeInvoiceId` resolution:** the maker's Fee invoice for the batch via `Invoice` where `PayoutBatchId == batchId AND MakerId == makerId AND Type == Fee` (the denormalized `Invoice.MakerId` + `Invoice.PayoutBatchId` columns, populated by T-0101 — confirmed present on master). Nullable: a batch row created before its Fee invoices rendered (race) surfaces `null` and T-0116 hides the download CTA until it populates.
- **Sort (list):** fixed `CompletedAt DESC, BatchNumber DESC`. `CompletedAt` is nullable (`Processing` batches have none); nulls sort last under `DESC` via the EF translation, so in-flight `Processing` batches appear after `Completed` ones — acceptable at MVP (a maker has at most one in-flight batch). No exposed sort selector. ULID-on-`BatchNumber` is not strictly time-ordered (it is `VYP-{CC}-{YYYY}-W{ww}`), but it is lexicographically week-ordered, which is a faithful secondary tiebreaker.
- **PageSize clamp:** `Page >= 1` (default 1), `PageSize ∈ [1, 50]` (default 20). Validator enforces. Same for the outbox-events pagination.
- **Maker authorization:** `[Authorize]` on the controller(s) + maker scheme enforced by host audience. Resolve `MakerId` via `IMakerRepository.GetByUserIdAsync` (existing, T-0049a).
- **NSwag regen:** **maker host only** (the gate T-0116 consumes). Customer / admin / public hosts untouched. The new `GET /api/v1/payout-batches`, `/payout-batches/{id}`, `/orders/{orderId}/events` endpoints + the three response types + the four new DTOs appear in the generated maker client. (T-0112a rides the **same** maker-host regen for its streaming download endpoint.)
- **No new error codes, migrations, outbox events, i18n keys.** `MakerNotFound` (T-0049a) for the no-maker-row path. Detail `null` → reuse the existing not-found envelope (no payout-specific code — a cross-maker batch id and an unknown batch id return the same `NotFound` so batch ids aren't enumerable across makers). Validation flows through the existing pipeline shape. i18n keys for T-0116 ship in T-0116 (frontend ticket).

## Scope

### Domain layer

- **`Core.Domain/Payouts/IPayoutQueries.cs`** — NEW read-side interface (ADR 0023). Three methods:
  ```csharp
  Task<PagedData<MakerPayoutListItemDto>> GetMakerPayoutsPagedAsync(
      string makerId, int page, int pageSize, CancellationToken ct);

  Task<MakerPayoutDetailDto?> GetMakerPayoutDetailAsync(
      string makerId, string batchId, CancellationToken ct);

  Task<PagedData<MakerOutboxEventDto>> GetMakerOutboxEventsForOrderAsync(
      string makerId, string orderId, int page, int pageSize, CancellationToken ct);
  ```
  XML doc carries the `IOrderQueries`-style owner-scoping contract: every method bakes the maker IDOR predicate into the EF `Where`; cross-tenant probes surface as empty page (list/events) or `null` (detail), never as an oracle. Projections are `AsNoTracking` + `IgnoreAutoIncludes`; the global `Auditable` soft-delete filter applies (no `IgnoreQueryFilters`).
- No new domain entity, no new enum on the domain side except the **DTO-local delivery-status enum** (see AppServices). `PayoutBatchState` (Processing/Completed) exists (T-0101). `OutboxErrorKind` exists (ADR 0014). No edit to `IPayoutBatchRepository` / `IOrderRepository` / `IOutbox`.

### AppServices layer

Four NEW DTOs (sealed records) under `Core.AppServices/Features/Payouts/DTOs/`:

- **`MakerPayoutListItemDto`**:
  ```csharp
  public sealed record MakerPayoutListItemDto(
      string BatchId,
      string BatchNumber,
      PayoutBatchState State,
      long MakerTotalPaidMinor,   // SUM of THIS maker's MakerPayoutAmountMinor in the batch
      int OrderCount,             // THIS maker's claimed orders in the batch
      string Currency,
      DateTimeOffset? CompletedAt, // null while Processing
      string? FeeInvoiceId);      // THIS maker's Fee invoice for the download link; null until rendered
  ```
  XML doc states the deliberate absence of `PayoutBatch.TotalAmountMinor` (cross-maker total — never surfaced) + `BankReference` (operator-internal) + any CSV reference (cross-maker PII, Q4).
- **`MakerPayoutDetailDto`**:
  ```csharp
  public sealed record MakerPayoutDetailDto(
      string BatchId,
      string BatchNumber,
      PayoutBatchState State,
      long MakerTotalPaidMinor,
      string Currency,
      DateTimeOffset? CompletedAt,
      string? FeeInvoiceId,
      IReadOnlyList<MakerPayoutOrderLineDto> Orders);
  ```
- **`MakerPayoutOrderLineDto`** (the per-order breakdown — Q4):
  ```csharp
  public sealed record MakerPayoutOrderLineDto(
      string OrderId,
      string OrderNumber,
      long ProductPriceMinor,     // Order.ProductPriceAmountMinor
      long ShippingPriceMinor,    // Order.ShippingPriceAmountMinor (reimbursed pass-through)
      long PlatformFeeAmountMinor, // deducted
      long MakerPayoutAmountMinor, // net = product − fee + shipping
      string Currency);
  ```
  XML doc notes the reconciliation invariant: `ProductPriceMinor − PlatformFeeAmountMinor + ShippingPriceMinor == MakerPayoutAmountMinor` (per `PricingBreakdown`), and `SUM(line.MakerPayoutAmountMinor) == MakerTotalPaidMinor`. Customer email / address deliberately NOT projected (T-0081 §A.2 GDPR lock carries forward — the payout breakdown is money, not contact).
- **`MakerOutboxEventDto`** (US-maker-0017):
  ```csharp
  public sealed record MakerOutboxEventDto(
      string EventType,
      OutboxDeliveryStatus Status,
      DateTimeOffset OccurredAt);   // OutboxEvent.CreatedAt
  ```
  Plus a NEW DTO-side enum `OutboxDeliveryStatus { Processed, Scheduled, Stalled }` (the maker-safe projection of `OutboxEvent`'s internal state — derived, see Infra). **No `PayloadJson`, no `LastErrorCode`, no `RetryCount`** — the maker sees *that* an event happened + its delivery health, never the payload (which carries customer email / address / provider refs).

Three NEW one-file features under `Core.AppServices/Features/Payouts/`:

- **`GetMakerPayouts.cs`** — `Query(int Page = 1, int PageSize = 20) : IRequest<BusinessResult<GetMakerPayoutsResponse>>`; `GetMakerPayoutsResponse(PagedData<MakerPayoutListItemDto> Payouts)`; `Validator` (`Page >= 1`, `PageSize ∈ [1,50]`); `Handler(IUserSessionProvider, IMakerRepository, IPayoutQueries)`:
  1. `var userId = sessionProvider.RequireUserId();` → `var maker = await makerRepository.GetByUserIdAsync(userId, ct);` → null → `Failure(NotFound(MakerNotFound))`.
  2. `var paged = await payoutQueries.GetMakerPayoutsPagedAsync(maker.Id, query.Page, query.PageSize, ct);`
  3. `Success(new GetMakerPayoutsResponse(paged))`.
- **`GetMakerPayoutDetail.cs`** — `Query(string BatchId) : IRequest<BusinessResult<GetMakerPayoutDetailResponse>>`; `GetMakerPayoutDetailResponse(MakerPayoutDetailDto Detail)`; `Validator` (`BatchId` not empty); `Handler`:
  1. Resolve maker (as above) → null → `NotFound(MakerNotFound)`.
  2. `var detail = await payoutQueries.GetMakerPayoutDetailAsync(maker.Id, query.BatchId, ct);` → null → `Failure(NotFound(...))` (the existing not-found envelope; cross-maker + unknown id return the same shape — no oracle).
  3. `Success(new GetMakerPayoutDetailResponse(detail))`.
- **`GetMakerOutboxEventsForOrder.cs`** — `Query(string OrderId, int Page = 1, int PageSize = 20) : IRequest<BusinessResult<GetMakerOutboxEventsResponse>>`; `GetMakerOutboxEventsResponse(PagedData<MakerOutboxEventDto> Events)`; `Validator` (`OrderId` not empty, `Page >= 1`, `PageSize ∈ [1,50]`); `Handler`:
  1. Resolve maker → null → `NotFound(MakerNotFound)`.
  2. `var paged = await payoutQueries.GetMakerOutboxEventsForOrderAsync(maker.Id, query.OrderId, query.Page, query.PageSize, ct);` (cross-maker / unknown order → empty page — the IDOR predicate is in the projection).
  3. `Success(new GetMakerOutboxEventsResponse(paged))`.

No `SaveChangesAsync()` in any handler (pipeline skips UoW for queries).

### Infrastructure / Database layer

- **`Infra.Database/Payouts/PayoutQueries.cs`** — NEW class implementing `IPayoutQueries`, primary-constructor DI on `MakablesDbContext`. Three methods:

  **`GetMakerPayoutsPagedAsync`** — the maker's batches with the per-maker slice:
  - Base: the distinct set of `PayoutBatch` ids that have ≥1 order of this maker. Derive from `Orders` (the join seed) rather than `PayoutBatch` directly, because the per-maker total/count are SUM/COUNT over the maker's claimed orders:
    ```csharp
    var grouped = dbContext.Orders
        .AsNoTracking()
        .IgnoreAutoIncludes()
        .Where(o => o.MakerId == makerId && o.PayoutBatchId != null)
        .GroupBy(o => o.PayoutBatchId!)
        .Select(g => new
        {
            BatchId = g.Key,
            MakerTotalPaidMinor = g.Sum(o => o.MakerPayoutAmountMinor),
            OrderCount = g.Count(),
        });
    ```
  - JOIN the grouped set to `PayoutBatch` (for `BatchNumber`, `State`, `Currency`, `CompletedAt`) and LEFT JOIN to `Invoice` (for `FeeInvoiceId` — `i.PayoutBatchId == batchId && i.MakerId == makerId && i.Type == Fee`, `.Select(i => i.Id).FirstOrDefault()`).
  - `OrderByDescending(x => x.CompletedAt).ThenByDescending(x => x.BatchNumber)`.
  - `CountAsync` on the grouped set for `TotalCount`; `Skip/Take`; `Select` into `MakerPayoutListItemDto`; `ToListAsync`. Return `PagedData<MakerPayoutListItemDto>`.

  **`GetMakerPayoutDetailAsync`** — IDOR via "the batch contains an order of this maker", project only this maker's orders:
  - Guard: `var anyForMaker = await dbContext.Orders.AsNoTracking().AnyAsync(o => o.PayoutBatchId == batchId && o.MakerId == makerId, ct);` → if false, return `null` (unknown OR cross-maker — same shape, no oracle).
  - Load the batch header (`PayoutBatch` where `Id == batchId`, `AsNoTracking`) for `BatchNumber`/`State`/`Currency`/`CompletedAt`.
  - Load this maker's claimed orders in the batch:
    ```csharp
    var lines = await dbContext.Orders
        .AsNoTracking()
        .IgnoreAutoIncludes()
        .Where(o => o.PayoutBatchId == batchId && o.MakerId == makerId)
        .OrderByDescending(o => o.OrderNumber)
        .Select(o => new MakerPayoutOrderLineDto(
            o.Id,
            o.OrderNumber,
            o.ProductPriceAmountMinor,
            o.ShippingPriceAmountMinor,
            o.PlatformFeeAmountMinor,
            o.MakerPayoutAmountMinor,
            o.Currency))
        .ToListAsync(ct);
    ```
  - `MakerTotalPaidMinor = lines.Sum(l => l.MakerPayoutAmountMinor);` (in-memory over the already-materialized slice — small N).
  - `FeeInvoiceId` via the same `Invoice` lookup as the list. Assemble `MakerPayoutDetailDto`. No customer email / address anywhere in the projection (GDPR lock).

  **`GetMakerOutboxEventsForOrderAsync`** — maker-scoped, read-only, payload-free:
  - IDOR guard: `var ownsOrder = await dbContext.Orders.AsNoTracking().AnyAsync(o => o.Id == orderId && o.MakerId == makerId, ct);` → if false, return an empty `PagedData` (not an oracle).
  - Base: `dbContext.Set<OutboxEvent>().AsNoTracking().Where(e => e.AggregateId == orderId)` ordered `CreatedAt DESC` (newest first; the drawer shows recent activity first — or `ASC` if T-0116 wants chronological; **DESC** chosen for pagination stability + "latest first" parity with the order lists).
  - `Select` into `MakerOutboxEventDto(e.EventType, <derived status>, e.CreatedAt)`. The status derivation runs as an EF-translatable conditional:
    - `e.ProcessedAt != null` → `Processed`
    - else `e.NextRetryAt == null && e.LastErrorKind` ∈ {`Permanent`, `Configuration`} → `Stalled`
    - else → `Scheduled`
  - **`e.PayloadJson` MUST NOT appear in this projection** (grep-friendly absence — same discipline as T-0081's email absence). The expression tree carries no payload reference, so a future SELECT-widen cannot leak customer PII embedded in the payload.
  - `CountAsync` / `Skip` / `Take` / `ToListAsync`; return `PagedData<MakerOutboxEventDto>`.

- **DI registration:** `services.AddScoped<IPayoutQueries, PayoutQueries>();` in the Infra.Database registration extension (alongside `IOrderQueries`).

### Web.Maker host

- **`Web.Maker/Controllers/PayoutsController.cs`** (NEW, route group `[Route("api/v1/payout-batches")]`):
  - `[HttpGet("")]` `List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct)` → `mediator.Send(new GetMakerPayouts.Query(page, pageSize), ct)` → `HandleResult`. `[ProducesResponseType<GetMakerPayoutsResponse>(200)]`. Route: `GET /api/v1/payout-batches`.
  - `[HttpGet("{batchId}")]` `Detail(string batchId, CancellationToken ct)` → `mediator.Send(new GetMakerPayoutDetail.Query(batchId), ct)` → `HandleResult`. `[ProducesResponseType<GetMakerPayoutDetailResponse>(200)]`. Route: `GET /api/v1/payout-batches/{batchId}`.
- **`Web.Maker/Controllers/OrderEventsController.cs`** (NEW, route `[Route("api/v1/orders")]` — or extend the existing maker `OrdersController` if present; PM note: a separate controller keeps the payout bundle's surface cohesive, but matching the existing maker `OrdersController` route group is acceptable):
  - `[HttpGet("{orderId}/events")]` `Events(string orderId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct)` → `mediator.Send(new GetMakerOutboxEventsForOrder.Query(orderId, page, pageSize), ct)` → `HandleResult`. `[ProducesResponseType<GetMakerOutboxEventsResponse>(200)]`. Route: `GET /api/v1/orders/{orderId}/events`.
- All actions: `[Authorize]` (maker scheme) — JWT audience enforced per host per ADR 0013. `[ProducesResponseType]` so NSwag generates the typed return shapes.

### Tests

#### Handler unit tests (~10, NSubstitute mocks: `IUserSessionProvider`, `IMakerRepository`, `IPayoutQueries`)

`backend/src/Makables.Tests/AppServices/Features/Payouts/` (three files mirroring the features):

1. **GetMakerPayouts_happy_path_returns_PagedData** — session → maker row; `IPayoutQueries.GetMakerPayoutsPagedAsync` returns 3 items. Assert `Payouts.Items.Count == 3`, `TotalCount == 3`.
2. **GetMakerPayouts_no_maker_row_returns_MakerNotFound** — `GetByUserIdAsync` null → `Failure(MakerNotFound)`; `IPayoutQueries` Received(0).
3. **GetMakerPayouts_resolved_makerId_forwarded** — capture arg; assert first positional == `maker.Id` (pins handler-layer IDOR shield).
4. **GetMakerPayouts_page_and_pageSize_forwarded** — `Page=2, PageSize=50` → captured `page==2, pageSize==50`.
5. **GetMakerPayoutDetail_happy_path_returns_detail** — `GetMakerPayoutDetailAsync` returns a populated `MakerPayoutDetailDto` → `Success` with the dto preserved (orders list intact).
6. **GetMakerPayoutDetail_no_maker_row_returns_MakerNotFound** — null maker → `Failure(MakerNotFound)`; `IPayoutQueries` Received(0).
7. **GetMakerPayoutDetail_null_projection_returns_NotFound** — `GetMakerPayoutDetailAsync` returns null (unknown OR cross-maker) → `Failure(NotFound)`. Pins the "no oracle" contract at the handler layer.
8. **GetMakerOutboxEvents_happy_path_returns_PagedData** — returns 2 events → `Success`, `Events.Items.Count == 2`.
9. **GetMakerOutboxEvents_no_maker_row_returns_MakerNotFound** — null maker → `Failure`; `IPayoutQueries` Received(0).
10. **GetMakerOutboxEvents_resolved_makerId_and_orderId_forwarded** — capture args; assert `makerId == maker.Id` and `orderId == query.OrderId` (pins both scoping params).

#### Integration tests (~4, Testcontainers postgres + `WebApplicationFactory` for the Maker host)

`backend/src/Makables.IntegrationTests/Payouts/MakerPayoutQueriesIntegrationTests.cs` — seeds 2 makers (`makerA`, `makerB`), 2 users, 1 `Completed` `PayoutBatch` claiming orders of **both** makers (the cross-maker batch — the critical IDOR case), per-maker Fee invoices, and outbox events on `makerA`'s + `makerB`'s orders.

1. **GET_payouts_list_returns_only_this_makers_slice** — log in as makerA; `GET /api/v1/payout-batches`. Assert 200 + the batch row's `MakerTotalPaidMinor` equals `SUM(makerA's MakerPayoutAmountMinor in the batch)` (DB cross-check) — **NOT** `PayoutBatch.TotalAmountMinor` (which includes makerB's slice). `OrderCount` == makerA's claimed-order count only. `FeeInvoiceId` == makerA's Fee invoice id (not makerB's).
2. **GET_payout_detail_per_order_breakdown_reconciles_and_is_maker_scoped** — makerA; `GET /api/v1/payout-batches/{batchId}`. Assert every `Orders[]` line belongs to makerA (DB cross-check `o.MakerId == makerA.Id`); NONE of makerB's order numbers appear; and `SUM(line.MakerPayoutAmountMinor) == MakerTotalPaidMinor`; and per line `ProductPriceMinor − PlatformFeeAmountMinor + ShippingPriceMinor == MakerPayoutAmountMinor`.
3. **GET_payout_detail_cross_maker_batch_id_returns_404_no_oracle** — makerB requests a batch they ARE in → 200 with only makerB's lines; then a maker with NO orders in any batch (seed a third bank-less maker, or makerB requesting a fabricated batch id) → 404. Asserts unknown id and cross-maker id return the **same** 404 (no enumeration oracle).
4. **GET_order_events_is_maker_scoped_and_payload_free** — makerA requests `GET /api/v1/orders/{makerA_orderId}/events` → 200 with events (status enum present, `OccurredAt` present); the response JSON contains **no `payloadJson` / `lastErrorCode` / customer email** member (assert the serialized body has none of those keys). Then makerA requests `GET /api/v1/orders/{makerB_orderId}/events` → 200 with an **empty** page (cross-maker order → empty, not an oracle, not 403).

### Docs

- **`docs/architecture/roles/payout-batch.md`** — note the new maker-scoped read surface: "Maker payout list/detail via `IPayoutQueries.GetMakerPayoutsPagedAsync` / `GetMakerPayoutDetailAsync` returns the per-maker slice (`SUM(this maker's MakerPayoutAmountMinor)`), never `PayoutBatch.TotalAmountMinor` (cross-maker). The operator CSV is admin-only — never surfaced to makers (cross-maker PII, Q4). T-0112 ships read-only; T-0103 ships completion."
- **`docs/architecture/roles/outbox.md`** — note: "Maker-scoped read-only outbox audit via `IPayoutQueries.GetMakerOutboxEventsForOrderAsync` (US-maker-0017): event type + derived `OutboxDeliveryStatus` + timestamp only; no `PayloadJson` / `LastErrorCode` / retry. Maker retry is admin-only (AC-2)."
- **`docs/tickets/INDEX.md`** — flip T-0112 row to `**done**` after PR merge (PM does this).

### NSwag regen

The three new endpoints are a contract change → **NSwag regen REQUIRED in the same PR (maker host)**. Per the pre-commit hook (T-0013), `frontend/src/lib/api-client/` cannot be edited manually — `npm run generate:api` produces the diff. The new `GetMakerPayoutsResponse`, `GetMakerPayoutDetailResponse`, `GetMakerOutboxEventsResponse`, the four DTOs (`MakerPayoutListItemDto`, `MakerPayoutDetailDto`, `MakerPayoutOrderLineDto`, `MakerOutboxEventDto`), and the `OutboxDeliveryStatus` + `PayoutBatchState` enums appear in the generated maker client. **T-0112a rides this same regen** (its streaming download endpoint is added in the same PR). Customer / admin / public clients untouched.

## Alternatives Considered

- **Option A — Surface `PayoutBatch.TotalAmountMinor` directly on the maker list.** *Rejected per ADR 0009 + Q4* — that column is the operator's whole-batch wire total across *every* maker. Showing it to one maker would inflate their apparent payout by other makers' slices. The per-maker `SUM(o.MakerPayoutAmountMinor WHERE o.MakerId == makerId)` is the only correct figure for a single maker.
- **Option B — Surface the operator CSV (or a "your line" extract) to the maker.** *Rejected per Q4* — the CSV is the bank-transfer file with every maker's IBAN + amount; one maker seeing it is a catastrophic cross-maker PII leak. Even a single-line extract leaks the file's existence + shape and invites a support loop. The Fee invoice (the maker's own commercial document, T-0112a download) + the per-order breakdown give the maker everything they legally need.
- **Option C — Extend `IOrderQueries` with the payout methods (like T-0081 extended it).** *Rejected per ADR 0023 + cohesion* — the payout list/detail are `PayoutBatch`-shaped reads, not `Order`-shaped; co-locating them on `IOrderQueries` would muddy the read seam. A dedicated `IPayoutQueries` keeps each interface single-aggregate-shaped. The outbox-events method lives here too (not on a hypothetical `IOutboxQueries`) because it is part of the same maker-money read bundle and shares the `makerId` discipline — splitting it into a fourth interface for one method is overkill at MVP.
- **Option D — State/date filters on the payout list (mirror T-0081's filter set).** *Rejected per Q5* — a maker has a handful of batches; "newest first, paginated" covers the MVP workflow. The two-state enum doesn't justify a filter UI. Add when a real workflow needs it (same stance as T-0081 §A.3).
- **Option E — Surface `BankReference` on the maker payout row.** *Rejected* — `BankReference` (T-0103 Q1) is the operator's internal reconciliation field (their bank's transfer id). The maker reconciles against their own bank statement + the Fee invoice; the operator's transfer ref is noise on the maker surface (and a minor info leak about operator banking).
- **Option F — Surface outbox `PayloadJson` / `LastErrorCode` on the events drawer.** *Rejected per ADR 0020 + US-maker-0017* — the payload carries customer email, addresses, provider refs; the error code is operator-internal diagnostic detail. The maker needs *that* an event happened + its delivery health (the derived 3-value `OutboxDeliveryStatus`), nothing more. A maker debugging a stalled email is not a workflow — they escalate to admin (AC-2: maker can't retry).
- **Option G — A maker-initiated outbox retry button.** *Rejected per US-maker-0017 AC-2 + out-of-scope* — retry is an admin-only operation (it can re-send emails, re-hit providers); a maker triggering it bypasses the admin's stall-triage. The drawer is read-only.
- **Option H — Compute `MakerTotalPaidMinor` from a denormalized column on `PayoutBatch`.** *Rejected* — `PayoutBatch` has no per-maker total column (it stores the cross-maker `TotalAmountMinor` + `MakerCount`). Adding a per-maker denorm table is overkill; the `GROUP BY o.PayoutBatchId` over the maker's claimed orders is a single indexed query (the `(MakerId, PayoutBatchId)` access pattern is already covered by the order indexes).
- **Option I — Return 403 (not empty/404) on a cross-maker order/batch id.** *Rejected per the T-0081-verbatim no-oracle convention* — a 403 distinguishes "exists but not yours" from "doesn't exist", letting a maker enumerate which batch/order ids exist. Empty page (list/events) + 404 (detail, same as unknown) closes the oracle.
- **Option J — Un-complete / reopen affordance on the payout row.** *Rejected per the reversibility lock* — there is no write-side un-complete (completion is financially terminal: immutable Fee invoices, executed transfer, sent emails, refund-ack gate). Exposing a reopen field would be a dead button. Errors are corrected forward via T-0105 / T-0107.

## Out of scope

- **Payout-batch completion write-side** (`PayoutBatch.Complete()`, `Order.Complete(clock)` loop, per-maker payout-sent email, `BankReference` column + migration) — **T-0103** owns all of it. T-0112 reads the columns T-0103 fills.
- **Fee-invoice PDF streaming download** — **T-0112a** (controller-direct stream per T-0088, `IInvoiceRepository.ForMaker` IDOR scope, reuses `OrderNotFound` / `InvoiceNotYetRendered`, no new code / migration). T-0112 only exposes the `FeeInvoiceId`.
- **The maker payouts frontend** (`/dashboard/maker/vyplaty` list + drill-in + events drawer, tykání, `formatCzk`, mobile-cards/desktop-table, URL-state pagination, cs-CZ strings) — **T-0116** consumes this ticket's NSwag-regenerated client.
- **The operator CSV surface to makers** — explicitly rejected (Q4, cross-maker PII).
- **`BankReference` on the maker surface** — explicitly rejected (Option E).
- **Outbox `PayloadJson` / `LastErrorCode` / maker retry** — explicitly rejected (Options F + G; US-maker-0017 AC-2).
- **State/date filters on the payout list** — explicitly rejected (Q5; post-MVP).
- **Sort selector** — fixed `CompletedAt DESC, BatchNumber DESC`. Add when a real workflow needs it.
- **Un-complete / reopen** — no write-side un-complete (reversibility lock); errors corrected forward via T-0105 / T-0107.
- **Admin payout views** — admin payout list/detail (T-0102b + downstream) is a separate host surface; this ticket is maker-host only.
- **New error codes / migrations / outbox events / i18n keys** — none ship in T-0112 (i18n for T-0116 ships in T-0116).

## Acceptance criteria

- **AC-1** Given a logged-in maker with a Maker row, when `GET /api/v1/payout-batches?page=1&pageSize=20` is called with a valid maker JWT, then it returns `200 OK` with body `GetMakerPayoutsResponse { Payouts: PagedData<MakerPayoutListItemDto> }`, default-sorted `CompletedAt DESC, BatchNumber DESC`.
- **AC-2** Given a `PayoutBatch` that claimed orders of BOTH makerA and makerB, when makerA requests the list, then the batch row's `MakerTotalPaidMinor` equals `SUM(makerA's MakerPayoutAmountMinor in the batch)` (DB cross-check) — NOT `PayoutBatch.TotalAmountMinor` — and `OrderCount` equals makerA's claimed-order count only.
- **AC-3** Given the same cross-maker batch, when makerA requests `GET /api/v1/payout-batches/{batchId}`, then `Detail.Orders[]` contains ONLY makerA's claimed orders (every line `o.MakerId == makerA.Id` via DB cross-check; none of makerB's order numbers appear), and `SUM(line.MakerPayoutAmountMinor) == Detail.MakerTotalPaidMinor`.
- **AC-4** Given any `MakerPayoutOrderLineDto`, when read, then `ProductPriceMinor − PlatformFeeAmountMinor + ShippingPriceMinor == MakerPayoutAmountMinor` (the `PricingBreakdown` reconciliation invariant holds per line).
- **AC-5** Given a maker requests a batch they have NO orders in (cross-maker id) OR a non-existent batch id, when `GET /api/v1/payout-batches/{batchId}` is called, then BOTH return `404` (same shape — no enumeration oracle distinguishing "exists but not yours" from "doesn't exist").
- **AC-6** Given a logged-in user with NO Maker row, when any of the three endpoints is called, then it returns `404` with error code `maker.notFound`. `IPayoutQueries` is NOT invoked (asserted via mock at the handler-test layer).
- **AC-7** Given a maker requests `GET /api/v1/orders/{orderId}/events` for an order they own, when called, then it returns `200` with `GetMakerOutboxEventsResponse { Events: PagedData<MakerOutboxEventDto> }`, each item carrying `EventType`, a derived `Status ∈ {Processed, Scheduled, Stalled}`, and `OccurredAt` — and NO `payloadJson` / `lastErrorCode` / customer-email member anywhere in the serialized body.
- **AC-8** Given a maker requests the events endpoint for ANOTHER maker's order id, when called, then it returns `200` with an EMPTY page (`TotalCount == 0`) — not 403, not an oracle. (IDOR shield in the projection.)
- **AC-9** Given a stalled outbox event (`NextRetryAt == null`, `ProcessedAt == null`, `LastErrorKind ∈ {Permanent, Configuration}`), when surfaced, then its `Status == Stalled`. Given a processed event (`ProcessedAt != null`), `Status == Processed`. Given a retry-scheduled event (`NextRetryAt != null`, `ProcessedAt == null`), `Status == Scheduled`.
- **AC-10** Given `?page=0` or `?pageSize=0` or `?pageSize=51` on the list or events endpoints, when called, then `400` with validation error (`Page >= 1`, `PageSize ∈ [1, 50]`). Defaults apply when omitted (`page=1, pageSize=20`).
- **AC-11** Given the `MakerPayoutListItemDto` / `MakerPayoutDetailDto` type definitions, when read, then NEITHER carries a field named `BankReference`, `CsvBlobPath`, or any reference to the cross-maker `PayoutBatch.TotalAmountMinor`. Given the `MakerOutboxEventDto`, it carries NO `PayloadJson` / `LastErrorCode` / `RetryCount`. Compile-time gate. Grep gate: `PayoutQueries.GetMakerOutboxEventsForOrderAsync` contains zero references to `e.PayloadJson`.
- **AC-12** Build clean. Unit tests: baseline + ~10 new (3 feature handler files). Integration tests: baseline + ~4 new (`MakerPayoutQueriesIntegrationTests` — per-maker slice, breakdown reconciles + maker-scoped, cross-maker 404 no-oracle, events maker-scoped + payload-free). `node scripts/check-consistency.mjs` exit 0. NSwag regen committed in the same PR (maker host); `frontend/src/lib/api-client/` types the three endpoints + the three responses + the four DTOs + the two enums. No manual edits to the api-client folder (pre-commit hook enforces). T-0112a rides the same regen.

## Technical notes

### Why the per-maker slice — not `PayoutBatch.TotalAmountMinor`

A `PayoutBatch` pays N makers in one operator wire run. `PayoutBatch.TotalAmountMinor` is the sum the operator sends from the company bank account — across all makers. A single maker's payout is *their* slice: `SUM(o.MakerPayoutAmountMinor) WHERE o.PayoutBatchId == batchId AND o.MakerId == makerId`. Surfacing the batch total to one maker would over-report their payout by the other makers' slices and leak the batch's aggregate size. The `GROUP BY o.PayoutBatchId` over the maker-filtered orders is the correct, single-query derivation; there is no per-maker denorm column (and there shouldn't be — the batch is immutable, so the SUM is stable and cheap over the indexed `(MakerId, PayoutBatchId)` access path).

### Why the CSV is never on the maker surface

The bank-transfer CSV (T-0102b) is the operator's instruction file to their bank: every line is `{maker IBAN},{amount},{reference}`. It contains every maker's account number and payout. Exposing it — or any extract that reveals the file's existence/shape — to a single maker is a cross-maker PII leak (Q4). The maker's legitimate artifacts are (a) their Fee invoice PDF (their own commercial document, T-0112a download) and (b) the per-order breakdown (their own orders' money). Those two cover the accountant's needs (record the platform fee as an expense; reconcile the net against the bank statement) without ever touching another maker's data.

### Why the outbox-events projection never names `PayloadJson`

`OutboxEvent.PayloadJson` serializes the customer-facing email payloads — `OrderPaidCustomerEmailPayload`, `OrderShippedCustomerEmailPayload`, etc. — which embed customer email, name, address, and provider refs. The maker's audit-trail need (US-maker-0017) is *that* an event happened + its delivery health, not the payload. By keeping `PayloadJson` out of the projection's expression tree entirely (neither selected nor named), a future SELECT-widen cannot accidentally leak the embedded PII — the same grep-friendly-absence discipline T-0081 uses for the customer email. The derived `OutboxDeliveryStatus` is a deliberate down-projection of the rich internal state (`ProcessedAt` / `NextRetryAt` / `RetryCount` / `LastErrorKind` / `LastErrorCode`) to a maker-safe 3-value enum.

### Why the IDOR shield is enforced twice (handler + projection)

Defence in depth, T-0081-verbatim. The handler resolves `makerId` from the session and forwards it; the projection re-filters on the maker predicate (`o.MakerId == makerId` for list/detail orders; "order's maker == this maker" for events). Either layer alone suffices for a correctly-routed call, but the two-layer setup means a future admin tool that legitimately bypasses the handler cannot accidentally bypass the projection. The handler-layer tests pin layer 1 (`…_resolved_makerId_forwarded`); the integration tests pin layer 2 (cross-maker batch 404 + cross-maker order empty page).

### Why a new `IPayoutQueries` interface (not extending `IOrderQueries`)

T-0081 extended `IOrderQueries` because its method was `Order`-shaped. T-0112's list/detail are `PayoutBatch`-shaped reads (the row IS a batch, the breakdown is the batch's orders). Co-locating them on `IOrderQueries` would blur the read seam's single-aggregate shape. A dedicated `IPayoutQueries` keeps each read interface aligned to one aggregate. The outbox-events method co-locates here (rather than a fourth `IOutboxQueries` interface for one method) because it is part of the same maker-money read bundle and shares the `makerId`-scoping discipline — a one-method interface would be ceremony.

### Why `CompletedAt DESC` with nulls-last is acceptable at MVP

`Processing` batches have `CompletedAt == null`; under `ORDER BY CompletedAt DESC` the EF/Postgres translation sorts nulls last, so an in-flight `Processing` batch appears after the `Completed` ones. A maker has at most one in-flight batch at a time (weekly cadence), so the in-flight row landing at the bottom of page 1 is fine — and arguably correct (paid batches are the maker's primary interest; the in-flight one is a preview). `BatchNumber DESC` is the stable secondary tiebreaker (lexicographically week-ordered: `VYP-CZ-2026-W23` > `VYP-CZ-2026-W22`).

## Files touched (expected)

### New
- `backend/src/Makables.Core.Domain/Payouts/IPayoutQueries.cs`
- `backend/src/Makables.Core.AppServices/Features/Payouts/DTOs/MakerPayoutListItemDto.cs`
- `backend/src/Makables.Core.AppServices/Features/Payouts/DTOs/MakerPayoutDetailDto.cs`
- `backend/src/Makables.Core.AppServices/Features/Payouts/DTOs/MakerPayoutOrderLineDto.cs`
- `backend/src/Makables.Core.AppServices/Features/Payouts/DTOs/MakerOutboxEventDto.cs` (+ `OutboxDeliveryStatus` enum)
- `backend/src/Makables.Core.AppServices/Features/Payouts/GetMakerPayouts.cs`
- `backend/src/Makables.Core.AppServices/Features/Payouts/GetMakerPayoutDetail.cs`
- `backend/src/Makables.Core.AppServices/Features/Payouts/GetMakerOutboxEventsForOrder.cs`
- `backend/src/Makables.Infra.Database/Payouts/PayoutQueries.cs`
- `backend/src/Makables.Web.Maker/Controllers/PayoutsController.cs`
- `backend/src/Makables.Web.Maker/Controllers/OrderEventsController.cs` (or extend existing maker `OrdersController`)
- `backend/src/Makables.Tests/AppServices/Features/Payouts/GetMakerPayoutsHandlerTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/Payouts/GetMakerPayoutDetailHandlerTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/Payouts/GetMakerOutboxEventsForOrderHandlerTests.cs`
- `backend/src/Makables.IntegrationTests/Payouts/MakerPayoutQueriesIntegrationTests.cs`

### Modified
- `backend/src/Makables.Infra.Database/` DI registration extension — register `IPayoutQueries → PayoutQueries`.
- `frontend/src/lib/api-client/*` — NSwag-regenerated (maker host); committed in the same PR (T-0112a rides this regen).
- `docs/architecture/roles/payout-batch.md` — note the maker-scoped read surface + per-maker-slice + CSV-never-to-makers.
- `docs/architecture/roles/outbox.md` — note the maker-scoped read-only payload-free events query.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0112.md`.

## Status log

- 2026-06-13 `draft → ready` by PM. Created as part of the payout-completion bundle (T-0103 completion write-side + T-0112 maker read queries + T-0112a fee-invoice download + T-0116 maker frontend). User locked the bundle at the 2026-06-13 deliberation: **Q1** `BankReference` + optional `PaymentDate` on completion (T-0103); **Q2** materialized order-id loop `Order.Complete(clock)` in one handler/UoW, no per-order mediator.Send (Q-0008 MARS lesson, T-0103); **Q3** one payout-sent email per maker per batch (T-0103); **Q4** maker detail = list + per-order breakdown + Fee-invoice PDF, CSV NEVER shown to makers (cross-maker PII) — shapes T-0112's read surface; **Q5** maker list = pagination only, no filters, sort `CompletedAt DESC` (PM default); **reversibility** no un-complete, completion financially terminal, errors corrected forward via T-0105/T-0107. PM-absorbed: `PayoutBatchState` is two-value (Processing/Completed) — overrides US-maker-0012 AC-2's stale enum; IDOR shield twice + `AsNoTracking` + `IgnoreAutoIncludes` + globally-unique response names (T-0081 verbatim); new `IPayoutQueries` interface (ADR 0023); outbox-events maker-scoped + read-only + payload-free (no maker retry — admin-only); NSwag regen maker host (the gate T-0116 + T-0112a consume); no new codes / migrations / outbox events / i18n keys. **Ready for dotnet-backend.** The implementer processes T-0103 → T-0112 → T-0112a in the same branch; the bundle ships in one PR (T-0116 frontend follows once the regen'd client is on master).
