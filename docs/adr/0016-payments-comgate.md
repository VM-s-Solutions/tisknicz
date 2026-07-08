---
id: 0016
title: Payments — Comgate as the launch provider; PaymentProvider role; webhook idempotency via outbox
status: amended by 0027
date: 2026-05-21
deciders: [Architect]
---

# 0016 — Payments (Comgate)

## Context

Comgate is the chosen Czech payment gateway (per `TISKNI_MVP_SPEC.md` and validated by the user). We need to wire it as the first implementation of the `PaymentProvider` role from ADR 0004 / patterns §A.15 — and design the webhook handling so that future providers (Stripe, Adyen) slot in without touching `Core.AppServices`.

## Decision

### Role: PaymentProvider

`docs/architecture/roles/payment-provider.md` (adapter role) describes the contract every implementation must satisfy:

**Responsibility:** Initiate a payment session for an order, verify its status authoritatively, and parse webhook callbacks into a normalized form.

**Collaborators:**
- `Order` (read: amount, currency, customer email, order number)
- `CountryConfiguration` (read: provider credentials key, test-mode flag)
- `Money` (input/output of amounts)

**Does NOT know:**
- How the order was created, who the customer is beyond email, what happens after payment succeeds
- Invoice generation, payout logic, fee calculation
- The order's internal state machine

### Interface

```csharp
// Core.Domain/Payments/IPaymentProvider.cs
public interface IPaymentProvider
{
    string Code { get; }   // "comgate", "stripe", ...

    Task<BusinessResult<PaymentSession>> CreatePaymentAsync(
        Order order,
        CancellationToken ct);

    Task<BusinessResult<PaymentStatus>> VerifyPaymentAsync(
        string providerRef,
        CancellationToken ct);

    Task<BusinessResult<WebhookPayload>> ParseAndVerifyWebhookAsync(
        HttpRequest request,
        CancellationToken ct);
}

public record PaymentSession(string ProviderRef, string RedirectUrl);
public record PaymentStatus(PaymentState State, string? PaymentMethod, DateTimeOffset? PaidAt);
public enum PaymentState { Pending, Authorized, Paid, Cancelled, Refunded, Failed }
public record WebhookPayload(string ProviderRef, PaymentState State, string? PaymentMethod);
```

Registration:

```csharp
services.AddKeyedScoped<IPaymentProvider, ComgatePaymentProvider>("comgate");
services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();
```

`IPaymentProviderFactory.ResolveAsync(countryCode)` reads `CountryConfiguration.DefaultPaymentProvider` and returns the right keyed service.

### Comgate adapter

Lives in `Makables.Infra.Clients/Comgate/ComgatePaymentProvider.cs`.

**Credentials** come from configuration (Azure Key Vault in production): `Comgate:MerchantId`, `Comgate:Secret`, `Comgate:TestMode` (`true` in dev/staging, `false` in production). Loaded once at startup; per-country configuration determines `prepareOnly` and `country`/`lang` parameters by reading `CountryConfiguration`.

### CreatePaymentAsync

POST to `https://payments.comgate.cz/v1.0/create` with form-urlencoded body:

```
merchant      = <MerchantId>
price         = <order.TotalPriceMinor>        // Comgate wants minor units, which we already store
curr          = <order.Currency>               // ISO 4217
label         = "Objednávka <OrderNumber>"
refId         = <order.Id>                     // our ULID; this is what we receive back in the webhook
email         = <order.CustomerEmail>
prepareOnly   = true
country       = <CountryConfiguration.CountryCode>
lang          = <CountryConfiguration.DefaultLanguageCode>
method        = ALL
secret        = <Secret>
```

Response (form-urlencoded too): `code=0&message=OK&transId=AB1C-D34E&redirect=https://...`

On success: store `transId` on the order as `PaymentProviderRef`. Return `PaymentSession(transId, redirect)`. The order moves to `PendingPayment` state — already set by the `CreateOrder.Handler` that called us.

### Error classification

The adapter classifies every failure mode according to ADR §A.14:

| Comgate response / exception | Maps to | Effect |
|---|---|---|
| HTTP 5xx, timeout, `HttpRequestException` | `ErrorType.Transient` | Customer sees "platba dočasně nedostupná, zkuste znovu"; order stays `PendingPayment`; can be retried by the customer clicking again. No background retry — the user is sitting in front of the screen. |
| `code != 0` with known error message (invalid amount, etc.) | `ErrorType.Permanent` | Logged; admin alerted; the order is left `PendingPayment` for manual review. Customer sees a generic "platba se nezdařila" with a contact-support hint. |
| Bad merchant id / secret / signature mismatch | `ErrorType.Configuration` | Alerts SecOps. Should never happen in steady state. |
| Anything else | `ErrorType.Unknown` | Logged with full payload; treated as Transient with capped retries. |

### Webhook handling (`/api/public/webhooks/comgate`)

The webhook is the **authoritative** transition from `PendingPayment` → `Paid`. We do not trust the customer's redirect-back URL for state changes — only the webhook.

Webhook payload arrives as form-urlencoded:

```
transId    = AB1C-D34E
refId      = <our order id>
status     = PAID | CANCELLED | AUTHORIZED
test       = true | false
```

Comgate also signs the body. Verification:

1. **IP allowlist** check first (Comgate publishes the IPs; configured via `Comgate:WebhookAllowedIps`). If the source is outside the list: 401 immediately, no body parse.
2. **Re-fetch the status from Comgate** via `GET https://payments.comgate.cz/v1.0/status?merchant=...&secret=...&transId=...`. Never trust the inbound POST body alone — this is the Cleansia + spec pattern. The re-fetch is also our defense against a forged webhook even if it slipped past the IP check.
3. **Idempotency**: look up the order by `PaymentProviderRef`. If the order is already in the target state (e.g. webhook fires twice for the same `PAID`), return 200 with no side effects.

### Idempotent state transition + outbox

The webhook handler dispatches a Mediator command `MarkOrderPaid.Command(orderId, providerRef, paymentMethod, paidAt)`. Inside the handler:

1. Re-check status via `VerifyPaymentAsync` (defense in depth — even if the controller did it, the handler does it too to protect against direct command invocation from e.g. an admin manual trigger).
2. Move the order from `PendingPayment` to `Paid`. If the transition is invalid (e.g. order is `Cancelled`), return `Error.Conflict("order.invalidTransition")` and let the webhook return 200 — Comgate must not keep retrying.
3. Insert a row into the **outbox** table for each side effect: customer email, maker email, invoice generation. The outbox table is part of the same transaction as the order update (`UnitOfWorkPipelineBehavior` commits both atomically).
4. After commit, an Azure Function (`ProcessOutbox`, runs every 30s and on demand) reads the outbox and dispatches each side effect. Each outbox row has its own retry classification per ADR §A.14.

#### Outbox table

```sql
CREATE TABLE outbox_event (
  id TEXT PRIMARY KEY,                         -- ULID
  aggregate_id TEXT NOT NULL,                  -- e.g. order id
  event_type TEXT NOT NULL,                    -- "order.paid", "order.shipped", etc.
  payload JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  processed_at TIMESTAMPTZ,
  retry_count INT NOT NULL DEFAULT 0,
  next_retry_at TIMESTAMPTZ,
  last_error_type TEXT,
  last_error_code TEXT
);

CREATE INDEX idx_outbox_event_unprocessed ON outbox_event(next_retry_at)
  WHERE processed_at IS NULL;
```

The outbox decouples webhook acknowledgment from side-effect success. Comgate gets a 200 the moment the order is `Paid`; if Resend is down and we can't send the customer email, the outbox retries on its own schedule.

### Refunds (admin)

`RefundOrder.Command` (admin host, audited per ADR 0014) calls `IPaymentProvider.RefundAsync(providerRef, amount)`. Comgate supports partial refunds; the command accepts an explicit amount. Refunds emit an outbox event for the customer email and trigger a credit-note invoice generation (Batch 4 invoice ADR will detail).

`IPaymentProvider.RefundAsync` is added to the interface; Comgate implements it; future providers must.

### Test mode

`Comgate:TestMode=true` activates the sandbox endpoint and uses sandbox merchant credentials. Production uses live. The `test` field returned in webhooks is also asserted: in production, `test=true` is a configuration error (raises an alert).

## Alternatives considered

- **Trust the redirect-back URL for state transitions** — rejected. Customers can refresh, close the tab, share the URL. The webhook + server-side status re-fetch is the only authoritative source.
- **No outbox; fire side effects directly from the webhook handler** — rejected. Would couple webhook acknowledgment to email/invoice success. If Resend is down, Comgate would see 5xx and keep retrying; the order would stay in limbo. Outbox is the standard pattern for this.
- **Use Comgate's "process" mode (no `prepareOnly`)** — rejected. `prepareOnly` gives us the redirect URL without immediately starting the payment, letting us record the `transId` before the customer hits the gateway.
- **Verify webhooks by HMAC signature instead of (or in addition to) re-fetching** — Comgate does sign, but the signature scheme is limited. Re-fetch is more robust and is the documented Cleansia + spec pattern.

## Consequences

### Positive
- Webhook handling is bulletproof: IP allowlist + re-fetch + idempotency check + outbox = no double-processing, no lost side effects, no fraudulent state changes.
- Outbox table makes every async side effect observable. Admin UI can show "outbox stalled" if `processed_at IS NULL AND next_retry_at < now() - 1h`.
- Adding Stripe later is a new keyed `IPaymentProvider` impl + a `CountryConfiguration` row change. Zero handler changes.

### Negative
- Outbox table is a piece of new infrastructure — needs the `ProcessOutbox` Function (Azure Function, timer + queue triggered).
- Side effects are eventually consistent, not immediately. Customer sees the order go to "Paid" instantly; the confirmation email may arrive seconds later. Acceptable.

## Compliance / verification

- SecOps: Comgate IP allowlist configured; webhook re-fetches status before any side effect.
- SecOps: secrets in Key Vault; never logged.
- Reviewer: every payment-related write goes through `IPaymentProvider` from `Infra.Clients/<Provider>/`, never direct HTTP.
- Reviewer: every state-changing webhook handler ends with an outbox insertion + a `BusinessResult.Success` for the webhook response; side effects never inline.
- Integration test: duplicate webhook for same `transId` results in 200 with no side effect duplication.
- Integration test: cancelled-order webhook with `PAID` status returns 200 (per spec) but does not transition state and logs a warning.

## Related

- Patterns: §A.14 error classification, §A.15 provider adapter, §A.20 idempotent webhooks
- Roles: `docs/architecture/roles/payment-provider.md` (to be authored), `docs/architecture/roles/order.md`
- ADR 0004 (CountryConfiguration.DefaultPaymentProvider)
- ADR 0014 (refunds are audited as admin actions)
