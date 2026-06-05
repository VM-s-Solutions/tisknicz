---
role: CreateOrder
kind: application-service
status: accepted
---

# CreateOrder

## Responsibility

Cross the gap between "customer has chosen a product + shipping method on the frontend" and "an `Order` row in `PendingPayment` exists in the DB". Owns the eight-step happy-path flow that authenticates the caller, validates the request, gates against deactivated products and maker state, asks the pricing service for a snapshot, reserves an order number, and persists the aggregate. Per T-0063 / US-customer-0010 + US-customer-0011.

## Collaborators

- **UserSessionProvider** (asks: the authenticated customer id; IDOR-safe — never trusts request body for the caller identity)
- **Product** (asks: existence + `IsActive` + `CountryCode` + `MakerId`; via `IProductRepository.GetByIdAsync`)
- **Maker** (asks: existence + `IsActive` + `IsVerified` + `PersonalPickupEnabled`; via `IMakerRepository.GetByIdAsync`)
- **OrderPricing** (asks: full priced breakdown for the product + shipping method; via `IPricingService.ComputeForProductAsync`)
- **OrderNumbering** (asks: next number in the country-local sequence; via `IOrderNumberGenerator.NextAsync(countryCode, ct)` — T-0062 TZ-aware contract, no `int year`)
- **IdGenerator** (asks: a new entity id; never `Guid.NewGuid()` directly)
- **OrderRepository** (asks: persist the new aggregate; commit handled by `UnitOfWorkPipelineBehavior`)
- **Logger** (asks: one structured `LogInformation` per successful create — no PII)

## Knows

- The shape of the inbound `Command` (8 fields: product id, quantity=1, shipping method, optional Zásilkovna pickup point id, customer contact triplet, optional notes)
- The four typed maker-state failures (`MakerDeactivated`, `MakerNotVerified`, `MakerPersonalPickupDisabled`, plus `ProductNotActive` on the product side) per user decision Q4
- The shape of the outbound `Response` (orderId + orderNumber + totalPriceMinor + currency) — the four fields the frontend needs to navigate to `/objednavka/<orderId>` and trigger T-0065's `CreatePaymentSession`
- The Czech phone-number regex used by the Validator (`internal static partial class CzechPhoneRegex.Pattern()`)
- The 8-step ordering (auth → product → maker → pricing → number → aggregate → persist → return)

## Does NOT know

- How payment sessions are created — Comgate is a follow-up call per user decision Q1
- How attachments are uploaded — T-0064 ships `POST /api/v1/orders/{id}/attachments` separately per user decision Q3
- Whether the customer has confirmed their email — that's `RequireEmailConfirmedMiddleware`, host-wide per T-0063 §Technical notes
- How idempotency is enforced — frontend handles double-submit with disabled-button + in-flight guard per user decision Q2
- How the order is invoiced, shipped, or paid out — separate roles (`Invoice`, `ShippingCarrier`, `PayoutBatch`)
- The country-specific pricing math — that lives behind `IPricingService`, which reads `CountryConfiguration`
- Whether to emit an `order-placed` outbox event — T-0067 (MarkPaid) owns the customer-facing "order received" email, fired after Comgate confirms

## Lifecycle

- **Created by:** `OrdersController.Create` on the Customer host — the only caller in MVP. Future cron / Functions callers (none planned) would need to set a system identity in `IUserSessionProvider`; the handler's first step backstops with `Error.Unauthorized()` if not.
- **Persisted by:** `IOrderRepository.AddAsync` followed by `UnitOfWorkPipelineBehavior` commit. The handler never calls `SaveChangesAsync` directly.
- **Destroyed by:** never. A failed-payment order persists in `PendingPayment` until the 24-hour retry window in US-customer-0010 AC-3 elapses; admin cancellation soft-deletes via the `Auditable` path.

## Steps (the 8-step flow)

1. **Resolve customer identity.** `session.GetUserId()`; null → `BusinessResult.Failure(Error.Unauthorized())`. Backstop guard — the host's `[Authorize]` should have returned 401 already.
2. **Load product (TOCTOU pre-check).** `products.GetByIdAsync`; null → `ProductNotFound`; `!IsActive` → `ProductNotActive`. Soft-deleted rows are hidden by the global query filter and surface as null.
3. **Load maker, defence-in-depth.** `makers.GetByIdAsync`; null or inactive → `MakerDeactivated`; not verified → `MakerNotVerified`; personal pickup chosen and `PersonalPickupEnabled == false` → `MakerPersonalPickupDisabled`.
4. **Compute pricing.** `pricing.ComputeForProductAsync(productId, shippingMethod, ct)`; failures (`ProductNotOrderable`, `CountryConfigurationNotFound`, `ProductNotFound`) propagate verbatim.
5. **Reserve order number.** `orderNumbers.NextAsync(product.CountryCode, ct)` — T-0062 TZ-aware year derived from `CountryConfiguration.TimeZoneId`; opens `FOR UPDATE` under the UoW transaction.
6. **Build the aggregate.** `Order.Create(id: ids.Next(), …)`. Trim every customer-supplied string locally; the entity re-trims defensively.
7. **Persist.** `orders.AddAsync(order, ct)`. No `SaveChangesAsync` — the pipeline commits.
8. **Return.** `Response(orderId, orderNumber, totalPriceMinor, currency)` wrapped in `BusinessResult.Success`.

## Invariants

- `customerUserId` comes only from `IUserSessionProvider`; never from the request body or path (IDOR shield).
- Maker-state gates run in the handler even though the frontend gates them — every customer-facing money-bearing flow is defence-in-depth per user decision Q4.
- Order number is reserved AFTER pricing succeeds and BEFORE the aggregate is built; a pricing failure does not consume a number.
- The persisted pricing snapshot is exactly what `IPricingService` returned — no per-handler tweaks, ever (the snapshot is the legal record).
- The handler is happy-path only after the gates; every expected failure returns `BusinessResult.Failure`, never an exception.

## Implementation pointer

- Feature file: `backend/src/Makables.Core.AppServices/Features/Orders/CreateOrder.cs`
- Controller: `backend/src/Makables.Web.Customer/Controllers/OrdersController.cs`
- Host middleware: `backend/src/Makables.Web.Customer/Middleware/RequireEmailConfirmedMiddleware.cs`
- Unit tests: `backend/src/Makables.Tests/AppServices/Features/Orders/CreateOrder{Validator,Handler}Tests.cs`
- Integration tests: `backend/src/Makables.IntegrationTests/Orders/CreateOrderTests.cs`

## Related

- ADRs: 0002 (BusinessResult), 0003 (Money + Currency), 0005 (Per-audience hosts), 0009 (Numbering), 0012 (Auth), 0013 (Data scoping), 0014 (Audit), 0017 (Packeta)
- Stories: US-customer-0010, US-customer-0011
- Roles: `order`, `order-pricing`, `order-numbering`, `product`, `maker`, `user-session-provider`, `id-generator`
- Tickets: T-0060 (Order entity), T-0061 (pricing service), T-0062 (order numbering), T-0064 (attachments — follow-up), T-0065 (Comgate session — follow-up)
