---
role: PaymentProvider
kind: adapter
status: accepted
---

# PaymentProvider

## Responsibility

Initiate a payment session for an order, verify its status authoritatively, parse and verify webhook callbacks, and process refunds. Adapter pattern: one implementation per gateway, selected per country.

## Collaborators

- **Order** (reads: amount, currency, customer contact, order number)
- **CountryConfiguration** (reads: credentials key, test-mode flag)

## Knows

- How to talk to its specific gateway (Comgate at launch; future Stripe, Adyen)
- How to map gateway responses to `PaymentState`
- How to classify errors (`Transient | Permanent | Configuration | Unknown`)

## Does NOT know

- How the order was created, who the customer is beyond email, what happens after payment succeeds
- Invoice generation, payout logic, fee calculation
- The order's state machine

## Interface

See ADR 0016 for the C# interface. Methods:
- `CreatePaymentAsync(Order)` → `PaymentSession (ProviderRef, RedirectUrl)`
- `VerifyPaymentAsync(providerRef)` → `PaymentStatus`
- `ParseAndVerifyWebhookAsync(HttpRequest)` → `WebhookPayload` — **T-0066 implemented**; three-layer security: IP allowlist + re-fetch + idempotency. No HMAC body signature verification (Comgate doesn't sign; sanctioned defence is IP + re-fetch + ref-mismatch checks).
- `RefundAsync(providerRef, amount)` → `RefundReceipt`

## Implementations

- **ComgatePaymentProvider** (`Infra.Clients/Comgate/`) — CZ launch
- Future: StripePaymentProvider, AdyenPaymentProvider

Registered as keyed scoped services. Resolved via `IPaymentProviderFactory.ResolveAsync(countryCode)` which reads `CountryConfiguration.DefaultPaymentProvider`.

## Invariants

- Webhook verification always re-fetches status from the gateway. Body alone is never trusted.
- Adapter never mutates the order. State transitions happen via Mediator commands inside the application layer.
- Adapter never writes to the database. Caller persists if needed.

## Implementation pointer

Interface: `backend/src/Makables.Core.Domain/Payments/IPaymentProvider.cs`. Comgate impl: `backend/src/Makables.Infra.Clients/Comgate/`.

## Related

- ADRs: 0004, 0016 (this role's defining ADR)
- Roles: `order`, `country-configuration`
