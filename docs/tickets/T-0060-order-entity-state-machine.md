---
id: T-0060
title: Order entity + state machine + IOrderRepository (scoped ForCustomer / ForMaker / Unscoped)
status: ready
size: L
owner: dotnet-backend
created: 2026-06-03
updated: 2026-06-03
depends_on: [T-0033, T-0041]
blocks: [T-0061, T-0062, T-0063, T-0064, T-0067, T-0068, T-0071, T-0072, T-0073, T-0076, T-0079, T-0080, T-0081, T-0082, T-0083, T-0100, T-0105, T-0106, T-0107, T-0110, T-0111]
user_stories: []
adrs: [0002, 0003, 0009, 0011, 0013, 0014, 0016, 0017, 0020]
phase: 4
---

# T-0060 — Order entity + state machine + IOrderRepository

## Context

T-0060 is the foundation of Phase 4 (Orders). It introduces the `Order` aggregate — the system-of-record for every customer purchase intent from `PendingPayment` through `Completed` / `Cancelled` / `Refunded` / `Disputed` — along with the EF mapping, the scoped repository per ADR 0013, and the value-shape primitives (`OrderState`, `ShippingMethod`, pricing snapshot fields) that every downstream Phase-4 ticket builds on. No commands, no controllers, no money math, no provider integration ships in this ticket; only the domain entity, the state-machine methods, the repository surface, the EF configuration, and a migration. Downstream tickets (T-0061 pricing service, T-0062 numbering wire-up, T-0063 `CreateOrder`, T-0065–T-0073 payments + shipping commands, T-0080–T-0082 read queries) all depend on the shape this ticket locks in.

Per the role doc (`docs/architecture/roles/order.md`): an Order knows its number (immutable), the customer contact snapshot at order time, the pricing snapshot at order time, the shipping choice, and the state + per-transition timestamps. An Order does NOT know how invoices render, how payouts settle, how emails dispatch, or how disputes adjudicate — those are sibling roles. This ticket honours that boundary.

## Scope

### Domain

- **`Order` entity** at `backend/src/Makables.Core.Domain/Orders/Order.cs`. Sealed; `Auditable` base (gives `CountryCode`, `IsActive` for soft-delete, audit columns). All properties have `private set;` — mutation only through state-machine methods or the static factory. Carries:
  - **Identity:** `Id` (ULID, ≤40), `OrderNumber` (string, immutable; set at creation — the generator wire-up is T-0062).
  - **Parties:** `CustomerUserId` (string), `MakerId` (string). Both immutable post-creation.
  - **Optional product link:** `ProductId` (string?, nullable for custom orders per role doc).
  - **Contact snapshot at order time** (inline columns, see Technical notes): `ContactName`, `ContactEmail`, `ContactPhone`.
  - **Pricing snapshot at order time** (inline columns): `ProductPriceAmountMinor`, `ShippingPriceAmountMinor`, `PlatformFeeAmountMinor`, `MakerPayoutAmountMinor`, `TotalAmountMinor` (all `long`), `Currency` (CHAR(3)), `VatRateBp` (`int`, basis points). All `long` per ADR 0003 minor-units rule. All immutable.
  - **Shipping choice:** `ShippingMethod` enum (`ZasilkovnaPickupPoint = 0 | PersonalPickup = 1`), `ZasilkovnaPickupPointId` (string?, nullable when method is `PersonalPickup`).
  - **State + per-transition timestamps:** `State` (`OrderState` enum), `PaidAt`, `AcceptedAt`, `ShippedAt`, `DeliveredAt`, `CompletedAt`, `CancelledAt`, `RefundedAt`, `DisputedAt` — all `DateTimeOffset?`.
  - **Provider refs:** `PaymentProviderRef` (string?, set-once), `ShippingCarrierRef` (string?, set-once on `Ship`).
  - **`AutoDeliverAt`** (`DateTimeOffset?`, set atomically with `ShippedAt` to `ShippedAt + 7 days`).
  - **Customer notes:** `CustomerNotes` (string?).
- **`OrderState` enum** at `backend/src/Makables.Core.Domain/Orders/OrderState.cs` with explicit values for storage stability: `PendingPayment = 0`, `Paid = 1`, `Accepted = 2`, `Shipped = 3`, `Delivered = 4`, `Completed = 5`, `Cancelled = 6`, `Refunded = 7`, `Disputed = 8`. Wire shape is the string name (the global `JsonStringEnumConverter` from T-0049b applies); the explicit int values matter only for the DB column.
- **`ShippingMethod` enum** at `backend/src/Makables.Core.Domain/Orders/ShippingMethod.cs`: `ZasilkovnaPickupPoint = 0`, `PersonalPickup = 1`.
- **State-machine methods** on `Order` — each returns `BusinessResult` (non-generic for transitions that produce no new value); illegal transitions return `BusinessResult.Failure(Error.Conflict("state", BusinessErrorMessage.OrderInvalidTransition))`:
  - `MarkAsPaid(IClock clock, string paymentProviderRef)` — `PendingPayment → Paid`. Sets `PaidAt` and `PaymentProviderRef`. Layered guards: the **state guard is primary** — a duplicate call on an order that is no longer `PendingPayment` is rejected with `Error.Conflict("state", OrderInvalidTransition)` from the state guard. A **secondary belt-and-braces set-once guard** on `PaymentProviderRef` returns `Error.Conflict("paymentProviderRef", OrderInvalidTransition)` and exists to prevent a silent overwrite if a future state-graph change ever lets a `Paid` order revisit `PendingPayment`. In the current graph this secondary check is unreachable and intentionally so.
  - `Accept(IClock clock)` — `Paid → Accepted`. Sets `AcceptedAt`.
  - `Ship(IClock clock, string? shippingCarrierRef, int autoDeliverWindowDays)` — `Accepted → Shipped`. Sets `ShippedAt`, `AutoDeliverAt = ShippedAt + window`, `ShippingCarrierRef` when supplied. The carrier-ref parameter is nullable because the personal-pickup path in T-0073 has no carrier ref. Layered guards mirror `MarkAsPaid`: the **state guard is primary** — a duplicate call on a non-`Accepted` order is rejected with `Error.Conflict("state", OrderInvalidTransition)`. A **secondary belt-and-braces set-once guard** on `ShippingCarrierRef` is **field-only** (any prior non-null carrier ref is sticky, even if a future call passes `null`) and returns `Error.Conflict("shippingCarrierRef", OrderInvalidTransition)`; it exists to prevent silent overwrite if a future state-graph change ever lets a `Shipped` order revisit `Accepted`. In the current graph this secondary check is unreachable and intentionally so. `autoDeliverWindowDays` is a **required** entity-level parameter (no default); the caller — T-0072 `ShipOrder.Handler` — hard-codes 7 days so the policy lives in one explicit call site (see Technical notes — "AutoDeliverAt window").
  - `MarkAsDelivered(IClock clock)` — `Shipped → Delivered`. Sets `DeliveredAt`.
  - `Complete(IClock clock)` — `Delivered → Completed`. Sets `CompletedAt`.
  - `Cancel(IClock clock)` — `PendingPayment | Paid | Accepted → Cancelled`. Sets `CancelledAt`. **Note:** the entity exposes the *state-graph edge*; role-based authorisation (customer can / maker can / admin can from which state) lives in the command-layer validators in T-0083 (auto-cancel) and T-0107 (admin manual change). See Technical notes — "Cancellation authorisation".
  - `Refund(IClock clock)` — `Paid | Accepted | Shipped | Delivered | Completed → Refunded`. Sets `RefundedAt`. Admin-only authorisation lives in `RefundOrder.Command` (T-0105); the entity allows the edge.
  - `OpenDispute(IClock clock)` — `Shipped | Delivered | Completed → Disputed`. Sets `DisputedAt`. Customer-or-maker authorisation lives in `OpenDispute.Command` (T-0106).
- **`Order.Create(...)` static factory.** Takes every required field (id, order number, customer user id, maker id, optional product id, contact snapshot, pricing snapshot fields, shipping method, optional pickup-point id, country code, customer notes?). Sets `State = PendingPayment` (per ADR 0016: orders are created in pending state and the payment session opens immediately). Validates internal invariants — throws `ArgumentException` for genuinely impossible inputs (negative amounts, blank id, blank order number, blank currency, currency not 3 chars, pickup-point id null when method is `ZasilkovnaPickupPoint`, pricing math inconsistent: `ProductPriceAmountMinor + ShippingPriceAmountMinor != TotalAmountMinor`, `MakerPayoutAmountMinor + PlatformFeeAmountMinor != ProductPriceAmountMinor + ShippingPriceAmountMinor` — caller's `Validator` catches user-input errors before `Create` runs). Returns the entity directly, not `BusinessResult`. Pattern mirror: `Product.Create` (T-0041).
- **Domain XML docs** on every public surface explain the invariants — same density as `Product.cs`.

### Repository interface (Core.Domain)

- **`IOrderRepository`** at `backend/src/Makables.Core.Domain/Orders/IOrderRepository.cs`. Surface per ADR 0013 §"Country and ownership scoping":
  - `IQueryable<Order> ForCustomer(string customerUserId)` — predicate filters on `o => o.CustomerUserId == customerUserId`.
  - `IQueryable<Order> ForMaker(string makerId)` — predicate filters on `o => o.MakerId == makerId`.
  - `IQueryable<Order> Unscoped()` — admin host only (enforced by Reviewer; no runtime guard).
  - `Task<Order?> GetByIdForCustomerAsync(string orderId, string customerUserId, CancellationToken ct)`.
  - `Task<Order?> GetByIdForMakerAsync(string orderId, string makerId, CancellationToken ct)`.
  - `Task<Order?> GetByIdUnscopedAsync(string orderId, CancellationToken ct)` — admin lookups (T-0107 manual state change, T-0105 refund) + GDPR reconciliation. Bypasses both **owner** scoping AND the global soft-delete query filter (calls `.IgnoreQueryFilters()` per ADR 0013); admin reconciliation paths legitimately need to see soft-deleted rows.
  - `Task<Order?> GetByPaymentProviderRefAsync(string providerRef, CancellationToken ct)` — **`Unscoped`** because the Comgate webhook handler (T-0066) has no user context. XML doc must call this out.
  - `Task AddAsync(Order order, CancellationToken ct)`.
- **No `UpdateAsync`** — EF Core change-tracking handles mutations.
- **No `DeleteAsync`** — soft-delete via `Auditable.MarkDeactivated()` only; admin GDPR hard-delete goes through `DeleteUserPermanently` (T-0110).
- IDOR-warning XML doc on every `GetByIdFor*` method matching the pattern from `IMakerRepository.GetByUserIdAsync` (callers MUST resolve the scoping id from the authenticated principal, never from a request param).

### Infrastructure

- **`OrderRepository`** at `backend/src/Makables.Infra.Database/Orders/OrderRepository.cs`. Primary-constructor DI of `MakablesDbContext`. Pattern mirror: `MakerRepository`, `ProductRepository`. Soft-delete query filter is automatic (Auditable global filter from T-0002).
- **`OrderEntityConfiguration`** at `backend/src/Makables.Infra.Database/Configurations/OrderConfiguration.cs`. Table `orders`. Snake-case columns. Auditable footer matches `ProductEntityConfiguration` / `MakerEntityConfiguration`. Conversion `OrderState` + `ShippingMethod` stored as `string` (`HasConversion<string>().HasMaxLength(...)`) to mirror `Product.PriceType` — keeps `dotnet ef database update` output readable.
  - **Indexes:**
    - **Unique** on `order_number` (named `ix_orders_order_number`). Partial `WHERE is_active` to mirror Maker/Product policy (a soft-deleted order frees its number for a future use — defensive; in practice order numbers are never reused, but the policy is consistent).
    - **Unique-partial** on `payment_provider_ref WHERE payment_provider_ref IS NOT NULL AND is_active` (webhook idempotency lookup; named `ix_orders_payment_provider_ref`).
    - **Composite** `(customer_user_id, created_at DESC)` named `ix_orders_customer_created` — backs T-0080 customer order list.
    - **Composite** `(maker_id, state, created_at DESC)` named `ix_orders_maker_state_created` — backs T-0081 maker dashboard.
    - **Single-column** on `state` named `ix_orders_state` — backs the T-0077 auto-deliver scan and the T-0083 pending-payment auto-cancel scan.
- **EF migration** at `backend/src/Makables.Infra.Database/Migrations/<timestamp>_Orders.cs`. Generated via `dotnet ef migrations add Orders --project ... --startup-project Web.Customer`. Creates the `orders` table + every index above. Apply cleanly against the SQLite test harness AND the Postgres dev DB.
- **DI wiring** in `backend/src/Makables.Config/Extensions/AddMakablesInfrastructure.cs` — register `IOrderRepository` → `OrderRepository` (Scoped, matching `IMakerRepository` / `IProductRepository`).

### Tests

- **Unit** at `backend/src/Makables.Tests/Domain/Orders/OrderTests.cs`. Exhaustive state-machine coverage:
  - Every legal transition fires (one test per edge).
  - Every illegal transition returns `Error.Conflict("state", OrderInvalidTransition)` (matrix test: for each source state, for each method not in its allow-list, assert failure).
  - Set-once invariants: `Ship` called twice → conflict; `MarkAsPaid` called twice → conflict on `paymentProviderRef`.
  - `AutoDeliverAt = ShippedAt + windowDays` math correct (parameterised over a few day values).
  - `Order.Create` rejects: negative amounts on each pricing column; blank id / order number / currency; non-3-char currency; pickup-point id null when method is `ZasilkovnaPickupPoint`; pricing math inconsistent (`product + shipping != total`).
  - `Order.Create` accepts `ProductId = null` (custom orders).
- **Integration** at `backend/src/Makables.Tests/Infra/Orders/OrderRepositoryTests.cs` (against the established `TestDbHarness` SQLite — not `Makables.IntegrationTests`, which is reserved for Testcontainers + `WebApplicationFactory` per the T-0049a precedent):
  - `ForCustomer(A)` returns customer A's orders only; doesn't return customer B's.
  - `ForMaker(A)` returns maker A's orders only.
  - Soft-deleted orders excluded from `ForCustomer` / `ForMaker` / `GetByIdForCustomerAsync` (global filter).
  - `GetByPaymentProviderRefAsync` finds the order by ref; returns `null` for an unknown ref.
  - `GetByPaymentProviderRefAsync` excludes soft-deleted rows (the global filter applies; webhook receiving a duplicate ref against a soft-deleted order returns `null`, which the webhook handler in T-0066 must treat as "unknown ref" — this is the right behaviour).
  - Unique-partial index on `order_number` enforces uniqueness across active rows.
  - Unique-partial index on `payment_provider_ref` enforces idempotency across active rows.
  - Schema matches the EF model (the harness uses `db.Database.EnsureCreated()` to scaffold the in-memory SQLite schema directly from the model — it does **not** execute migration files, so this validates model→schema shape only). Migration-script validation is tracked separately in follow-up ticket T-0123.

## Out of scope

- **`OrderPricing` domain service** — T-0061.
- **`OrderNumber` generator wire-up** — T-0062 (the generator interface already exists from T-0007).
- **`CreateOrder` command + controller** — T-0063.
- **Order attachments** (file paths beyond the column, upload/download endpoints) — T-0064.
- **Payment provider integration** (Comgate adapter, payment-session creation) — T-0065+.
- **Webhook handler** (`MarkOrderPaid.Command` dispatching) — T-0066–T-0067.
- **Invoice entity + generation** — T-0068–T-0069.
- **Shipping integration** (Packeta adapter, `ShipOrder.Command`) — T-0070–T-0073.
- **Order messages** (`OrderMessage` entity) — T-0079.
- **Order list / detail queries** — T-0080–T-0082.
- **All frontend** — T-0084+.
- **Authorisation rules** ("can the customer cancel from `Accepted`?") — those live in the command-layer validators in T-0083, T-0105, T-0106, T-0107. The entity exposes the state-graph edges; the command layer decides who may take them.
- **Order attachments file-path columns** — deferred to T-0064 (which will add an owned `OrderAttachment` collection following the `ProductImage` pattern).

## Acceptance criteria

- **AC-1** Given the codebase, when the solution builds, then `Order` exists at `backend/src/Makables.Core.Domain/Orders/Order.cs` as a sealed class inheriting `Auditable`, with private setters on every property and the full shape listed under Scope > Domain.
- **AC-2** Given an order in state `S` and a transition method `M`, when the (S, M) pair is illegal per the role-doc state graph, then the method returns `BusinessResult.Failure(Error.Conflict("state", BusinessErrorMessage.OrderInvalidTransition))` and mutates nothing.
- **AC-3** Given an order in state `Accepted` with `ShippingCarrierRef = null`, when `Ship("PKT-123", 7)` is called, then state becomes `Shipped`, `ShippingCarrierRef = "PKT-123"`, `ShippedAt = clock.UtcNow`, `AutoDeliverAt = ShippedAt + 7d`. A second `Ship(...)` call returns `OrderInvalidTransition` from the state guard (the order is in `Shipped`, not `Accepted`). The field-only set-once guard on `ShippingCarrierRef` is a belt-and-braces secondary check that mirrors `MarkAsPaid`'s pattern; in the current state graph it is unreachable and intentionally so (see Scope > State-machine methods > `Ship`).
- **AC-4** Given `Order.Create` is called with inputs where `ProductPriceAmountMinor + ShippingPriceAmountMinor != TotalAmountMinor`, then it throws `ArgumentException` referencing the inconsistent total.
- **AC-5** Given `IOrderRepository`, when the interface is declared, then it exposes `ForCustomer`, `ForMaker`, `Unscoped`, `GetByIdForCustomerAsync`, `GetByIdForMakerAsync`, `GetByIdUnscopedAsync`, `GetByPaymentProviderRefAsync`, `AddAsync` — and **no** `UpdateAsync` / `DeleteAsync`.
- **AC-6** Given customer A's order and customer B's order both exist, when `repo.ForCustomer(A).ToListAsync()` runs, then the result contains only A's order. Same scoping holds for `ForMaker`.
- **AC-7** Given a soft-deleted order (`MarkDeactivated` called), when any scoped `Get*` or `For*` method runs, then the soft-deleted order is excluded (the global `Auditable` query filter from T-0002 applies automatically).
- **AC-8** Given two orders attempt to claim the same `OrderNumber`, when the second is committed, then `SaveChangesAsync` raises a unique-constraint violation surfaced by the partial unique index `ix_orders_order_number`.
- **AC-9** Given a webhook receives the same `PaymentProviderRef` twice, when both attempt to `INSERT`, then the second is rejected by the partial unique index `ix_orders_payment_provider_ref`. (`MarkAsPaid` is what callers use to set the ref on an existing row; this AC pins the index-level safety net for any future code path that tries to *create* a second order with the same ref.)
- **AC-10** Given the migration runs on a clean DB, when `dotnet ef database update` is invoked, then it applies cleanly and creates `orders` + all five named indexes. (Note: the SQLite `TestDbHarness` validates the EF model via `EnsureCreated()`, not the migration pipeline — automated migration-script validation is tracked in follow-up ticket T-0123.)
- **AC-11** Given all changes are in place, when the test suite runs, then build is clean and total test count exceeds 855 (current baseline ~855 across `Makables.Tests` + `Makables.IntegrationTests`; this ticket adds at minimum 30 new tests — ~20 state-machine + ~10 repository).

## Technical notes

### Contact snapshot — inline columns vs owned value object

Inline columns (`ContactName`, `ContactEmail`, `ContactPhone` directly on `Order`) are simpler and queryable. An owned value object would add EF ceremony (`OwnsOne(o => o.Contact, c => ...)`) with no behavioural benefit — the snapshot is fixed forever at creation, never partially updated. **Decision:** inline columns. Same call as `Maker`'s ARES snapshot fields, which are also inline (see `MakerEntityConfiguration`).

### Pricing snapshot — inline columns vs owned value object

Same reasoning. Inline `ProductPriceAmountMinor`, `ShippingPriceAmountMinor`, `PlatformFeeAmountMinor`, `MakerPayoutAmountMinor`, `TotalAmountMinor`, `Currency`, `VatRateBp`. Direct SQL aggregation for admin reports stays simple. **Decision:** inline.

### `OrderState` storage — explicit values

Default int values would break DB compatibility if a future PR reorders the enum. Explicit values pin the wire-to-storage mapping. Wire shape ships as the string name (`JsonStringEnumConverter` from T-0049b), so explicit ints are storage-only — but they matter because the column is `varchar` (per `Product.PriceType` convention) and a developer reading the migration sees the canonical name. **Decision:** explicit values. The configuration uses `HasConversion<string>()` so the column stores the name, not the int — keeping the explicit ints belt-and-braces for any caller that bypasses the EF converter.

### `AutoDeliverAt` window — required parameter, caller hard-codes 7 days

The role doc says 7 days. ADR 0017 (Packeta) confirms 7 days for `AutoDeliverAt`. Country-driven (read from `CountryConfiguration`) is the architecturally consistent move — multi-country ready. But it's a single-launch market and per-country variation is unlikely. **Decision:** the window is a **required parameter** on `Order.Ship(...)` (no default in the entity); the caller — T-0072 `ShipOrder.Handler` — hard-codes 7 days. The parameter exists so a future `CountryConfiguration`-driven window can be plumbed without re-shaping the entity. The entity stays simple; the policy lives explicitly at the command-layer call site rather than being hidden in a method-signature default. Per the user decision logged in the status log: add `CountryConfiguration.AutoDeliverWindowDays` only if a future country materially differs.

### Cancellation authorisation

The role doc says "Most states → Cancelled (with rules)" without locking the rules. The state-graph edges in this ticket are:

- `PendingPayment → Cancelled` — auto-cancel after 24h (T-0083) + admin manual.
- `Paid → Cancelled` — maker refuses the order; customer cancellation pre-acceptance.
- `Accepted → Cancelled` — admin manual (T-0107); the customer-facing flow after `Accepted` becomes a refund request (`OpenDispute` → resolve as `Refund`), not a cancel.

The **entity** allows all three edges. The **command layer** in downstream tickets restricts who may take which edge — this is where the user's preferences are expressed without locking the entity. T-0107 (`ChangeOrderStateManually.Command`) is admin-audited per ADR 0014 so any admin-driven state change leaves an audit row. **Open question 1 below proposes locking the customer-cancellation rules at the command layer in T-0083; flagged for user review before T-0083 starts.**

### Why `GetByPaymentProviderRefAsync` is `Unscoped`

Per ADR 0013, `Unscoped()` is admin-host-only — but the Comgate webhook is on the Public host (T-0066) and has no user context. The repository method's XML doc must call this out explicitly: this is the one legitimate non-admin caller of an unscoped lookup, justified by the webhook flow having no caller principal to scope against. The webhook controller already runs through IP-allowlist + signature verification (T-0066), so the scoping invariant is held by the network boundary rather than by application-layer scoping. Reviewer must verify no other host calls this method.

### IDs are ULIDs

The `id` column matches the existing `Auditable.Id` pattern — 40 chars, ULID-generated via `IIdGenerator`. ULIDs are lexicographically time-ordered, so `Id desc` is a faithful "newest first" proxy if a downstream query needs to sort without depending on `created_at` (see T-0049a precedent — SQLite can't ORDER BY `DateTimeOffset`).

### `BusinessErrorMessage` codes — verification

The codes the entity surfaces are:

- `OrderInvalidTransition` — **already exists** at line 44 of `BusinessErrorMessage.cs`. Reused.
- `OrderAlreadyAccepted` — already exists at line 43; **not used by this ticket**. The state-machine method uses the generic `OrderInvalidTransition` for every illegal transition; the more-specific `OrderAlreadyAccepted` is reserved for T-0071 `AcceptOrder.Command` where the maker-facing message benefits from "this order is already accepted" specificity.
- No new codes needed.

### Test harness pattern

Tests live in `Makables.Tests/Domain/Orders/` (unit) and `Makables.Tests/Infra/Orders/` (EF projection / repository) against `TestDbHarness` (SQLite). The `Makables.IntegrationTests` project is reserved for Testcontainers-Postgres + `WebApplicationFactory` end-to-end tests per the T-0049a precedent. Don't put repository tests in `IntegrationTests`.

### Mutation discipline — `IClock` is injected at the call site

Every state-machine method accepts `IClock clock` as its first argument and reads `clock.UtcNow` for the timestamp. This is intentional: the entity does not depend on a static `DateTimeOffset.UtcNow` (which would break time-based unit tests), and the entity does not hold a clock reference (which would couple the aggregate to a service). The command-layer handler obtains `IClock` from DI and forwards it. Pattern mirror: `Maker.RegisterMaker` flow (T-0033).

## Files touched (expected)

- `backend/src/Makables.Core.Domain/Orders/Order.cs` (new) — sealed entity + state-machine methods + `Create` factory.
- `backend/src/Makables.Core.Domain/Orders/OrderState.cs` (new) — enum with explicit values.
- `backend/src/Makables.Core.Domain/Orders/ShippingMethod.cs` (new) — enum with explicit values.
- `backend/src/Makables.Core.Domain/Orders/IOrderRepository.cs` (new) — interface + IDOR-warning XML docs.
- `backend/src/Makables.Infra.Database/Orders/OrderRepository.cs` (new) — EF impl, primary-constructor DI.
- `backend/src/Makables.Infra.Database/Configurations/OrderConfiguration.cs` (new) — table mapping + 5 indexes.
- `backend/src/Makables.Infra.Database/Migrations/<timestamp>_Orders.cs` (new) — generated migration.
- `backend/src/Makables.Infra.Database/MakablesDbContext.cs` — add `DbSet<Order> Orders { get; set; }`.
- `backend/src/Makables.Config/Extensions/AddMakablesInfrastructure.cs` — register `IOrderRepository` → `OrderRepository`.
- `backend/src/Makables.Tests/Domain/Orders/OrderTests.cs` (new) — ~20 state-machine + factory tests.
- `backend/src/Makables.Tests/Infra/Orders/OrderRepositoryTests.cs` (new) — ~10 repository + index tests.

## Test plan reference

Inline above (see Acceptance criteria + Scope > Tests). No separate `docs/test-plans/` file — the test list is small enough to live in the ticket.

## Status log

- 2026-06-03 `draft → ready` by PM. Expanded from INDEX row. Three open questions logged for user review before downstream tickets land (cancellation authorisation rules in T-0083; per-country `AutoDeliverAt` window for T-0072; nothing blocking T-0060 itself).
- 2026-06-03 user decisions captured. (a) Cancel is allowed at the entity layer from `PendingPayment | Paid | Accepted`; role enforcement deferred to commands (customer cancels from `PendingPayment` only; maker from `Paid` only; admin from any state, audited). (b) `Order.Ship(autoDeliverWindowDays)` takes the window as a required parameter; T-0072 will hard-code `7`. Add `CountryConfiguration.AutoDeliverWindowDays` only if a future country materially differs.
- 2026-06-03 done. Build clean, 922 tests pass (838 unit + 84 integration; +65 new). Code-quality review CLEAR after 2 Mediums folded.
  - **M-1 — `UniqueConstraintTranslator` over-mapped two indexes** that no application-level pre-check guards. Per the file's own policy block, generator-monotonicity invariants (`ix_orders_order_number`; T-0062 reserves the number under `FOR UPDATE` per ADR 0009) and idempotency-protected races (`ix_orders_payment_provider_ref`; T-0066 webhook pre-checks via `GetByPaymentProviderRefAsync` and returns 200 idempotently per the role doc) should stay unmapped — translating them to a typed `Error.Conflict` masks the underlying bug (for the order number) and causes Comgate to retry on the webhook race (the wrong resolution; the role doc's "idempotent webhook" pattern is the right one). Moved both into the existing "intentionally unmapped" doc block with the rationale.
  - **M-2 — `ix_orders_state` was unfiltered.** Soft-deleted orders are never the target of T-0077 (auto-deliver) or T-0083 (pending-payment auto-cancel) scans. Added `HasFilter("is_active")` matching the `ix_makers_catalog_sort` convention. Regenerated the migration cleanly; the filter is captured in the `CreateIndex` block.
  - **M-3 — Currency ASCII doc note** — informational only; not folded. ISO 4217 codes are always ASCII by spec.
- 2026-06-03 Copilot review on PR #17 — 6 findings, all confirmed real by a 3-lens × 6-finding adversarial verification workflow (5/6 unanimous; C-2 ripple-lens dissented but code-truth + skeptic both confirmed). All folded in one follow-up commit.
  - **C-1 — Ticket Scope section documented `Ship(...autoDeliverWindowDays = 7)` with a default;** code requires the parameter. Ticket aligned with code (default removed; explanatory sentence added pointing to the Technical-notes section).
  - **C-2 — Technical-notes section header + decision text still said "default parameter on `Ship(...)`".** Renamed the section to "required parameter, caller hard-codes 7 days" and rewrote the decision paragraph so the entity-vs-caller split is unambiguous.
  - **C-3 — Ticket Scope said `GetByIdUnscopedAsync` was for "admin lookups + soft-delete reconciliation"** without noting the implementation skipped soft-deleted rows. Rewritten so the doc + code agree on what "Unscoped" means (now: bypasses BOTH owner scoping AND the soft-delete filter, with `.IgnoreQueryFilters()` per ADR 0013).
  - **C-4 — `OrderConfiguration` comment on `ix_orders_payment_provider_ref`** claimed the `UniqueConstraintTranslator` would surface a typed conflict; the translator explicitly leaves it unmapped (the M-1 fix the same review confirmed). Comment rewritten to match the policy: 23505 rethrows; the next webhook delivery hits the pre-check and returns 200 idempotently.
  - **C-5 — `OrderRepositoryTests.Unique_order_number_index_rejects_duplicates_on_active_rows` comment** claimed the translator maps the constraint name to a typed conflict (Postgres-only). It doesn't — `ix_orders_order_number` is intentionally unmapped per the M-1 fix. Comment rewritten to explain the unmapped-by-design intent (a 23505 here means the generator broke, not a user-facing conflict).
  - **C-6 — `GetByIdUnscopedAsync` did not call `.IgnoreQueryFilters()`** despite its XML doc + the ticket documenting it as the admin + GDPR-reconciliation lookup. Real code change: added `.IgnoreQueryFilters()` with an ADR-0013 comment; updated the XML doc to make the contract explicit; flipped `Soft_deleted_orders_are_excluded_by_default_scoped_queries` to drop its GetByIdUnscopedAsync assertion (renamed → `Soft_deleted_orders_are_excluded_by_owner_scoped_queries`); added two sibling tests pinning the carve-out scope — `GetByIdUnscopedAsync_returns_soft_deleted_rows_for_admin_reconciliation` (proves the new behaviour) and `GetByPaymentProviderRefAsync_still_hides_soft_deleted_rows` (audit-pin: only `GetByIdUnscopedAsync` is the carve-out, the webhook lookup still honours the global filter).
- 2026-06-03 second Copilot review on PR #17 — 2 findings on set-once invariant order-of-checks (R2-1 `MarkAsPaid`, R2-2 `Ship`). 4-lens × 2-finding adversarial verify (8 verdicts + 1 synthesis). Both findings reached 3/4-real consensus but with diverging resolutions: R2-1 split realistic-caller (FOLD-TICKET) vs. set-once-semantics (FOLD-CODE); R2-2 unanimous on FOLD-CODE within the real camp. Resolution: make `MarkAsPaid`'s pattern canonical (state guard first, set-once guard second as belt-and-braces) and bring `Ship` into line; soften the ticket wording where it promised behaviour the canonical pattern doesn't deliver.
  - **R2-1 — `MarkAsPaid` set-once ordering — FOLD-TICKET.** The ticket promised `Error.Conflict("paymentProviderRef", ...)` "regardless of current state", but in the canonical layered-guard pattern the state guard fires first for the only realistic caller (T-0066 Comgate retry). Replaced the bullet with an explicit two-guard description: primary state guard surfaces `Error.Conflict("state", ...)`, secondary belt-and-braces set-once guard would surface `Error.Conflict("paymentProviderRef", ...)` only if a future state-graph change made `Paid → PendingPayment` reachable. Code unchanged (the existing comment in `Order.cs:340-344` already documented this intent honestly). Existing test `MarkAsPaid_from_non_pending_returns_invalid_transition` stays correct under the softened spec.
  - **R2-2 — `Ship` set-once was doubly broken — FOLD-CODE.** The previous check `if (shippingCarrierRef is not null && ShippingCarrierRef is not null)` was parameter-AND-field coupled, so a second `Ship` call with a `null` carrier ref would silently overwrite the prior ref (set-once violated). It also diverged from `MarkAsPaid`'s field-only pattern. Rewrote to `if (ShippingCarrierRef is not null)` matching `MarkAsPaid`'s layering. Updated the `Ship` bullet in Scope and AC-3 to describe the canonical layered guards; added the missing `Ship_called_twice_returns_invalid_transition_from_state_guard` test that the ticket's Scope > Tests section promised but didn't ship.
- 2026-06-03 third Copilot review on PR #17 — 4 findings on doc/spec accuracy. 3-lens × 4-finding adversarial verify (12 verdicts + 1 synthesis). All four unanimously confirmed real. R3-1 + R3-2 fixed in `docs/status/sprint-7.md`. R3-3 + R3-4 corrected the false "harness applies all migrations" claim in this ticket's Scope > Tests bullet and AC-10 — `TestDbHarness.Create()` calls `db.Database.EnsureCreated()` (verified at `Makables.Tests/Infra/Database/TestDbHarness.cs:63`), which builds schema directly from the EF model and never executes migration files. The real migration-pipeline coverage gap is moved to follow-up ticket **T-0123 — Migration-pipeline validation harness (Postgres + `Database.Migrate()`)** queued in INDEX. No code or test changes in this PR (the SQLite harness validates exactly what the rewritten bullets now claim).
- 2026-06-03 fourth Copilot review on PR #17 — 1 finding on negative-money test isolation. 3-lens adversarial verify (3 verdicts + 1 synthesis); all 3 unanimous. **R4-1 FOLD.** The original `Create_rejects_negative_money_columns` Theory only varied product/shipping/total, hard-coded `platformFee = 0`, and aliased `makerPayout = total`. The third InlineData row `(100, 0, -1)` actually fired the `makerPayoutAmountMinor` guard (because `total` was reused as `makerPayout`) before reaching the `totalAmountMinor` guard — so the test never exercised the total-negative check it claimed to, and never exercised `platformFee` or `makerPayout` independently. The test also asserted `Throw<ArgumentException>()` without pinning `ParamName`, so a regression that swapped two guards would still pass. Rewrote to 5 InlineData rows, one per money field, each driving exactly one field negative while keeping the other four satisfying both pricing-consistency invariants (`product + shipping == total` AND `maker + fee == product + shipping`). Each row asserts the expected `ParamName`. Filed test now isolates regressions to a named guard.
