# T-0066 — Comgate webhook controller (IP allowlist + re-fetch + idempotency)

**Phase:** 4 (Orders)
**Size:** M
**State:** `ready`
**Depends on:** T-0065 (`IPaymentProvider.ParseAndVerifyWebhookAsync` stub, `ComgateOptions.WebhookAllowedIps`), T-0060 (`Order.MarkAsPaid`, `IOrderRepository.GetByPaymentProviderRefAsync`)
**Owner:** `dotnet-backend`
**ADRs:** 0002 (BusinessResult), 0005 (per-audience hosts — `Web.Public`), 0016 (Comgate)
**Stories:** US-customer-0010 AC-2 (order transitions to Paid after Comgate confirms)
**Role doc:** [docs/architecture/roles/payment-provider.md](../architecture/roles/payment-provider.md) — implements the `ParseAndVerifyWebhookAsync` member declared there.

## Why now

T-0065 shipped the customer-facing `CreatePaymentSession` endpoint but `ParseAndVerifyWebhookAsync` throws `NotSupportedException` — no order can ever transition out of `PendingPayment` until T-0066 lands. Concretely:

- **US-customer-0010 AC-2 is broken end-to-end:** the customer reaches Comgate, pays, gets redirected back to `/objednavka/<id>`, and the order shows `PendingPayment` forever.
- **T-0067 (`MarkOrderPaid` + outbox events for customer/maker emails + invoice generation) is blocked** on the controller that dispatches its command.
- **T-0083 (24h auto-cancel of stale `PendingPayment` orders)** would silently cancel every successfully-paid order if T-0066 doesn't ship first.

T-0066 implements the webhook handler with the three-layer security posture from ADR 0016 §"Webhook handling": IP allowlist (no body parse on failure), re-fetch from Comgate (never trust the inbound body), and DB-level idempotency check (return 200 on already-Paid).

## Scope

### User decisions captured upfront (research workflow + synthesis)

1. **T-0066/T-0067 boundary (Q1):** T-0066 ships a working `MarkOrderPaid.Command` that does the state transition only (no outbox event emission). T-0067 adds the outbox plumbing — customer email, maker email, invoice generation. End-to-end Paid transition works immediately on T-0066 merge; the customer-facing "order received" email waits for T-0067.
2. **IP allowlist format (Q2):** accept BOTH individual IPs and CIDR ranges. Implementation uses `Microsoft.AspNetCore.HttpOverrides.IPNetwork` for CIDR (`192.0.2.0/24`); falls back to `IPAddress.Equals` for bare IPs. Startup validation rejects malformed entries.
3. **Unknown `transId` response (Q3):** return `200 OK` + `Critical` log + alert. Comgate stops retrying; ops sees the anomaly within minutes. Refusing with 4xx would let Comgate retry-storm if a real bug existed in T-0065's ref-persistence path.
4. **`refId` vs `transId` mismatch (Q4):** return `401 Unauthorized` + `Critical` log. The body `refId` is the only field a forged webhook could use to redirect us to a different order than the `transId` belongs to. Refusing with 401 forces Comgate to retry (correct behaviour if Comgate had a bug); ops alert fires.

Two secondary defaults baked in (PM may revisit):
- **No Comgate HMAC signature verification beyond the three-layer posture.** ADR 0016 doesn't mention HMAC; the sanctioned defence is IP allowlist + re-fetch. Document in the role doc that this is intentional.
- **3 new `BusinessErrorMessage` codes** under a `payment.webhook.*` sub-family: `PaymentWebhookMalformed`, `PaymentWebhookIpRejected`, `PaymentWebhookRefIdMismatch`. Keeps log/alert filtering clean.

### Controller (`Makables.Web.Public/Controllers/Webhooks/ComgateWebhookController.cs`)

New file. Lives on `Web.Public` (host has no `[Authorize]` default per the survey of `Program.cs:16-17`). Mirrors the `ProductImageController.cs` shape for the anonymous-public pattern.

```csharp
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/public/webhooks/comgate")]
[AllowAnonymous]
[ComgateWebhookIpAllowlist]   // <-- filter, see §Filter below
[Consumes("application/x-www-form-urlencoded")]
public sealed class ComgateWebhookController(
    IPaymentProviderFactory providers,
    IOrderRepository orders,
    IMediator mediator,
    ILogger<ComgateWebhookController> logger) : MakablesApiController
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Receive(CancellationToken ct);
}
```

The body is parsed by `IPaymentProvider.ParseAndVerifyWebhookAsync(HttpRequest, ct)` — not by `[FromForm]` binding — so the adapter owns the form-parse + re-fetch in one place. The controller is thin (10–15 lines after the body).

### Controller flow (8-step)

1. **Resolve provider.** `providers.ResolveAsync(<configured country code>, ct)`. At MVP we ship a single Comgate provider keyed `"comgate"`; the country code parameter doesn't change behaviour but keeps the factory call honest. If resolution fails (config missing) → 500 (handled by global error mapping).
2. **Parse + re-fetch.** `provider.ParseAndVerifyWebhookAsync(Request, ct)`. The adapter (`ComgatePaymentProvider`) reads form fields, calls `VerifyPaymentAsync(transId, ct)` to re-fetch authoritative status, logs Critical on `test`/`TestMode` mismatch, and returns `WebhookPayload(transId, state, paymentMethod)` from the **re-fetched** status (never the body's `status` field). Failure → 400 `payment.webhook.malformed`.
3. **Lookup order by `PaymentProviderRef`.** `orders.GetByPaymentProviderRefAsync(payload.ProviderRef, ct)`. Null → log Critical + return **200** (Q3 decision — Comgate stops retrying).
4. **Spoof check on `refId`.** Read the body's `refId` from `Request.Form["refId"]` (the adapter discarded it after parsing). If `order.Id != body.refId` → log Critical + return **401** (Q4 decision — Comgate retries; ops alert).
5. **Idempotency short-circuit.** If `order.State` already equals the target state for the inbound `payload.State` (e.g. `Paid` for inbound `PAID`) → log `Information` + return **200**, no command dispatched.
6. **State mapping → command.** Switch on `payload.State`:
   - `Paid` → `MarkOrderPaid.Command(orderId, providerRef, paymentMethod, paidAt)`.
   - `Cancelled` → currently no T-0066 state machine action (orders sit in `PendingPayment` until T-0083 auto-cancel). Log `Information` + return 200.
   - `Authorized | Pending` → not a terminal Comgate state for our flow; log + return 200.
   - `Failed | Refunded` → log Warning + return 200 (these are future-ticket territory).
7. **Dispatch.** `await mediator.Send(command, ct)`. If handler returns `BusinessResult.Failure(OrderInvalidTransition)` (race: another webhook just transitioned the same order) → log `Information` + return **200** (ADR 0016:132 — Comgate must not retry on a benign race).
8. **Return 200** on every non-failure path. The webhook never returns 4xx for "business rejected"; only for IP/spoof/malformed-body.

### IP allowlist filter (`Makables.Web.Public/Filters/ComgateWebhookIpAllowlistAttribute.cs`)

New `Attribute` + `IAuthorizationFilter` (runs **before** model binding so a rejected source never has its body parsed).

- Reads `IOptionsMonitor<ComgateOptions>.CurrentValue.WebhookAllowedIps` per request (live reload).
- For each configured entry:
  - If it parses as a CIDR via `Microsoft.AspNetCore.HttpOverrides.IPNetwork.TryParse` → use `network.Contains(remoteIp)`.
  - Else if it parses as a bare `IPAddress` → use `IPAddress.Equals`.
  - Else (malformed entry): log `Critical` at startup discovery; skip the entry at request time.
- Matches `HttpContext.Connection.RemoteIpAddress` only. Does **NOT** trust `X-Forwarded-For`; the public host doesn't run `UseForwardedHeaders` (per the survey).
- On no match → set `context.Result = new StatusCodeResult(StatusCodes.Status401Unauthorized)`. No body, no log of the body (we never parsed it).
- On match → fall through; the controller action runs.

Empty `WebhookAllowedIps` rejects everything (no implicit allow-all). At startup, if the list is empty AND the host environment is not `Development`, log a `Warning` so ops see misconfiguration on first boot.

### `ComgatePaymentProvider.ParseAndVerifyWebhookAsync` impl (`Makables.Infra.Clients/Comgate/ComgatePaymentProvider.cs:220-224`)

Replace the existing `NotSupportedException` stub.

Body:
1. `if (!request.HasFormContentType)` → `BusinessResult.Failure<WebhookPayload>(Error.Validation("body", BusinessErrorMessage.PaymentWebhookMalformed))`.
2. `var form = await request.ReadFormAsync(ct);`
3. Extract `transId`, `refId`, `bodyStatus`, `bodyTestFlag`. If `transId` or `refId` is blank → same `PaymentWebhookMalformed` failure.
4. `if (bodyTestFlag != options.TestMode)` → `logger.LogCritical("Comgate webhook test-mode mismatch: body.test={BodyTest}, config.TestMode={ConfigTestMode}, transId={TransId}", bodyTestFlag, options.TestMode, transId);` Do NOT fail.
5. `var verifyResult = await VerifyPaymentAsync(transId, ct);` Failure → return verbatim (the Transient/Permanent/Configuration classification is already correct).
6. Optional divergence log: if `bodyStatus` (as a string) does not match the re-fetched `verifyResult.Value.State` mapped to Comgate string → `logger.LogWarning(...)`. The body status is otherwise ignored.
7. Return `BusinessResult.Success(new WebhookPayload(transId, verifyResult.Value.State, verifyResult.Value.PaymentMethod))`.

**Discard the existing 1-test stub-pin** (`ParseAndVerifyWebhookAsync_throws_NotSupportedException_with_T_0066_reference`) — that test was the entire point until T-0066. Replace with the new test list under §Scope.Tests.

### `MarkOrderPaid` feature (`Core.AppServices/Features/Orders/MarkOrderPaid.cs`)

New file. Q1 stub scope: state transition only, NO outbox event emission.

**Command:** `(string OrderId, string ProviderRef, string? PaymentMethod, DateTimeOffset PaidAt) : ICommand<Response>`.

**Response:** `(string OrderId)`. (Minimal — the controller doesn't need anything back; this is for test verifiability.)

**Validator:** `OrderId.NotEmpty().MaximumLength(40)`, `ProviderRef.NotEmpty().MaximumLength(200)`. `PaidAt` not validated (can be `default(DateTimeOffset)` if Comgate didn't return it; the handler uses `IClock.UtcNow` as fallback).

**Handler (5-step):**

1. Resolve `IClock` (DI).
2. Load order via `orders.GetByPaymentProviderRefAsync(command.ProviderRef, ct)` — defence-in-depth (controller already did this, but the handler can be called from a future ticket without the controller). Null → `BusinessResult.Failure(Error.NotFound(OrderNotFound))`.
3. **Defence-in-depth ref check:** if `order.Id != command.OrderId` → log Critical + `BusinessResult.Failure(Error.Conflict(PaymentWebhookRefIdMismatch))`. The controller already vetted this; handler refuses to mutate an order that doesn't match.
4. `order.MarkAsPaid(clock, command.ProviderRef)` — existing entity method from T-0060. Returns `BusinessResult.Failure(OrderInvalidTransition)` on race; the controller maps that to 200.
5. Return `BusinessResult.Success(new Response(order.Id))`. **No `SaveChangesAsync`** — UoW pipeline commits.

`PaymentMethod` and `PaidAt` are accepted in the Command **for T-0067's benefit** — T-0067 will persist them on `Order` (currently no columns; T-0067 ships the migration). At T-0066 these fields are accepted-and-ignored; the handler does NOT store them. This keeps the Command signature stable across the T-0066 → T-0067 transition.

### New `BusinessErrorMessage` codes (`Core.Domain/Common/BusinessErrorMessage.cs`)

Under a new `// === PaymentWebhook ===` block (or extending the existing `// === Payment ===` block from T-0065):

- `PaymentWebhookMalformed = "payment.webhook.malformed"`
- `PaymentWebhookIpRejected = "payment.webhook.ipRejected"`
- `PaymentWebhookRefIdMismatch = "payment.webhook.refIdMismatch"`

Note: `PaymentWebhookIpRejected` is logged but never returned to the caller (the filter sets `StatusCodeResult(401)` with no body — Comgate just sees 401). The code is for our log/alert filtering.

### Frontend i18n (`frontend/src/lib/i18n/cs-CZ.ts`)

3 new keys. These are NOT customer-facing in T-0066 (the webhook is server-to-server); they ARE used for admin audit log surfacing in T-0118. Draft Czech wording (PM/UX may refine):

```ts
'payment.webhook.malformed':       'Webhook od platební brány nemá očekávaný formát.',
'payment.webhook.ipRejected':      'Webhook přišel z neoprávněné IP adresy.',
'payment.webhook.refIdMismatch':   'Referenční ID ve webhooku se neshoduje s objednávkou.',
```

### NSwag regen

Public-host TypeScript client gets nothing for a server-to-server webhook — the spec emits the endpoint but the frontend never calls it. Run the regen step but expect a near-zero diff on `public-api.v1.ts`.

### Tests

#### Unit — `Makables.Tests/Infra/Clients/Comgate/ComgatePaymentProviderWebhookTests.cs`

~10 tests. Use the `StubHttpMessageHandler` precedent + a synthetic `HttpRequest` (Microsoft.AspNetCore.Mvc.Testing's `DefaultHttpContext` for building one).

- `ParseAndVerifyWebhookAsync` happy path — form body parsed, re-fetch returns PAID → `WebhookPayload(transId, Paid, "CARD_CZ")`.
- Body `status=PAID` but re-fetch returns `PENDING` → returned `WebhookPayload.State == Pending` (body status discarded) + `Warning` log on divergence.
- Body `test=true` but `options.TestMode=false` → `Critical` log fired, processing continues.
- Body `test=false` but `options.TestMode=true` → `Critical` log fired.
- Body missing `transId` → `PaymentWebhookMalformed`.
- Body missing `refId` → `PaymentWebhookMalformed`.
- `!HasFormContentType` (e.g. JSON body) → `PaymentWebhookMalformed`.
- `VerifyPaymentAsync` returns Transient → propagate verbatim (NOT `PaymentWebhookMalformed`).
- `VerifyPaymentAsync` returns Configuration → propagate verbatim.
- Comgate state mapping: `PAID → Paid`, `CANCELLED → Cancelled`, `AUTHORIZED → Authorized`, `PENDING → Pending`.

#### Unit — `Makables.Tests/Web/Public/ComgateWebhookIpAllowlistAttributeTests.cs`

~8 tests using `Microsoft.AspNetCore.TestHost` or hand-built `AuthorizationFilterContext`:

- Bare IP match (`203.0.113.5` in list) → allowed.
- CIDR /24 match (`203.0.113.0/24` in list, request from `.5`) → allowed.
- CIDR /32 match (`203.0.113.5/32`) → allowed.
- Non-match → `401`, no body.
- Empty list → `401`.
- Malformed config entry (`not-an-ip`) → logged Critical at startup discovery; skipped at request time; remaining entries still evaluated.
- IPv6 address with IPv6 CIDR.
- `RemoteIpAddress == null` (edge case in test environments) → `401`.

#### Unit — `Makables.Tests/AppServices/Features/Orders/MarkOrderPaidHandlerTests.cs`

~7 tests. NSubstitute over `IOrderRepository`, `IClock`. (No `IOutbox` — that's T-0067.)

- Happy path: `PendingPayment` order, matching ref → state becomes `Paid`, response is `(orderId)`.
- Order not found → `OrderNotFound`.
- `order.Id != command.OrderId` (controller bypassed) → `PaymentWebhookRefIdMismatch` + Critical log.
- `order.State == Paid` already (idempotency race) → `OrderInvalidTransition` from `Order.MarkAsPaid`.
- `order.State == Cancelled` → `OrderInvalidTransition`.
- `order.PaymentProviderRef` is set to a different ref → `OrderInvalidTransition` from the set-once invariant.
- `command.PaymentMethod` and `command.PaidAt` are accepted but NOT persisted (T-0067 territory) — assert the saved order has no new fields touched.

#### Integration — `Makables.IntegrationTests/Webhooks/ComgateWebhookTests.cs`

PostgresHarness (`[Collection("postgres")]`) + customer-host `WebApplicationFactory` (we test against Web.Public, not Web.Customer — set this up if no precedent exists). Inject `FakeComgatePaymentProvider` from T-0065 to script the VerifyPaymentAsync response. ~10 tests:

- POST happy path (IP allowed, body valid, fake provider returns PAID, order in PendingPayment) → 200, order transitions to Paid, `Order.PaymentProviderRef` matches transId.
- POST from disallowed IP → 401, no DB writes.
- POST with malformed body → 400.
- POST with `transId` not in DB → 200 + Critical log captured (verify via in-memory log provider).
- POST with `refId` mismatch (body refId=X, order whose ref equals transId has Id=Y) → 401 + Critical log.
- POST twice (idempotency) → second call returns 200, `MarkOrderPaid` not dispatched the second time.
- POST when order is already `Cancelled` → 200, no transition.
- POST when fake VerifyPaymentAsync returns Transient → 400 (propagated as a 4xx so Comgate retries — debatable but matches the malformed flow).
- POST when fake VerifyPaymentAsync returns Configuration → 500 (server bug; admin alert).
- POST with `test=true` in production-mode config → 200 + Critical log captured.

### Docs

- Update `docs/architecture/roles/payment-provider.md` — section on `ParseAndVerifyWebhookAsync` updated from "T-0066 deferred" to "T-0066 implemented; three-layer security: IP allowlist + re-fetch + idempotency". Document the no-HMAC decision (the IP allowlist + re-fetch + ref-mismatch checks are the sanctioned defence).
- Optional: `docs/architecture/security/webhook-verification.md` — update if it references T-0066 as pending.

## Acceptance criteria

- **AC-1** `POST /api/v1/public/webhooks/comgate` exists on `Web.Public`, decorated with `[AllowAnonymous]`, `[ComgateWebhookIpAllowlist]`, and `[Consumes("application/x-www-form-urlencoded")]`. Returns 200 on happy path with empty body.
- **AC-2** `ComgateWebhookIpAllowlistAttribute` is an `IAuthorizationFilter` (runs BEFORE model binding). Empty `WebhookAllowedIps` rejects all requests with `401` (no implicit allow-all). Non-matching IP → `401` with no body.
- **AC-3** IP matching supports BOTH bare IPs and CIDR ranges. `203.0.113.5` and `203.0.113.0/24` and `203.0.113.5/32` all behave correctly per Q2.
- **AC-4** Malformed CIDR/IP entries in `WebhookAllowedIps` are logged `Critical` at startup discovery and skipped at request-time; remaining valid entries still evaluated.
- **AC-5** `ComgatePaymentProvider.ParseAndVerifyWebhookAsync` parses form-urlencoded body, calls `VerifyPaymentAsync(transId, ct)` to re-fetch authoritative status, and returns `WebhookPayload` using the **re-fetched** state (never the body's `status` field). Body status divergence is logged `Warning` but never affects the return.
- **AC-6** Body `test` flag mismatching `ComgateOptions.TestMode` triggers `Critical` log; webhook still processes normally per ADR 0016:165-166.
- **AC-7** Controller resolves order via `IOrderRepository.GetByPaymentProviderRefAsync(payload.ProviderRef, ct)`; null → Critical log + `200` (per Q3).
- **AC-8** `refId` mismatch (body `refId` ≠ order's `Id` after transId lookup) → Critical log + `401` (per Q4).
- **AC-9** Idempotency: if `order.State` already equals the target state for the inbound `payload.State`, controller returns `200` without dispatching `MarkOrderPaid.Command`. Verified by NSubstitute `mediator.Received(0).Send(...)`.
- **AC-10** `MarkOrderPaid.Command/Validator/Handler` exists in `Core.AppServices/Features/Orders/MarkOrderPaid.cs`. Handler transitions `Order.MarkAsPaid(clock, providerRef)` and returns `Response(orderId)`. **NO outbox event emission** (T-0067 adds that).
- **AC-11** `MarkOrderPaid` handler accepts `PaymentMethod` and `PaidAt` in the Command but does NOT persist them (no DB columns yet). T-0067 will ship the migration + persistence.
- **AC-12** Comgate state mapping: `PAID → Paid → MarkOrderPaid.Command dispatched`; `CANCELLED / AUTHORIZED / PENDING / FAILED / REFUNDED → 200 + log + no dispatch` (those states are future-ticket territory).
- **AC-13** 3 new `BusinessErrorMessage` codes (`PaymentWebhookMalformed`, `PaymentWebhookIpRejected`, `PaymentWebhookRefIdMismatch`) added. Matching Czech i18n keys land in `cs-CZ.ts` in the same PR.
- **AC-14** Architectural compliance: no `BlobClient.GenerateSasUri` (N/A but verify); no `Console.*` in any new file; no `SaveChangesAsync()` in handler; no `dynamic`; all errors use `BusinessErrorMessage` constants; no inline strings in `Error.*` calls.
- **AC-15** Test count: at least 25 new unit tests + 10 new integration tests. Build clean. Baseline post-T-0065 master = 1030 unit + 121 integration; target 1055+ unit + 131+ integration.

## Out of scope

- **Outbox events** for `order.paid` side effects (customer email, maker email, invoice generation). **T-0067 owns this.** T-0066's `MarkOrderPaid.Handler` ships with a comment marking the outbox-insert site.
- **`Order.PaymentMethod` + `Order.PaidAt` columns.** T-0067 ships the migration. T-0066's command accepts these fields but ignores them.
- **HMAC body signature verification.** Comgate doesn't sign webhooks per ADR 0016; IP allowlist + re-fetch is the sanctioned posture. Document the decision in the role doc.
- **`X-Forwarded-For` trust.** The public host doesn't run `UseForwardedHeaders`. If Azure Front Door / WAF lands later, the allowlist filter will need updating to read `XFF` after proper config — out of scope for T-0066.
- **Webhook replay testing in production.** No admin endpoint to "replay last N webhooks" — that would be a separate ticket if ops needed it.
- **Refund webhook handling.** Comgate emits webhooks on refund too; we don't process them yet (T-0105 admin refund command will trigger refunds, but the webhook side falls under `payload.State == Refunded` → `200 + log + no dispatch`).

## Technical notes

### Why the IP allowlist is a filter, not middleware

A middleware applies to the entire pipeline; the allowlist is webhook-controller-specific. Filters run after routing (so we know we're hitting the webhook), before model binding (so a rejected source never gets its body parsed). The standard ASP.NET Core idiom is `[Authorize]` (which IS a filter under the hood); we follow the same pattern.

### Why `Order.MarkAsPaid` is the entity method we call

`Order.MarkAsPaid(IClock, providerRef)` already enforces the state-machine invariant (`PendingPayment → Paid` only) and the set-once invariant on `PaymentProviderRef` per T-0060 R2-1. We don't need a new entity method.

### Why the unknown-transId path returns 200 (Q3)

A 404 here would force Comgate to retry indefinitely with exponential backoff. If the cause is "T-0065 forgot to persist the ref on `ReservePaymentSession`" (a bug in our code), Comgate retrying doesn't help — every retry hits the same null lookup. The Critical log + alert lets ops investigate within minutes; meanwhile Comgate moves on and doesn't generate retry traffic. The trade-off is "silent acceptance of a real bug vs. retry storm of a real bug"; in practice the log captures the bug regardless.

### Why the refId-mismatch path returns 401 (Q4)

The body's `refId` is the only field where a spoofed webhook could try to redirect us to a different order than the one the `transId` actually belongs to. If we proceed despite the mismatch, we mark someone else's order as paid. Returning 401 + Critical log means Comgate retries (forcing ops attention) and we don't mutate anything. The alternative ("trust the transId, log the mismatch, proceed") trusts the IP allowlist absolutely; we want defence-in-depth.

### Why `MarkOrderPaid.Command` is dispatched via MediatR (not called inline)

Three reasons:
1. The `UnitOfWorkPipelineBehavior` runs validation + commits the transaction. Calling `Order.MarkAsPaid` inline from the controller bypasses both.
2. T-0067 (and future tickets) will add outbox-emission, audit-log entries, etc. via pipeline behaviours; doing this once via MediatR is cheap.
3. Testability: NSubstitute over `IMediator` lets us verify `Send(...)` was called exactly once or zero times (idempotency tests).

### Why `MarkOrderPaid.Command` carries `PaymentMethod` + `PaidAt` even though T-0066 ignores them

These are real values returned by Comgate's `VerifyPaymentAsync` — discarding them at T-0066 only to re-fetch them at T-0067 is wasteful. Accepting them now keeps the Command signature stable. T-0067 will:
- Ship the migration (`AddOrderPaymentMethodAndPaidAt`) adding two nullable columns.
- Update the handler to persist both fields (via a new `Order.MarkAsPaidWithDetails(...)` or by extending `MarkAsPaid` signature).
- Add the outbox event emission.

T-0066's Handler ignoring those parameters is fine — the Command remains backward-compatible.

### Why the public host (not Customer)

Webhooks come from external IPs and bypass authentication. The Customer host expects a JWT; routing webhooks there would either need a special exemption (gross) or a different audience. The Public host already serves anonymous endpoints (product images per T-0064); it's the natural home.

### Why we skip `X-Forwarded-For` trust

The host isn't behind a reverse proxy yet. If it ever lands behind Azure Front Door or a WAF, we'll need `UseForwardedHeaders` configured with the proxy's IP range as `KnownProxies`. Until then, `Connection.RemoteIpAddress` is the only honest source. A code comment in the filter documents this.

## Test plan

Inline above (see Scope > Tests). No separate `docs/test-plans/` file.

## Status log

- 2026-06-05 `draft → ready` by PM. Expanded from INDEX row after T-0065 merged. Four user decisions captured upfront via a 5-reader research workflow + synthesis judge:
  - **Q1 — T-0066 ships a stub `MarkOrderPaid` command** that does the state transition only (no outbox event emission). T-0067 adds outbox plumbing. End-to-end Paid transition works immediately on merge; customer/maker emails wait for T-0067.
  - **Q2 — IP allowlist accepts BOTH bare IPs and CIDR ranges.** `Microsoft.AspNetCore.HttpOverrides.IPNetwork.TryParse` for CIDR; `IPAddress.Equals` for singles. Malformed entries logged Critical at startup; skipped at request-time.
  - **Q3 — Unknown `transId` → 200 + Critical log.** Refusing with 4xx would let Comgate retry-storm on a real T-0065 ref-persistence bug; logging is enough for ops.
  - **Q4 — `refId` ≠ `order.Id` after transId lookup → 401 + Critical log.** Spoof suspicion; the body refId is the only field that could redirect us to the wrong order.

  Two secondary defaults baked in: no Comgate HMAC verification (sanctioned posture is IP + re-fetch + ref-mismatch checks); 3 new `BusinessErrorMessage` codes under `payment.webhook.*` sub-family for clean log/alert filtering.

  Verified upfront: `Order.MarkAsPaid(IClock, providerRef)` at `Order.cs:421` already enforces `PendingPayment → Paid` + set-once invariant; `IOrderRepository.GetByPaymentProviderRefAsync` at `IOrderRepository.cs:123` already exists; `ComgateOptions.WebhookAllowedIps` registered in T-0065 (empty by default; T-0066 adds the validator + filter consumer); `IPaymentProvider.ParseAndVerifyWebhookAsync` stub at `ComgatePaymentProvider.cs:220-224` ready for replacement. The `Web.Public` host has no global `[Authorize]` default and runs CORS + RequestEnrichment middleware + 60/min rate limit (per `AddMakablesRateLimiting.cs:43`).
- 2026-06-06 done. `dotnet-backend` agent implemented per ticket. Reviewer pass APPROVE with one Medium (M-1) and 7 Lows. Build clean; **1190 tests pass** (1059 unit + 131 integration; baseline T-0065 master = 1030 + 121 = 1151; net +29 unit + 10 integration). Docker daemon up; the 10 new Postgres-backed integration tests executed end-to-end.
  - **Five agent deviations** all confirmed sound by reviewer:
    1. **`System.Net.IPNetwork`** substituted for the deprecated `Microsoft.AspNetCore.HttpOverrides.IPNetwork` (`ASPDEPR005` in .NET 10). Same `TryParse` + `Contains(IPAddress)` API; mechanical swap.
    2. **`Order.MarkAsPaid` belt-and-braces relaxation** — was: refuse on ANY existing `PaymentProviderRef`; now: refuse only on DIFFERENT existing ref. T-0065's `ReservePaymentSession` always pre-sets the ref on the happy customer-pays path, which made the original set-once-on-`MarkAsPaid` guard fire on every legit webhook. The relaxation preserves the security property (no overwrite of a different ref); only the matching-ref case (the legit T-0065→T-0066 wire-up) now succeeds. Reviewer explicitly verified this is a sound cross-ticket evolution, not a bug-hiding workaround. Documented in `Order.cs:432-446` with a comment block referencing T-0066.
    3. **`PaidAt` plumbing** — `WebhookPayload` doesn't carry `PaidAt` (it's on `PaymentStatus` and consumed inside the adapter). Controller passes `null` to `MarkOrderPaid.Command`; handler ignores it (T-0067 territory). If T-0067 wants the real `PaidAt`, the cheapest path is to widen `WebhookPayload`.
    4. **`ResetLoggedMalformedEntriesForTesting()` is `public`** instead of `internal` + `InternalsVisibleTo`. The agent felt the `InternalsVisibleTo` plumbing for one method was over-scope. Documented in the filter's XML doc.
    5. **`RemoteIpStartupFilter` in integration tests** — `WebApplicationFactory<Web.Public.Program>` leaves `RemoteIpAddress` null by default. A private nested `IStartupFilter` stamps `203.0.113.5` on every request; the test allowlist contains exactly `203.0.113.5/32`. Standard ASP.NET Core idiom.
  - **M-1 (folded in this commit)** — AC-4 startup-discovery half was missing. The filter logged Critical at request time for malformed `WebhookAllowedIps` entries but never walked the list at boot. Added a fifth `.Validate(...)` block to `ComgateOptions.ValidateOnStart` chain at `AddMakablesClients.cs:175-189` that enumerates each entry and rejects on any unparseable one — same fail-loud posture as the `MerchantId/Secret/BaseUrl` guards. Host now refuses to boot on malformed config; ops sees the misconfig at deploy time instead of on the first webhook.
  - **L-1 (folded in this commit)** — refId-mismatch returned `Unauthorized(Error.Conflict(...))` — `ErrorType.Conflict` semantically maps to 409 per `MakablesApiController.MapErrorToActionResult`, but the controller wrapped it in a 401 envelope. Fixed at `ComgateWebhookController.cs:135` to `Error.Unauthorized(BusinessErrorMessage.PaymentWebhookRefIdMismatch)` so the type matches the status. Integration test (`POST_with_refId_mismatch_returns_401_no_DB_mutation`) still passes.
  - **L-5 (folded in this commit)** — no positive domain test pinned the matching-ref relaxation. The existing `MarkAsPaid_from_non_pending_returns_invalid_transition` test hits the state-guard before reaching the ref check (second call's state is `Paid`); the new `MarkOrderPaidHandlerTests.Existing_PaymentProviderRef_set_to_different_ref_…` only pins the negative path. Added **2 new domain tests** in `OrderTests.cs`: `MarkAsPaid_with_matching_pre_set_PaymentProviderRef_succeeds` (positive: simulates T-0065's ReservePaymentSession pre-stamp + T-0066's webhook MarkAsPaid) and `MarkAsPaid_with_DIFFERENT_pre_set_PaymentProviderRef_trips_set_once` (negative: pre-stamp with `tx-original`, MarkAsPaid with `tx-different` → InvalidTransition; state stays `PendingPayment`).
  - **L-2, L-3, L-4, L-6, L-7, L-8 + 3 nits — DEFERRED.** Style/wording polish; none affect behaviour or AC compliance:
    - **L-2** — `Cancelled | Authorized | Pending` → Information; `Failed | Refunded` → Warning. Ticket text vs implementation drift; behaviour is correct.
    - **L-3** — `IsAlreadyInTargetState` returns true for `Accepted | Shipped | Delivered | Completed | Refunded` against a `PAID` payload. ADR 0016:193 says Warning; current code logs Information. Future log-level tweak.
    - **L-4** — `POST_when_order_already_Cancelled_returns_200_no_transition` integration test name is misleading (it actually exercises the Q3 unknown-transId branch because the seed leaves `paymentProviderRef: null`). Rename + reseed in a follow-up.
    - **L-6** — Ticket said `VerifyPaymentAsync` Transient → 400; impl returns 503 (correct per ErrorType.Transient mapping). Update the ticket text; behaviour is right.
    - **L-7** — `ResetLoggedMalformedEntriesForTesting` public test seam. Two follow-up options if we want to remove it: scope dedupe to the `IOptionsMonitor` callback, or move dedupe into the `IOptions` validator.
    - **L-8** — Role doc `payment-provider.md` update could mention the IP-allowlist-filter ownership in Collaborators/Boundaries.
    - **3 nits** — single-expression `ParseBoolFlag` rewrite; duplicated TryParse in the filter; `PaidAt` widening on `WebhookPayload`. Style only.
  - Reviewer verdict on the **deviation #2 belt-and-braces relaxation** was ACCEPTABLE with explicit rationale: the original T-0060 R2-1 guard was correct at the time (no `ReservePaymentSession` existed); T-0065 introduced legitimate pre-stamping; the relaxation maintains the actual security property while accepting the legit path. T-0067 will not need a further relaxation.
