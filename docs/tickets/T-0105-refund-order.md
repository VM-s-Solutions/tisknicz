---
id: T-0105
title: RefundOrder command (admin) + Comgate RefundAsync + order-refunded email
status: ready
size: M
owner: dotnet-backend
created: 2026-06-12
updated: 2026-06-12
depends_on: [T-0066, T-0067]
blocks: [T-0106, T-0107, T-0118]
user_stories: [US-admin-0008]
adrs: [0013, 0014, 0016]
phase: 5
manual_steps: []
security_touching: true
layers: [domain, appservices, infra-database, infra-clients, web-admin]
---

# T-0105 — RefundOrder command (admin) + Comgate RefundAsync + order-refunded email

## Context

T-0105 is the **first ticket in the order-cleanup bundle** (T-0105 refund → T-0106 disputes → T-0107 admin manual state change). Bundle order is locked: T-0106's `ResolveDispute` dispatches `RefundOrder.Command` for refund outcomes, and T-0107's strict allow-list names `RefundOrder` as the only sanctioned path into `Refunded` — so the refund command must exist first.

This ticket satisfies **US-admin-0008 — Refund an order** (AC-1 full/partial refund via `PaymentProvider.RefundAsync` + customer email; AC-2 Completed/post-payout acknowledgement warning; AC-3 provider Permanent error surfaced with order unchanged). It also closes the standing ops liability flagged in `ComgateWebhookController` (T-0083 SecOps Gate 3 check 7): a PAID webhook landing on a Cancelled order logs *"manual refund required until T-0105 ships"* — after this ticket, admins have an automated flow for that money.

Three seams already exist on master and de-risk this ticket: (1) `IPaymentProvider.RefundAsync` is declared (T-0065) with `RefundReceipt(RefundProviderRef, AmountMinor, Currency, RefundedAt)`; `ComgatePaymentProvider.RefundAsync` throws `NotSupportedException` naming this ticket. (2) `Order.Refund(IClock)` exists with the state edge Paid/Accepted/Shipped/Delivered/Completed → Refunded — this ticket **reshapes** it for partial refunds per user decision Q1. (3) `AdminAuditPipelineBehavior` + `IAdminAuditableCommand` give before/after JSONB auditing for free (ADR 0014).

**This is the first admin-host endpoint.** `Web.Admin/Program.cs` is fully wired (auth, audience, rate limiting, OpenAPI) but has no `Controllers/` folder yet — T-0105 creates `Web.Admin/Controllers/OrdersController.cs` and establishes the admin controller precedent (one-liner Mediator dispatch, `[Authorize]` under the admin audience per ADR 0013). NSwag regen for the admin host produces `frontend/src/lib/api-client/admin-api.v1.ts` for the first time.

Money movement makes this ticket **security_touching**. The handler's order of operations is a locked decision (§A.5): provider call BEFORE domain mutation.

## Locked design decisions

Captured per `docs/process/deliberation.md`. User locked 5 dimensions at the 2026-06-12 batched deliberation (Q1–Q5); PM absorbed the rest from T-0067/T-0076/T-0083 precedents.

### A. User-locked (non-negotiable)

1. **Full + partial refunds (Q1).** Admin enters an amount ≤ remaining refundable (`TotalAmountMinor − RefundedAmountMinor`). Partial refunds accumulate into a new `Order.RefundedAmountMinor` column with **no state change**; when cumulative == total the order transitions to `State = Refunded` + `RefundedAt`. No credit-note invoice at MVP (v1.1). **Rejected:** full-only (forces admins into all-or-nothing on shipping-only disputes); partial flips state (a partially-refunded order is still a live order — the maker still ships/delivers).
2. **Disputed orders are NOT refundable directly (Q2 interplay).** The state gate stays Paid/Accepted/Shipped/Delivered/Completed. T-0106's `ResolveDispute` restores `Order.PreDisputeState` first, then dispatches `RefundOrder.Command` against the restored state. Forward note pinned for T-0106 grooming.
3. **T-0107 allow-list interlock (Q4).** Manual state change can never reach `Refunded` — `RefundOrder` is the only sanctioned path (money must move before the state does). The blocked-transition error in T-0107 names this command.
4. **Completed-state refund = acknowledgement gate (Q5).** Refunding a `Completed` order (maker already paid out) requires `AcknowledgePostPayout == true`, else the command is blocked with `payment.refund.postPayoutAckRequired`. The acknowledgement is recorded in the audit entry. Maker-share recovery is **manual at MVP**; forward note pinned for T-0102 grooming (negative-balance ledger).
5. **Provider-first order of operations.** The handler calls `RefundAsync` BEFORE mutating the order. Rationale: a recorded-but-not-refunded order lies to the customer and to T-0107's allow-list; a refunded-but-unrecorded order (provider success, commit failure) is recoverable — Comgate caps cumulative refunds at the captured amount, so an admin retry cannot over-refund at the gateway, and ops reconciles from the Warning log. **Rejected:** mutate-then-compensate (no compensation API for "un-record a refund"; the failure mode inverts to the worse one).

### B. ADR-locked (no relitigation)

- **ADR 0013** — admin host only. `GetByIdUnscopedAsync` (tracked) is the sanctioned admin lookup; the endpoint runs under `aud=admin`; a customer/maker JWT cannot be replayed against `Web.Admin`.
- **ADR 0014** — `Command : ICommand, IAdminAuditableCommand`; `AdminAuditPipelineBehavior` captures before/after JSONB; failed commands write no audit row; `UnitOfWorkPipelineBehavior` commits mutation + outbox + audit atomically; handler never calls `SaveChangesAsync()`.
- **ADR 0016** — provider adapter pattern. Refund HTTP lives only in `Infra.Clients/Comgate`; error classification Transient/Permanent/Configuration per the existing `MapComgateBusinessError` table; selection via `IPaymentProviderFactory` reading `CountryConfiguration.DefaultPaymentProvider` (no `if (countryCode == "CZ")`).
- **One-file feature shape** — `Features/Orders/RefundOrder.cs` with nested `Command`, `RefundOrderResponse` (globally-unique name), `Validator`, `Handler`.
- **Centralized error codes** — all new failures under `BusinessErrorMessage` `payment.refund.*`; no inline strings.

### C. PM-absorbed (no user input needed)

- **Silent Success on re-refund of a fully-Refunded order** (T-0067/T-0076 precedent): handler returns Success WITHOUT provider call, mutation, outbox, or duplicate state change. Mirrors `CancelExpiredOrder` step 2.
- **TDD red-first** for the domain refund predicates (T-0067+ hard rule): `Order.ValidateRefund` tests are committed failing before the implementation commit.
- **Pure predicate + mutator split**: `Order.ValidateRefund(long amountMinor, bool acknowledgePostPayout)` returns `BusinessResult` without mutating; `Order.Refund(IClock, long, bool)` calls it then mutates. The handler pre-flights via the same predicate BEFORE money moves (locked A.5) — one source of truth, no drift.
- **Enrichment-at-enqueue email pattern** kept pending Q-0012: full payload (`OrderRefundedCustomerEmailPayload`) serialized into the outbox row; pre-baked `ActionUrl` from `PublicAppUrlsOptions`.
- **Acknowledgement in audit**: `Command.Notes` folds the reason + a `[post-payout refund acknowledged]` marker when the flag is set — the pipeline behavior persists `Notes` verbatim, satisfying US-admin-0008 AC-2 without behavior changes.
- **ActionCode** `order.refund`, `TargetEntity` `order`, `TargetId` = OrderId.
- **`RefundProviderRef` fallback**: Comgate's `/v1.0/refund` response carries `code`/`message` only (no distinct refund id) — the adapter returns the original `transId` as `RefundProviderRef`, documented on the receipt.
- **No new currency column** — refunds are always in `Order.Currency`; `refunded_amount_minor BIGINT NOT NULL DEFAULT 0` satisfies the money-column convention alongside the existing `currency CHAR(3)`.
- **NSwag regen: admin host only** in this ticket (first generation of `admin-api.v1.ts`). Customer + maker host regens land with T-0106's dispute endpoints per Q3.
- **No idempotency key on partial refunds at MVP** — the remaining-amount cap bounds double-submit damage; T-0118's admin UI adds a confirm step. Deferred post-MVP (see Risk).

## Scope

### Domain layer

- **`Core.Domain/Orders/Order.cs`** — reshape the refund surface:
  - NEW `public long RefundedAmountMinor { get; private set; }` (default 0) + computed `public long RemainingRefundableMinor => TotalAmountMinor - RefundedAmountMinor;`.
  - NEW `public BusinessResult ValidateRefund(long amountMinor, bool acknowledgePostPayout)` — pure, no mutation: `amountMinor <= 0` → `ArgumentException` (programmer error; Validator catches user input); state ∉ {Paid, Accepted, Shipped, Delivered, Completed} → `payment.refund.invalidState`; `amountMinor > RemainingRefundableMinor` → `payment.refund.amountExceedsRemaining`; `State == Completed && !acknowledgePostPayout` → `payment.refund.postPayoutAckRequired`; else Success.
  - RESHAPED `public BusinessResult Refund(IClock clock, long amountMinor, bool acknowledgePostPayout)` — calls `ValidateRefund`, then accumulates `RefundedAmountMinor += amountMinor`; when cumulative == total: `State = Refunded; RefundedAt = clock.UtcNow;` otherwise state + `RefundedAt` untouched. Existing `Refund(IClock)` signature is removed (no callers on master; existing domain tests reshape).
- **`Core.Domain/Common/BusinessErrorMessage.cs`** — 4 new codes: `payment.refund.invalidState`, `payment.refund.amountExceedsRemaining`, `payment.refund.postPayoutAckRequired`, `payment.refund.noProviderRef`.
- **`Core.Domain/Outbox/OutboxEventTypes.cs`** — NEW `OrderRefundedCustomerEmail = "order.refunded.customerEmail"` + added to `IsEmailSend`.
- **`Core.Domain/Outbox/OrderRefundedCustomerEmailPayload.cs`** — NEW record: `(OrderId, OrderNumber, Email, ContactName, RefundedAmountMinor, Currency, IsFullRefund, LanguageCode, ActionUrl)`. Mirrors `OrderCancelledCustomerEmailPayload`.
- **`Core.Domain/Email/EmailTemplateType.cs`** — NEW `OrderRefundedCustomer = 12`.

### AppServices layer

- **`Core.AppServices/Features/Orders/RefundOrder.cs`** — NEW one-file feature:
  - `Command(string OrderId, long AmountMinor, string Reason, bool AcknowledgePostPayout) : ICommand<RefundOrderResponse>, IAdminAuditableCommand` — `ActionCode => "order.refund"`, `TargetEntity => "order"`, `TargetId => OrderId`, `Notes => AcknowledgePostPayout ? $"{Reason} [post-payout refund acknowledged]" : Reason`.
  - `RefundOrderResponse(string OrderId, OrderState State, long RefundedAmountMinor, long RemainingRefundableMinor, bool IsFullRefund)`.
  - `Validator`: `OrderId` NotEmpty + Max 40; `AmountMinor` GreaterThan(0); `Reason` NotEmpty + Max 2000 (audit-log column width, VerifyMaker m-3 precedent).
  - `Handler(IOrderRepository orders, IPaymentProviderFactory providerFactory, IUserRepository users, IOutbox outbox, IClock clock, ILanguageResolver languageResolver, IOptions<PublicAppUrlsOptions> publicAppUrls, IUserSessionProvider session, ILogger<Handler> logger)` steps (NO `SaveChangesAsync()`):
    1. **Fail-closed session check** — `session.GetUserId()` empty → `Error.Unauthorized()` (VerifyMaker T-0034 precedent; audit must never attribute to "system").
    2. **Load tracked unscoped** — `GetByIdUnscopedAsync`; null → `order.notFound`.
    3. **Silent Success** — `State == Refunded` → `Success(response)` without provider call / mutation / outbox; Info log.
    4. **Pre-flight** — `PaymentProviderRef is null` → `payment.refund.noProviderRef`; then `order.ValidateRefund(AmountMinor, AcknowledgePostPayout)` — any failure surfaces here, **provider never called**.
    5. **Provider refund (money moves FIRST per A.5)** — resolve via `providerFactory.ResolveAsync(order.CountryCode)`; `provider.RefundAsync(order.PaymentProviderRef, AmountMinor, order.Currency, ct)`. Failure (Transient/Permanent/Configuration) → surfaced verbatim, order unchanged, NO outbox, NO audit row (pipeline skips on failure) — US-admin-0008 AC-3.
    6. **Mutate** — `order.Refund(clock, AmountMinor, AcknowledgePostPayout)`; failure here is belt-and-braces (pre-flight covered it) — log Critical (money moved, record refused) and surface.
    7. **Email** — resolve customer (`OrderCustomerUserMissing` Critical on FK violation, CancelExpiredOrder precedent) + language; enqueue `order.refunded.customerEmail` with the full payload + pre-baked `ActionUrl` (`{WebBaseUrl}/objednavka/{order.Id}`).
    8. UoW commits mutation + outbox + audit atomically.

### Infra.Clients (Comgate)

- **`Infra.Clients/Comgate/ComgatePaymentProvider.cs`** — implement `RefundAsync` replacing the `NotSupportedException` stub: form-urlencoded POST `{BaseUrl}/v1.0/refund` with fields `merchant`, `transId` (= providerRef), `amount` (minor units, invariant culture), `curr`, `test` (when `options.TestMode`), `secret` last (never logged, never in URL); reuse `CallComgateAsync` (Polly retry pipeline, fresh request per attempt) + `ReadResponseAsync` + `MapComgateBusinessError` (1100/1102 → Configuration + Critical; other nonzero → Permanent `PaymentProviderRejected`; transport → Transient `PaymentProviderUnavailable`). `code == "0"` → `RefundReceipt(transId, amountMinor, currency, clock.UtcNow)` — inject `IClock` (constructor extension; entity-grade time discipline). Structured logging mirrors `CreatePaymentAsync` (named properties; no body interpolation).

### Infrastructure / Database

- **`Infra.Database/Orders/OrderConfiguration.cs`** — map `RefundedAmountMinor` → `refunded_amount_minor BIGINT NOT NULL DEFAULT 0`. `RefundedAt`/`refunded_at` already exists from T-0060 (verify; add if absent).
- **NEW EF migration** — adds `refunded_amount_minor` (existing rows backfill 0 via the DEFAULT) + seeds `EmailTemplateType.OrderRefundedCustomer` rows for **cs-CZ + en-US** (T-0067 seed-in-migration precedent): subject "Vrácení peněz k objednávce {orderNumber}" / "Refund for order {orderNumber}"; body renders amount (CZK display rules), full-vs-partial copy via `IsFullRefund`, and the action link.

### Web.Admin host

- **`Web.Admin/Controllers/OrdersController.cs`** — NEW (first admin controller; establishes the precedent): `POST /api/v1/admin/orders/{orderId}/refund`, `[Authorize]` under the admin audience (ADR 0013), body `{ amountMinor, reason, acknowledgePostPayout }`, one-liner `mediator.Send(new RefundOrder.Command(...))` → `HandleResult`. `[ProducesResponseType(typeof(RefundOrderResponse), 200)]` for NSwag.

### Frontend

- **`frontend/src/lib/i18n/cs-CZ`** — 4 new keys mirroring the `payment.refund.*` codes.
- **NSwag regen, admin host** — first generation of `frontend/src/lib/api-client/admin-api.v1.ts`; committed in the same PR; no manual edits (pre-commit hook).

### Tests

#### Domain — `OrderRefundTests` (red-first commit, ~7 unit)

1. `ValidateRefund_rejects_amount_exceeding_remaining` (after a prior partial, `remaining + 1` → `amountExceedsRemaining`).
2. `ValidateRefund_throws_on_non_positive_amount` (`ArgumentException` for 0 / −1).
3. `ValidateRefund_rejects_invalid_states` (PendingPayment, Cancelled, Disputed, Refunded → `invalidState`).
4. `ValidateRefund_on_Completed_requires_acknowledgement` (false → `postPayoutAckRequired`; true → Success).
5. `Refund_partial_accumulates_without_state_change` (RefundedAmountMinor grows; State + RefundedAt untouched).
6. `Refund_full_transitions_to_Refunded_with_timestamp` (pinned IClock).
7. `Refund_two_partials_summing_to_total_transitions_to_Refunded`.

#### Handler — `RefundOrderHandlerTests` (~4 unit, NSubstitute)

8. `Happy_path_full_refund` — provider called once with `(PaymentProviderRef, amount, currency)`; order Refunded; outbox enqueued with `order.refunded.customerEmail` + payload fields (`IsFullRefund == true`, ActionUrl pre-baked).
9. `Provider_permanent_error_surfaces_with_order_unchanged_and_no_outbox` (US-admin-0008 AC-3).
10. `Already_refunded_returns_silent_success_without_provider_call_or_outbox`.
11. `Missing_provider_ref_blocks_before_provider_call` (`noProviderRef`; `RefundAsync` never invoked).

#### Integration — `RefundOrderIntegrationTests` (~3, Testcontainers + extended `FakeComgatePaymentProvider`)

1. `POST_full_refund_as_admin` — 200; DB: `state = Refunded`, `refunded_amount_minor == total`, `refunded_at` set; outbox row with the event type; `admin_audit_log` row with `action_code = order.refund`, before/after JSONB showing the state flip, notes carrying the reason.
2. `Partial_then_over_refund` — first POST (partial) → 200, state unchanged, amount accumulated; second POST exceeding remaining → 409 `payment.refund.amountExceedsRemaining`, no second provider call recorded by the fake.
3. `Customer_JWT_rejected_on_admin_host` — 401 (audience enforcement, ADR 0013); anonymous → 401.

### Docs

- **`docs/architecture/roles/order.md`** — refund surface: cumulative `RefundedAmountMinor`, the `ValidateRefund`/`Refund` split, T-0107 allow-list interlock, T-0106 PreDisputeState-restore-then-refund sequencing.
- **`docs/tickets/INDEX.md`** — PM flips T-0105 post-merge.

## Alternatives Considered

- **Option A — Mutate the order first, compensate on provider failure.** *Rejected per A.5* — there is no compensation API for "un-record a refund" short of hand-editing rows; a recorded-but-not-refunded order misleads the customer email, the audit trail, and T-0107's allow-list. Provider-first leaves the recoverable failure mode (Comgate caps cumulative refunds at the capture, so retry is gateway-safe).
- **Option B — Full refunds only at MVP.** *Rejected per Q1* — shipping-damage and late-delivery disputes routinely warrant partial compensation; full-only forces admins to over-refund or do nothing.
- **Option C — Partial refund transitions state to `Refunded`.** *Rejected per Q1* — a partially-refunded order is still live (maker still ships/delivers); killing it on a 50 Kč shipping rebate is wrong. State changes only when cumulative == total.
- **Option D — Credit-note invoice in this ticket.** *Rejected per Q1* — v1.1. At MVP the refund is evidenced by the audit row + customer email; the invoicing role gains credit notes later.
- **Option E — Refund directly from `Disputed`.** *Rejected per A.2* — `Disputed` masks the real lifecycle position; T-0106 restores `PreDisputeState` then dispatches this command, keeping one state gate.
- **Option F — Per-refund child entity (refund ledger) instead of a cumulative column.** *Rejected at MVP* — the audit log's before/after snapshots already record each refund's amount and actor; a ledger adds a table + repository for no MVP read path. Re-evaluate with T-0102's negative-balance ledger.
- **Option G — Idempotency key on the command.** *Deferred* — Silent Success covers the full-refund re-fire; partial double-submits are bounded by the remaining-amount cap and gated by T-0118's confirm UI. A key adds client-coordination cost ahead of demonstrated need.

## Out of scope

- **Dispute entity + open/resolve endpoints** — T-0106 (which dispatches this command).
- **Admin manual state change + allow-list** — T-0107.
- **Credit-note invoice** — v1.1 per Q1.
- **Maker-share recovery / negative-balance ledger** — manual at MVP per Q5; forward note pinned for T-0102 grooming.
- **Admin refund UI** — T-0118 (incl. the acknowledgement checkbox + confirm step).
- **Maker notification email on refund** — customer-only at MVP; the maker learns via T-0118's order detail (revisit if support load shows otherwise).
- **Refund idempotency key** — deferred (Alternatives G).
- **Customer/maker host NSwag regen** — T-0106 per Q3.

## Acceptance criteria

- **AC-1** Given an order in `Paid`–`Delivered` with `PaymentProviderRef`, when admin POSTs `/api/v1/admin/orders/{id}/refund` with `amountMinor == TotalAmountMinor`, a reason, and a valid admin JWT, then Comgate `/v1.0/refund` is called with `(transId, amount, curr)`, the order transitions to `Refunded` with `RefundedAt` set and `refunded_amount_minor == total`, and the response is 200 with `{ state: Refunded, isFullRefund: true, remainingRefundableMinor: 0 }`.
- **AC-2** Given the same order, when admin refunds a partial amount < total, then `refunded_amount_minor` accumulates, `State` and `RefundedAt` are unchanged, and a second partial covering the remainder transitions the order to `Refunded`.
- **AC-3** Given a prior partial refund, when admin submits an amount > remaining, then 409 `payment.refund.amountExceedsRemaining` and the provider is **never called**.
- **AC-4** Given an order in `Completed`, when admin submits without `acknowledgePostPayout`, then 409 `payment.refund.postPayoutAckRequired` and no provider call; when submitted with the flag, the refund proceeds and the audit entry's notes carry the `[post-payout refund acknowledged]` marker (US-admin-0008 AC-2).
- **AC-5** Given Comgate returns a Permanent rejection (e.g. refund window expired), when the command runs, then the error code surfaces to the admin, the order row is byte-identical to before, and **no** outbox row and **no** audit row exist (US-admin-0008 AC-3).
- **AC-6** Given an order already in `Refunded`, when the command re-fires, then Silent Success: 200, provider not called, no mutation, no outbox (T-0067/T-0076 precedent).
- **AC-7** Given an order without `PaymentProviderRef` or in `PendingPayment`/`Cancelled`/`Disputed`, when the command runs, then `payment.refund.noProviderRef` / `payment.refund.invalidState` respectively; provider never called.
- **AC-8** Given a successful refund, when the UoW commits, then exactly one `order.refunded.customerEmail` outbox row exists with the full payload (amount, currency, `IsFullRefund`, language, pre-baked ActionUrl), `OutboxEventTypes.IsEmailSend` routes it, and `EmailTemplateType.OrderRefundedCustomer` seeds exist for cs-CZ + en-US.
- **AC-9** Given a successful refund, when the audit log is inspected, then one row has `action_code = "order.refund"`, `target_entity = "order"`, before/after JSONB pinning the `refunded_amount_minor` (and state, when full) change, the admin's user id, and the reason in notes.
- **AC-10** Given an anonymous request or a customer/maker-audience JWT, when the endpoint is called, then 401 — the admin audience is enforced per host (ADR 0013).
- **AC-11** Given the migration runs against existing data, then `refunded_amount_minor` is `BIGINT NOT NULL DEFAULT 0` and every pre-existing order reads 0; the `_minor` + `currency` money convention holds.
- **AC-12** Build clean. Unit: baseline + ~11 new (domain red-first commit precedes implementation). Integration: baseline + ~3 new. `payment.refund.*` codes have cs-CZ i18n keys. NSwag admin-host client generated and committed in the same PR; no manual `api-client/` edits. `node scripts/check-consistency.mjs` exit 0.

## Risk

- **Refunded-but-unrecorded** (provider success → commit failure): accepted per A.5. Comgate refuses cumulative refunds beyond the capture, so a blind admin retry cannot over-refund at the gateway; the Critical log on step 6 failure is the ops reconciliation trigger.
- **Partial double-submit**: no idempotency key (Alternatives G); damage bounded by the remaining cap; T-0118 UI adds confirmation.
- **Comgate sandbox refund semantics**: `/v1.0/refund` behavior on sandbox transIds must be verified during implementation; the integration suite uses `FakeComgatePaymentProvider` so CI does not depend on the sandbox.
- **Maker-share exposure on post-payout refunds**: platform fronts the refund until T-0102's negative-balance ledger lands — Q5 explicitly accepts this; volume is expected to be near-zero at MVP.

## Test plan reference

Inline above (Scope > Tests). No separate `docs/test-plans/T-0105.md`. Red-first ordering: domain `OrderRefundTests` commit (failing) → domain + migration commit → feature + adapter + controller commit → handler/integration tests commit.

## Files touched (expected)

### New
- `backend/src/Makables.Core.AppServices/Features/Orders/RefundOrder.cs`
- `backend/src/Makables.Core.Domain/Outbox/OrderRefundedCustomerEmailPayload.cs`
- `backend/src/Makables.Web.Admin/Controllers/OrdersController.cs`
- `backend/src/Makables.Infra.Database/Migrations/*_AddOrderRefundedAmountAndRefundEmailTemplate.cs`
- `backend/src/Makables.Tests/Domain/Orders/OrderRefundTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/Orders/RefundOrderHandlerTests.cs`
- `backend/src/Makables.IntegrationTests/Orders/RefundOrderIntegrationTests.cs`
- `frontend/src/lib/api-client/admin-api.v1.ts` (NSwag-generated, first time)

### Modified
- `backend/src/Makables.Core.Domain/Orders/Order.cs` — `RefundedAmountMinor` + `ValidateRefund` + reshaped `Refund`
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` — 4 `payment.refund.*` codes
- `backend/src/Makables.Core.Domain/Outbox/OutboxEventTypes.cs` — new event + `IsEmailSend`
- `backend/src/Makables.Core.Domain/Email/EmailTemplateType.cs` — `OrderRefundedCustomer = 12`
- `backend/src/Makables.Infra.Clients/Comgate/ComgatePaymentProvider.cs` — `RefundAsync` implementation (+ `IClock` injection)
- `backend/src/Makables.Infra.Database/Orders/OrderConfiguration.cs` — column mapping
- `backend/src/Makables.IntegrationTests/Common/FakeComgatePaymentProvider.cs` — refund support + call recording
- `frontend/src/lib/i18n/cs-CZ/*` — 4 new error keys
- `docs/architecture/roles/order.md`, `docs/tickets/INDEX.md`

## Commits hint

1. `test(T-0105): pin Order refund predicates (red)` — domain tests failing.
2. `feat(T-0105): Order.ValidateRefund/Refund + refunded_amount_minor migration + error codes`
3. `feat(T-0105): ComgatePaymentProvider.RefundAsync + RefundOrder feature + admin OrdersController + email seed`
4. `test(T-0105): handler + integration coverage; NSwag admin-host regen + i18n keys`

## Status log

- 2026-06-12 `draft → ready` by BA/PM. User locked Q1–Q5 at the 2026-06-12 batched deliberation (full+partial refunds with cumulative column; Disputed handled via T-0106 restore-then-refund; T-0107 allow-list interlock; Completed acknowledgement gate; provider-first order of operations). PM absorbed 11 decisions in §C (Silent Success, red-first, predicate/mutator split, enrichment-at-enqueue, acknowledgement-in-Notes, RefundProviderRef fallback, admin-host-only NSwag, no idempotency key). First ticket of the order-cleanup bundle: T-0105 → T-0106 → T-0107, one branch, sequential. **Ready for dotnet-backend.**

## Definition of Ready

- [x] User story exists with AC (US-admin-0008, `docs/user-stories/admin/README.md`)
- [x] Open questions resolved (Q1–Q5 locked 2026-06-12; no entries pending in `docs/questions/open.md` for this scope)
- [x] Dependencies on master (T-0066 webhook, T-0067 MarkAsPaid + email-seed precedent, `IPaymentProvider.RefundAsync` declared, `RefundReceipt` shaped, `AdminAuditPipelineBehavior` live)
- [x] Security review flagged (`security_touching: true` — money movement; SecOps gate applies at PR)
- [x] Bundle order + ownership confirmed (dotnet-backend; one PR for T-0105–T-0107)
