# T-0065 — IPaymentProvider + ComgatePaymentProvider + IPaymentProviderFactory + CreatePaymentSession endpoint

**Phase:** 4 (Orders)
**Size:** L
**State:** `ready`
**Depends on:** T-0010 (`CountryConfiguration.DefaultPaymentProvider`), T-0049/049c (multipart precedent — not directly needed but the Polly + HttpClient wiring pattern lives in the same `AddMakablesClients` file), T-0063 (`Order.PaymentProviderRef` + `OrdersController`)
**Owner:** `dotnet-backend`
**ADRs:** 0002 (BusinessResult + error classification), 0005 (per-audience hosts), 0008 (provider factory pattern), 0016 (Comgate), 0023 (deployment + secrets), 0014 (audit)
**Stories:** US-customer-0010 AC-2 + AC-3 (redirect to Comgate + 24h retry window)
**Role doc:** [docs/architecture/roles/payment-provider.md](../architecture/roles/payment-provider.md) (existing; T-0065 implements the interface this doc describes)

## Why now

T-0063 left `CreateOrder` JSON-only per user decision Q1: the response is `{orderId, orderNumber, total, currency}` — no redirect URL. The frontend navigates to `/objednavka/<orderId>` and from there must call **a separate endpoint** to get the Comgate redirect. T-0065 builds that endpoint.

Until T-0065 lands:
- US-customer-0010 AC-2 ("redirect to Comgate") is fully broken; the customer reaches the order page and the "Pay" button has no URL.
- US-customer-0010 AC-3 (24h retry window) is moot; nothing to retry.
- T-0066 (Comgate webhook) and T-0067 (`MarkOrderPaid`) cannot proceed; both depend on `Order.PaymentProviderRef` being set by *something*, and that something is T-0065.
- T-0105 (admin refund) is blocked on `IPaymentProvider.RefundAsync` existing — which T-0065 declares (even if it ships as `NotSupportedException` per user decision Q2).

This is also the **first external-payment-provider adapter** in the codebase. It sets the keyed-services-with-factory precedent that future SK/PL/HU launches will reuse for their respective payment gateways.

## Scope

### User decisions captured upfront (research workflow + synthesis)

1. **Retry recovery story (Q1):** call `VerifyPaymentAsync` first; re-create only if the existing session is `Cancelled` or `Failed`. Cache the redirect URL on `Order` (new nullable `PaymentRedirectUrl` column) so retries within the 24h US-customer-0010 AC-3 window don't burn a Comgate round-trip. Set-once invariant on `PaymentProviderRef` becomes set-once-or-overwrite-after-rejection.
2. **Interface scope (Q2):** declare the full 4-method `IPaymentProvider` interface now (`CreatePaymentAsync`, `VerifyPaymentAsync`, `ParseAndVerifyWebhookAsync`, `RefundAsync`). T-0065 implements the first two end-to-end; the latter two ship as `NotSupportedException` with explicit T-0066/T-0105 ticket references in the body. Locks the contract for future providers.
3. **Keyed migration scope (Q3):** leave `SendGridEmailProvider` and `AresCompanyRegistry` as direct DI. T-0065 introduces the keyed pattern only for `IPaymentProvider`. Open **T-0124** (cross-cutting follow-up) for the migration. The lone-keyed-provider awkwardness lasts until T-0070 (Packeta) which also wants keyed.
4. **Secrets scope (Q4):** `Comgate:*` is a global config section (`MerchantId`, `Secret`, `BaseUrl`, `TestMode`, `WebhookAllowedIps`). Per-country variation (`prepareOnly`, `lang`, `country`) is read from `CountryConfiguration` at call time. Matches ADR 0016. If SK/PL/HU need separate merchant accounts later, we add `CountryConfiguration.PaymentMerchantId` then.

### Domain (`Core.Domain/Payments/`)

New folder. Contains the contract and the value-object records — zero third-party references (CLAUDE.md §1).

**`IPaymentProvider.cs`** — 4-method interface (Q2 decision):

```csharp
public interface IPaymentProvider
{
    /// <summary>The provider code (e.g. "comgate"). Matches CountryConfiguration.DefaultPaymentProvider.</summary>
    string Code { get; }

    /// <summary>T-0065. Create a payment session at the provider; return the redirect URL + the provider's reference.</summary>
    Task<BusinessResult<PaymentSession>> CreatePaymentAsync(Order order, CancellationToken cancellationToken);

    /// <summary>T-0065. Verify the current status of an existing payment session at the provider.</summary>
    Task<BusinessResult<PaymentStatus>> VerifyPaymentAsync(string providerRef, CancellationToken cancellationToken);

    /// <summary>T-0066. Parse + verify an inbound webhook from the provider. NOT IMPLEMENTED at T-0065.</summary>
    Task<BusinessResult<WebhookPayload>> ParseAndVerifyWebhookAsync(HttpRequest request, CancellationToken cancellationToken);

    /// <summary>T-0105. Refund a captured payment. NOT IMPLEMENTED at T-0065.</summary>
    Task<BusinessResult<RefundReceipt>> RefundAsync(string providerRef, long amountMinor, string currency, CancellationToken cancellationToken);
}
```

**`IPaymentProviderFactory.cs`** — resolve by country code:

```csharp
public interface IPaymentProviderFactory
{
    Task<BusinessResult<IPaymentProvider>> ResolveAsync(string countryCode, CancellationToken cancellationToken);
}
```

**Value records** (all in `Core.Domain/Payments/`):

```csharp
public sealed record PaymentSession(string ProviderRef, string RedirectUrl);

public sealed record PaymentStatus(PaymentState State, string? PaymentMethod, DateTimeOffset? PaidAt);

public sealed record WebhookPayload(string ProviderRef, PaymentState State, string? PaymentMethod);

public sealed record RefundReceipt(string RefundProviderRef, long AmountMinor, string Currency, DateTimeOffset RefundedAt);

public enum PaymentState { Pending, Authorized, Paid, Cancelled, Refunded, Failed }
```

### `Order` aggregate edit (`Core.Domain/Orders/Order.cs`)

Two changes (Q1 decision):

1. Add nullable `string? PaymentRedirectUrl { get; private set; }` — cached for the 24h retry window. Set only when `ReservePaymentSession` succeeds.
2. New method `BusinessResult ReservePaymentSession(string providerRef, string redirectUrl, IClock clock)`:
   - Guard: `State == PendingPayment`. Otherwise return `Error.Conflict("state", BusinessErrorMessage.OrderInvalidTransition)`.
   - Idempotent: if `PaymentProviderRef` is non-null and equals the incoming ref, just update `redirectUrl` (Comgate retries on the same `refId` return the same `transId`) — succeed. If the existing ref differs from incoming, this is an overwrite-after-rejection path (Q1 allows it when the prior session was Cancelled/Failed; the handler enforces that via `VerifyPaymentAsync` before calling `ReservePaymentSession`).
   - Sets `PaymentProviderRef = providerRef`, `PaymentRedirectUrl = redirectUrl`, `UpdatedOn = clock.UtcNow`.
   - Does NOT change `State`. The transition to `Paid` is exclusively T-0067's job via the webhook.

`MarkAsPaid` keeps its existing set-once belt-and-braces guard on `PaymentProviderRef` (T-0060 R2-1) — if a future state-graph change lets a `Paid` order revisit `PendingPayment`, the belt-and-braces guard fires.

### Database migration

Add `payment_redirect_url VARCHAR(500) NULL` to the `orders` table. Nullable because it stays NULL until the customer's first `CreatePaymentSession` call. Migration name: `AddOrderPaymentRedirectUrl`.

### Infra adapter (`Infra.Clients/Comgate/`)

New folder, 3 files:

**`ComgateOptions.cs`:**

```csharp
public sealed class ComgateOptions
{
    public const string SectionName = "Comgate";

    public string MerchantId { get; init; } = string.Empty;
    public string Secret { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://payments.comgate.cz";
    public bool TestMode { get; init; }
    public IReadOnlyList<string> WebhookAllowedIps { get; init; } = Array.Empty<string>();
}
```

`WebhookAllowedIps` lives here even though T-0066 owns the webhook — registering it now keeps T-0066's DI footprint to zero.

**`ComgatePaymentProvider.cs`** — primary-ctor DI: `(IHttpClientFactory factory, IOptions<ComgateOptions> options, ICountryConfigurationRepository configs, ResiliencePipelineProvider<string> pipelines, ILogger<ComgatePaymentProvider> logger)`. Static constants:

```csharp
public const string HttpClientName = "comgate";
public const string ProviderCode = "comgate";
public string Code => ProviderCode;
```

**`CreatePaymentAsync` flow** (per ADR 0016 + Q1):

1. Load `CountryConfiguration` for `order.CountryCode` (needed for `lang`, `country`).
2. Build form-urlencoded body:
   - `merchant = options.MerchantId`
   - `price = order.TotalAmountMinor` (Comgate wants minor units — same as our storage)
   - `curr = order.Currency`
   - `label = $"Objednávka {order.OrderNumber}"`
   - `refId = order.Id` (our ULID; webhook returns this)
   - `email = order.ContactEmail`
   - `prepareOnly = true` (rejected alternative per ADR 0016:172 — gives us the redirect URL without starting the payment)
   - `country = order.CountryCode`
   - `lang = config.DefaultLanguageCode.Substring(0, 2)` (Comgate expects 2-letter, we store `cs-CZ`)
   - `method = ALL`
   - `secret = options.Secret`
3. POST to `{options.BaseUrl}/v1.0/create` via the resilience-wrapped HttpClient. `Content-Type: application/x-www-form-urlencoded`. No `Authorization` header; auth is the body `secret` field.
4. Read response body, parse with `System.Web.HttpUtility.ParseQueryString` (response is also form-urlencoded per ADR 0016:93).
5. If `code == "0"`: extract `transId` + `redirect`. Return `BusinessResult.Success(new PaymentSession(transId, redirect))`.
6. Else: map to typed error (see Error mapping below).
7. **Secret never appears in logs.** Use structured logging with named properties; the `secret` form field is excluded from any logged body.

**`VerifyPaymentAsync` flow:**

1. GET `{options.BaseUrl}/v1.0/status?merchant=...&secret=...&transId=...` via the resilience pipeline.
2. Parse form-urlencoded response. On `code == "0"`: map Comgate `status` (`PENDING|AUTHORIZED|PAID|CANCELLED`) to `PaymentState`. Extract `method` (card / bank-transfer / etc.) and `paymentTime` if present.
3. Return `BusinessResult.Success(new PaymentStatus(state, method, paidAt))`.
4. Same error-mapping rules as `CreatePaymentAsync`.

**`ParseAndVerifyWebhookAsync` + `RefundAsync`** — body is:

```csharp
throw new NotSupportedException(
    "ParseAndVerifyWebhookAsync is implemented in T-0066. " +
    "If you are reading this, the webhook handler in T-0066 has not yet shipped.");
```

(Likewise for `RefundAsync` referencing T-0105.)

**Error mapping** (per ADR 0016:97-106 + ADR 0002):

| Comgate response | `BusinessErrorMessage` code | `ErrorType` | Log level |
|---|---|---|---|
| `HttpRequestException`, `TaskCanceledException` (timeout), `5xx`, `429`, `408`, `504` | `payment.providerUnavailable` | `Transient` | `Warning` |
| `code != 0` with known business error (insufficient merchant balance, invalid currency) | `payment.providerRejected` | `Permanent` | `Error` |
| `code` indicating bad merchant / secret | `payment.providerMisconfigured` | `Configuration` | `Critical` |
| Anything else | `payment.unknownError` | `Transient` (capped at 3 retries — Mapbox/Ares precedent) | `Error` |

**`PaymentProviderFactory.cs`** — primary-ctor DI: `(IServiceProvider services, ICountryConfigurationRepository configs, IMemoryCache cache, ILogger<PaymentProviderFactory> logger)`. Caches the `CountryConfiguration.DefaultPaymentProvider` lookup with a 5-minute TTL (admin edits to country config are rare; payment-session traffic is hot). Returns `BusinessResult.Failure(payment.providerNotRegistered)` if the country's selected provider isn't a registered keyed service.

### DI wiring (`Config/Extensions/AddMakablesClients.cs`)

Add at the location flagged by the existing `// T-0065 Comgate` placeholder in the file header (line 27 — placeholder is a docstring marker; the actual block goes near the existing ARES/Mapbox registrations). Pattern mirrors Mapbox at lines 119/162-179:

```csharp
// Comgate (T-0065)
services.AddOptions<ComgateOptions>()
    .Bind(configuration.GetSection(ComgateOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.MerchantId), "Comgate:MerchantId is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Secret), "Comgate:Secret is required.")
    .Validate(o => Uri.IsWellFormedUriString(o.BaseUrl, UriKind.Absolute)
                && new Uri(o.BaseUrl).Scheme == Uri.UriSchemeHttps,
              "Comgate:BaseUrl must be absolute HTTPS.")
    .ValidateOnStart();

services.AddHttpClient(ComgatePaymentProvider.HttpClientName);

// Keyed registration — T-0065 introduces the keyed pattern; SendGrid + ARES migration in T-0124.
services.AddKeyedScoped<IPaymentProvider, ComgatePaymentProvider>(ComgatePaymentProvider.ProviderCode);
services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();

// Resilience pipeline (existing TryAddBuilder block at lines 162-179):
registry.TryAddBuilder<HttpResponseMessage>(
    ComgatePaymentProvider.HttpClientName,
    (builder, _) => builder
        .AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(200),
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .Handle<TaskCanceledException>()
                .HandleResult(r => (int)r.StatusCode is 408 or 429 or >= 500),
        }));
```

### CQRS feature (`Core.AppServices/Features/Orders/CreatePaymentSession.cs`)

Single static class with nested `Command/Response/Validator/Handler`. Pattern mirrors `AddOrderAttachment.cs` from T-0064.

**`Command`:** `(string OrderId)` — `CustomerUserId` resolved from `IUserSessionProvider`, never from body (IDOR).

**`Response`:** `(string PaymentProviderRef, string RedirectUrl)`.

**`Validator`:** `OrderId.NotEmpty().MaximumLength(40)` (matches `Order.Id` shape from T-0060).

**Handler — 7-step flow** (Q1 verify-then-recreate):

1. Resolve `customerUserId` from `IUserSessionProvider`. Null → `Error.Unauthorized()` (backstop guard).
2. Load order via `orders.GetByIdForCustomerAsync(orderId, customerUserId, ct)`. Null → `Error.NotFound("orderId", OrderNotFound)` (404 not 403, leak-resistant per T-0063 AC-2).
3. State gate: `order.State != PendingPayment` → `Error.Conflict("state", OrderInvalidStateForPayment)`. **New code** (see §BusinessErrorMessage). After-Paid retries are NOT allowed — the customer's already paid; the frontend redirects them based on state.
4. Resolve provider via `factory.ResolveAsync(order.CountryCode, ct)`. Failure → propagate.
5. **Q1 retry logic.** If `order.PaymentProviderRef` is non-null:
   - Call `provider.VerifyPaymentAsync(order.PaymentProviderRef, ct)`.
   - If status is `Pending` or `Authorized`: return cached `Response(order.PaymentProviderRef, order.PaymentRedirectUrl)` — no new Comgate call. Customer reuses the same redirect.
   - If status is `Cancelled` or `Failed`: fall through to step 6 (create a new session).
   - If status is `Paid`: state-machine mismatch (we should have transitioned via webhook). Log `Critical` and return `Error.Conflict("state", OrderPaymentAlreadyCaptured)`. **New code.**
   - If status is `Refunded`: same `Critical` log + state mismatch error.
6. Call `provider.CreatePaymentAsync(order, ct)`. Failure → propagate.
7. `order.ReservePaymentSession(session.ProviderRef, session.RedirectUrl, clock)`. The aggregate guard catches a malformed state-transition. Return `Response(session.ProviderRef, session.RedirectUrl)`. UoW commits via the pipeline behavior.

### Customer-host endpoint (`Web.Customer/Controllers/OrdersController.cs`)

Append to the existing controller (mirrors `UploadAttachment` pattern from T-0064):

```csharp
[HttpPost("{orderId}/payment-session")]
[ProducesResponseType(typeof(CreatePaymentSessionResponse), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
public async Task<IActionResult> CreatePaymentSession(string orderId, CancellationToken ct);
```

Controller-level `CreatePaymentSessionResponse` wrapper for OpenAPI schema-name collision avoidance. Controller body is a one-liner: `Mediator.Send(new CreatePaymentSession.Command(orderId), ct)` then `HandleResult`.

### New `BusinessErrorMessage` codes (`Core.Domain/Common/BusinessErrorMessage.cs`)

Under a new `// === Payment ===` block:

- `PaymentProviderUnavailable = "payment.providerUnavailable"`
- `PaymentProviderRejected = "payment.providerRejected"`
- `PaymentProviderMisconfigured = "payment.providerMisconfigured"`
- `PaymentProviderNotRegistered = "payment.providerNotRegistered"`
- `PaymentUnknownError = "payment.unknownError"`

Under the existing `// === Order ===` block:

- `OrderInvalidStateForPayment = "order.invalidStateForPayment"`
- `OrderPaymentAlreadyCaptured = "order.paymentAlreadyCaptured"`

Per CLAUDE.md parity rule: matching i18n keys land in `frontend/src/lib/i18n/cs-CZ.ts` in the same PR.

### Frontend i18n (`frontend/src/lib/i18n/cs-CZ.ts`)

7 new keys. Draft Czech wording (PM/UX may refine on review):

```ts
'payment.providerUnavailable': 'Platební brána je momentálně nedostupná. Zkuste to prosím za pár minut.',
'payment.providerRejected':    'Platba byla zamítnuta. Zkontrolujte údaje a zkuste to znovu.',
'payment.providerMisconfigured': 'Platba dočasně není možná z technických důvodů.',
'payment.providerNotRegistered': 'Platba pro tuto zemi není podporována.',
'payment.unknownError':        'Nastala neznámá chyba při zpracování platby. Zkuste to prosím znovu.',
'order.invalidStateForPayment':  'Tuto objednávku již nelze platit.',
'order.paymentAlreadyCaptured':  'Tato objednávka už byla zaplacena.',
```

### NSwag regen

Customer-host TypeScript client gets `OrdersClient.createPaymentSession(orderId)` returning `{paymentProviderRef, redirectUrl}`. Per CLAUDE.md cross-stack rule, regenerated in the same PR.

### Tests

#### Unit — `Makables.Tests/Infra/Clients/Comgate/ComgatePaymentProviderTests.cs`

Use the `StubHttpMessageHandler` precedent from `Makables.Tests/Infra/Clients/Mapbox/MapboxAddressGeocoderTests.cs:281-296`. ~12 tests:

- `CreatePaymentAsync` happy path → `transId` + `redirect` extracted from form-urlencoded response.
- `CreatePaymentAsync` posts `prepareOnly=true`, `method=ALL`, `secret=<from options>`, `refId=<order.Id>` (assert body shape).
- `CreatePaymentAsync` secret NEVER appears in URL.
- `CreatePaymentAsync` secret NEVER appears in any log scope (`InMemoryLoggerProvider` assert).
- `CreatePaymentAsync` Comgate `code=1300` → `payment.providerRejected`, Permanent, Error log.
- `CreatePaymentAsync` Comgate bad-merchant `code=1100` → `payment.providerMisconfigured`, Configuration, Critical log.
- `CreatePaymentAsync` 503 then 200 → Polly retries, succeeds, logs single Warning.
- `CreatePaymentAsync` 503 × 4 → Polly exhausts, `payment.providerUnavailable`, Transient.
- `CreatePaymentAsync` `HttpRequestException` → Polly retries.
- `VerifyPaymentAsync` happy path → status `PAID` mapped to `PaymentState.Paid`.
- `VerifyPaymentAsync` status `PENDING` → `PaymentState.Pending` with `null` PaidAt.
- `ParseAndVerifyWebhookAsync` + `RefundAsync` → assert `NotSupportedException` with the T-0066/T-0105 ticket-reference text.

#### Unit — `Makables.Tests/Infra/Clients/Comgate/PaymentProviderFactoryTests.cs`

NSubstitute over `IServiceProvider`, `ICountryConfigurationRepository`, `IMemoryCache`. ~6 tests:

- `ResolveAsync` reads `DefaultPaymentProvider`, returns the keyed service.
- `ResolveAsync` caches the country lookup with 5-min TTL (second call hits the cache).
- `ResolveAsync` returns `payment.providerNotRegistered` when the country's provider code is unknown.
- `ResolveAsync` returns `country.notConfigured` when the country code itself is unknown.
- `ResolveAsync` cache is per-country (CZ and SK don't collide).
- `ResolveAsync` evicts on TTL expiry.

#### Unit — `Makables.Tests/Domain/Orders/OrderReservePaymentSessionTests.cs`

~7 tests:

- Happy path: `PendingPayment` order, no existing ref → sets `PaymentProviderRef` + `PaymentRedirectUrl`, returns Success.
- Idempotent on same ref: existing ref equals incoming → updates `RedirectUrl` (Comgate may return a fresher URL on retry), returns Success.
- Overwrite on different ref: existing ref differs → succeeds (the handler is responsible for verifying the prior was Cancelled/Failed first).
- State `Paid` → returns `OrderInvalidTransition`.
- State `Cancelled` → returns `OrderInvalidTransition`.
- State `Shipped` → returns `OrderInvalidTransition`.
- `State` is not changed by `ReservePaymentSession` (assert it's still `PendingPayment` after success).

#### Unit — `Makables.Tests/AppServices/Features/Orders/CreatePaymentSessionHandlerTests.cs`

NSubstitute. ~10 tests:

- Happy path: no existing ref → `CreatePaymentAsync` called, ref + URL persisted.
- Existing ref + `VerifyPaymentAsync` returns `Pending` → cached URL returned, no new Comgate call.
- Existing ref + `VerifyPaymentAsync` returns `Authorized` → cached URL returned.
- Existing ref + `VerifyPaymentAsync` returns `Cancelled` → `CreatePaymentAsync` re-called, ref overwritten.
- Existing ref + `VerifyPaymentAsync` returns `Failed` → `CreatePaymentAsync` re-called.
- Existing ref + `VerifyPaymentAsync` returns `Paid` → `OrderPaymentAlreadyCaptured` + Critical log.
- Order not found → `OrderNotFound`, never calls factory.
- Order owned by different customer → `OrderNotFound` (IDOR).
- Order state `Paid` → `OrderInvalidStateForPayment`.
- Factory returns `payment.providerNotRegistered` → propagate verbatim.

#### Integration — `Makables.IntegrationTests/Orders/CreatePaymentSessionTests.cs`

Use the `PostgresHarness` from T-0062 with `[Collection("postgres")]`. Inject a `FakeComgatePaymentProvider` at `Makables.IntegrationTests/Common/FakeComgatePaymentProvider.cs` (parallel to T-0064's `FakeBlobStorageClient`) that records calls and returns scripted responses. Bootstrap by overriding the keyed `IPaymentProvider` registration in the `WebApplicationFactory`. ~7 tests:

- POST happy path → 200, body has `paymentProviderRef` + `redirectUrl`, DB persists both on the `Order` row, `State == PendingPayment` unchanged.
- POST without JWT → 401.
- POST with JWT for different customer → 404.
- POST on order in `Paid` state → 409 `order.invalidStateForPayment`.
- POST when fake provider returns `payment.providerUnavailable` → 503 with the typed error code.
- POST twice in succession → second call returns cached URL, fake provider's `CreatePaymentAsync` invoked exactly once.
- POST twice where second `VerifyPaymentAsync` returns `Cancelled` → second `CreatePaymentAsync` invoked, ref overwritten.

### Docs

- Role doc `docs/architecture/roles/payment-provider.md` already exists; verify it matches the shipped interface signature (synthesis brief flagged it — read + update if the contract drifted).
- Update `docs/architecture/patterns.md` §A.10 (or wherever provider-factory patterns live) with the keyed-services pattern.

## Acceptance criteria

- **AC-1** `IPaymentProvider`, `IPaymentProviderFactory`, `PaymentSession`, `PaymentStatus`, `WebhookPayload`, `RefundReceipt`, `PaymentState` all exist in `Core.Domain/Payments/` with the signatures in §Scope.Domain. `Core.Domain` has no third-party package references.
- **AC-2** `ComgatePaymentProvider.CreatePaymentAsync` POSTs to `{BaseUrl}/v1.0/create` with the form-urlencoded body in §Scope.Infra. The `secret` is in the body — never in URL, never in headers, never in log scope.
- **AC-3** Successful Comgate response (`code=0`) → returns `PaymentSession(transId, redirect)`. `transId` is persisted on `Order.PaymentProviderRef` and `redirect` on `Order.PaymentRedirectUrl` via `Order.ReservePaymentSession`. `Order.State` remains `PendingPayment`.
- **AC-4** Calling `POST /api/v1/orders/{orderId}/payment-session` on an order in any state other than `PendingPayment` returns `409` with `order.invalidStateForPayment`.
- **AC-5** Calling the endpoint on an order belonging to a different customer returns `404` with `order.notFound` (leak-resistant; same shape as T-0063 AC-2).
- **AC-6** Comgate `5xx | 408 | 429` + `HttpRequestException | TaskCanceledException` trigger Polly retries (3 attempts, exponential jittered backoff). After exhaustion the handler returns `payment.providerUnavailable` (Transient).
- **AC-7** Comgate `code != 0` permanent errors → `payment.providerRejected` (Permanent). Bad-merchant / bad-secret errors → `payment.providerMisconfigured` (Configuration) + `LogLevel.Critical`.
- **AC-8** `ComgateOptions` validates `MerchantId`, `Secret` non-empty and `BaseUrl` absolute HTTPS at startup via `.ValidateOnStart()`. The host refuses to boot if any is missing or invalid.
- **AC-9** `PaymentProviderFactory.ResolveAsync` reads `CountryConfiguration.DefaultPaymentProvider` and returns the matching keyed `IPaymentProvider`. Unknown provider code → `payment.providerNotRegistered`. Unknown country code → `country.notConfigured`. Country-config lookup cached via `IMemoryCache` with 5-min TTL.
- **AC-10** `Order.ReservePaymentSession` enforces the state-gate (only `PendingPayment`) and the idempotent set-ref invariant (same ref → success; different ref → also success, the handler vetted it). Does NOT change `State`.
- **AC-11** Retry-within-24h flow: second `CreatePaymentSession` call on an order with `Pending` or `Authorized` Comgate status returns the cached `redirect_url` without calling Comgate's `create` again. `VerifyPaymentAsync` IS called.
- **AC-12** Retry-after-rejection flow: second call on an order whose existing session is `Cancelled`/`Failed` calls `CreatePaymentAsync` again, overwrites the ref, returns the fresh redirect.
- **AC-13** `ParseAndVerifyWebhookAsync` + `RefundAsync` throw `NotSupportedException` with text referencing T-0066 / T-0105 respectively. Unit tests assert the exception text contains the ticket reference.
- **AC-14** New customer-host endpoint `POST /api/v1/orders/{orderId}/payment-session` is `[Authorize]`-protected, gated by `RequireEmailConfirmedMiddleware`, and returns `CreatePaymentSessionResponse(PaymentProviderRef, RedirectUrl)`.
- **AC-15** NSwag-generated frontend client at `frontend/src/lib/api-client/customer-api.v1.ts` exposes the new method; CI parity check passes.
- **AC-16** 7 new `BusinessErrorMessage` codes added; 7 matching Czech i18n keys added; all dotted values match character-for-character.
- **AC-17** Test count: at least 35 new unit tests + 7 new integration tests. Build clean. Baseline (post-T-0064 master) = 977 unit + 114 integration; target 1012+ unit + 121+ integration.

## Out of scope

- T-0066 — Comgate webhook controller. `ParseAndVerifyWebhookAsync` ships as `NotSupportedException`; the actual webhook endpoint is T-0066. `WebhookAllowedIps` lives in `ComgateOptions` so T-0066 needs no DI changes.
- T-0067 — `MarkOrderPaid` command. T-0065 never transitions `Order` past `PendingPayment`.
- T-0105 — admin refund. `RefundAsync` ships as `NotSupportedException`.
- T-0124 — migrating `SendGridEmailProvider` + `AresCompanyRegistry` to keyed services. Separate ticket (Q3).
- Per-country merchant IDs / multi-currency. ADR 0016 says one Comgate merchant covers CZ at MVP; SK/PL/HU may need a `CountryConfiguration.PaymentMerchantId` field when they launch.
- Auto-retry of failed payments. The 24h retry window is customer-driven (frontend shows a retry button); no background job re-tries Comgate.

## Technical notes

### Why `PaymentRedirectUrl` is cached on `Order`

Per Q1: the customer who comes back 6 hours later expects the same Pay button to work. Re-calling Comgate is cheap but not free (~200ms + retries on transient errors) — caching the URL means the second through Nth visit are pure DB reads. The URL becomes invalid only if the underlying Comgate session is `Cancelled` or `Failed`, which `VerifyPaymentAsync` catches before we serve the stale URL.

### Why the verify-then-recreate handler flow (not just always-recreate)

Two reasons:
1. Comgate IS idempotent on `refId` per ADR 0016, but only within a session-lifetime window. After Comgate cancels a session (timeout, customer abandons checkout), `refId` becomes reusable but the old session is gone — calling `create` again returns a NEW `transId`. We'd overwrite our stored ref with a different value; the original Comgate session vanishes from our records.
2. The verify path catches the `Paid` state mismatch where the webhook should have fired but didn't (Critical log → ops can investigate before the customer pays twice).

### Why `IPaymentProvider.Code` is a string, not an enum

Future providers (Stripe, GoPay, Adyen) are open-ended. An enum would require modification on every new provider; a string lets `CountryConfiguration.DefaultPaymentProvider` be admin-editable without a deploy.

### Why `IMemoryCache` on `PaymentProviderFactory`

Without cache, every payment-session POST hits the `country_configuration` table. At MVP volume (single-digit orders per minute) this is negligible; at scale it's wasteful. 5-min TTL is short enough that an admin "fix the wrong provider for CZ" change propagates within a sprint cycle; long enough that the cache works.

### Why throw `NotSupportedException` (not `BusinessResult.Failure`) for unimplemented methods

The two methods are NOT user-facing failures — they're internal contract violations. A caller that reaches `ParseAndVerifyWebhookAsync` in T-0065 is a developer mistake (T-0066 wires the call site). Throwing surfaces the bug immediately; returning `BusinessResult.Failure` would let it slip past to a generic error response.

### Test mode

`Comgate:TestMode = true` flips the base URL to Comgate's sandbox + uses sandbox credentials. The sandbox returns realistic responses including transient errors when triggered by specific test card numbers (per Comgate docs). T-0065 doesn't add anything specific for test-mode; the URL switch is the only behavior change.

### Why the redirect URL is plaintext-stored (not hashed/encrypted)

The URL contains a Comgate session token, but it's only usable to access the Comgate payment page for THIS order — not a session token in the security sense. An attacker with the URL can pay the customer's order on their behalf (suboptimal but not catastrophic — the maker still gets the money). Encrypting at rest would gain marginal protection at the cost of admin debuggability. Defer to a follow-up if any compliance constraint requires it.

## Test plan

Inline above (see Scope > Tests). No separate `docs/test-plans/` file.

## Status log

- 2026-06-05 `draft → ready` by PM. Expanded from INDEX row after T-0064 merged. Four user decisions captured upfront via a 5-reader research workflow + synthesis judge:
  - **Q1 — Retry recovery via verify-then-recreate.** Handler calls `VerifyPaymentAsync` before re-creating. Cached redirect URL persisted on `Order.PaymentRedirectUrl` (new nullable column) for the 24h US-customer-0010 AC-3 window.
  - **Q2 — Full 4-method `IPaymentProvider` interface declared now.** `CreatePaymentAsync` + `VerifyPaymentAsync` implemented end-to-end; `ParseAndVerifyWebhookAsync` (T-0066) + `RefundAsync` (T-0105) throw `NotSupportedException` with explicit ticket references.
  - **Q3 — Leave SendGrid + ARES direct DI.** T-0065 introduces the keyed pattern only for `IPaymentProvider`. Open T-0124 (cross-cutting follow-up) for the migration.
  - **Q4 — Global `Comgate:*` config section.** `MerchantId`, `Secret`, `BaseUrl`, `TestMode`, `WebhookAllowedIps`. Per-country variation (`lang`, `country`, `prepareOnly`) read from `CountryConfiguration` at call time. Matches ADR 0016.

  Two secondary defaults baked in (PM may revisit on review): `BusinessErrorMessage` namespace is flat `Payment` prefix (matching existing `Order`/`Maker`/`Product` patterns); retry cap for `Unknown` errors is 3 (matching Mapbox/Ares precedent).

  Verified upfront: `CountryConfiguration.DefaultPaymentProvider` exists at `Makables.Core.Domain/Configuration/CountryConfiguration.cs:59`; the resilience-pipeline registry pattern is established at `AddMakablesClients.cs:162-179` (Mapbox + ARES); `Order.PaymentProviderRef` set-once invariant + `MarkAsPaid` belt-and-braces guard at `Order.cs:149-157, 403-426`. ADR 0016 + role doc `docs/architecture/roles/payment-provider.md` are the canonical contract — implementation must match.
- 2026-06-05 done. `dotnet-backend` agent implemented per ticket. Reviewer pass APPROVE conditional on M-1 (single unused `using` removal). Build clean; **1151 tests pass** (1030 unit + 121 integration; baseline T-0064 = 1091; net +53 unit + 7 integration). Docker daemon up; the 7 new Postgres integration tests executed end-to-end.
  - **Five agent deviations** all confirmed sound by reviewer:
    1. **`Microsoft.AspNetCore.App` FrameworkReference added to `Core.Domain.csproj`** — necessary because `IPaymentProvider.ParseAndVerifyWebhookAsync(HttpRequest, ...)` per ADR 0016 §"Interface" verbatim and role doc `payment-provider.md:35` verbatim. CLAUDE.md §1 says "Core.Domain references no third-party packages"; a `FrameworkReference` is a shared-runtime reference, not a NuGet package, but pulling ASP.NET surface (`HttpRequest`) into Core.Domain bends the layering rule. **Reviewer verdict: accept as a one-time exception** — rejecting would require amending the accepted ADR + role doc in this PR (process violation for an implementing agent at close-out); cleaner alternatives (a domain port `IWebhookRequest`, or moving the interface to `Core.AppServices`) were considered and ruled overengineering today (worth revisiting if a second domain port ever needs HTTP semantics). Follow-up ticket suggested: **(a)** amend CLAUDE.md §1 to permit documented framework references when an accepted ADR mandates a framework type at a domain boundary, **(b)** revisit the `IWebhookRequest` port if Stripe / Adyen / GoPay land. csproj comment block at `Makables.Core.Domain.csproj:3-13` documents the exception inline.
    2. **Secret IS in URL for `VerifyPaymentAsync` (GET)** — matches ADR 0016 line 124 verbatim; ticket's "secret never in URL" assertion specifically scopes to `CreatePaymentAsync` (POST). Code comment at `ComgatePaymentProvider.cs:170-175` documents the OTel `SensitivePropertyMasker` reliance (T-0031 sec B-1 precedent).
    3. **Comgate `code` 1100/1102 → `Configuration` (`payment.providerMisconfigured` + Critical log)** — ADR 0016 says "bad merchant / bad secret" without enumerating; agent picked 1100 (bad merchant) + 1102 (bad secret) from Comgate's public docs. If staging contact reveals different codes the mapping is a 30-second fix.
    4. **Test code 1300** is an arbitrary non-zero non-1100/1102 placeholder exercising the catch-all `Permanent` branch. Production code branches purely on `code != "0"` after subtracting the misconfigured codes.
    5. **`OrderInvalidStateForPayment` (handler step 3) vs `OrderInvalidTransition` (aggregate guard)** — deliberate layered-guard split: handler returns a UX-specific code; aggregate returns the generic transition-violation code as defence-in-depth for the race window between handler's state-read and EF commit.
  - **M-1 (folded in this commit) — Unused `using System.Net.Http.Headers;`** removed from `ComgatePaymentProvider.cs:2`. `FormUrlEncodedContent` lives in `System.Net.Http`; the headers namespace was carried in but never referenced.
  - **5 Lows + 0 Nits** noted by the reviewer, all deferred as informational:
    - **L-1** — role doc `payment-provider.md:36` bullet on `RefundAsync` shows old `(providerRef, amount)` shape; shipped interface is `(providerRef, amountMinor, currency, ct)`. Role doc explicitly defers to ADR 0016 for the canonical signature so the drift is informal; one-line clarification optional.
    - **L-2** — `PaymentRedirectUrl` DB column is `VARCHAR(500)`; `Order.ReservePaymentSession` only `Trim()`s the URL. If Comgate ever returns >500 chars the EF SaveChanges throws mid-request rather than failing fast at the adapter. Current Comgate URLs are ~120 chars; defer the defensive guard.
    - **L-3** — `VerifyPaymentAsync` has no test pin asserting "secret never in any log scope" parallel to the existing `CreatePaymentAsync` pin. The `SensitivePropertyMasker` is the runtime defence; the test pin would close the symmetry. Optional.
    - **L-4** — Integration test `BuildOrderInState` doesn't cover `Cancelled`. Unit test `OrderReservePaymentSessionTests.State_Cancelled_returns_OrderInvalidTransition` pins entity behaviour. Optional.
    - **L-5** — Czech wording on `payment.providerRejected`: "Zkontrolujte údaje" implies the customer entered details, but rejection is at create-session before card entry. Reviewer suggests "Zkuste prosím jiný způsob platby." Defer to PM/UX.
