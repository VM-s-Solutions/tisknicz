# T-0063 — CreateOrder command + Validator + Handler + controller

**Phase:** 4 (Orders)
**Size:** L
**State:** `ready`
**Depends on:** T-0060 (`Order` entity), T-0061 (`IPricingService`), T-0062 (`IOrderNumberGenerator` TZ-aware signature), T-0033 (`Maker` entity), T-0041 (`Product` aggregate), T-0024 (`EmailConfirmedAt` field on User)
**Owner:** `dotnet-backend`
**ADRs:** 0002 (BusinessResult), 0003 (Money + Currency), 0005 (Per-audience hosts), 0009 (Numbering), 0011 (Storage — referenced for T-0064 boundary), 0013 (Data scoping), 0014 (Audit), 0017 (Packeta)
**Stories:** US-customer-0010, US-customer-0011
**Role doc:** [docs/architecture/roles/order.md](../architecture/roles/order.md), [docs/architecture/roles/create-order.md](../architecture/roles/create-order.md) (to be added if missing)

## Why now

T-0060 plumbed every monetary field + the state machine; T-0061 produces the priced breakdown; T-0062 hands out the order number. None of that surfaces to the customer until T-0063 ships the actual `POST /api/v1/orders` endpoint. Every downstream Phase-4 ticket — T-0064 (attachments), T-0065 (Comgate session), T-0066 (webhook), T-0067 (MarkPaid), T-0068 (invoice), T-0072 (ship) — depends on **a real order in `PendingPayment` state existing in the DB**. T-0063 is the single ticket that crosses that gap.

This is the first time the customer-facing API surface becomes user-callable. Customer-host audience binding, IDOR guards, defence-in-depth on maker state, and the email-confirmed gate all converge here.

## Scope

### CQRS feature (`Core.AppServices/Features/Orders/CreateOrder.cs`)

Single file with nested `Command`, `Response`, `Validator`, `Handler` per `patterns.md:49`. Mirrors `RegisterMaker.cs` (~273 lines) shape. ICommand interface from `Core.AppServices/Behaviors/`.

#### `Command`

```csharp
public sealed record Command(
    string ProductId,
    int Quantity,                       // == 1 at MVP (per T-0061 Q4)
    ShippingMethod ShippingMethod,
    string? ZasilkovnaPickupPointId,    // required iff ShippingMethod == ZasilkovnaPickupPoint
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,
    string? CustomerNotes
) : ICommand<Response>;
```

**Attachments are intentionally NOT in the Command** per user decision Q3 — T-0064 ships a separate multipart endpoint at `POST /api/v1/orders/{id}/attachments` that takes the orderId in the path. T-0063 keeps the controller JSON-only.

**No payment URL** per user decision Q1 — Comgate is a follow-up call to T-0065.

#### `Response`

```csharp
public sealed record Response(
    string OrderId,
    string OrderNumber,         // M-CZ-{YYYY}{NNNN}
    long TotalPriceMinor,
    string Currency
);
```

The frontend uses this to navigate to `/objednavka/<orderId>`, then triggers T-0065 `CreatePaymentSession` from that page. If Comgate is down, the order persists in `PendingPayment` and the customer can retry per US-customer-0010 AC-3's 24h window.

#### `Validator` (FluentValidation, `Cascade(CascadeMode.Stop)` per `patterns.md:382-411`)

Sync only — no DB-backed `ExistsAsync` checks at this layer; existence is the handler's job (the validator stays fast, and the handler's `BusinessResult.Failure(ProductNotFound)` covers the case identically).

| Field | Rule | Error code |
|---|---|---|
| `ProductId` | `NotEmpty().MaximumLength(64)` | `Required`, `MaxLength` |
| `Quantity` | `Equal(1)` — fail loud, do not silently truncate (MVP invariant per T-0061 Q4) | **`OrderInvalidQuantity`** *(new)* |
| `ShippingMethod` | `IsInEnum()` | `InvalidEnumValue` |
| `ZasilkovnaPickupPointId` | `When(c => c.ShippingMethod == ShippingMethod.ZasilkovnaPickupPoint, () => RuleFor(c => c.ZasilkovnaPickupPointId).NotEmpty().MaximumLength(64))` | `Required`, `MaxLength` |
| `CustomerName` | `NotEmpty().MinimumLength(2).MaximumLength(100)` | `Required`, `MinLength`, `MaxLength` |
| `CustomerEmail` | `NotEmpty().EmailAddress().MaximumLength(254)` | `Required`, `InvalidEmailFormat`, `MaxLength` |
| `CustomerPhone` | `NotEmpty().Matches(CzechPhoneRegex)` — see Technical notes | `Required`, `InvalidPhoneFormat` |
| `CustomerNotes` | `MaximumLength(2000).When(c => c.CustomerNotes is not null)` | `MaxLength` |

Validator does **not** check email-confirmed, product-active, or maker-state — those are the handler's job (TOCTOU surface) and the email-confirmed gate is middleware (see §Controller).

#### `Handler` (8-step flow, primary-ctor DI per `RegisterMaker.cs:111`)

Dependencies: `IUserSessionProvider`, `IProductRepository`, `IMakerRepository`, `IPricingService`, `IOrderNumberGenerator`, `IOrderRepository`, `IIdGenerator`, `ILogger<Handler>`.

```
Step 1 — Resolve customer identity
    customerUserId = userSessionProvider.GetCurrentUserId()
    if null → BusinessResult.Failure(Error.Unauthorized("auth.required"))
    // Backstop guard. The [Authorize] middleware should have already returned 401;
    // arriving here without a session means the handler was called outside the
    // controller path (a future cron job, a CLI tool) without a session.

Step 2 — Load product (TOCTOU pre-check)
    product = await productRepository.GetByIdAsync(cmd.ProductId, ct)
    if product is null → Failure(Error.NotFound("productId", ProductNotFound))
    if !product.IsActive → Failure(Error.Conflict("productId", ProductNotActive)) // (new)

Step 3 — Load maker, defence-in-depth on all maker-state gates (per user decision Q4)
    maker = await makerRepository.GetByIdAsync(product.MakerId, ct)
    if maker is null || !maker.IsActive → Failure(Error.Conflict("makerId", MakerDeactivated)) // (new)
    if !maker.IsVerified → Failure(Error.Conflict("makerId", MakerNotVerified)) // (new)
    if cmd.ShippingMethod == PersonalPickup && !maker.PersonalPickupEnabled
        → Failure(Error.Conflict("shippingMethod", MakerPersonalPickupDisabled)) // (new)

Step 4 — Compute pricing via IPricingService
    pricingResult = await pricingService.ComputeForProductAsync(
        cmd.ProductId, cmd.ShippingMethod, ct)
    if !pricingResult.IsSuccess → return Failure(pricingResult.Error)
    // surfaces ProductNotOrderable (PriceType == OnRequest), CountryConfigurationNotFound,
    // ProductNotFound (rare — beats step 2 to the deactivation)
    pricing = pricingResult.Value

Step 5 — Reserve order number (T-0062 — TZ-aware, FOR UPDATE under UoW txn)
    orderNumber = await orderNumberGenerator.NextAsync(product.CountryCode, ct)
    // year derived from CountryConfiguration.TimeZoneId — do NOT pass clock.UtcNow.Year

Step 6 — Build aggregate
    order = Order.Create(
        id: idGenerator.NewOrderId(),  // or ids.NewId() per existing IIdGenerator pattern
        orderNumber,
        customerUserId,
        makerId: maker.Id,
        productId: product.Id,
        contactName: cmd.CustomerName.Trim(),
        contactEmail: cmd.CustomerEmail.Trim(),
        contactPhone: cmd.CustomerPhone.Trim(),
        productPriceAmountMinor: pricing.ProductPrice.AmountMinor,
        shippingPriceAmountMinor: pricing.ShippingPrice.AmountMinor,
        platformFeeAmountMinor:   pricing.PlatformFee.AmountMinor,
        makerPayoutAmountMinor:   pricing.MakerPayout.AmountMinor,
        totalAmountMinor:         pricing.TotalPrice.AmountMinor,
        currency:                 pricing.TotalPrice.Currency,
        vatRateBp:                pricing.VatRateBp,
        shippingMethod:           cmd.ShippingMethod,
        zasilkovnaPickupPointId:  cmd.ZasilkovnaPickupPointId?.Trim(),
        countryCode:              product.CountryCode,
        customerNotes:            cmd.CustomerNotes?.Trim())

Step 7 — Persist (NO SaveChangesAsync; UnitOfWorkPipelineBehavior commits)
    await orderRepository.AddAsync(order, ct)
    logger.LogInformation(
        "Order {OrderId} ({OrderNumber}) created in PendingPayment for customer {CustomerId}",
        order.Id, order.OrderNumber, customerUserId)

Step 8 — Return
    return BusinessResult.Success(new Response(
        order.Id, order.OrderNumber, order.TotalAmountMinor, order.Currency))
```

**Outbox event "order-placed" is NOT in T-0063 scope** — it lands in T-0067 (MarkPaid) when the order is actually paid (the customer-facing "order received" email comes after Comgate confirms, not when the row is created). If a notification on `PendingPayment` is later desired, that's a separate ticket; current US-customer-0010 ACs don't require it.

### Controller (`Web.Customer/Controllers/OrdersController.cs`)

First controller on the customer host. Mirrors `Web.Maker/Controllers/ProductController.cs` shape exactly:

```csharp
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/orders")]
[Authorize]
public sealed class OrdersController(IUserSessionProvider session) : MakablesApiController
{
    public sealed record CreateOrderRequest(
        string ProductId, int Quantity, ShippingMethod ShippingMethod,
        string? ZasilkovnaPickupPointId, string CustomerName, string CustomerEmail,
        string CustomerPhone, string? CustomerNotes);

    // Controller-level wrapper to avoid OpenAPI schema-name collision
    // (per ProductController.cs:49-58 precedent — every Features/*/Xxx.Response
    // would emit as "Response" and NSwag would pick one).
    public sealed record CreateOrderResponse(
        string OrderId, string OrderNumber, long TotalPriceMinor, string Currency);

    [HttpPost]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrderRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new CreateOrder.Command(
            body.ProductId, body.Quantity, body.ShippingMethod,
            body.ZasilkovnaPickupPointId, body.CustomerName, body.CustomerEmail,
            body.CustomerPhone, body.CustomerNotes), ct);

        return result.IsSuccess
            ? HandleResult(BusinessResult.Success(new CreateOrderResponse(
                result.Value!.OrderId, result.Value.OrderNumber,
                result.Value.TotalPriceMinor, result.Value.Currency)))
            : HandleResult(BusinessResult.Failure<CreateOrderResponse>(result.Error!));
    }
}
```

**Email-confirmed gate** — added as a per-host middleware in `Web.Customer/Program.cs` (NOT a per-action filter; it covers every authenticated customer endpoint that will follow). Reads `User.EmailConfirmedAt` from the claims (or loads the User row via a cached `IUserSessionProvider.GetEmailConfirmedAtAsync`) and returns `403 AuthEmailNotConfirmed` if null. Sits AFTER `UseAuthentication` and `UseAuthorization`. Skips `/auth/*` paths (the SendEmailConfirmation endpoint must be reachable while unconfirmed).

The existing `BusinessErrorMessage.AuthEmailNotConfirmed` code already exists per the survey of `Core.Domain/Common/BusinessErrorMessage.cs` — no new code needed for this gate, just middleware.

### New `BusinessErrorMessage` codes (in `Core.Domain/Common/BusinessErrorMessage.cs`)

Under a new `// === Order ===` section (keep adjacent to the existing Order codes from T-0060):

| Code constant | Dotted value | Czech wording (vykání) — **PM to review on PR** |
|---|---|---|
| `OrderInvalidQuantity` | `order.invalidQuantity` | "Množství musí být 1." |
| `ProductNotActive` | `product.notActive` | "Tento výrobek již není k dispozici." |
| `MakerDeactivated` | `maker.deactivated` | "Tento výrobce momentálně nepřijímá objednávky." |
| `MakerNotVerified` | `maker.notVerified` | "Tento výrobce ještě nebyl ověřen a nemůže přijímat objednávky." |
| `MakerPersonalPickupDisabled` | `maker.personalPickupDisabled` | "Tento výrobce osobní odběr nenabízí." |

Per `CLAUDE.md` parity rule: matching i18n keys land in `frontend/src/lib/i18n/cs-CZ` (or wherever the catalogue lives) in the same PR. The Czech wording above is the draft; flag for user review in the PR description.

### NSwag client regeneration

After the controller exists, regenerate the customer-host TypeScript client and commit in the same PR per `CLAUDE.md:75`. CI parity check enforces this.

### Tests

#### Unit — `Makables.Tests/AppServices/Features/Orders/`

- **`CreateOrderValidatorTests.cs`** — one test per Validator rule (happy path + each failure). Stub `Validator<Command>` directly; no MediatR involvement. ~10 tests.
  - `Validator_passes_for_well_formed_zasilkovna_command`
  - `Validator_passes_for_well_formed_personal_pickup_command`
  - `Validator_rejects_blank_productId_with_Required`
  - `Validator_rejects_quantity_other_than_one_with_OrderInvalidQuantity` ([Theory] 0, 2, -1)
  - `Validator_rejects_blank_zasilkovnaPickupPointId_when_method_is_zasilkovna`
  - `Validator_allows_null_zasilkovnaPickupPointId_when_method_is_personal_pickup`
  - `Validator_rejects_invalid_email_with_InvalidEmailFormat`
  - `Validator_rejects_phone_not_matching_czech_pattern`
  - `Validator_accepts_czech_phone_with_or_without_+420_prefix` ([Theory])
  - `Validator_rejects_customer_notes_over_2000_chars`

- **`CreateOrderHandlerTests.cs`** — NSubstitute over every dependency. ~12 tests covering each branch of the 8-step flow.
  - `Handler_returns_Unauthorized_when_session_has_no_user`
  - `Handler_returns_ProductNotFound_when_repository_returns_null`
  - `Handler_returns_ProductNotActive_when_product_is_deactivated`
  - `Handler_returns_MakerDeactivated_when_maker_is_null_or_inactive`
  - `Handler_returns_MakerNotVerified_when_maker_is_not_verified`
  - `Handler_returns_MakerPersonalPickupDisabled_for_personal_pickup_when_maker_disallows_it`
  - `Handler_does_not_check_PersonalPickupEnabled_for_zasilkovna_shipping`
  - `Handler_surfaces_pricing_service_failure_verbatim` ([Theory] over ProductNotOrderable + CountryConfigurationNotFound)
  - `Handler_calls_order_number_generator_with_product_country_no_year_parameter`
  - `Handler_persists_order_in_PendingPayment_with_pricing_snapshot_from_breakdown`
  - `Handler_returns_response_with_orderId_orderNumber_total_currency_on_success`
  - `Handler_does_not_call_SaveChangesAsync` (UoW pipeline owns it)

#### Integration — `Makables.IntegrationTests/Orders/`

Postgres harness from T-0062 (the `[Collection("postgres")]` precedent). 4 tests:
- `CreateOrder_happy_path_zasilkovna_persists_order_in_PendingPayment_with_M_CZ_YYYY_NNNN_number` — full POST → 200 round-trip; assert DB row with expected state + price snapshot + number format.
- `CreateOrder_happy_path_personal_pickup_persists_order_with_zero_shipping_price`
- `CreateOrder_returns_403_AuthEmailNotConfirmed_when_user_email_unconfirmed` — the middleware path.
- `CreateOrder_returns_401_AuthRequired_when_no_bearer_token`

### Docs

- **`docs/architecture/roles/create-order.md`** — add a new role doc per ADR 0015 (RDD requires a role file per use case). Responsibilities (8 steps), knows (`Command` shape, `Response` shape), collaborators (Product, Maker, PricingService, OrderNumberGenerator, OrderRepository).
- **Update `docs/architecture/patterns.md` §A.7** — the existing CreateOrder template was speculative; rewrite to match the shipped implementation. Reference T-0063 in the section header.

## Acceptance criteria

- **AC-1** `Makables.Core.AppServices/Features/Orders/CreateOrder.cs` exists with nested `Command`, `Response`, `Validator`, `Handler` in a single static class per `patterns.md:49`.
- **AC-2** `Command` shape matches §Scope exactly (8 fields, no `Attachments`, no payment URL, `int Quantity` validated `== 1`).
- **AC-3** Validator enforces every rule in the §Scope.Validator table; `Cascade(CascadeMode.Stop)`; every failure uses a `BusinessErrorMessage` constant (no inline strings per `CLAUDE.md:52`).
- **AC-4** Handler executes the 8 steps in §Scope.Handler in order. No `SaveChangesAsync()` call (per `CLAUDE.md:38`). `customerUserId` read from `IUserSessionProvider`, never from request body (IDOR).
- **AC-5** Handler calls `IOrderNumberGenerator.NextAsync(product.CountryCode, ct)` — TZ-aware year contract from T-0062, no `int year` parameter.
- **AC-6** Handler calls `IPricingService.ComputeForProductAsync` (not the pure `OrderPricing.Compute`); pricing failures (`ProductNotOrderable`, `CountryConfigurationNotFound`, `ProductNotFound`) propagate verbatim.
- **AC-7** Defence-in-depth on maker state per user decision Q4: handler returns `MakerDeactivated`, `MakerNotVerified`, `MakerPersonalPickupDisabled` typed failures (not silent allow, not soft warn).
- **AC-8** Controller at `Web.Customer/Controllers/OrdersController.cs` with `[ApiController]`, `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/orders")]`, `[Authorize]`. `[ProducesResponseType]` declared for 200, 400, 401, 403, 404, 409.
- **AC-9** Email-confirmed middleware on the customer host returns `403 AuthEmailNotConfirmed` for any authenticated request whose `User.EmailConfirmedAt` is null; bypasses `/auth/*` paths.
- **AC-10** Five new `BusinessErrorMessage` codes (`OrderInvalidQuantity`, `ProductNotActive`, `MakerDeactivated`, `MakerNotVerified`, `MakerPersonalPickupDisabled`) added under an `// === Order ===` block. Czech i18n keys added to the frontend catalogue in the same PR (parity per `CLAUDE.md:68`).
- **AC-11** NSwag-generated customer-host TypeScript client at `frontend/src/lib/api-client/` is regenerated and committed; CI parity check passes.
- **AC-12** New role doc at `docs/architecture/roles/create-order.md` exists; `patterns.md` §A.7 updated to match the shipped impl.
- **AC-13** Test suite: at least 22 new tests (10 validator + 12 handler) in the unit suite; at least 4 new integration tests using the `PostgresHarness` from T-0062. Build clean; all suites green. Baseline (after T-0062 merges): 866 unit + 90 integration; target 888+ unit + 94+ integration.

## Out of scope

- T-0064 — order attachments upload endpoint (separate ticket, separate PR; attachments are uploaded AFTER order creation per user decision Q3).
- T-0065 — Comgate payment session (Customer calls T-0065's `CreatePaymentSession` from the order page after T-0063 returns). Per user decision Q1.
- Idempotency-Key middleware or server-side dedup (frontend handles via disabled-button + in-flight guard per user decision Q2). No new `idempotency_keys` table, no header requirement.
- Outbox `order-placed` event (T-0067 emits it when the order is actually paid — current US-customer-0010 ACs don't require a "received" notification on `PendingPayment`).
- Multi-line / `Quantity > 1` (T-0061 invariant; revisited when warranted).
- Per-product attachment allow-list — T-0064 owns the MIME / size / count rules.
- Address geocoding at order time (the customer's address is captured as a contact snapshot only; the Zásilkovna pickup-point ID is the destination).

## Technical notes

### Why the email-confirmed gate is middleware, not validator

The check is identical across every authenticated customer endpoint that follows (T-0064 attachments, T-0080 customer order list, T-0082 order detail, T-0099 checkout preview). Centralising it in a middleware means each new ticket gets the gate for free; replicating it in every Validator is a parity-leak waiting to happen. Mirrors how `[Authorize]` itself works as middleware, not per-handler.

The gate sits AFTER `UseAuthentication`/`UseAuthorization` so it never runs on anonymous requests (those are 401'd earlier). It skips `/auth/*` paths so the SendEmailConfirmation endpoint stays reachable for unconfirmed accounts.

### Why pricing failures aren't pre-checked by the validator

The validator stays sync and stateless. Loading the Product, computing the price, looking up the country config — all I/O — belongs in the handler. The handler surfaces `IPricingService` failures verbatim, so a `ProductNotOrderable` from the pricing service reads identically whether it came from the validator or the handler. Single source of truth for pricing-related errors.

### Why `Quantity == 1` is in the validator, not the handler

It's a free, sync, schema-level invariant. If `Quantity != 1` arrives, the entire pricing + numbering + persistence chain is wasted work. Validator fails fast with `OrderInvalidQuantity`; frontend (FE-managed per T-0061 Q4) can show the message immediately.

### Why we don't add idempotency middleware

User decision Q2. The frontend's disabled-button + in-flight guard is the standard UX pattern and zero backend cost. The minor risk (a malicious / buggy client posting twice) is mitigated by manual cancel; customer support sees it via T-0080 / T-0118. If support tickets show real abuse, we add `Idempotency-Key` middleware in a follow-up ticket. Carving out a stable infrastructure for one endpoint that may never see the problem is YAGNI.

### Czech phone regex — draft

```csharp
internal static partial class CzechPhoneRegex
{
    [GeneratedRegex(@"^(\+420\s?)?[6-9]\d{2}\s?\d{3}\s?\d{3}$")]
    public static partial Regex Pattern();
}
```

Matches `+420 9XX XXX XXX`, `9XX XXX XXX`, with or without spaces. Covers Czech mobile (`6`–`7`–`9` prefixes) + most landlines. **PM to review on PR** — if a tighter or looser regex is preferred, swap the character class. Aligns with T-0030 (Address phone validation) precedent; check that file in implementation to keep a single source of truth.

### Why Personal pickup needs explicit Maker check

The Order entity allows `ShippingMethod.PersonalPickup` unconditionally; T-0060's invariants only ensure pricing math and snapshot integrity, not eligibility. The Maker aggregate owns `PersonalPickupEnabled` (T-0034). Skipping the handler check means a customer of a Maker who has disabled personal pickup can still place an order with zero shipping cost, then the Maker is stuck. Better to fail fast with `MakerPersonalPickupDisabled`.

### IIdGenerator usage

Existing `IIdGenerator` (likely produces 26-char base32 or similar — confirm by reading `Core.Domain/Common/IIdGenerator.cs`). Use it for the new Order id. Do not call `Guid.NewGuid().ToString("N")` directly even if convenient — the codebase has a single id strategy.

### Logging

One `LogInformation` per successful order, no logging on failure (the pipeline behaviours + middleware log at the boundary). Include `OrderId`, `OrderNumber`, `CustomerId`. No PII (no email/phone in the log message).

## Test plan

Inline above (see Scope > Tests). No separate `docs/test-plans/` file.

## Status log

- 2026-06-04 `draft → ready` by PM. Expanded from INDEX row after T-0061 + T-0062 (in flight). Research workflow (5 parallel readers + synthesis judge) surfaced 7 open questions; 4 blocking decisions captured upfront via the user:
  - **Q1 — Comgate is a follow-up call.** CreateOrder returns `{orderId, orderNumber, totalPriceMinor, currency}`; frontend then calls T-0065 from the order page. Keeps CreateOrder atomic and matches US-customer-0010 AC-3's 24h retry window.
  - **Q2 — Frontend handles idempotency.** Disabled-button + in-flight guard. No backend Idempotency-Key middleware. Revisit if support tickets show abuse.
  - **Q3 — Attachments AFTER order creation.** T-0064 ships `POST /api/v1/orders/{id}/attachments` (multipart) separately. T-0063 keeps the order endpoint JSON-only.
  - **Q4 — Backend defence-in-depth on maker state.** Returns typed `MakerDeactivated` / `MakerNotVerified` / `MakerPersonalPickupDisabled` failures. Customer-facing endpoints always defence-in-depth; never trust the frontend gate alone.

  Three secondary decisions taken with sensible defaults flagged for PM review on the PR: (a) Czech phone regex `^(\+420\s?)?[6-9]\d{2}\s?\d{3}\s?\d{3}$`, (b) `CustomerNotes` max length 2000, (c) Czech i18n wording for the 5 new error codes drafted in this ticket — UX may refine the copy in the PR.

  Verified upfront: customer host has no controllers yet (`Web.Customer/` only has `Program.cs`), so T-0063 sets the customer-controller precedent. Maker host's `ProductController.cs` is the convention reference (`[ApiController]`, `[ApiVersion]`, `[Route("api/v{version:apiVersion}/<resource>")]`, `[Authorize]`, controller-level Request/Response wrappers). No `RequireEmailConfirmed` middleware exists yet — T-0063 adds it as the first customer-host middleware so every subsequent customer endpoint inherits the gate.
- 2026-06-05 done. `dotnet-backend` agent implemented per ticket; reviewer pass requested changes on one Medium (M-1) which was folded in the same commit. Build clean; **994 tests pass (899 unit + 95 integration)**; baseline (T-0062 master) 866 unit + 90 integration = 956; net +33 unit + 5 integration new. Docker daemon up; the 5 new Postgres tests executed end-to-end.
  - **Critical latent-bug fix bundled (out-of-nominal-scope but surfaced as a hard blocker).** `Makables.Config/Extensions/AddMakablesMediator.cs:18` referenced `typeof(AssemblyReference).Assembly` with the unqualified name. C# name resolution prefers types declared in **enclosing namespaces** over types imported via `using` directives — and three `AssemblyReference` markers exist (`Makables.Config`, `Makables.Core.AppServices`, `Makables.Core.Domain`). The file lives in `namespace Makables.Config.Extensions;`, so the parent-namespace `Makables.Config.AssemblyReference` won, and `RegisterServicesFromAssembly` scanned the wrong DLL and silently registered **zero MediatR handlers**. Latent since T-0001 — no prior PR added a new feature where the missing registration would have surfaced. Fixed by fully-qualifying to `typeof(Makables.Core.AppServices.AssemblyReference).Assembly` + a comment block documenting the trap. Added `CreateOrderRegistrationTest.AddMakablesMediator_registers_CreateOrder_Handler` as a regression net that exercises the wrapper (not just inline `AddMediatR`) — empirically verified to fail if the qualifier is reverted.
  - **M-1 (folded in the same commit) — `RequireEmailConfirmedMiddleware.IsAuthSubpath` bypass was broken.** The original used `PathString.StartsWithSegments("/v", ...)` which is whole-segment-anchored — `/v1` does NOT start-with-segment `/v` (it requires `/` or end after the candidate). Result: every authenticated-but-unconfirmed customer call to `/api/v1/auth/*` would have been 403'd by the middleware before reaching the `[AllowAnonymous]` auth controller, contradicting AC-9 and the middleware's own XML doc. Defect was masked today because every existing auth endpoint is `[AllowAnonymous]` and reaches the middleware as anonymous (which early-returns on `!IsAuthenticated`); the first authenticated-unconfirmed user hitting (e.g.) a future `/api/v1/auth/resend-confirmation` would have surfaced it. **Rewrote** `IsAuthBypassPath` with a segment-aware split: splits the path on `/`, checks `segments[0] == "api"`, then either `segments[1] == "auth"` (version-less) or `IsVersionSegment(segments[1]) && segments[2] == "auth"` (versioned, any future v{N}). Added `RequireEmailConfirmedMiddleware_bypasses_auth_subpath_for_authenticated_unconfirmed_user` integration test that POSTs to `/api/v1/auth/logout` with an authenticated-unconfirmed JWT and asserts the response is NOT 403 (anything else proves the bypass fired and the request reached the auth controller). Test was empirically verified to fail against the pre-fix code.
  - **Three informational Lows** noted by the reviewer, all deferred:
    - **L-1** — Czech phone regex test coverage gap (only `6`/`7` prefixes tested; `8`/`9` covered by the regex but not by the theory rows). Defer; ticket reviewer noted "tighten regex to `[679]`" is also worth a UX clarification first.
    - **L-2** — Validator emits multiple errors when `ShippingMethod` is invalid AND `ZasilkovnaPickupPointId` is blank. Probably intended; FE rendering of multiple field errors is the FE's call.
    - **L-3** — `CreateOrderRegistrationTest` (singular) deviates from `*Tests.cs` (plural) convention; trivial rename, deferred.
    - **L-4** — Czech wording on `order.invalidQuantity` is technically correct but UX-shaped wording ("V této verzi lze objednat pouze jeden kus.") would be friendlier; defer to UX/PM review on the PR.
    - **L-5** — NSwag emits `as any` in unreachable promise fallback; CLAUDE.md exempts generated code.
    - **L-6** — JWT `email_confirmed_at` claim only emitted on access tokens, not refresh tokens; refresh tokens are opaque per the existing auth design, so this is correct as-is.
  - **`PostgresHarness.ResetMutableTablesAsync`** extended to TRUNCATE `users, addresses, makers, categories, products` alongside the T-0062 base of `numbering_sequence, orders`. Additive; `countries` and `country_configuration` preserved (seed-once tables). Inline comment explains.
  - **`Makables.Tests.csproj`** gains a `ProjectReference` to `Makables.Config` so the new `AddMakablesMediator_registers_CreateOrder_Handler` regression test can invoke the wrapper. Direction is non-circular: `Config` → `Core.AppServices` → `Core.Domain`; `Tests` references all three directly.
- 2026-06-05 Copilot review on PR — 5 findings (1 High + 2 Mediums + 2 Lows). 3-lens × 5-finding adversarial verify (15 verdicts + 1 synthesis). Four FOLDED; one DECLINED.
  - **H-1 — FOLDED (real wire-shape bug).** `RequireEmailConfirmedMiddleware.WriteForbiddenAsync` used raw `JsonSerializer.Serialize(error)` with default options → PascalCase property names (`Field`, `Code`, `Type`) + numeric `ErrorType` enum. The rest of the API (`AddMakablesControllers`) emits camelCase + string-named enums via `JsonStringEnumConverter`; the NSwag-generated TypeScript client + `frontend/src/lib/runtime/api-fetch.ts:209` reads `payload.code` / `payload.type` (camelCase). With the bug, a 403 from this middleware would silently fail to be parsed — frontend would fall through to a generic 403 message instead of resolving the `auth.emailNotConfirmed` i18n key. **Fix:** added static `JsonSerializerOptions` (built from `JsonSerializerDefaults.Web` + `JsonStringEnumConverter`) mirroring controller config; rewrote `WriteForbiddenAsync` to use `context.Response.WriteAsJsonAsync(error, options, ct)` instead of raw `JsonSerializer.Serialize`. The static-field pattern matches existing precedent (zero per-request allocation). **Regression test:** tightened the existing `CreateOrder_returns_403_AuthEmailNotConfirmed_when_user_email_unconfirmed` integration test to assert the actual wire shape (`"code":"auth.emailNotConfirmed"` lowercase, `"type":"Forbidden"` string-enum, and NO PascalCase `"Code":` or `"Type":` keys). Test was empirically verified to fail against the pre-fix code and pass against the post-fix code.
  - **M-1 — DECLINED (3/3 lenses).** Copilot suggested parsing the JWT `email_confirmed_at` claim as a unix-seconds long and requiring `> 0`, claiming `"0"` or garbage would currently pass the gate. Verified: `JwtIssuer.cs:78-84` only emits the claim when `user.EmailConfirmedAt is not null`, and always emits via `ToUnixTimeSeconds()` (a valid positive long). The claim is structurally absent for unconfirmed users (`MakablesClaimTypes.EmailConfirmedAt` remarks document this explicitly). JWT signature validation (HS256) prevents forgery — an attacker cannot create a token with `"0"` or garbage without the signing key. The malformed-claim case is unreachable in production. `!string.IsNullOrEmpty(...)` is sufficient under the signature trust assumption. No code change.
  - **M-2 — FOLDED (doc/code drift).** XML doc on `CreateOrder.cs:28-32` said soft-deleted products surface as `ProductNotActive`. Actual handler returns `ProductNotFound` for `product is null` and `ProductNotActive` for `!product.IsActive` (two distinct branches). The global soft-delete query filter on `Auditable` already turns soft-deleted rows into null lookups, so they correctly surface as `ProductNotFound`. Rewrote the XML doc to enumerate both branches explicitly and explain the UX reason (customer sees a different message for "purged" vs "deactivated").
  - **L-1 — FOLDED (same drift, code-block comment).** Inline comment in the handler repeated the same wrong claim. Rewrote to match the actual two-branch behaviour.
  - **L-2 — FOLDED (test naming convention).** Renamed `CreateOrderRegistrationTest.cs` → `CreateOrderRegistrationTests.cs` (plural) and the class identifier accordingly. Matches the 91 sibling `*Tests` files in `Makables.Tests`.
  - Build clean. **994 tests pass** (899 unit + 95 integration; net count unchanged — 4 doc-only + 1 code fix + 1 test tightening). Docker daemon up; the regression test for H-1 executed end-to-end.
- 2026-06-05 T-0062 merged to master (commit `ee63f75`). T-0063 branch rebased onto master cleanly (no conflicts; 994 tests still pass post-rebase). Force-pushed to refresh PR.
- 2026-06-05 second Copilot review pass on PR — surfaced 4 findings (1 Medium + 3 Lows). Workflow-verified (3 lenses × 4 findings = 12/12 unanimous "real" verdicts). **However:** spot-checking each cited file against the current `HEAD` (`edc07eb` post-rebase) revealed all four claimed defects are **already resolved**. The review pass appears to have read a stale commit version — likely the pre-rebase `655a941`, before the H-1/M-2/L-1/L-2 folds in the immediately prior commit. Verified the remote PR head matches local `HEAD` (`git ls-remote` ↔ `git rev-parse HEAD` agree on `edc07eb`).
  - **R2-M1 (already resolved):** middleware XML doc claim about health/openapi bypass was removed during the original feature commit `7a07b16` — current XML doc at lines 33-37 only mentions `/api/v*/auth/*`. No false claim ever shipped in the rebased state.
  - **R2-M2 (already resolved):** `JwtIssuer.cs:78-81` already credits JWT signature validation: "The gate relies on JWT signature validation: callers cannot forge this claim without the server signing key." Matches Copilot's suggested wording.
  - **R2-M3 (already resolved):** `MakablesClaimTypes.cs:22-23` already reads "JWT signature validation prevents clients from forging this claim; unconfirmed users simply omit it from the token." Matches Copilot's suggested wording.
  - **R2-L1 (already resolved):** `OrdersController.cs:70-77` already reads "401 is declared via [ProducesResponseTypeAttribute] for OpenAPI completeness even though, on the normal controller path, the framework's authentication challenge can short-circuit before the handler runs." No contradiction.
  - **Outcome:** no code/doc changes needed. Status log entry serves as the audit trail for future readers who see the Copilot comments in the PR history but don't see folding commits — the resolution is "already correct."
- 2026-06-05 third Copilot review pass — surfaced 5 findings (3 Mediums + 2 Lows) on the same threat-model + XML-doc surfaces. Spot-checking found **4 of 5 already resolved** (same phantoms as the round-2 audit: JwtIssuer comment, MakablesClaimTypes XML doc, middleware XML doc, OrdersController 401 XML doc — all match Copilot's suggested wording verbatim in the rebased state). **One real finding caught:** the middleware's **inline** comment at `RequireEmailConfirmedMiddleware.cs:80-83` (NOT the XML doc) still carried the old `"Absence (not '= 0')" is the unconfirmed signal — see MakablesClaimTypes.EmailConfirmedAt remarks on why we don't emit zero for unconfirmed users` wording. The earlier folds touched the XML doc + the JwtIssuer comment + the MakablesClaimTypes doc, but missed this inline comment immediately above the `FindFirstValue` call. Rewrote to: "Confirmed if the claim is present at all; unconfirmed users structurally omit it. The gate relies on JWT signature validation: callers cannot add this claim to a token without the server signing key." Build clean; the two integration tests covering this branch (`CreateOrder_returns_403_AuthEmailNotConfirmed_when_user_email_unconfirmed` and `RequireEmailConfirmedMiddleware_bypasses_auth_subpath_for_authenticated_unconfirmed_user`) still pass. Comment-only edit.
