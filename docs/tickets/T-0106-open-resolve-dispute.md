---
id: T-0106
title: Open + resolve dispute — Dispute entity, party/carrier/admin open endpoints, admin resolution
status: ready
size: M
owner: dotnet-backend
created: 2026-06-12
updated: 2026-06-12
depends_on: [T-0011, T-0060, T-0077, T-0078, T-0079, T-0105]
blocks: [T-0107, T-0118]
user_stories: [US-admin-0011]
adrs: [0013, 0014, 0017, 0019, 0020]
phase: 5
manual_steps:
  - "Set ADMIN_NOTIFICATION_EMAIL (new EmailOptions setting) in every environment — recipient of order.disputed.adminEmail. Document in deployment env-var docs."
security_touching: true
layers: [domain, appservices, infra-database, infra-email, web-customer, web-maker, web-admin, functions]
---

# T-0106 — Open + resolve dispute

## Context

T-0106 is the **second ticket in the order-cleanup admin bundle** (T-0105 refund → T-0106 dispute → T-0107 manual state change; one PR, sequential implementation — refund first because `ResolveDispute` dispatches `RefundOrder.Command`). It satisfies **US-admin-0011 — Open and resolve a dispute** AC-1 (party-opened dispute → `Disputed` + admin notification), AC-2 (admin resolution via existing commands + notes + notification), and AC-3 (auto-deliver skips disputed orders).

The dispute domain replaces the **T-0078 STUB** at `Core.AppServices/Features/Orders/DisputeShipment.cs`. The stub detects Packeta `Returned`/`Failed` terminal states, logs a Warning, and emits the unrouted `order.disputed.carrierSourced` outbox event **without mutating Order state** — its own XML doc promises "T-0106 will wire the real `OrderState.Disputed` transition" and that "a Disputed order short-circuits in the T-0078 Function's Shipped predicate", resolving the stub's intentional repeat-emission behaviour. This ticket keeps the stub's `Command(OrderId, DisputeReason)` shape so the `SyncShipmentStatusesFunction` call sites (lines 133/150) and its unit tests stay untouched, and rewires the handler body to the real dispute flow.

`Order.OpenDispute(IClock)` already exists on master with allow-list Shipped/Delivered/Completed (T-0060). This ticket **changes that allow-list** (see §C.1: Paid/Accepted in, Completed out), adds `PreDisputeState` preservation, and adds the `Order.ResolveDispute(IClock, OrderState restoreTo)` counterpart — so `Disputed` becomes a true parenthesis state: open stores where the order was, resolve restores it, then the outcome edge (refund / cancel / nothing) applies from the restored state. Existing `OrderTests` pins for the old allow-list are rewritten **red-first** per the T-0067+ hard rule.

**Auto-deliver exclusion needs no predicate change.** `OrderRepository.GetAutoDeliverableUnscopedReadOnlyAsync` selects `State == OrderState.Shipped && AutoDeliverAt != null && AutoDeliverAt < asOf`; opening a dispute flips `State` to `Disputed`, so the disputed order drops out of the sweep *by definition*. The same holds for `GetCarrierSyncableUnscopedReadOnlyAsync` (`State == Shipped` predicate) — a disputed shipment stops re-firing in the carrier sweep. AC-11 pins both with tests rather than touching the predicates.

**Evidence channel = the T-0079 message thread.** Per the T-0079 state-guard ruling, `PendingPayment` is the ONLY state that blocks posting (verified at `PostCustomerOrderMessage.cs:104-107` / `PostMakerOrderMessage.cs:97-100`) — the thread stays open on `Disputed`, so both parties keep submitting evidence there. No new evidence-upload surface ships (US-admin-0011 names `OrderMessage` as the preserved evidence role). AC-12 pins this with a regression test.

Endpoints ship NOW for customer + maker + admin hosts (UI later, T-0118); the carrier path arrives via the rewired `DisputeShipment`. NSwag regen: customer + maker + admin clients in the same PR.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 5 dimensions + 1 open-question ruling at the 2026-06-12 batched deliberation; the remainder are PM-absorbed from T-0067/T-0076/T-0080/T-0083 precedents.

### A. User-locked 2026-06-12 (non-negotiable)

1. **Q1 — Refund shape (T-0105, referenced here).** Full + partial refunds; admin enters amount ≤ remaining total; partial accumulates `refunded_amount_minor` on Order with NO state change; full (cumulative == total) → `State = Refunded`. No credit-note invoice at MVP (v1.1). **T-0106 consequence:** `ResolveDispute` outcome `Refunded` dispatches `RefundOrder.Command` for the **full remaining amount** (`TotalAmountMinor − RefundedAmountMinor`) — the dispute lane never issues partials; partial refunds are a direct T-0105 admin action.
2. **Q2 — Dispute = state + child entity.** `State = Disputed` on Order **plus** a dedicated `Dispute : Auditable` child entity (Id, OrderId FK, Category enum, Description, Source enum [Customer, Maker, Carrier, Admin], ResolutionNotes nullable, ResolvedAt nullable, ResolutionOutcome enum nullable) **plus** `Order.PreDisputeState` column restored on resolve. **Rejected:** state-only (loses category/description/source/notes — admin can't triage without the why); separate dispute aggregate with no Order state change (auto-deliver + carrier sweeps would keep claiming the order; escrow semantics demand the state flip).
3. **Q3 — Open endpoints for all four sources now.** Customer + maker host endpoints ship NOW (UI later), carrier-sourced via T-0078 stub rewiring, admin variant on the admin host. NSwag regen: customer + maker + admin. **Rejected:** admin-only at MVP (parties would phone/email disputes in, admin re-types them — loses the structured category + the party's own words as evidence).
4. **Q4 — T-0107 strict allow-list (referenced here).** Manual state change never → `Paid` without providerRef, never → `Refunded` (T-0105's job), never out of `Refunded`; mandatory non-empty reason; blocked transition returns a code naming the sanctioned command. **T-0106 consequence:** dispute open/resolve are the sanctioned commands for `Disputed`-related transitions; T-0107 will refuse manual `→ Disputed` / `Disputed →` hops and point here.
5. **Q5 — Refund on paid-out orders.** Warning + explicit admin acknowledgement flag + audit (T-0105); maker-share recovery is manual at MVP; forward note pinned for T-0102 grooming (negative-balance ledger). **T-0106 consequence:** `Completed` is NOT disputable (§C.1), so `ResolveDispute(Refunded)` cannot hit the paid-out warning path in the common case — but see Risk §2 for the Delivered-order-in-Processing-batch edge.
6. **Q-0016 RULED — option (a):** maker invoice email accepted as sanctioned commercial-document content. Docs reconciliation only; **architect agent owns it** in this grooming phase. Nothing for the T-0106 implementer.

### B. ADR-locked (no relitigation)

- **ADR 0013 (per-audience hosts + scoped repositories).** Customer endpoint loads via `GetByIdForCustomerAsync(orderId, customerUserId)`; maker via `GetByIdForMakerAsync(orderId, makerId)` — the predicate IS the IDOR shield; cross-tenant probes get `404 order.notFound`, never a 403 that leaks existence. Admin open + resolve load via `GetByIdUnscopedAsync`. JWT audience enforced per host — a customer JWT cannot replay against the maker or admin dispute endpoints.
- **ADR 0014 (UoW pipeline + admin audit).** All five features are commands → `UnitOfWorkPipelineBehavior` commits per request; handlers NEVER call `SaveChangesAsync()`. Order mutation + Dispute row + outbox row commit atomically. Admin `OpenDispute` + `ResolveDispute` implement `IAdminAuditableCommand` → `AdminAuditPipelineBehavior` captures before/after automatically. Party + carrier variants are NOT admin-audited (not admin commands).
- **ADR 0017/0020 (outbox).** Both new notifications go through the outbox table; no new Azure Function — the existing dispatcher + `EmailSendService` routing handle them. The retired `order.disputed.carrierSourced` event type and its payload record are deleted (it was never routed; the stub's integration test is rewritten).
- **ADR 0019 (email).** `EmailSendService` gains routing branches for the two new event types; templates render Czech copy; recipient resolution and links are pre-baked in the payload per the enqueue-time enrichment pattern (§C.8).
- **One-file feature shape, `BusinessResult<T>`, centralized `BusinessErrorMessage` codes, globally-unique Response names** (post-PR #38 NSwag convention) — all per the standing catalog.

### C. PM-absorbed (no user input needed)

1. **Disputable-states allow-list = `Paid | Accepted | Shipped | Delivered`.** Reasoning: `PendingPayment` — no money has moved; abandonment is T-0083 auto-cancel's lane, nothing to dispute. `Cancelled`/`Refunded` — terminal, nothing held in escrow. `Completed` — payout already settled to the maker; allowing party-initiated disputes on arbitrarily old Completed orders creates an unbounded liability lane with nothing left to freeze (escrow released); post-completion complaints route through the T-0079 message thread + direct admin action (T-0105 refund with the Q5 acknowledgement, or T-0107). `Disputed` — re-dispute is Silent-Success (§C.4). **This CHANGES the existing `Order.OpenDispute` allow-list** (was Shipped/Delivered/Completed): Paid + Accepted in (the "maker silent after payment" / "accepted but never ships" escalation lanes; `Delivered` is the post-delivery complaint lane), Completed out. Existing `OrderTests` pins rewritten red-first.
2. **`Disputed` is a parenthesis state.** `OpenDispute` stamps `PreDisputeState = State` before flipping; `ResolveDispute(IClock, OrderState restoreTo)` guards `State == Disputed`, restores `State = restoreTo`, clears `PreDisputeState = null`, keeps `DisputedAt` set (historical marker, same as `PaidAt`/`ShippedAt`). The Refund/Cancel outcome edges then apply from the restored state — `Order.Refund`'s and `Order.Cancel`'s allow-lists stay untouched.
3. **Resolve sequencing in the handler:** restore first (`Order.ResolveDispute`), mark the `Dispute` row resolved (outcome + notes + `ResolvedAt`), enqueue the customer email, THEN per outcome: `Refunded` → nested `mediator.Send(new RefundOrder.Command(...))` full-remaining (T-0105 pipeline runs; its UoW commit flushes the shared DbContext — resolution mutations + refund land atomically); `Resumed` → nothing further; `Cancelled` → `order.Cancel(clock, OrderCancellationSource.Admin)` (enum value exists since T-0083). `Cancelled` outcome is only reachable when `restoreTo ∈ Cancel`'s allow-list (here: Paid/Accepted); a Shipped/Delivered-restored order surfaces the entity's `order.invalidTransition` and the dispute is NOT resolved (transaction rolls back) — admin uses `Refunded` for shipped goods. AC-10 pins both branches.
4. **Idempotency = Silent-Success on party re-open** (T-0067/T-0076 precedent): opening a dispute on an already-`Disputed` order returns 200 with the EXISTING open dispute's id — no second `Dispute` row, no second outbox row. Backed by a **partial unique index** `UNIQUE (order_id) WHERE resolved_at IS NULL` (at most one open dispute per order; makes the concurrent-open race safe). Admin re-RESOLVE is NOT silent: resolve on a non-`Disputed` order returns `409 order.dispute.notOpen` — loud is better for admin ops, mirrors T-0107 strictness.
5. **`DisputeReason` (carrier wire enum) stays; `DisputeCategory` is the domain enum.** `DisputeShipment.Command(OrderId, DisputeReason)` keeps its shape (Function call sites + `SyncShipmentStatusesFunctionTests` + the `DisputeShipmentResponse(OrderId, Reason)` contract untouched); the rewired handler maps `CarrierReturned → DisputeCategory.CarrierReturned`, `CarrierFailed → DisputeCategory.CarrierFailed`. The `DisputeReason` XML doc note "T-0106 will append customer/maker reasons" is superseded — update the doc comment instead of appending values.
6. **Carrier categories are carrier-reserved on party endpoints.** Customer/maker Validators reject `CarrierReturned`/`CarrierFailed` with new code `order.dispute.categoryNotAllowed`. Admin open accepts any category (admin may transcribe a phone-reported carrier failure).
7. **Description:** required, trimmed, max 2000 chars (matches `Order.MaxCustomerNotesLength`). `ResolutionNotes`: required on resolve, max 2000, **customer-visible** — rendered in the resolve email (US-admin-0011 AC-2 "resolved with notes, both parties get notified"); the future admin UI labels the field accordingly.
8. **Enrichment-at-enqueue email pattern kept** (pending Q-0012): payloads carry OrderId, OrderNumber, Category, Description (open) / Outcome, ResolutionNotes (resolve), recipient address, and **pre-baked action URLs** (admin: `/dashboard/admin/orders/{id}` on the admin host; customer: order detail on the customer host).
9. **Admin recipient address** = new `EmailOptions.AdminNotificationAddress` bound from `ADMIN_NOTIFICATION_EMAIL` (no such setting exists today — verified). Missing config at send time → the outbox event fails `Configuration`-class per ADR 0020 (visible in admin outbox tooling, retried after fix).
10. **`AutoDeliverAt` is NOT extended on resume.** If a Shipped order's window elapsed during the dispute, the next sweep auto-delivers immediately after a `Resumed` resolution — defensible because `Resumed` means "the order proceeds as if undisputed", and in the typical resume scenario the package is already with the customer. Monitorable; revisit if ops data shows premature closes.
11. **Bundle order:** T-0105 → T-0106 → T-0107 in one branch/PR; TDD red-first for the disputable-states predicate + restore logic; ticket structure mirrors T-0080.

## Scope

### Domain layer

- **`Core.Domain/Orders/DisputeCategory.cs`** — NEW enum (`: short` backing per the `OrderCancellationSource` precedent; stable wire values, new categories append):
  ```csharp
  public enum DisputeCategory : short
  {
      NotDelivered = 0,    // party-selectable
      DamagedItem = 1,     // party-selectable
      NotAsDescribed = 2,  // party-selectable
      CarrierReturned = 3, // carrier-reserved (§C.6)
      CarrierFailed = 4,   // carrier-reserved (§C.6)
      Other = 5,           // party-selectable
  }
  ```
- **`Core.Domain/Orders/DisputeSource.cs`** — NEW enum (`: short`): `Customer = 0`, `Maker = 1`, `Carrier = 2`, `Admin = 3`. Always set server-side by the handler — never client-supplied.
- **`Core.Domain/Orders/DisputeResolutionOutcome.cs`** — NEW enum (`: short`): `Refunded = 0`, `Resumed = 1`, `Cancelled = 2`.
- **`Core.Domain/Orders/Dispute.cs`** — NEW `Dispute : Auditable` entity per Q2:
  ```csharp
  public sealed class Dispute : Auditable
  {
      public const int MaxDescriptionLength = 2000;     // §C.7
      public const int MaxResolutionNotesLength = 2000; // §C.7

      public string OrderId { get; }                    // FK, immutable
      public DisputeCategory Category { get; }
      public string Description { get; }                // trimmed at factory
      public DisputeSource Source { get; }
      public string? ResolutionNotes { get; }           // null until resolved
      public DateTimeOffset? ResolvedAt { get; }        // null == OPEN
      public DisputeResolutionOutcome? ResolutionOutcome { get; }
  }
  ```
  Factory `Dispute.Open(id, orderId, category, description, source, countryCode)` validates required fields + lengths (ArgumentException tail — user-input validation lives in the command Validators); `Resolve(IClock clock, DisputeResolutionOutcome outcome, string resolutionNotes)` refuses double-resolve with `Error.Conflict("dispute", OrderDisputeNotOpen)` and sets `ResolutionOutcome`/`ResolutionNotes`/`ResolvedAt`.
- **`Core.Domain/Orders/IDisputeRepository.cs`** — NEW: `AddAsync(Dispute, ct)` + `GetOpenByOrderIdAsync(orderId, ct)` (tracked; `ResolvedAt == null` predicate; at most one row exists per the partial unique index).
- **`Core.Domain/Orders/Order.cs`** — MODIFIED: new `OrderState? PreDisputeState` property; `OpenDispute(IClock)` allow-list changed to Paid/Accepted/Shipped/Delivered + stamps `PreDisputeState = State` before flipping (§C.1/§C.2); NEW method per §C.2:
  ```csharp
  public BusinessResult ResolveDispute(IClock clock, OrderState restoreTo)
  // guards: State == Disputed, else InvalidTransition;
  // restores State = restoreTo; PreDisputeState = null; DisputedAt KEPT.
  ```
  XML docs updated (the entity's authorisation note already delegates edge-takers to the T-0106 command layer).
- **`Core.Domain/Orders/DisputeReason.cs`** — doc-comment update only (§C.5).
- **`Core.Domain/Common/BusinessErrorMessage.cs`** — 2 new codes: `OrderDisputeCategoryNotAllowed = "order.dispute.categoryNotAllowed"`, `OrderDisputeNotOpen = "order.dispute.notOpen"`.
- **`Core.Domain/Outbox/OutboxEventTypes.cs`** — add `OrderDisputedAdminEmail = "order.disputed.adminEmail"` + `OrderDisputeResolvedCustomerEmail = "order.disputeResolved.customerEmail"` (camelCase per the file's `<domain>.<action>.<modality>` convention), both added to `IsEmailSend`; DELETE `OrderDisputedCarrierSourced` + `Core.Domain/Outbox/OrderDisputedCarrierSourcedPayload.cs` (replaced, never routed). New payload records `OrderDisputedAdminEmailPayload` + `OrderDisputeResolvedCustomerEmailPayload` per §C.8.

### AppServices layer

All five features are one-file CQRS shapes with globally-unique Response names (post-PR #38 NSwag convention). No new pagination, no read-side queries.

- **`Features/Orders/OpenCustomerDispute.cs`** — NEW.
  - `Command(string OrderId, DisputeCategory Category, string Description) : ICommand<OpenCustomerDisputeResponse>`; `OpenCustomerDisputeResponse(string OrderId, string DisputeId)`.
  - Validator: `OrderId` required + max 40; `Category` `IsInEnum` + `Must(not carrier-reserved)` with `OrderDisputeCategoryNotAllowed` (§C.6); `Description` required + max 2000.
  - Handler steps: (1) session customerId; (2) `GetByIdForCustomerAsync` — null → `404 order.notFound` (IDOR shield); (3) `State == Disputed` → load open dispute, return its id (Silent-Success, §C.4 — no mutation, no outbox); (4) `order.OpenDispute(clock)` — failure → `409 order.invalidTransition`; (5) `Dispute.Open(..., Source: Customer)` + `AddAsync`; (6) enqueue `order.disputed.adminEmail` (payload per §C.8). NO `SaveChangesAsync` — UoW pipeline commits.
- **`Features/Orders/OpenMakerDispute.cs`** — NEW maker-host mirror: `GetByIdForMakerAsync` scope, `Source: Maker`. Identical state/category/idempotency semantics; `OpenMakerDisputeResponse`.
- **`Features/Orders/OpenDispute.cs`** — NEW admin variant: `GetByIdUnscopedAsync`, `Source: Admin`, any category allowed (admin may transcribe phone-reported carrier failures), implements `IAdminAuditableCommand` → before/after audit free via the pipeline.
- **`Features/Orders/DisputeShipment.cs`** — REWIRED (T-0078 stub → real). `Command(OrderId, DisputeReason)` + `DisputeShipmentResponse(OrderId, Reason)` shapes KEPT (§C.5 — Function call sites + tests untouched). New handler body: (1) unscoped load — Function context has no user identity; (2) already-`Disputed` → Silent-Success (kills the stub's intentional repeat-emission across sweeps); (3) `order.OpenDispute(clock)`; (4) `Dispute.Open(..., Category: CarrierReturned/CarrierFailed per the §C.5 mapping, Description: canned carrier text incl. ShippingCarrierRef, Source: Carrier)`; (5) enqueue `order.disputed.adminEmail`. The ops Warning log is retained; the stub commentary + Step-5 "NO state mutation" block is deleted.
- **`Features/Orders/ResolveDispute.cs`** — NEW admin command per §C.3.
  - `Command(string OrderId, DisputeResolutionOutcome Outcome, string ResolutionNotes) : ICommand<ResolveDisputeResponse>`, implements `IAdminAuditableCommand`; `ResolveDisputeResponse(string OrderId, OrderState State, DisputeResolutionOutcome Outcome)`.
  - Validator: `OrderId` required + max 40; `Outcome` `IsInEnum`; `ResolutionNotes` required + max 2000 (§C.7 — customer-visible).
  - Handler steps: (1) unscoped tracked load; (2) `State != Disputed` → `409 order.dispute.notOpen`; (3) `GetOpenByOrderIdAsync` — defensive null → same code; (4) `order.ResolveDispute(clock, order.PreDisputeState!.Value)`; (5) `dispute.Resolve(clock, outcome, notes)`; (6) enqueue `order.disputeResolved.customerEmail`; (7) outcome branch — `Refunded`: nested `mediator.Send(RefundOrder.Command)` full-remaining, propagate failure (dispute stays open, Risk §4); `Cancelled`: `order.Cancel(clock, OrderCancellationSource.Admin)`, propagate failure; `Resumed`: done.

### Infrastructure / hosts / Functions

- **`Infra.Database`** — `DisputeRepository` + EF configuration (FK to orders, max-length columns, enum→SMALLINT conversions); migration `AddDisputeTableAndPreDisputeState`: `disputes` table + partial unique index `(order_id) WHERE resolved_at IS NULL` + `orders.pre_dispute_state SMALLINT NULL`.
- **`Infra.Email` / `EmailSendService`** — routing branches + 2 Czech templates (admin dispute-opened digest; customer dispute-resolved with outcome + notes + order link).
- **`Config`** — DI: `AddScoped<IDisputeRepository, DisputeRepository>()`; `EmailOptions.AdminNotificationAddress` binding.
- **`Web.Customer` / `Web.Maker`** — `POST /api/v1/{customer|maker}/orders/{orderId}/dispute` on the existing `OrdersController`s; `[Authorize]`, one-liner `Mediator.Send`, `ProducesResponseType` for NSwag.
- **`Web.Admin`** — `POST /api/v1/admin/orders/{orderId}/dispute` + `POST /api/v1/admin/orders/{orderId}/dispute/resolve` (extend or create the admin `OrdersController`).
- **Functions** — `SyncShipmentStatusesFunction` UNCHANGED (Command shape preserved, §C.5).
- **NSwag regen** — customer + maker + admin clients in the same PR; no manual `lib/api-client/` edits. Frontend `cs-CZ` i18n keys for the 2 new error codes.
- **Docs** — `docs/architecture/roles/order.md` (dispute parenthesis-state + child entity + allow-list); `docs/tickets/INDEX.md` flip post-merge; forward note for T-0102 grooming: negative-balance ledger for refunds against in-flight/settled payouts (Q5).

### Tests (~12 unit groups + ~4 integration; red-first for predicates + restore)

#### Unit (`Makables.Tests`)

1. **OpenDispute_allowed_from_disputable_states** (theory: Paid/Accepted/Shipped/Delivered) — transition succeeds; `PreDisputeState` stamps the origin state; `DisputedAt` set. **Write red first.**
2. **OpenDispute_refused_from_non_disputable_states** (theory: PendingPayment/Cancelled/Refunded/Completed/Disputed) — `order.invalidTransition`; entity unmutated. **Rewrites the existing T-0060 pins (`OrderTests.cs:686-729`); write red first.**
3. **ResolveDispute_restores_state_and_clears_PreDisputeState** — Disputed (was Shipped) → `ResolveDispute(clock, Shipped)` → `State == Shipped`, `PreDisputeState == null`, `DisputedAt` KEPT.
4. **ResolveDispute_refused_when_not_disputed** — any non-Disputed state → `order.invalidTransition`; no mutation.
5. **Dispute_Resolve_sets_outcome_notes_timestamp_and_refuses_double_resolve** — second `Resolve` → `order.dispute.notOpen` Conflict.
6. **OpenCustomerDispute_happy_path** — NSubstitute mocks; asserts Dispute row added (Source Customer, trimmed description), order Disputed, ONE `order.disputed.adminEmail` enqueued with pre-baked admin URL.
7. **OpenCustomerDispute_IDOR_foreign_order_returns_notFound** — scoped repo returns null → `404 order.notFound`; no repo writes, no outbox.
8. **OpenCustomerDispute_validator_rejects_carrier_categories_and_bad_description** — `CarrierReturned`/`CarrierFailed` → `order.dispute.categoryNotAllowed`; empty / 2001-char description → validation failure.
9. **Reopen_on_Disputed_is_silent_success** — returns existing open dispute id; `AddAsync` NOT called; no outbox emission.
10. **OpenMakerDispute_mirror** — maker scope + `Source: Maker`; same semantics.
11. **DisputeShipment_rewired** — Shipped + `CarrierReturned` → Disputed + Dispute(Carrier, CarrierReturned) + adminEmail; already-Disputed re-fire → Silent-Success, no new rows/emission (stub repeat-emission behaviour gone).
12. **ResolveDispute_handler_outcomes** — Resumed: restore only; Refunded: `RefundOrder.Command` dispatched with `TotalAmountMinor − RefundedAmountMinor`, refund failure leaves dispute OPEN; Cancelled (PreDisputeState Paid/Accepted): `Cancel(Admin)`; Cancelled (Shipped/Delivered): `order.invalidTransition`, dispute open; non-Disputed: `order.dispute.notOpen`; customer email enqueued on every success path.

#### Integration (`Makables.IntegrationTests`, Testcontainers Postgres)

1. **Customer_POST_dispute_e2e** — seeded Delivered order; 200; DB asserts: `orders.state = Disputed`, `pre_dispute_state = Delivered`, `disputes` row, outbox row.
2. **Disputed_order_not_claimed_by_auto_deliver_sweep** — Shipped order, `AutoDeliverAt` in the past, open dispute, run `GetAutoDeliverableUnscopedReadOnlyAsync` → id NOT yielded (AC-11 predicate pin; no predicate change shipped).
3. **Admin_resolve_resumed_e2e** — state restored, dispute resolved, `admin_audit_log` entry with before/after JSON, customer-email outbox row.
4. **Message_post_on_disputed_order_succeeds** — evidence-channel pin (AC-12); posts via the T-0079 customer endpoint against a Disputed order → 200.

The stub's `DisputeShipmentIntegrationTests` (`..._emits_outbox_event_without_Order_state_mutation`) is rewritten for the real behaviour — the old pin is now FALSE by design.

## Alternatives Considered

- **Option A — state-only dispute (no child entity).** *Rejected per Q2* — admin triage needs category/description/source/notes; cramming them onto Order bloats the aggregate and loses multi-dispute history.
- **Option B — dispute as a separate aggregate without the Order state flip.** *Rejected per Q2* — the `State == Shipped` predicates in the auto-deliver + carrier sweeps would keep claiming the order; the state flip IS the escrow hold and the natural sweep exclusion.
- **Option C — keep `Completed` disputable (current entity behaviour).** *Rejected per §C.1* — payout settled, nothing to freeze, unbounded post-completion liability window. Post-completion complaints go via messages + T-0105/T-0107 direct admin action.
- **Option D — merge `DisputeReason` into `DisputeCategory`.** *Rejected per §C.5* — would churn the Function call sites, the stub's tests, and the stable-int wire payload for zero behavioural gain; a two-value carrier wire enum mapping into the domain enum is cheaper.
- **Option E — auto-refund inside `ResolveDispute` without dispatching `RefundOrder.Command`.** *Rejected per §C.3* — duplicates T-0105's Comgate call + Q5 acknowledgement + partial-accumulation logic; nested dispatch reuses one tested money path (and is why the bundle orders refund first).
- **Option F — modify the auto-deliver predicate to add `&& State != Disputed`.** *Rejected* — redundant: `State == Shipped` already excludes `Disputed` by definition once the state flips. AC-11 pins the behaviour with a test instead of dead predicate code.
- **Option G — Silent-Success on admin re-resolve (mirror re-open).** *Rejected per §C.4* — a silently "succeeding" second resolve with a DIFFERENT outcome would mask an admin race; loud `409` is the safer ops posture.

## Out of scope

- **Partial refunds from the dispute lane** — `ResolveDispute(Refunded)` is full-remaining only; partials are direct T-0105 actions (Q1).
- **Credit-note invoice** — v1.1 (Q1). **Maker-share recovery / negative-balance ledger** — manual at MVP; T-0102 grooming note (Q5).
- **Dispute UI** (customer/maker forms, admin resolution screen) — T-0118.
- **Maker dispute-resolved email** — only the customer email ships (locked scope); the maker sees the state change on the dashboard. Divergence from US-admin-0011 AC-2's "both parties" is deliberate at MVP; revisit with T-0118.
- **Customer "your shipment is disputed" email on carrier-sourced open** — the old stub comment suggested it; the locked outbox scope (adminEmail on open) supersedes.
- **Mark-as-fraud outcome, mediation workflows, dispute SLAs/reminders, evidence file uploads** (messages thread is the channel), **re-dispute history UI**, **T-0107 manual-change allow-list** (next ticket).

## Acceptance criteria

- **AC-1** Given an order in `Delivered` owned by customer C, when C `POST`s `/api/v1/customer/orders/{id}/dispute` with `{category: DamagedItem, description: "..."}`, then 200 with `{orderId, disputeId}`; order has `State = Disputed`, `PreDisputeState = Delivered`, `DisputedAt` set; a `disputes` row exists with `Source = Customer`, the category, the trimmed description, `ResolvedAt = null`; one `order.disputed.adminEmail` outbox row exists with pre-baked admin URL.
- **AC-2** Given an order in `Accepted`, when the maker `POST`s `/api/v1/maker/orders/{id}/dispute`, then the mirror of AC-1 with `Source = Maker`, `PreDisputeState = Accepted`.
- **AC-3** Given customer A probes customer B's order id, then `404 order.notFound` — the scoped-repository predicate is the IDOR shield; no existence leak.
- **AC-4** Given an order in `PendingPayment`, `Cancelled`, `Refunded`, or `Completed`, when any open-dispute endpoint fires, then `409 order.invalidTransition`; no `Dispute` row, no outbox row, order unchanged.
- **AC-5** Given an order already in `Disputed`, when the same or the other party re-opens, then 200 returning the EXISTING open dispute's id; no second `disputes` row (partial unique index `(order_id) WHERE resolved_at IS NULL` enforces it), no second outbox row.
- **AC-6** Given a Shipped Zásilkovna order whose Packeta status reads `Returned`, when `SyncShipmentStatusesFunction` dispatches `DisputeShipment.Command(orderId, CarrierReturned)`, then the order transitions to `Disputed` with `PreDisputeState = Shipped`, a `Dispute(Source: Carrier, Category: CarrierReturned)` row exists, and one `order.disputed.adminEmail` row is enqueued. `order.disputed.carrierSourced` no longer exists anywhere in the codebase. A re-fire on the now-Disputed order is Silent-Success with no new rows.
- **AC-7** Given a customer or maker submits `category: CarrierReturned` or `CarrierFailed`, then `400 order.dispute.categoryNotAllowed`. The admin open endpoint accepts all six categories.
- **AC-8** Given a Disputed order (`PreDisputeState = Shipped`), when admin `POST`s `.../dispute/resolve` with `{outcome: Resumed, resolutionNotes: "..."}`, then 200; `State = Shipped`, `PreDisputeState = null`, `DisputedAt` still set; the dispute row has `ResolutionOutcome = Resumed`, the notes, `ResolvedAt` set; one `order.disputeResolved.customerEmail` outbox row exists; an admin audit entry captures before/after (AdminAuditPipelineBehavior).
- **AC-9** Given a Disputed order with `RefundedAmountMinor = 0`, when admin resolves with `outcome: Refunded`, then `RefundOrder.Command` is dispatched with the full remaining amount and the order ends in `State = Refunded` end-to-end. Given a non-Disputed order, when resolve is called, then `409 order.dispute.notOpen`.
- **AC-10** Given `PreDisputeState ∈ {Paid, Accepted}` and `outcome: Cancelled`, then the order ends `Cancelled` with `CancellationSource = Admin`. Given `PreDisputeState ∈ {Shipped, Delivered}` and `outcome: Cancelled`, then `409 order.invalidTransition` and the dispute remains OPEN (transaction rolled back) — the error steers admin to the `Refunded` outcome.
- **AC-11** Given a Shipped order with `AutoDeliverAt` in the past, when a dispute opens and the auto-deliver sweep runs, then `GetAutoDeliverableUnscopedReadOnlyAsync` does NOT yield the order (its `State == Shipped` predicate naturally excludes `Disputed` — pinned by integration test, no predicate change). The carrier sweep (`GetCarrierSyncableUnscopedReadOnlyAsync`) likewise stops yielding it.
- **AC-12** Given a Disputed order, when either party posts to the T-0079 message thread, then the post succeeds (PendingPayment remains the only blocked state) — the thread is the dispute evidence channel.
- **AC-13** Build clean. Unit: baseline + ~12 new/rewritten (old `OpenDispute` pins rewritten red-first). Integration: baseline + ~4 new (stub integration test rewritten). `node scripts/check-consistency.mjs` exit 0. NSwag regen for customer + maker + admin clients committed in the same PR; `cs-CZ` i18n keys for both new error codes; no manual `lib/api-client/` edits.

## Risk

1. **Allow-list change on a shipped entity method.** `Order.OpenDispute` pins exist on master (`OrderTests.cs:686-729`, `OrderAddAttachmentTests`); rewrite red-first and check every caller — only the T-0078 stub calls it today (it doesn't yet, it only logs — verify no other call sites).
2. **Delivered order inside a `Processing` payout batch** (US-admin-0007): disputable per §C.1; a `Refunded` resolution refunds the customer while the maker payout is in flight → manual maker-share recovery per Q5. Mitigation: admin email surfaces the order; forward note pinned for T-0102 negative-balance ledger.
3. **Nested `mediator.Send(RefundOrder.Command)`** runs the full pipeline including a UoW commit mid-request — implementer must verify the shared-DbContext commit ordering (resolution mutations flushed with/before the refund) and that the outer commit is a safe no-op; integration test AC-9 is the guard.
4. **Comgate refund failure inside resolve** — `RefundOrder` returns a failure (e.g. refund window expired): `ResolveDispute` must surface it and leave the dispute OPEN (roll back the restore) so the admin retries or picks another outcome; do not half-resolve.

## Test plan reference

Inline above (Scope > Tests). No separate `docs/test-plans/T-0106.md`. Red-first commits: domain predicate + restore tests precede the Order.cs change; handler tests precede feature bodies (T-0067+ hard rule).

## Files touched (expected)

### New
- `backend/src/Makables.Core.Domain/Orders/Dispute.cs`
- `backend/src/Makables.Core.Domain/Orders/DisputeCategory.cs`
- `backend/src/Makables.Core.Domain/Orders/DisputeSource.cs`
- `backend/src/Makables.Core.Domain/Orders/DisputeResolutionOutcome.cs`
- `backend/src/Makables.Core.Domain/Orders/IDisputeRepository.cs`
- `backend/src/Makables.Core.Domain/Outbox/OrderDisputedAdminEmailPayload.cs`
- `backend/src/Makables.Core.Domain/Outbox/OrderDisputeResolvedCustomerEmailPayload.cs`
- `backend/src/Makables.Core.AppServices/Features/Orders/OpenCustomerDispute.cs`
- `backend/src/Makables.Core.AppServices/Features/Orders/OpenMakerDispute.cs`
- `backend/src/Makables.Core.AppServices/Features/Orders/OpenDispute.cs`
- `backend/src/Makables.Core.AppServices/Features/Orders/ResolveDispute.cs`
- `backend/src/Makables.Infra.Database/Orders/DisputeRepository.cs` + EF entity configuration + migration `AddDisputeTableAndPreDisputeState`
- 2 email templates (admin dispute-opened; customer dispute-resolved) in the existing template location
- Unit + integration test files per the test plan

### Modified
- `backend/src/Makables.Core.Domain/Orders/Order.cs` — allow-list change + `PreDisputeState` + `ResolveDispute`
- `backend/src/Makables.Core.Domain/Orders/DisputeReason.cs` — doc-comment update only (§C.5)
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` — 2 new codes
- `backend/src/Makables.Core.Domain/Outbox/OutboxEventTypes.cs` — +2 events in `IsEmailSend`; `OrderDisputedCarrierSourced` removed
- `backend/src/Makables.Core.AppServices/Features/Orders/DisputeShipment.cs` — stub → real
- `EmailSendService` + routing — 2 new branches
- `backend/src/Makables.Config/...` — `IDisputeRepository` DI registration + `EmailOptions.AdminNotificationAddress`
- `Web.Customer` / `Web.Maker` / `Web.Admin` OrdersControllers — dispute actions
- `frontend/src/lib/api-client/*` — NSwag regen, 3 hosts, same PR
- `frontend/src/lib/i18n/cs-CZ` — keys for `order.dispute.categoryNotAllowed` + `order.dispute.notOpen`
- `docs/architecture/roles/order.md`, `docs/tickets/INDEX.md`
- `backend/src/Makables.Tests/Domain/Orders/OrderTests.cs` — OpenDispute pins rewritten (red-first)
- `backend/src/Makables.IntegrationTests/Orders/DisputeShipmentIntegrationTests.cs` — rewritten

### Deleted
- `backend/src/Makables.Core.Domain/Outbox/OrderDisputedCarrierSourcedPayload.cs` — replaced by the routed admin-email event

## Commits hint

1. `test(T-0106): red — disputable-states predicate + PreDisputeState restore pins`
2. `feat(T-0106): Dispute entity + enums + Order parenthesis-state + migration + DI`
3. `feat(T-0106): open endpoints ×3 hosts + DisputeShipment rewire + ResolveDispute + outbox/email routing`
4. `test(T-0106): handler + integration coverage; NSwag regen 3 hosts; i18n keys`

## Status log

- 2026-06-12 `draft` by PM. Second ticket in the order-cleanup admin bundle (T-0105 → T-0106 → T-0107, one PR). Replaces the T-0078 `DisputeShipment` stub; changes the `Order.OpenDispute` allow-list per §C.1; introduces the `Dispute` child entity per user-locked Q2.
- 2026-06-12 `draft → ready` by BA/PM. User locked Q1–Q5 + Q-0016 ruling at the batched 2026-06-12 deliberation (§A). 11 PM-absorbed decisions captured in §C (disputable-states reasoning, parenthesis-state semantics, resolve sequencing, Silent-Success boundaries, enum split, category reservation, lengths/visibility, enrichment-at-enqueue, admin recipient config, AutoDeliverAt-on-resume, bundle order). One manual step (ADMIN_NOTIFICATION_EMAIL). **Ready for dotnet-backend** after T-0105 lands in the bundle branch.

## Definition of Ready

- [x] User story linked (US-admin-0011) and AC traceable (AC-1/2 → US AC-1; AC-8/9/10 → US AC-2; AC-11 → US AC-3; AC-12 → US "OrderMessage preserved as evidence").
- [x] All blocking design questions ruled (Q1–Q5 batched 2026-06-12; Q-0016 ruled option (a), architect owns docs reconciliation).
- [x] Dependencies identified: T-0105 `RefundOrder` must exist first (nested dispatch); T-0078 stub + Function shapes verified on master; T-0079 message state-guard verified (`PendingPayment`-only block).
- [x] Security posture stated: party endpoints IDOR-shielded via scoped predicates; per-host JWT audiences; admin commands audited via `IAdminAuditableCommand`; state machine guarded at the entity.
- [x] Out-of-scope list prevents creep (UI, maker resolve email, partials from dispute lane, fraud outcome).
- [x] Test plan inline; red-first targets named; integration pins for the two predicate-exclusion claims.
