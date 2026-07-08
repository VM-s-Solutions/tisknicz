---
role: PayoutAccountProvider
kind: adapter
status: accepted
---

# PayoutAccountProvider

## Responsibility

Onboard a maker onto the payment gateway's connected/sub-account (KYC), and report that account's current payout-readiness state. Maker-scoped, not order-scoped — a deliberately separate role from `PaymentProvider`.

## Collaborators

- **Maker** (reads: id, to correlate the onboarding link + resulting account ref; writes back nothing directly — see below)
- **CountryConfiguration** (reads: which provider code is active, so the same country-selection seam as `PaymentProvider` resolves this factory too)

## Knows

- How to start its gateway's hosted onboarding flow for a given maker (Stripe Connect Express account-link creation)
- How to translate the gateway's account-status shape into `PayoutAccountState` (`NotStarted | PendingRequirements | Enabled | Disabled`)
- How to verify and parse the gateway's account-status webhook (e.g. Stripe `account.updated`)

## Does NOT know

- Anything about `Order`, payment sessions, or money movement for a specific transaction — that is `PaymentProvider`'s job
- Why a maker is being gated (product-visibility rules, order-acceptance rules) — it only reports state; the `Maker` aggregate and its callers decide what the state means for catalog/order behavior
- The payout-batch release logic — it never calls `ReleaseFundsAsync`; that lives on `PaymentProvider`, invoked by `PayoutBatch`

## Interface

```csharp
public interface IPayoutAccountProvider
{
    string Code { get; } // "stripe"

    Task<BusinessResult<OnboardingLink>> CreateOnboardingLinkAsync(
        string makerId, string returnUrl, string refreshUrl, CancellationToken ct);

    Task<BusinessResult<PayoutAccountStatus>> GetAccountStatusAsync(
        string payoutAccountRef, CancellationToken ct);
}

public sealed record OnboardingLink(string PayoutAccountRef, string Url, DateTimeOffset ExpiresAt);
public enum PayoutAccountState { NotStarted, PendingRequirements, Enabled, Disabled }
public sealed record PayoutAccountStatus(PayoutAccountState State, string? DisabledReason);
```

Registered as a keyed scoped service per provider code, resolved via a new `IPayoutAccountProviderFactory.ResolveAsync(countryCode)` reading `CountryConfiguration.DefaultPaymentProvider` — the same provider code selects both this interface and `IPaymentProvider` for a given country (one gateway implements both facets; Comgate implements neither, so a CZ-on-Comgate deployment never resolves this factory).

## Implementations

- **StripeConnectPayoutAccountProvider** (`Infra.Clients/Stripe/`) — Stripe Connect Express onboarding. T-0142.
- No Comgate implementation exists (Comgate has no connected-account/KYC concept). Country configs pointing at Comgate never resolve this factory.

## Invariants

- The adapter never writes `Maker.PayoutAccountStatus` directly. `GetAccountStatusAsync` is a pull-based read (used for reconciliation/support); the authoritative status update path is the webhook → `UpdateMakerPayoutAccountStatus.Command` → `Maker` mutation, exactly mirroring how `PaymentProvider`'s webhook never mutates `Order` directly (§A.20 discipline).
- `PayoutAccountRef` is assigned once, at first `CreateOnboardingLinkAsync` call, and never changes for a given maker (mirrors `Maker.RegistrationNumber`'s "set once at registration, never changes" invariant).
- A maker with `PayoutAccountStatus != Enabled` must not be able to publish products or accept new orders — enforced on `Maker`, not here (this role only reports the fact).

## Scenario walk (CRC check)

A maker completes Stripe's hosted onboarding form. Stripe sends `account.updated` with `charges_enabled: true, payouts_enabled: true`. The webhook controller calls `ParseAndVerifyWebhookAsync`-equivalent parsing (signature check first), then dispatches `UpdateMakerPayoutAccountStatus.Command(makerId, PayoutAccountState.Enabled)`. The command handler loads `Maker`, calls `maker.SetPayoutAccountStatus(Enabled)`, commits. The maker's dashboard next polls `GetMyMakerProfile` (unrelated existing query) and sees the account is enabled — no direct call from that query back into this role is needed, because the state already lives on `Maker`. This confirms the collaborator list is sufficient: `PayoutAccountProvider` never needed to know about products, orders, or the dashboard query — it only ever touched `Maker`'s identity (the id) and its own gateway's API.

## Implementation pointer

Interface: `backend/src/Makables.Core.Domain/Payments/IPayoutAccountProvider.cs` (T-0142). Stripe impl: `backend/src/Makables.Infra.Clients/Stripe/` (T-0142).

## Related

- ADRs: 0004, 0027 (this role's defining ADR)
- Roles: `payment-provider` (sibling adapter, order-scoped instead of maker-scoped), `maker`, `country-configuration`
