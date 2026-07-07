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
- `ParseAndVerifyWebhookAsync(HttpRequest)` → `WebhookPayload` — **T-0066 implemented**; three-layer security: IP allowlist + re-fetch + idempotency. No HMAC body signature verification (Comgate doesn't sign; sanctioned defence is IP + re-fetch + ref-mismatch checks). `WebhookPayload` carries `(ProviderRef, PaymentState, PaymentMethod?, PaidAt?)` — T-0067 widened the record with the nullable `PaidAt` so the customer-facing invoice records the gateway's authoritative capture timestamp instead of our webhook-receive moment.
- `RefundAsync(providerRef, amount)` → `RefundReceipt`

## Implementations

- **ComgatePaymentProvider** (`Infra.Clients/Comgate/`) — CZ launch; single-merchant model (ADR 0016). Stays registered as an inactive fallback after cutover per ADR 0027.
- **StripeConnectPaymentProvider** (`Infra.Clients/Stripe/`) — marketplace-escrow model per ADR 0027 ("B-tok 3"); T-0142. Uses Stripe Connect Express + separate charges and transfers (PaymentIntent captured on the platform account; `ReleaseFundsAsync` explicitly transfers to the maker's connected account, only on the platform's instruction).
- Future: AdyenPaymentProvider, MangopayPaymentProvider (named fallbacks in ADR 0027 if Stripe fails KYC/fee/ČNB verification)

Registered as keyed scoped services. Resolved via `IPaymentProviderFactory.ResolveAsync(countryCode)` which reads `CountryConfiguration.DefaultPaymentProvider`.

### Fifth method (ADR 0027): `ReleaseFundsAsync`

`ReleaseFundsAsync(payoutAccountRef, amountMinor, currency)` → `TransferReceipt`. Transfers previously-captured funds from the platform's gateway balance to a maker's connected/payout account. Takes a plain string account reference — the role still does **not** need to know about `Maker` as an aggregate (RDD boundary preserved). Comgate's implementation throws `NotSupportedException` (no connected-account concept exists in Comgate's single-merchant model) — same precedent as `RefundAsync`/`ParseAndVerifyWebhookAsync` throwing until their owning tickets shipped.

Called only from the weekly `PayoutBatch` claim path (see `payout-batch.md`), never directly from a customer- or maker-facing handler.

## Invariants

- Webhook verification always re-fetches status from the gateway. Body alone is never trusted.
- Adapter never mutates the order. State transitions happen via Mediator commands inside the application layer.
- Adapter never writes to the database. Caller persists if needed.

## Implementation pointer

Interface: `backend/src/Makables.Core.Domain/Payments/IPaymentProvider.cs`. Comgate impl: `backend/src/Makables.Infra.Clients/Comgate/`.

## Related

- ADRs: 0004, 0016 (this role's defining ADR for Comgate), 0027 (amends — adds `ReleaseFundsAsync`, Stripe Connect implementation, marketplace-escrow money flow)
- Roles: `order`, `country-configuration`, `payout-account-provider` (sibling adapter role — maker-scoped onboarding, deliberately separate from this order-scoped role), `payout-batch` (caller of `ReleaseFundsAsync`)
