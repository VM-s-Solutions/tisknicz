---
id: 0027
title: Marketplace-escrow payments via Stripe Connect Express — supersedes the Comgate-only launch model
status: accepted
date: 2026-07-07
deciders: [Architect]
living_docs: [docs/architecture/extension-points.md, docs/architecture/money.md]
---

# 0027 — Marketplace-escrow payments via Stripe Connect Express

## Context

Q1 of the 2026-07-04 business-decision meeting (`docs/meetings/dopady-rozhodnuti-na-platformu.md` §1, §2.1, §4 open-question 4) locks a business-model pivot: **"B-tok 3"** — a marketplace/escrow model where a licensed payment gateway holds the customer's payment in the maker's own sub-ledger ("wallet") from the moment of capture, and releases (transfers) it to the maker **only on the platform's instruction**, after delivery. This is a change to the *money flow*, not to the platform's legal framing (Q2 = B2: the maker remains seller of record to the customer per the ToS "in the name and on behalf of the maker" clause — already the ToS framing before this ADR).

Today (ADR 0016) Comgate is a single-merchant gateway: customer money lands directly in the JVM YORE bank account; weekly payouts are a CSV the operator wires by hand (T-0101–T-0104); refunds go through `IPaymentProvider.RefundAsync` (T-0105). Comgate has no concept of a maker-scoped sub-account, so it structurally cannot implement B-tok 3 — Comgate cannot hold funds "in the maker's wallet." A gateway with **marketplace/connected-account primitives** is required.

Q17 already lists **Stripe** as an approved data processor. §2.1 names Stripe Connect as the practical fit, with Mangopay / Adyen for Platforms as fallbacks if Stripe doesn't fit. §5.4 lists the items that must be verified before implementation starts (fees, hold-until-delivery support, KYC pass rates for small Czech sole traders, whether this triggers a ČNB registration duty for JVM YORE) — this architect does not have live Stripe account access, a signed fee schedule, or a legal opinion, so those items are carried forward as **explicit assumptions requiring human/legal confirmation**, not fabricated numbers.

The existing architecture is deliberately built for this swap: `IPaymentProvider` is a keyed adapter (ADR 0016, patterns §A.15) selected via `CountryConfiguration.DefaultPaymentProvider`. This ADR's job is to decide *how* Stripe Connect fits that seam, and where the seam needs to grow.

**Applies to:** backend only. No frontend contract changes are decided here (T-0142 will need a new Stripe.js Checkout/Payment Element client mount plus a Stripe Connect onboarding-link redirect — all still driven by `lib/api-client/`, no direct browser→Stripe business logic per CLAUDE.md; frontend shape choices are left to T-0142 since implementation is out of scope for this spike).

## Decision

### Gateway pick: Stripe Connect, **Express** account type, **separate charges and transfers**

**Stripe Connect Express** is the recommended implementation, for these reasons:

1. It is the only one of the three candidates already on the approved-processor list (Q17) — no new DPA/vendor-onboarding cycle before a spike can even start integrating.
2. Express accounts are the lightest-weight Connect onboarding for individual/small-sole-trader makers (hosted Stripe onboarding UI, Stripe carries most of the KYC UX and compliance burden) — the best fit for "makeři budou plátci i neplátci," many likely first-time-online-seller sole traders (Q4).
3. Stripe's **separate charges and transfers** model (as opposed to *destination charges*) is the one Stripe itself documents for marketplaces that need to **hold funds and release them on a later, explicit instruction** — which is exactly B-tok 3's "uvolní je až po doručení na pokyn platformy." Concretely:
   - The customer's `PaymentIntent`/charge is created **on the platform's Stripe account** — the money lands in the **platform's Stripe balance**, not the maker's, at capture time.
   - No transfer to the connected (maker) account happens automatically. The platform creates an explicit `Transfer` object to the maker's connected account **only when we decide to release** (on `Order.Delivered`, batched to the weekly cadence per Q12).
   - This is a materially different mechanic from *destination charges* (`transfer_data.destination` on the PaymentIntent), where Stripe moves funds to the connected account **at capture time** by default — that model does not naturally support "hold until we say so" without additional scheduling gymnastics. Separate charges and transfers gives us that hold for free, at the cost of us owning the transfer trigger (which we already need to own anyway, to enforce the delivery gate).
4. Comgate stays wired as an inactive keyed adapter (§2.1 point 6) — no code deletion, just a `CountryConfiguration.DefaultPaymentProvider` seed flip when we cut over.

**Fallback path if Stripe fails verification** (see "Assumptions requiring confirmation" below): Mangopay or Adyen for Platforms, both of which have first-class "hold in seller's wallet, release on marketplace instruction" primitives (arguably a closer semantic fit to "peněženka" than Stripe's balance model) and are named as acceptable in Q17. If Stripe fails the CZ-sole-trader KYC pass-rate check or the ČNB-duty check, this ADR should be revisited rather than silently reworked — that is a new proposed ADR, not a patch to this one.

### Money flow

**Merchant of record.** Under separate-charges-and-transfers, the **PaymentIntent is created on the platform's Stripe account** — for Stripe's own risk/underwriting purposes, JVM YORE (the platform account) is the charging party. This is a point of tension with the ToS framing (Q2/B2: maker is seller of record to the customer) that **must be reconciled by legal counsel** — see assumptions below. It does not block the technical design: the customer invoice already carries the maker's identity as issuer (T-0143, separate ADR-track); Stripe's internal accounting of "who charged the card" is a distinct question from "who is the seller under Czech consumer law," and marketplaces commonly separate the two. Flagged, not resolved, here.

**Release trigger.** `Order.State → Delivered` (already the trigger for the payout-eligibility scan per `PayoutBatch`, §A.19/A.20 precedent) is the earliest a release instruction is *eligible*. To preserve Q12's weekly payout rhythm and the `PayoutBatch` aggregate's existing shape (reporting, admin visibility, exclusion tracking), the actual `Transfer` calls are issued **per claimed order inside the weekly batch claim**, not immediately on delivery — this changes *what a batch does* (§ below) but not *when a batch runs*.

**Dispute (our platform `Dispute`, T-0106).** Opening an internal dispute on `Paid | Accepted | Shipped | Delivered` already detours the order to `Disputed` (patterns §A.22) — a `Disputed` order is **not** `Delivered`, so it is already excluded from the payout-eligibility scan by the existing state predicate (no code change needed; this is the same "sweep exclusion by definition" property the `dispute.md` role documents for the auto-deliver/carrier sweeps). Effect: opening a dispute **automatically extends the hold** — the transfer simply never gets created while the order sits in `Disputed`. On `Resumed`, the order restores to its pre-dispute state and (if that state was `Delivered`) rejoins the next batch. On `Refunded`, the nested `RefundOrder.Command` runs *before any transfer exists* in the common case (order was never `Delivered` when disputed, or was disputed same-batch-week) — refunding straight off the platform's Stripe balance via `Refund` is trivial, no connected-account interaction needed. **Edge case requiring T-0142 design attention:** an order disputed *after* its batch already transferred funds to the maker — a post-payout refund. Q10 already rules post-payout refunds as a **manual admin process** (deduct from the maker's next payout, outside the system) — this ADR does not change that; T-0142 must NOT attempt an automatic reversing `Transfer` from the connected account back to the platform (that call can fail on insufficient connected-account balance and is exactly the kind of automation Q10 declined to build).

**Card-network disputes (chargebacks) — distinct from our `Dispute` entity.** Stripe delivers these via `charge.dispute.created`/`.updated`/`.closed` webhooks against the platform's own charge. This is a **new webhook surface**, unrelated to the existing `ComgateWebhookController` shape (different provider, different event grammar) but following the same idempotency discipline (§A.20): verify Stripe's webhook signature, look up the order by `PaymentProviderRef`, and — because the charge lives on the platform account — the platform's Stripe balance is debited/held automatically by Stripe, independent of whether we've transferred to the maker yet. Policy: if no transfer has happened yet for that order, block the order from entering a future payout batch until the Stripe dispute resolves (mirrors the internal-dispute hold, just gateway-driven instead of order-state-driven); if a transfer already happened, the loss falls to the manual post-payout process (same Q10 boundary). **T-0142 must design the correlation + hold mechanism explicitly — do not assume it falls out of the existing `Dispute` entity, which models customer↔maker disputes, not card-network chargebacks.**

**Maker's KYC revoked mid-flow.** Stripe delivers `account.updated` webhooks carrying `requirements.disabled_reason` (e.g. rejected, past-due requirements). Policy:
- **New maker gate** (already scoped by §2.1 point 2): a maker with a non-enabled payout account cannot publish products or accept new orders. This is enforced the same way `IsVerified` already gates catalog visibility (`maker.md` role) — add a payout-readiness check alongside it, not a replacement for it.
- **In-flight orders when KYC is revoked**: a `Transfer` call to a disabled connected account fails at the Stripe API. This is a `ErrorType.Configuration`-classified failure per patterns §A.14 (not `Transient` — retrying an API call against a disabled account will not succeed on its own) — it alerts ops, not an automated retry loop. The affected order is **excluded from the batch** and its exclusion is counted, mirroring the existing `PayoutBatch.ExcludedNoBankAccountOrderCount` precedent (a new `ExcludedPayoutAccountNotReadyOrderCount`, same shape). It rides the next batch once the maker's account is re-enabled, or is resolved manually by admin (refund the customer) if the maker's account is never restored — this is the same escalation shape §2.7's three-tier sanction ladder already anticipates, just for a payment-specific trigger instead of a behavioral one.

### Codebase shape changes (for T-0142, not built here)

1. **New keyed `IPaymentProvider` implementation**, `StripeConnectPaymentProvider`, registered `services.AddKeyedScoped<IPaymentProvider, StripeConnectPaymentProvider>("stripe")` (patterns §A.15 — zero change to the interface's existing four methods' *shape*, but see #2). `ComgatePaymentProvider` stays registered under `"comgate"`; only the CZ `CountryConfiguration.DefaultPaymentProvider` seed value changes when we cut over.

2. **`IPaymentProvider` grows one method**, following the same "not every provider supports every method" precedent already established for `RefundAsync`/`ParseAndVerifyWebhookAsync` (which threw `NotSupportedException` in Comgate's early T-0065 shape until T-0066/T-0105 landed):

   ```csharp
   Task<BusinessResult<TransferReceipt>> ReleaseFundsAsync(
       string payoutAccountRef,   // the maker's connected-account id, e.g. Stripe "acct_..."
       long amountMinor,
       string currency,
       CancellationToken cancellationToken);

   public sealed record TransferReceipt(
       string TransferProviderRef,
       DateTimeOffset TransferredAt);
   ```

   `ComgatePaymentProvider.ReleaseFundsAsync` throws `NotSupportedException` (Comgate has no connected-account concept; the CSV-batch path never calls it — see #4). This keeps `IPaymentProvider`'s "Does NOT know" boundary intact: the method takes a plain string account reference, not a `Maker`, so the payment-provider role still does not need to know about the `Maker` aggregate.

3. **New adapter role + interface: `IPayoutAccountProvider`** (maker-scoped onboarding — deliberately **not** folded into `IPaymentProvider`, which is order-scoped; see role file below for the RDD walk-through):

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

   Registered the same keyed-service way, resolved via a new `IPayoutAccountProviderFactory` reading the same `CountryConfiguration.DefaultPaymentProvider` (one provider code drives both interfaces for a given country — Stripe implements both; Comgate implements neither, so CZ-on-Comgate never resolves this factory).

4. **New `Maker` fields** (mirrors the existing `BankAccount` field precedent in `maker.md`):
   - `PayoutAccountRef` (`string?`, nullable — Stripe connected-account id, e.g. `acct_1AbCdEfG...`)
   - `PayoutAccountStatus` (`PayoutAccountState`, default `NotStarted`)
   - `Maker.CanAcceptOrders` gains a new precondition: `PayoutAccountStatus == Enabled` (alongside the existing `IsVerified`/`IsActive`/`EmailConfirmedAt` checks) — the exact gate boundary (does "cannot publish" also mean "existing published products get hidden," or just "no *new* orders") is a T-0142 design decision, not fixed here; flagged so it isn't accidentally decided by whichever handler happens to check it first.
   - Set by a new `UpdateMakerPayoutAccountStatus` internal command, invoked from the `account.updated` webhook handler — never client-settable; the maker only ever *initiates* onboarding (gets a redirect URL), Stripe is the sole authority on account status.

5. **`PayoutBatch` reshaped from "CSV batch" to "batch of release instructions"** (role file amended below). Two provider-driven behaviors coexist behind the same aggregate, selected by which `IPaymentProvider`/`IPayoutAccountProvider` pair is active for the country — this is not a new `if (country == "CZ")` branch, it is the existing provider-selection seam doing its job:
   - **Comgate-active countries** (none once CZ cuts over, but the code path is not deleted): batch generation still produces the bank-transfer CSV exactly as today (`IPayoutCsvFormatter`, unchanged).
   - **Stripe-active countries**: batch generation, per claimed order, calls `IPaymentProvider.ReleaseFundsAsync(maker.PayoutAccountRef, order.MakerPayoutAmountMinor, order.Currency, ct)` and records the resulting `TransferReceipt.TransferProviderRef` against the order (new nullable `Order.PayoutTransferProviderRef` column, same shape as `PaymentProviderRef`). `PayoutBatch.Complete` (admin marks the batch settled) becomes a **confirmation step, not the trigger** — the transfers already happened at claim time; `Complete` records that ops reconciled the batch against Stripe's own payout/statement, replacing today's "bank wire executed" semantics with "Stripe transfers reconciled." The set-once `CsvBlobPath` field stays null for Stripe-mode batches (nothing to attach); a parallel `TransferInstructionCount`/failure-count summary is the natural analogue, mirroring the existing `ExcludedNoBankAccountOrderCount` pattern rather than inventing a new shape.
   - New exclusion count: `ExcludedPayoutAccountNotReadyOrderCount` (see "KYC revoked" above).
   - **This is the one piece of this ADR most likely to need its own follow-up design pass at T-0142 implementation time** — reshaping an existing aggregate's core behavior per active provider is more invasive than "add a new adapter," and deserves its own review gate rather than being waved through as "just another keyed service."

6. **Refunds (`RefundOrder.Command`, T-0105)** — no interface change. `IPaymentProvider.RefundAsync` already exists; the Stripe implementation calls Stripe's `Refund` API against the original PaymentIntent. Pre-payout refunds (funds still on the platform balance) work exactly like Comgate's today from the command's point of view. Post-payout refunds stay the existing Q10 manual process — `RefundAsync` is simply not called for orders whose `PayoutTransferProviderRef` is already set; that guard is new logic in `RefundOrder.Handler`, not a gateway concern.

7. **`OrderPricing`** — **unchanged**. The platform-fee calculation already lives in `CountryConfiguration.PlatformFeeRateBp` → `OrderPricing`, snapshotted onto the order at creation (ADR 0004, §2.2 in the meeting doc confirms this explicitly: "OrderPricing čte sazbu už dnes z configu, snapshot na objednávce zůstává — změna je bezpečná"). Under separate-charges-and-transfers, the platform fee is realized simply by transferring *less than the full order total* (`MakerPayoutAmountMinor`, already the exact snapshot field) — there is no Stripe `application_fee_amount` concept to wire, because we are not using destination charges. This is a genuine simplification versus a naive "use Stripe application fees" design.

### Assumptions requiring human/legal confirmation before T-0142 starts (do not fabricate — verify)

These are carried forward from dopady §5.4 unresolved, not answered by this ADR:

1. **CZ card-acceptance fees** (Stripe's published rate vs. Comgate's) — needed to confirm the unit economics still work at 7%/3.5% commission before committing engineering time.
2. **Hold-until-delivery mechanics actually behave as designed** — confirm in a live Stripe test account that separate-charges-and-transfers genuinely lets an arbitrary delay elapse between capture and `Transfer` creation with no forced-payout timer on the platform side (Stripe's *own* payout schedule to the platform's bank is separate from Connect transfers and should not interfere, but this needs a sandbox check, not a documentation read).
3. **Express-account KYC pass rate for small Czech IČO sole traders** — anecdotal risk that Stripe's automated identity checks reject a meaningful fraction of one-person Czech businesses; no data available to this architect. If pass rates are poor, this alone could be a Stripe-disqualifying finding and trigger the Mangopay/Adyen fallback.
4. **ČNB registration duty** — whether JVM YORE, by issuing release *instructions* to a licensed gateway (rather than itself holding client funds), avoids any Czech National Bank payment-institution registration duty. This is the single highest legal-risk item in this whole ADR and needs an explicit written legal opinion, not an architect's inference, before any implementation ships to production. (Reasoning for why this is *plausible* to resolve favorably, not a substitute for that opinion: Stripe Payments Europe, Ltd. is itself the licensed/regulated entity holding and moving the funds; JVM YORE calling Stripe's API to say "release now" is analogous to any other Stripe Connect platform operator, none of whom independently register as payment institutions — but "analogous to common practice" is not the same as "confirmed compliant for this specific business," especially given the "peněženka" framing in the business decision could be read as implying JVM YORE itself controls client funds.)
5. **Unverified/newly-onboarded connected-account transfer limits** — Stripe imposes payout caps on newly onboarded accounts before they build a processing history; needs confirmation this doesn't stall week-one payouts for early makers.

These five items are logged as **Q-0036** in `docs/questions/open.md`, owner `user` (external: Stripe partner/sales contact + legal counsel), resolve-by **pre-launch** — T-0142 (the implementation ticket) stays blocked on Q-0036 per its INDEX row, exactly as T-0141 already documented itself as the vehicle for these questions.

## Alternatives considered

- **Stay on Comgate, simulate escrow with an internal ledger** (JVM YORE keeps holding the money as today, but tracks a "released" flag internally and pays out via the existing CSV once "released"). Rejected: this does not satisfy Q1's actual requirement — the money must sit in a *licensed gateway's* custody in the maker's name, not in JVM YORE's own bank account under an internal bookkeeping fiction. It also does not reduce any regulatory exposure (arguably increases it, since JVM YORE would be the one holding client funds with zero gateway involvement in the hold).
- **Stripe Connect, destination charges + `transfer_data`** (funds move to the connected account automatically at capture, held there via Stripe's own manual-payout-schedule setting on the connected account). Rejected as the primary design: this pattern is meant for marketplaces that want money to *leave the platform account immediately* and only defer the *connected account's payout to its bank*, not defer the *transfer to the connected account itself*. It technically could be forced into a hold shape (`on_behalf_of` + manual payout schedule + not touching `transfer_data.amount` until ready), but that overloads a feature meant for a different problem and is harder to reason about for the dispute-hold-extension requirement (a chargeback against a destination charge automatically reverses the transfer from the connected account, which is not what we want while an internal `Dispute` is unresolved). Separate charges and transfers gives explicit control with fewer surprising automatic reversals.
- **Mangopay** (marketplace-native wallet model — literally names its holding construct a "wallet," arguably the closer semantic fit to "peněženka"). Not rejected outright — kept as the named fallback per Q17/§2.1. Not chosen as primary because Stripe is already the approved processor (Q17) with zero new vendor-onboarding lead time, and Stripe Connect Express's KYC UX is generally considered lower-friction for very small merchants than Mangopay's KYC flow — but this is exactly the kind of claim item 3 above needs to verify with real data, not architect intuition.
- **Adyen for Platforms.** Not rejected outright — kept as the second named fallback. Deprioritized versus Stripe for the same reason as Mangopay (not yet an approved processor; would add a vendor-onboarding cycle Stripe avoids), and Adyen's platform product historically skews toward larger, higher-volume marketplaces than Makables' CZ-only MVP scale — a soft signal, not a hard verified fact, and revisit if Stripe fails verification.

## Consequences

- Positive: preserves the adapter seam completely — `IPaymentProvider` and the new `IPayoutAccountProvider` are both keyed-service, country-selected interfaces; adding Stripe (or later, a second country's chosen gateway) never touches `Core.AppServices` handlers beyond the one new `ReleaseFundsAsync` call site inside the payout-batch claim path.
- Positive: eliminates a categorically new risk (JVM YORE holding customer funds directly) in favor of a licensed intermediary — this is very likely a *reduction* in regulatory exposure versus the status quo, pending the ČNB confirmation.
- Positive: the platform-fee/`OrderPricing` design already snapshots the fee on the order, so this migration touches zero pricing logic — pure realization-layer change.
- Negative: `PayoutBatch`, the most business-critical aggregate after `Order`, gains a second behavior mode (CSV vs. transfer-instruction) selected per active provider — real complexity, flagged explicitly above as needing its own T-0142 design review rather than being treated as "just another adapter."
- Negative: introduces a wholly new webhook surface (Stripe `account.updated`, `charge.dispute.created`, `payment_intent.*`) with its own signature-verification and idempotency discipline to build and test, on top of the existing Comgate webhook code (which is retained, not replaced).
- Negative / risk: five unverified assumptions (Q-0036) gate T-0142's start; if the ČNB item resolves unfavorably, this ADR would need a full reconsideration (not a patch), since the "instruction-only, gateway holds funds" framing is the crux of the whole design.

## Compliance / verification

- Reviewer: any new payment-related code lives in `Infra.Clients/Stripe/`, never a direct `HttpClient` call from a handler (patterns §A.15 / CLAUDE.md rule 10).
- Reviewer: `StripeConnectPaymentProvider` and the new `StripeConnectPayoutAccountProvider` never write to the database directly (payment-provider.md invariant, unchanged) and never mutate `Order`/`Maker` — state transitions happen via Mediator commands.
- Reviewer: `Maker.PayoutAccountStatus` is settable ONLY by the `account.updated` webhook-driven command — grep for any other write path and reject the PR.
- Reviewer: `RefundOrder.Handler` must guard on `Order.PayoutTransferProviderRef IS NULL` before calling `RefundAsync` — a missing guard means a post-payout refund silently attempts an automated path Q10 explicitly declined to build.
- Reviewer: every Stripe webhook handler follows §A.20 (signature verification first, idempotency lookup by `provider_ref`, side effects deferred to after commit) — same bar as the Comgate webhook, not a lesser one because it's "just Stripe's SDK."
- Integration test (T-0142): a claimed order whose maker's `PayoutAccountStatus != Enabled` at batch-claim time is excluded and counted in `ExcludedPayoutAccountNotReadyOrderCount`, never silently dropped.
- Integration test (T-0142): opening a `Dispute` on a `Delivered`-but-not-yet-batched order removes it from the next batch's claim set (already true by the existing state predicate — this test PINS that the new behavior doesn't accidentally regress it, since the payout claim logic is being touched by this change).
- Product/legal sign-off: Q-0036 items 1–5 answered in `docs/questions/open.md` before any `Infra.Clients/Stripe/` code is merged to a production-bound branch.

## Related

- Supersedes/amends: [ADR 0016](./0016-payments-comgate.md) — 0016's Comgate design stays valid for the Comgate adapter itself (still shipped, still keyed, still the CZ launch provider until cutover); its "single merchant account, CSV payout" *framing* is superseded by this ADR for any country whose `CountryConfiguration.DefaultPaymentProvider = "stripe"`.
- ADR 0004 (`CountryConfiguration` as the provider-selection control plane), ADR 0009 (payout batch numbering, unaffected), ADR 0013 (scoped repositories), ADR 0014 (admin-audited actions — `PayoutBatch.Complete` stays audited), ADR 0020 (background jobs / outbox — the weekly batch timer trigger is unchanged).
- Roles: `payment-provider` (amended), `payout-account-provider` (new), `payout-batch` (amended), `maker` (amended), `dispute` (referenced, unchanged).
- Patterns: §A.14 (error classification — `Configuration` for a disabled connected account), §A.15 (provider adapter — this ADR's whole shape), §A.20 (idempotent webhooks — the new Stripe webhook surface).
- Tickets: T-0141 (this spike), T-0142 (the now-unblocked implementation ticket — **must split before `ready`** per its own INDEX row; see "what T-0142 needs to build" below).
- Questions: Q-0036 (new — the five unverified assumptions), Q-0010/§5.3 (tax-advisor questions — separate track, T-0143).

## What T-0142 needs to build

Concrete starting list for whoever splits T-0142 (per its own "explicitly oversized, must split" flag):

1. **Slice — Stripe payment adapter + webhooks.** `StripeConnectPaymentProvider` (`CreatePaymentAsync`/`VerifyPaymentAsync`/`ParseAndVerifyWebhookAsync`/`RefundAsync`/`ReleaseFundsAsync`), registered `"stripe"`; new `Web.Public` webhook controller for `payment_intent.*` + `charge.dispute.*` (separate from the Connect-account webhook below); Stripe signature verification (`Stripe-Signature` header, Stripe's official verification helper — this is a real HMAC scheme, unlike Comgate, so the "re-fetch status" belt-and-braces pattern from ADR 0016 still applies but signature check comes first).
2. **Slice — Connect Express onboarding + KYC gate.** `IPayoutAccountProvider`/`IPayoutAccountProviderFactory` + `StripeConnectPayoutAccountProvider`; `Maker.PayoutAccountRef`/`PayoutAccountStatus` migration; onboarding-link endpoint (maker host); `account.updated` webhook → `UpdateMakerPayoutAccountStatus.Command`; extend the maker's publish/accept-orders gate.
3. **Slice — Payout-batch release-instruction rework.** `PayoutBatch` claim path branches per active provider (CSV formatter vs. `ReleaseFundsAsync` loop); new `Order.PayoutTransferProviderRef` column; new `ExcludedPayoutAccountNotReadyOrderCount`; `PayoutBatch.Complete` semantics updated to "reconciled," not "triggered."
4. **Slice — Refund guard.** `RefundOrder.Handler` gains the pre/post-payout branch (`PayoutTransferProviderRef IS NULL` check) before calling `RefundAsync`; no change to the Stripe refund call itself.
5. **Pre-work, not code:** confirm Q-0036 items 1–5 land in `docs/questions/open.md` before slice 1 starts building against a live Stripe account.
6. **Explicitly NOT in scope for T-0142** (per this ADR): automatic reversing transfers for post-payout refunds (Q10 stays manual); any change to `OrderPricing`/fee-rate calculation (unchanged, see Decision §7); any change to the customer-facing invoice issuer identity (that's T-0143's track).
