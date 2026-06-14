---
id: T-0103
title: MarkPayoutBatchCompleted command — settle a payout batch (Processing → Completed, orders → Completed, per-maker payout-sent emails)
status: ready
size: M
owner: dotnet-backend
created: 2026-06-13
updated: 2026-06-13
depends_on: [T-0102a, T-0102b, T-0011]
blocks: [T-0104, T-0116]
user_stories: [US-admin-0007, US-maker-0012]
adrs: [0009, 0013, 0014, 0019, 0020]
phase: 5
manual_steps: []
security_touching: true
layers: [domain, appservices, infra-database, web-admin]
---

# T-0103 — MarkPayoutBatchCompleted command — settle a payout batch (Processing → Completed, orders → Completed, per-maker payout-sent emails)

## Context

T-0103 is the **settlement ticket of the payout bundle** (T-0101 entity + repository → T-0102a claim command → T-0102b fee invoices + CSV export → **T-0103 MarkPayoutBatchCompleted** → T-0104 timer Function). It ships PR #2 of the bundle. The operator has, by hand, executed the bank wire described by the batch's CSV (every maker's account, one transfer file) and now records that fact: the admin calls `POST /api/v1/payout-batches/{id}/complete` with the **bank reference** (the transaction id their bank assigned the wire) and an **optional payment date**. The command moves the batch `Processing → Completed`, drives every claimed order `Delivered → Completed` in the same UnitOfWork, and enqueues **one payout-sent email per maker** summarizing what that maker was paid.

This directly satisfies **US-admin-0007 AC-2** ("mark paid + payout-sent emails") and finalizes the maker-facing half of **US-maker-0012** — once a batch is `Completed`, the maker payout list (T-0112/T-0116) renders it as paid with the settlement date the operator recorded here.

This is a **money-terminal admin command** (`security_touching: yes`): completion is the irreversible commit point of the weekly payout. At the instant the batch flips to `Completed`, three facts become immutable downstream — the per-batch Fee invoices (numbered, T-0102b), the executed bank transfer (real money left the company account), and the post-payout refund gate (`Order.ValidateRefund` requires `AcknowledgePostPayout` once an order is `Completed`, T-0105). The transition is therefore **forward-only**: there is no un-complete. Mistakes are corrected forward via T-0105 (refund) / T-0107 (manual state change), never by reopening a settled batch.

The bundle's domain seam (`PayoutBatch.Complete`) ships **with its only caller in this ticket** — T-0101 reserved the `CompletedAt`/`CompletedBy` columns and the doc comment promised the method here (no dead code earlier). T-0103 also adds the new `BankReference` column (one migration) and the new `PayoutSentMaker` email template (one seed migration) + its outbox event + the `EmailSendService` routing branch.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 5 dimensions plus a reversibility decision at the 2026-06-13 payout-settlement deliberation (Q1–Q5 + Reversibility below). PM absorbed the rest.

### A. User-locked 2026-06-13 (non-negotiable)

1. **Q1 — Capture `BankReference` (string, required) + `PaymentDate` (`DateOnly?`, optional).** Both prompted in the admin completion form. `BankReference` is the operator's bank-assigned wire transaction id; it persists on a **new `PayoutBatch.BankReference` column** (migration) for the audit trail and the maker-facing "paid on / ref" surface. `PaymentDate` is the value date the operator selects; when omitted, `CompletedAt` defaults to `clock.UtcNow`; when present, `CompletedAt` is that local date at **midnight UTC** (`paymentDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)`). **Rejected:** reference-only / date-only (the maker payout row needs both "what was the wire" and "when did it settle"); a free-text "notes" blob (unstructured — the bank ref is a discrete field the maker UI links on).

2. **Q2 — Materialize the batch's claimed order ids and loop `Order.Complete(clock)` directly in the ONE handler under one UoW.** No per-order `mediator.Send` (the **Q-0008 MARS lesson**: a nested `Send` inside a command handler opens a second pipeline scope and re-enters `UnitOfWorkPipelineBehavior`, breaking the single-transaction guarantee and risking a Multiple Active Result Sets fault on the shared `DbContext`). The orders are already inside the batch's scope (claimed by T-0102a); completion is a **pure entity transition** with no provider call, no cross-aggregate validation — exactly the case the materialized loop is for. **Rejected:** a `CompleteOrder` sub-command fanned out via `mediator.Send` per order (MARS + nested-UoW hazard, no isolation benefit since it all commits together anyway); a domain event per order consumed asynchronously (decouples the order completion from the batch settlement — they MUST be atomic, a half-settled batch is a correctness hole).

3. **Q3 (PM default) — One payout-sent email per MAKER per batch.** In the same materialized loop, group the claimed orders by `MakerId`; enqueue exactly one `PayoutBatchPayoutSentMakerEmail` outbox event per distinct maker. The email summarizes: batch number, **total paid to that maker** (Σ that maker's `MakerPayoutAmountMinor`), order count, and a deep link to the maker's fee-invoice download (T-0112a surface). **Rejected:** one email per order (inbox spam — a maker with 12 orders in the batch gets 12 emails for one wire); a single digest email to all makers (cross-maker PII — one maker would see another's payout total).

4. **Q4 — Maker batch view = list + drill-into-batch with per-order breakdown.** Each drilled row shows: order number, product price, platform fee, net payout; plus the fee-invoice PDF download. **The bank CSV is NEVER shown to makers** — it is the operator's bank file containing every maker's account number (cross-maker PII; a GDPR + competitive-data leak). Implemented by T-0116 (frontend) + T-0112 (queries) + T-0112a (fee-invoice download). T-0103's only obligation here is to expose the settlement facts (`CompletedAt`, `BankReference`) that those tickets read. **Rejected:** exposing the CSV download to makers (cross-maker PII); a flat list with no per-order breakdown (US-maker-0012 AC-3 names the breakdown).

5. **Q5 (PM default) — Maker payout list = pagination only, no state/date filters at MVP.** Default sort: batch `ProcessedAt`/`CompletedAt` **DESC**. Implemented by T-0112/T-0116; recorded here for bundle traceability. **Rejected:** state + date filters at MVP (a maker has single-digit batches at MVP; filters are premature; pagination alone covers the surface).

6. **Reversibility — NO un-complete. Completion is financially terminal.** Once `Completed`: the Fee invoices are immutable (numbered, legally final), the bank transfer is executed (real money moved), the payout-sent emails are dispatched, and the post-payout refund gate is armed. Reopening a batch would desynchronize all four. **Errors are corrected forward** — over/underpaid a maker → refund (T-0105) or a manual follow-up wire; a wrong order in the batch → T-0107 manual state change + a corrective refund. **T-0103 ships forward-only**; there is no `Reopen()`/`Uncomplete()` method anywhere. **Rejected:** an admin "reopen batch" path (a correctness minefield — see the four desynchronized facts; the forward-correction tools already exist).

### B. ADR-locked (no relitigation)

- **ADR 0009 (numbering).** No new number allocated — the batch keeps its `VYP-CZ-YYYY-Www` number from T-0102a. The payout-sent email references it.
- **ADR 0013 (per-audience hosts).** Admin host only; `[Authorize]` with admin JWT audience. The batch + order loads are `Unscoped` repository reads — admin-host-only per the convention.
- **ADR 0014 (UoW + audit).** Batch transition + N order transitions + N outbox rows + the `admin_audit_log` row commit **atomically in ONE UnitOfWork**. The handler never calls `SaveChangesAsync()`; the pipeline commits. Audit + outbox rows ride only the committed transaction.
- **ADR 0019 (admin audit).** Completion is a privileged money event → an `admin_audit_log` row via `IAdminAuditableCommand` (the batch id is known at command time, so the pipeline contract is satisfiable — unlike T-0102a's create, which needed a handler-written entry). `beforeJson` = the `Processing` snapshot; `afterJson` = the `Completed` snapshot with bank ref + settlement date + the per-maker payout summary.
- **ADR 0020 (outbox).** The payout-sent emails are enqueued as outbox rows in the same UoW, routed through the existing `send-email` queue. Idempotent re-call (already `Completed`) enqueues nothing.
- **Patterns §A.4 `BusinessResult<T>`.** Every expected failure is a `BusinessErrorMessage` code; one-file feature; globally-unique response name (post-PR-#38 NSwag convention).

### C. PM-absorbed (no user input needed)

1. **Idempotency = Silent-Success on already-`Completed` re-call.** Load the batch; if `State == Completed`, return 200 with the stored completion facts (`CompletedAt`, `BankReference`) and `AlreadyCompleted = true` — **no re-transition, no order re-completion, no email re-emit, no second audit row** (T-0067/T-0076/T-0105 Silent-Success precedent). The bank ref / date in the second call are ignored (the first settlement is authoritative). This makes the timer/retry path safe.
2. **`PayoutBatchState` stays Processing/Completed only — no `Pending`.** US-maker-0012 AC-2 names a stale "`Pending` or `Processing`" enum; the actual two-value enum (T-0101 lock A.4) is authoritative. The maker UI renders `Processing → "připravujeme"` and `Completed → "vyplaceno"` (paid). **This ticket's AC supersedes US-maker-0012 AC-2's enum** — BA updates the story post-merge.
3. **`PayoutBatch.Complete(IClock, string bankReference, DateOnly? paymentDate, string completedBy)` is the domain seam** (ships now with its only caller). Guard: refuses unless `State == Processing` (a non-`Processing` batch returns `BusinessErrorMessage.PayoutBatchNotProcessing`); on success sets `State = Completed`, `BankReference = bankReference.Trim()`, `CompletedBy = completedBy`, `CompletedAt = paymentDate is not null ? paymentDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : clock.UtcNow`. The already-`Completed` branch is handled in the **handler** as Silent-Success (not in the domain guard) so the response can echo the stored facts — the domain method is reached only on the live transition. `bankReference` blank → `ArgumentException` (impossible input; the Validator catches it first as a clean 400).
4. **Order loop via `GetByPayoutBatchIdUnscopedAsync`** (`IOrderRepository`, already on master, `:269`). The handler materializes the claimed orders **tracked** (T-0103 needs to mutate them — the existing method is `AsNoTracking` read-only for T-0102b's artifact reads, so T-0103 adds a **tracked sibling** `GetByPayoutBatchIdForCompletionUnscopedAsync(batchId, ct)` rather than detune the existing read). Each `order.Complete(clock)` is belt-and-braces (all claimed orders are `Delivered` by T-0102a's invariant; a refusal is a programmer error → surface the failure, UoW rolls back everything).
5. **Per-maker grouping is the second red-first surface.** Group the materialized orders by `MakerId`; per group compute `MakerTotalPaidMinor = Σ MakerPayoutAmountMinor` and `OrderCount`; enqueue one `PayoutBatchPayoutSentMakerEmail`. Distinct-maker dedup (a maker with N orders → exactly one email) is pinned by a unit test.
6. **New outbox event `PayoutBatchPayoutSentMakerEmail = "payout.payoutSent.makerEmail"`** added next to `PayoutFeeInvoiceMakerEmail` (`OutboxEventTypes.cs:148`) + added to the `IsEmailSend` set (`:178`). New `EmailTemplateType.PayoutSentMaker = 16` next to `PayoutFeeInvoiceMaker = 15` (`EmailTemplateType.cs:125`).
7. **New payload `PayoutBatchPayoutSentMakerEmailPayload`** (mirrors `PayoutFeeInvoiceMakerEmailPayload` shape): `MakerEmail`, `BatchNumber`, `MakerTotalPaidMinor`, `Currency`, `OrderCount`, `FeeInvoiceActionUrl`, `LanguageCode`. Enrichment-at-enqueue (maker email + language resolved in the handler, pending Q-0012). `FeeInvoiceActionUrl` is the maker fee-invoice deep link (`{MakerAppBaseUrl}/dashboard/maker/vyplaty/{batchId}`, the T-0116 drill route).
8. **Seed migration `SeedPayoutSentMakerEmailTemplate`** inserts the `PayoutSentMaker` template + its cs-CZ + en-US translations. Subjects use **double-brace** `{{batch_number}}` placeholders per the **Q-0017 lesson** (single-brace subjects render the raw token; the renderer expects `{{token}}`). Tykání for the maker audience (T-0102b precedent). cs-CZ subject: `"Výplata za dávku {{batch_number}} odeslána"`.
9. **`EmailSendService` routing branch** for `PayoutSentMaker` (maps the outbox event → template type → render context: `batch_number`, `total_paid` via `formatCzk`-equivalent server formatting, `order_count`, `fee_invoice_url`). No PDF attachment (unlike the fee-invoice email) — this is a plain summary email.
10. **New error codes (reuse where possible):** `PayoutBatchNotFound` already exists (`BusinessErrorMessage.cs:463`) — reuse. **One new code:** `PayoutBatchNotProcessing = "payoutBatch.notProcessing"` (Conflict) for the domain guard when a non-`Processing`, non-`Completed` batch is targeted (defensive — the enum is two-valued, so this is structurally unreachable today, but the guard makes the method total and future-proofs a third state). cs-CZ key shipped.
11. **Audit via `IAdminAuditableCommand`** (not handler-written, unlike T-0102a) — the batch id is the `TargetId`, known at command time. `ActionCode = "payoutBatch.complete"`, `TargetEntity = "payout_batch"`, `TargetId = PayoutBatchId`, `Notes = BankReference` (so the wire ref is in the audit notes column). Fail-closed session check first (RefundOrder step-1 precedent — money settlement is never attributed to "system").
12. **Endpoint `POST /api/v1/payout-batches/{id}/complete`** on the existing `Web.Admin/Controllers/PayoutBatchesController.cs` (created by T-0102a). `[Authorize]` admin audience; `[ProducesResponseType(typeof(MarkPayoutBatchCompletedResponse), 200)]`; one-liner `mediator.Send`. Body = `{ bankReference, paymentDate? }`; the id is the route param. 200 on both the live-transition and the Silent-Success paths.
13. **NSwag admin regen is OPTIONAL in this ticket.** The new admin endpoint is a contract change, but the **admin frontend is T-0118** (no consumer yet on master). Per the bundle convention, the admin client regen rides T-0118's PR. Note the contract change in the PR description; do not block on regen. (Contrast: T-0116 is the maker frontend and rides T-0112 + T-0112a's maker-host regen — not this ticket.)
14. **TDD red-first:** (a) the `PayoutBatch.Complete` guard + field-setting (Processing→Completed, bank ref, CompletedAt from paymentDate-or-clock); (b) the per-maker grouping/dedup (N orders for one maker → one email + correct summed total). Both pinned before the handler exists.

## Scope

### Domain layer

- **`Core.Domain/Payouts/PayoutBatch.cs`** — NEW `Complete` method + NEW `BankReference` property:
  ```csharp
  public const int MaxBankReferenceLength = 140;

  /// <summary>Operator's bank-assigned wire transaction id (T-0103). Null while Processing.</summary>
  public string? BankReference { get; private set; }

  public BusinessResult Complete(
      IClock clock, string bankReference, DateOnly? paymentDate, string completedBy);
  ```
  Guard: `ArgumentException` on blank `bankReference`/`completedBy` or `bankReference` over `MaxBankReferenceLength` (impossible inputs — Validator + session check catch them first). `State != Processing` → `BusinessResult.Failure(Error.Conflict("state", BusinessErrorMessage.PayoutBatchNotProcessing))`. On success: `State = Completed`, `BankReference = bankReference.Trim()`, `CompletedBy = completedBy`, `CompletedAt = paymentDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) ?? clock.UtcNow`. Update the class doc's "only mutations are the set-once `AttachCsvBlobPath` and the T-0103 completion" line to reference the now-shipped method.
- **`Core.Domain/Orders/IOrderRepository.cs`** — NEW `GetByPayoutBatchIdForCompletionUnscopedAsync(string payoutBatchId, CancellationToken ct)`: **tracked** (the existing `:269` sibling is `AsNoTracking` for T-0102b's artifact reads; T-0103 mutates, so it needs a tracked load), `Include(o => o.Maker)` for the email maker-email/language resolution, `Where(o => o.PayoutBatchId == payoutBatchId)`. Unscoped = admin host only (ADR 0013). Doc comment cross-references the read-only sibling.
- **`Core.Domain/Common/BusinessErrorMessage.cs`** — extend the `=== Payout batch ===` block: `PayoutBatchNotProcessing = "payoutBatch.notProcessing"` (`PayoutBatchNotFound` already exists — reuse).
- **`Core.Domain/Email/EmailTemplateType.cs`** — NEW `PayoutSentMaker = 16` (doc: "Výplata za dávku {{batch_number}} odeslána" maker notification; outbox event `PayoutBatchPayoutSentMakerEmail`; tykání; T-0103).
- **`Core.Domain/Outbox/OutboxEventTypes.cs`** — NEW `PayoutBatchPayoutSentMakerEmail = "payout.payoutSent.makerEmail"` (next to `PayoutFeeInvoiceMakerEmail`, `:148`) + add to the `IsEmailSend` set (`:178`).
- **`Core.Domain/Outbox/PayoutBatchPayoutSentMakerEmailPayload.cs`** — NEW sealed record (mirrors `PayoutFeeInvoiceMakerEmailPayload`):
  ```csharp
  public sealed record PayoutBatchPayoutSentMakerEmailPayload(
      string MakerId,
      string MakerEmail,
      string BatchNumber,
      long MakerTotalPaidMinor,
      string Currency,
      int OrderCount,
      string FeeInvoiceActionUrl,
      string LanguageCode);
  ```

### AppServices layer

- **`Core.AppServices/Features/PayoutBatches/MarkPayoutBatchCompleted.cs`** — NEW one-file feature:
  - `Command(string PayoutBatchId, string BankReference, DateOnly? PaymentDate) : ICommand<MarkPayoutBatchCompletedResponse>, IAdminAuditableCommand` with `ActionCode => "payoutBatch.complete"`, `TargetEntity => "payout_batch"`, `TargetId => PayoutBatchId`, `Notes => BankReference`.
  - `MarkPayoutBatchCompletedResponse(string BatchId, PayoutBatchState State, DateTimeOffset CompletedAt, string BankReference, int OrderCount, int MakerCount, long TotalAmountMinor, string Currency, bool AlreadyCompleted)` — globally-unique name.
  - `Validator : AbstractValidator<Command>`: `PayoutBatchId` `NotEmpty` + `MaximumLength(40)`; `BankReference` `NotEmpty` + `MaximumLength(PayoutBatch.MaxBankReferenceLength)` (caps to the column width so an oversize payload is a clean 400, not a SaveChanges 500 — RefundOrder reason precedent). `PaymentDate` needs no rule (any valid `DateOnly` is acceptable; a future date is the operator's call).
  - `Handler(IPayoutBatchRepository payoutBatches, IOrderRepository orders, IOutbox outbox, IClock clock, ILanguageResolver languageResolver, IOptions<MakerAppUrlsOptions> makerAppUrls, IUserSessionProvider session, ILogger<Handler> logger)`; steps (NO `SaveChangesAsync()`):
    1. **Fail-closed session check** → `Error.Unauthorized()` when `session.GetUserId()` is empty (RefundOrder precedent — settlement is never "system").
    2. **Load batch Unscoped** — `payoutBatches.GetByIdUnscopedAsync(command.PayoutBatchId, ct)`; null → `payoutBatch.notFound`.
    3. **Silent-Success if already `Completed`** — return 200 with stored `CompletedAt`/`BankReference` and `AlreadyCompleted = true`. No transition, no order loop, no email, no audit (the pipeline skips a no-op? — NO: this is a Silent-Success *return*, so the audit pipeline records nothing because the handler returns Success with no mutation; matches RefundOrder AC-6). Log an info line.
    4. **Transition the batch** — `batch.Complete(clock, command.BankReference, command.PaymentDate, session.GetUserId()!)`; non-success (only `PayoutBatchNotProcessing` is reachable) → surface the failure.
    5. **Materialize claimed orders** — `orders.GetByPayoutBatchIdForCompletionUnscopedAsync(batch.Id, ct)` (tracked, Maker included).
    6. **Loop + group** — single pass: `order.Complete(clock)` per order (a refusal → `LogCritical` + surface the failure, UoW rolls back); accumulate per-`MakerId` (`MakerTotalPaidMinor += order.MakerPayoutAmountMinor`, `OrderCount++`, capture `order.Maker`).
    7. **Per-maker emails** — for each distinct maker group: resolve language via `languageResolver`, build `PayoutBatchPayoutSentMakerEmailPayload` (FeeInvoiceActionUrl = `{makerAppBaseUrl}/dashboard/maker/vyplaty/{batch.Id}`), `outbox.Enqueue(aggregateId: batch.Id, eventType: OutboxEventTypes.PayoutBatchPayoutSentMakerEmail, payloadJson: ...)`.
    8. **Return Success** — `MarkPayoutBatchCompletedResponse` (live-transition facts, `AlreadyCompleted = false`). The pipeline commits batch + N order transitions + N outbox rows + the `payoutBatch.complete` audit row (before/after JSONB) atomically (ADR 0014).

### Infrastructure / Database layer

- **`Infra.Database/Orders/OrderRepository.cs`** — implement `GetByPayoutBatchIdForCompletionUnscopedAsync` (tracked, `Include(o => o.Maker)`, no `IgnoreQueryFilters` — soft-deleted orders stay invisible; mirrors the existing read-only sibling minus `AsNoTracking`).
- **`Infra.Database/Migrations/2026xxxx_AddPayoutBatchBankReference.cs`** — NEW migration: add nullable `bank_reference VARCHAR(140)` column to `payout_batches`. `Down()` drops it. Model snapshot updated.
- **`Infra.Database/Migrations/2026xxxx_SeedPayoutSentMakerEmailTemplate.cs`** — NEW seed migration (runs after the bank-ref migration): insert the `PayoutSentMaker` `email_templates` row + cs-CZ + en-US `email_template_translations`. **Double-brace** `{{batch_number}}` / `{{total_paid}}` / `{{order_count}}` / `{{fee_invoice_url}}` placeholders (Q-0017 lesson — single-brace subjects render the raw token). cs-CZ subject `"Výplata za dávku {{batch_number}} odeslána"`; tykání body. `Down()` deletes the rows.
- **`Infra.*` `EmailSendService`** (the email-render/route service consumed by `ProcessOutboxFunction`) — NEW branch mapping `OutboxEventTypes.PayoutBatchPayoutSentMakerEmail` → `EmailTemplateType.PayoutSentMaker`, deserialize `PayoutBatchPayoutSentMakerEmailPayload`, build the render context (`batch_number`, `total_paid` server-formatted CZK, `order_count`, `fee_invoice_url`), no attachment. Resolve recipient from `payload.MakerEmail`, language from `payload.LanguageCode`.
- **`PayoutBatchConfiguration`** (EF) — map the new `BankReference` column (`HasMaxLength(PayoutBatch.MaxBankReferenceLength)`, nullable).
- **DI:** no new registration (the new repository method rides the existing `IOrderRepository` registration; `EmailSendService` branch needs no new wiring).

### Web.Admin host

- **`Web.Admin/Controllers/PayoutBatchesController.cs`** — extend (created by T-0102a) with `[HttpPost("{id}/complete")]` → `POST /api/v1/payout-batches/{id}/complete`; `[Authorize]` admin audience; `[ProducesResponseType(typeof(MarkPayoutBatchCompletedResponse), StatusCodes.Status200OK)]`; body record `{ string BankReference, DateOnly? PaymentDate }`; one-liner `mediator.Send(new MarkPayoutBatchCompleted.Command(id, body.BankReference, body.PaymentDate), ct)`. 200 on both the live-transition and Silent-Success paths.

### i18n

- **`frontend/src/lib/i18n/cs-CZ.ts`** — `'payoutBatch.notProcessing': 'Tuto výplatní dávku nelze dokončit — není ve stavu zpracování.'` (`payoutBatch.notFound` already keyed by T-0102b).

### Tests

#### PayoutBatchCompleteTests (NEW, RED-FIRST — commit before the handler, ~4 unit tests)

`backend/src/Makables.Tests/Domain/Payouts/PayoutBatchCompleteTests.cs`:
1. **Processing_to_Completed_sets_all_fields** — `Create` (born `Processing`) → `Complete(clock, "REF-123", paymentDate: null, "admin-1")`: `State == Completed`, `BankReference == "REF-123"`, `CompletedBy == "admin-1"`, `CompletedAt == clock.UtcNow`.
2. **PaymentDate_provided_sets_CompletedAt_to_midnight_utc** — `Complete(clock, "REF", new DateOnly(2026, 6, 10), "admin")`: `CompletedAt == 2026-06-10T00:00:00Z`.
3. **Complete_on_already_Completed_batch_returns_NotProcessing** — second `Complete` → `Failure` with `PayoutBatchNotProcessing`; fields unchanged from the first completion.
4. **Blank_bankReference_throws** — `Complete(clock, "", null, "admin")` → `ArgumentException` (impossible input).

#### MarkPayoutBatchCompletedHandlerTests (NEW, ~6 unit tests)

`backend/src/Makables.Tests/AppServices/Features/PayoutBatches/MarkPayoutBatchCompletedHandlerTests.cs` — NSubstitute mocks. Plus the **per-maker grouping** red-first assertion folded into #1/#3.
1. **Happy_path_completes_batch_and_orders_and_emails_per_maker** — `Processing` batch; `GetByPayoutBatchIdForCompletion...` returns 3 orders across 2 makers (maker A ×2, maker B ×1). Assert: batch `Complete` called; each `order.Complete` called; **exactly 2** outbox enqueues (one per maker); maker A's payload `MakerTotalPaidMinor == ΣA`, `OrderCount == 2`; maker B's `OrderCount == 1`; response `AlreadyCompleted == false`.
2. **Already_Completed_is_Silent_Success** — batch `State == Completed`: response `AlreadyCompleted == true` with stored `CompletedAt`/`BankReference`; `order` repo never queried; **zero** outbox enqueues; `Complete` never called again.
3. **Single_maker_many_orders_gets_one_email_with_summed_total** — 5 orders, all maker A. Assert: exactly **1** outbox enqueue; `MakerTotalPaidMinor == Σ5`; `OrderCount == 5` (dedup pinned).
4. **Batch_not_found_returns_NotFound** — repo returns null → `payoutBatch.notFound`; no transition, no email.
5. **Empty_session_returns_Unauthorized** — `session.GetUserId()` empty → `Error.Unauthorized()`; nothing loaded.
6. **Validator_rejects_blank_BankReference_and_oversize** — `BankReference = ""` → invalid (Required); `BankReference` 141 chars → invalid (MaxLength).

#### MarkPayoutBatchCompletedIntegrationTests (NEW, ~3 integration tests)

`backend/src/Makables.IntegrationTests/PayoutBatches/MarkPayoutBatchCompletedIntegrationTests.cs` — Testcontainers Postgres + admin `WebApplicationFactory`. Seed a `Processing` batch (via T-0102a's claim or a direct seed) with Delivered orders across 2 makers.
1. **Settle_e2e** — POST `/api/v1/payout-batches/{id}/complete` with `{ bankReference, paymentDate }` → 200; the `payout_batches` row is `Completed` with `bank_reference` + `completed_at` (= paymentDate midnight UTC) + `completed_by` set; **all** claimed orders are `Completed`; **N `outbox_events`** rows (`payout.payoutSent.makerEmail`, one per maker) exist; one `admin_audit_log` row `payoutBatch.complete` with the bank ref in notes + before/after JSONB.
2. **Idempotent_re_call** — second POST (different bank ref) → 200 with `AlreadyCompleted == true`; the batch row unchanged (first bank ref + date retained); **no new** outbox or audit rows; orders still `Completed` (not re-touched).
3. **Multi_maker_grouping** — seed 4 orders (maker A ×3, maker B ×1); POST → 200; exactly **2** `payout.payoutSent.makerEmail` rows; deserialize each payload: maker A `OrderCount == 3` + `MakerTotalPaidMinor == ΣA`, maker B `OrderCount == 1`.

### NSwag regen

The new `POST /api/v1/payout-batches/{id}/complete` admin endpoint is a contract change, but the **admin frontend is T-0118** — there is no admin client consumer on master yet. Per §C.13, the admin client regen rides **T-0118's PR**; T-0103 notes the contract change in its PR description and does NOT block on regen. The maker-host regen (T-0116's consumer surface) rides T-0112 + T-0112a, not this ticket.

## Alternatives Considered

- **Option A — Do nothing / leave settlement manual (SQL UPDATE by the operator).** *Rejected* — US-admin-0007 AC-2 names a "mark paid" action with payout-sent emails; a raw SQL UPDATE skips the order transitions, the per-maker emails, the audit row, and the post-payout refund gate. The self-running-marketplace mandate (CLAUDE.md) wants one auditable command.
- **Option B — Per-order `mediator.Send(new CompleteOrder.Command(...))` fan-out.** *Rejected per Q2 (Q-0008 MARS lesson)* — a nested `Send` re-enters `UnitOfWorkPipelineBehavior`, opening a second transaction scope on the shared `DbContext` and risking a MARS fault. The orders are already in the batch's scope; the materialized loop keeps the single-transaction guarantee with no isolation cost (it all commits together regardless).
- **Option C — Order completion via an async domain event.** *Rejected per Q2* — decouples order completion from batch settlement; a half-settled batch (flipped Completed, but some orders still Delivered until the event drains) is a correctness hole and breaks the post-payout refund gate's invariant ("Completed ⟹ paid out").
- **Option D — One payout-sent email per order.** *Rejected per Q3* — a maker with 12 orders in one wire gets 12 emails for one payment. The per-maker digest is one email summarizing the whole wire.
- **Option E — One digest email to all makers.** *Rejected per Q3* — cross-maker PII: each maker would see every other maker's payout total. One email per maker, scoped to that maker's orders.
- **Option F — Show the bank CSV to makers in the payout view.** *Rejected per Q4* — the CSV is the operator's bank file listing every maker's account number. Exposing it leaks cross-maker PII (GDPR) + competitive data. The maker drill-in (T-0116) shows only that maker's per-order breakdown + their fee invoice.
- **Option G — Allow un-complete / reopen a settled batch.** *Rejected per Reversibility* — completion makes four facts immutable (numbered Fee invoices, executed wire, dispatched emails, armed refund gate); reopening desynchronizes all four. Errors are corrected forward via T-0105 / T-0107.
- **Option H — Handler-written audit row (like T-0102a).** *Rejected* — T-0102a's create-command could not name a `TargetId` at command time (the batch did not exist yet). Completion targets an existing batch, so `IAdminAuditableCommand` with `TargetId = PayoutBatchId` works and the pipeline resolves before/after snapshots for free.
- **Option I — Detune the existing `GetByPayoutBatchIdUnscopedAsync` to tracked and reuse it.** *Rejected* — that method is `AsNoTracking` by contract for T-0102b's read-only artifact reads; making it tracked would silently slow every artifact render and risk accidental mutation. A tracked sibling (`...ForCompletion...`) keeps each call site's intent explicit.
- **Option J — Store `PaymentDate` as-is (a `DateOnly` column) instead of folding it into `CompletedAt`.** *Rejected per Q1* — `CompletedAt` is the single settlement-timestamp the maker UI and audit read; a separate `PaymentDate` column duplicates it and forces every consumer to coalesce. The midnight-UTC derivation keeps one timestamp authoritative; the operator's chosen value-date is preserved in it.

## Out of scope

- **Claiming orders into a batch / fee invoices / CSV generation** — T-0102a (claim) + T-0102b (fee invoices + CSV). T-0103 only settles an existing `Processing` batch.
- **Maker payout-list queries** (`GetMakerPayouts` paged + per-batch breakdown) — T-0112. T-0103 only exposes the settlement facts those queries read.
- **Maker fee-invoice download endpoint** — T-0112a (`GET /api/v1/maker/files/invoices/{invoiceId}`). The payout-sent email's `FeeInvoiceActionUrl` deep-links to the T-0116 page that calls it.
- **Maker payout frontend** (`/dashboard/maker/vyplaty` list + drill-in + download) — T-0116.
- **Admin payout frontend** + the admin NSwag regen — T-0118.
- **Timer/HTTP Function trigger for settlement** — settlement is operator-initiated by design (a human confirms the wire executed). T-0104 owns the *creation* timer; there is no auto-settle.
- **Un-complete / reopen / cancel a settled batch** — rejected per Reversibility; no method ships.
- **Negative-balance carryover for post-payout refunds** — payout-side netting is post-MVP (US-admin-0008 AC-2 warning).
- **Multi-country iteration** — the command is country-agnostic (it operates on a batch by id); only the default country produces batches at MVP.

## Acceptance criteria

- **AC-1** Given a `Processing` batch with 3 claimed Delivered orders across 2 makers, when an admin POSTs `/api/v1/payout-batches/{id}/complete` with `{ bankReference: "REF-1", paymentDate: "2026-06-10" }`, then 200 with `State = Completed`, `CompletedAt = 2026-06-10T00:00:00Z`, `BankReference = "REF-1"`, `AlreadyCompleted = false`; the batch row is `Completed`; all 3 orders are `Completed`.
- **AC-2** Given the same call with `paymentDate` omitted, then `CompletedAt = clock.UtcNow` (the settlement is timestamped at completion time).
- **AC-3** Given the completed batch from AC-1, when the admin re-POSTs with a different bank ref, then 200 with `AlreadyCompleted = true`, the **first** `BankReference`/`CompletedAt` retained, no order re-touched, no new outbox row, no new audit row (Silent-Success).
- **AC-4** Given the batch settles, then exactly **one** `payout.payoutSent.makerEmail` outbox row is enqueued **per distinct maker** (maker A with 2 orders → 1 email; maker B with 1 order → 1 email = 2 rows total), each payload carrying that maker's `MakerTotalPaidMinor` (Σ their `MakerPayoutAmountMinor`), `OrderCount`, `BatchNumber`, and a `FeeInvoiceActionUrl` deep link.
- **AC-5** Given a single maker with 5 orders in the batch, when settled, then **one** email with `OrderCount = 5` and `MakerTotalPaidMinor` = the sum of all 5 (dedup, not 5 emails).
- **AC-6** Given a non-existent batch id, when POSTed, then `payoutBatch.notFound` (404); no transition, no email.
- **AC-7** Given a batch in a state other than `Processing`/`Completed` (structurally unreachable today; guarded for future states), when targeted, then `payoutBatch.notProcessing` Conflict; no mutation.
- **AC-8** Given the settle path runs, then the batch transition + all order transitions + N outbox rows + the `payoutBatch.complete` audit row (real batch id, `Notes = bankReference`, before = `Processing` JSONB, after = `Completed` JSONB) commit in **ONE** transaction; no `SaveChangesAsync()` in the handler; a forced mid-loop failure persists nothing.
- **AC-9** Given an anonymous request or a customer/maker JWT, when POSTing, then 401/403 — admin audience per host; an empty session inside the handler fails closed as `Unauthorized`.
- **AC-10** Given the `PayoutSentMaker` template is seeded, then its cs-CZ + en-US subject + body use **double-brace** `{{batch_number}}` placeholders (no single-brace token survives to the recipient — Q-0017 lesson); the cs-CZ body is tykání.
- **AC-11** Build clean; unit baseline + ~10 new (~4 `PayoutBatchComplete` red-first + ~6 handler); integration baseline + ~3 new; `node scripts/check-consistency.mjs` exit 0; the new error code has a cs-CZ key; the `BankReference` migration + the `PayoutSentMaker` seed migration apply cleanly; admin NSwag regen deferred to T-0118 (contract change noted in the PR description).

## Technical notes

### Why the materialized loop, not per-order `mediator.Send` (Q-0008 MARS lesson)

A `mediator.Send` inside a command handler opens a fresh MediatR pipeline scope, which re-enters `UnitOfWorkPipelineBehavior` and (depending on the DI scope) either nests a second `SaveChanges`/transaction or contends on the same `DbContext`'s open reader — the classic Multiple Active Result Sets fault. The claimed orders are already inside the batch's transactional scope; `order.Complete(clock)` is a pure in-memory state flip with no provider call and no cross-aggregate validation. Materializing the ids once (`GetByPayoutBatchIdForCompletionUnscopedAsync`) and looping in the single handler keeps one `DbContext`, one transaction, one commit — exactly what ADR 0014 wants for an atomic settlement.

### Why completion is forward-only

At the `Completed` instant, four facts harden: the per-batch Fee invoices are numbered and legally immutable (T-0102b); the bank wire has executed (real money left the company account); the per-maker payout-sent emails are queued for dispatch; and `Order.ValidateRefund` now requires `AcknowledgePostPayout` (T-0105) because the maker has been paid. A reopen would have to un-number invoices, claw back a wire, un-send emails, and disarm the refund gate — none of which is safe at MVP. Forward correction (refund T-0105, manual state change T-0107) handles every real error.

### Why one email per maker (not per order, not one digest)

A weekly wire pays a maker for every Delivered order at once; the maker wants one "you were paid X for N orders, here's your fee invoice" email, not one per order (inbox spam) and certainly not a shared digest that would expose other makers' totals (cross-maker PII). Grouping by `MakerId` in the same loop that completes the orders costs one dictionary and emits exactly one outbox row per distinct maker.

### Why the bank CSV is never shown to makers

The CSV is the operator's bank-transfer file: one row per maker, each with that maker's **bank account number** and payout amount. It is structurally a cross-maker document. A maker viewing it would see every other maker's account and payout — a GDPR personal-data leak and a competitive-data leak. The maker payout view (T-0116) renders only that maker's own per-order breakdown (order #, product price, platform fee, net payout) plus their own Fee invoice PDF; the CSV stays admin-only.

### Why double-brace email subjects (Q-0017 lesson)

The email renderer resolves `{{token}}` placeholders. The Q-0017 bug seeded subject literals with single-brace `{token}` because the seed SQL was built inside C# `$@"..."` interpolated strings where `{{` collapses to `{`. The `PayoutSentMaker` seed migration writes `{{batch_number}}` (and friends) verbatim — built via non-interpolated constants or escaped correctly — so no raw token reaches a recipient.

## Files touched (expected)

### New
- `backend/src/Makables.Core.Domain/Outbox/PayoutBatchPayoutSentMakerEmailPayload.cs`
- `backend/src/Makables.Core.AppServices/Features/PayoutBatches/MarkPayoutBatchCompleted.cs`
- `backend/src/Makables.Infra.Database/Migrations/2026xxxx_AddPayoutBatchBankReference.cs` (+ Designer)
- `backend/src/Makables.Infra.Database/Migrations/2026xxxx_SeedPayoutSentMakerEmailTemplate.cs` (+ Designer)
- `backend/src/Makables.Tests/Domain/Payouts/PayoutBatchCompleteTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/PayoutBatches/MarkPayoutBatchCompletedHandlerTests.cs`
- `backend/src/Makables.IntegrationTests/PayoutBatches/MarkPayoutBatchCompletedIntegrationTests.cs`

### Modified
- `backend/src/Makables.Core.Domain/Payouts/PayoutBatch.cs` — `Complete` method + `BankReference` property + `MaxBankReferenceLength`
- `backend/src/Makables.Core.Domain/Orders/IOrderRepository.cs` + `backend/src/Makables.Infra.Database/Orders/OrderRepository.cs` — `GetByPayoutBatchIdForCompletionUnscopedAsync` (tracked)
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` — `PayoutBatchNotProcessing`
- `backend/src/Makables.Core.Domain/Email/EmailTemplateType.cs` — `PayoutSentMaker = 16`
- `backend/src/Makables.Core.Domain/Outbox/OutboxEventTypes.cs` — `PayoutBatchPayoutSentMakerEmail` + `IsEmailSend`
- `backend/src/Makables.Infra.Database/...PayoutBatchConfiguration.cs` — map `BankReference`
- `backend/src/Makables.Infra.*/...EmailSendService.cs` — `PayoutSentMaker` routing branch
- `backend/src/Makables.Web.Admin/Controllers/PayoutBatchesController.cs` — `POST /{id}/complete` action
- `frontend/src/lib/i18n/cs-CZ.ts` — `payoutBatch.notProcessing` key
- `docs/user-stories/maker/README.md` — BA updates US-maker-0012 AC-2 (drop stale `Pending` enum; Processing → "připravujeme", Completed → "vyplaceno") post-merge

## Commits hint

1. `test(T-0103): pin PayoutBatch.Complete guard + per-maker grouping (red)`.
2. `feat(T-0103): PayoutBatch.Complete + BankReference column migration + tracked order load`.
3. `feat(T-0103): MarkPayoutBatchCompleted feature + PayoutSentMaker template seed + outbox event + EmailSendService branch + controller + error code + i18n`.
4. `test(T-0103): handler + integration coverage (settle e2e, idempotent re-call, multi-maker grouping)`.

## Test plan reference

Inline above (Scope > Tests). No separate `docs/test-plans/T-0103.md`.

## Status log

- 2026-06-13 `draft` by BA. Settlement ticket of the payout bundle (PR #2). Consumes T-0102a's claim (`Processing` batch + claimed orders), T-0102b's Fee invoices (linked from the payout-sent email), T-0101's `PayoutBatch` entity (`CompletedAt`/`CompletedBy` columns reserved; `Complete()` ships here), and T-0011's email-template/outbox infrastructure. Adds the `BankReference` column, the `PayoutSentMaker` template, and the `PayoutBatchPayoutSentMakerEmail` outbox event.
- 2026-06-13 `draft → ready`. User locked Q1–Q5 + Reversibility at the 2026-06-13 payout-settlement deliberation (recorded §A); 14 PM-absorbed decisions in §C, including Silent-Success idempotency, the materialized-loop-not-mediator.Send MARS guard (Q-0008), the per-maker grouping/dedup, the double-brace seed (Q-0017), `IAdminAuditableCommand` audit (vs T-0102a's handler-written entry), and the deferred admin NSwag regen (T-0118). No manual_steps. **Ready for dotnet-backend** — sequence: red `PayoutBatch.Complete` + grouping tests → `Complete` method + bank-ref migration → feature + template seed + outbox event + EmailSendService branch + controller → handler + integration coverage.
- 2026-06-14 **AC-3 softened (Q-0021 ruled, architect).** The "no second audit row" clause is DROPPED platform-wide: the shared `AdminAuditPipelineBehavior` correctly writes an audit row on every successful `IAdminAuditableCommand`, and a no-op re-call row is itself an audit-worthy "admin attempted X" record (no pipeline change). AC-3 now asserts robust **state-idempotency only** — `AlreadyCompleted = true`, first `BankReference`/`CompletedAt` retained, no order re-touched, no second outbox row. The benign no-op `payoutBatch.complete` audit row on the idempotent re-call is accepted and expected. See [Q-0021](../questions/open.md#q-0021--adminauditpipelinebehavior-writes-a-no-op-audit-row-on-idempotent-silent-success-re-calls).

## Definition of ready

- [x] User stories linked (US-admin-0007 AC-2; US-maker-0012 settlement facts) with AC traceability
- [x] Blocking design decisions locked (Q1–Q5 + Reversibility) with rebutted alternatives
- [x] Dependencies on master or in-bundle (T-0102a claim, T-0102b fee invoices, T-0101 entity + columns, T-0011 email infra)
- [x] Error codes + i18n keys enumerated (reuse `PayoutBatchNotFound`; one new `PayoutBatchNotProcessing`)
- [x] Test surface named (domain guard + grouping red-first; ~10 unit, ~3 integration)
- [x] Security posture stated (admin audience, fail-closed session, unscoped reads admin-only, atomic terminal money transition, forward-only)
