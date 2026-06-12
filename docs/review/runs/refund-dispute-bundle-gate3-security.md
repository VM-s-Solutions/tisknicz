# Gate 3 (Security) — refund-dispute bundle (T-0105 + T-0106 + T-0107)

**Branch:** `feat/refund-dispute-bundle` (6 commits, `abf5cb1..dfb731e`)
**Reviewer:** Security & DevOps agent
**Date:** 2026-06-12

## Verdict: GATE3_FOLD

One MEDIUM finding (check 3 — user free text now flows through the email substitution engine whose
own SECURITY comment forbids exactly that), foldable in-bundle with a small change to
`EmailSendService` + a template-authoring rule. Two LOW advisories. All authorization, IDOR,
invariant-bypass, secret-handling and audit checks pass.

---

## Check 1 — Refund authorization: PASS

- Admin host only: `backend/src/Makables.Web.Admin/Controllers/OrdersController.cs:24` (`[Authorize]`
  class-level), audience pinned at `backend/src/Makables.Web.Admin/Program.cs:9,16`
  (`MakablesHosts.Admin` → `AddMakablesAuth`); customer/maker JWTs fail audience validation (ADR 0013).
- Fail-closed session check before any work: `RefundOrder.cs:114-117`.
- Amount validated server-side: `Order.ValidateRefund` (`Order.cs:954-982`) — state allow-list
  (Paid/Accepted/Shipped/Delivered/Completed), `amountMinor > RemainingRefundableMinor` → 409
  `payment.refund.amountExceedsRemaining`, Completed requires explicit `AcknowledgePostPayout`.
  Pre-flighted at `RefundOrder.cs:144` BEFORE the provider call; `Order.Refund` re-runs the same
  predicate (cannot drift).
- No client-supplied currency: request body is `(AmountMinor, Reason, AcknowledgePostPayout)` only
  (`Web.Admin/Controllers/OrdersController.cs:28`); currency comes from the order snapshot
  (`RefundOrder.cs:158`). `PaymentProviderRef is null` → 409 before the provider (`:139-143`).
- `OrderState.Refunded` reachable ONLY via this command — see check 5.

## Check 2 — Comgate RefundAsync secret handling + response trust: PASS

- Secret discipline matches the CreatePayment precedent byte-for-byte:
  `ComgatePaymentProvider.cs:339-354` — secret appended LAST to the form body, never in the URL
  (POST), never in any log (all log statements use named properties: amount, currency, transId,
  operation label, Comgate code/message only). Exception paths (`CallComgateAsync:426-437`) log the
  exception + operation label; `HttpRequestException` carries no request body, so no secret leakage.
- Misconfigured credentials → `LogCritical` + Configuration-class error without echoing values
  (`MapComgateBusinessError:535-542`).
- Response trust model is sound: the refund is a synchronous outbound TLS call to a config-derived
  URL; the `code`-checked response of a call WE initiated is trustworthy — unlike inbound webhooks
  (which re-fetch, unchanged this bundle). Comgate's refund response carries `code`/`message` only;
  no refund id exists to re-verify against. Comgate additionally caps cumulative refunds at the
  captured amount gateway-side, so a duplicate submission cannot over-refund. Receipt timestamp from
  injected `IClock` (documented, `:317-324`).

## Check 3 — Party dispute endpoints: IDOR PASS; email injection MEDIUM (foldable)

**IDOR shield: PASS.**
- Customer: principal from session only (`OpenCustomerDispute.cs:82`), scoped load
  `GetByIdForCustomerAsync` with `o.Id == orderId && o.CustomerUserId == customerUserId` baked into
  SQL (`OrderRepository.cs:49-61`). Cross-tenant and unknown ids both → generic
  `order.notFound` (`OpenCustomerDispute.cs:92-96`). No enumeration oracle.
- Maker: JWT → `IMakerRepository.GetByUserIdAsync` → `GetByIdForMakerAsync` (`o.MakerId == maker.Id`,
  `OrderRepository.cs:63-75`); maker-JWT-without-maker-row also returns `order.notFound`
  (`OpenMakerDispute.cs:76-83`) — leaks nothing.
- `DisputeSource` stamped server-side in every variant; carrier-reserved categories rejected for
  parties (`OpenCustomerDispute.cs:55-56`, `OpenMakerDispute.cs:44-45`); admin variant accepts all
  six (documented §C.6).
- Description: max length enforced 3× (Validator 2000, `Dispute.Open` ArgumentException tail,
  `varchar(2000)` column via `DisputeConfiguration.cs:32-35`).

**MEDIUM (F1) — user free text now flows through the substitution engine that explicitly forbids it.**
`EmailSendService.SubstitutePlainTextPlaceholders` (`IEmailSendService.cs:827-838`) carries this
comment: *"The current callers feed only URL / timestamp / language tag values; revisit if that
changes."* This bundle changes it without revisiting: `["description"] = payload.Description`
(attacker-controlled customer/maker text, `IEmailSendService.cs:149`) and
`["resolution_notes"]` (`:193`) now flow through. Concrete consequences:

1. **Placeholder re-expansion (plain-text body + subject).** Substitution is a sequential
   `string.Replace` loop — a Description containing `{{language_code}}` (or any key iterated after
   `description`) gets expanded INSIDE the attacker's text. Injectable values are limited to other
   substitution values (our own URLs/ids), so impact is content spoofing in a plain-text email — LOW
   on its own, but it is an unescaped template engine processing hostile input.
2. **HTML path relies on out-of-repo convention.** The full substitution dict rides
   `EmailMessage.Data` → SendGrid dynamic-template data (`IEmailSendService.cs:714`). SendGrid
   Handlebars escapes `{{var}}` by default; HTML injection into the admin's mailbox occurs the day a
   template author uses triple-stache `{{{description}}}` (e.g. to preserve line breaks). Nothing in
   the repo prevents or documents this.
3. **Phishing-by-relay (inherent).** A customer can write "log in here: https://evil.example" in
   Description; the admin email relays it. Mitigation is presentation (label the block as
   user-submitted), not escaping.

**Fold F1:** neutralize placeholder sequences in substitution VALUES (or switch to single-pass
substitution) in `SubstitutePlainTextPlaceholders`; update the now-stale SECURITY comment; document
the SendGrid template rule (double-stache only for `description` / `resolution_notes`; render under
a "text from the customer/maker" label) in `docs/security/` (email-injection pattern) or the email
template seed doc.

## Check 4 — Dispute spam posture: PASS (Q-0011 noted, not blocking)

- Per-order double-open blocked at three layers: handler Silent-Success (`§C.4`), partial unique
  index `ux_disputes_order_open` (`UNIQUE(order_id) WHERE resolved_at IS NULL`,
  `DisputeConfiguration.cs:55-58`) which also decides the concurrent-open race, and the
  Disputed-without-row invariant is LogCritical + refuse (`OpenCustomerDispute.cs:142-151`).
- Breadth: a customer can open at most one dispute per OWNED order; each emits one admin email;
  blast radius is bounded by their own order count and freezes only their own orders. Acceptable at
  MVP volume. Rate limiting remains Q-0011 (open since order-cleanup Gate 3, check 13) — noted, not
  re-blocked here.

## Check 5 — T-0107 invariant bypass: PASS

`ManualOrderTransitionPolicy.Evaluate` (`ManualOrderTransitionPolicy.cs:90-150`), verified against
the matrix tests (208 lines, exhaustive over `OrderState × OrderState`):
- Manual → Paid without `PaymentProviderRef`: impossible — both `PendingPayment→Paid` (`:127-130`)
  and `Accepted→Paid` (`:138-141`) gate on `hasPaymentProviderRef`, else
  `order.manualTransition.paidRequiresProviderRef`. No revenue fabrication path.
- → Refunded blocked (`:107-108`, names RefundOrder); FROM Refunded blocked (`:99-100`, terminal);
  FROM Disputed blocked (`:103-104`, names ResolveDispute); → Disputed blocked (`:111-112`);
  Delivered→Completed blocked (`:116-117`, payout integrity); Paid/Accepted→Cancelled blocked
  (`:121-122`, stranded funds → names RefundOrder).
- No generic state setter: every allowed pair routes to the semantic domain method
  (`ChangeOrderStateManually.cs:105-121`) so timestamps, sources (`OrderCancellationSource.Admin`,
  `OrderDeliverySource.AdminManual`) and set-once guards apply; `RevertAcceptance` clears
  `AcceptedAt` (`Order.cs:624-633`). Entity guards remain as defence-in-depth (`:122-127`).

## Check 6 — Admin audit completeness + "system" fallback: PASS (judgment: precedent-consistent)

- All three commands implement `IAdminAuditableCommand` with mandatory notes: RefundOrder
  (reason 1–2000 + post-payout marker), OpenDispute (description), ResolveDispute (notes),
  ChangeOrderStateManually (reason ≥10 chars — forces a real sentence). Before/after JSONB rides
  `AdminAuditPipelineBehavior`; failures write no audit row and UoW rolls back.
- The `?? "system"` fallback (`AdminAuditPipelineBehavior.cs:60`): **unreachable for this bundle.**
  All four admin handlers fail closed on an empty session BEFORE any mutation (`RefundOrder.cs:114`,
  `OpenDispute.cs:76`, `ResolveDispute.cs:106`, `ChangeOrderStateManually.cs:80`), and `[Authorize]`
  on the admin host guarantees a principal. This matches the VerifyMaker/T-0034 precedent — not a
  blocker. **Advisory (A1):** hoist fail-closed into the behavior itself for every
  `IAdminAuditableCommand` so the guarantee stops depending on per-handler discipline.
- Nested `RefundOrder.Command` from ResolveDispute (`ResolveDispute.cs:192-198`) re-runs the full
  pipeline → second audit row (`order.refund`) with the same admin id. Good.

## Check 7 — ADMIN_NOTIFICATION_EMAIL: PASS

- Read at SEND time, never baked into the payload (`IEmailSendService.cs:121-134`); missing →
  `LogCritical` (order id only, no PII) + Configuration-class
  `email.adminRecipientNotConfigured` (`BusinessErrorMessage.cs`) — the outbox row parks visibly and
  retries after the fix (ADR 0020). No silent drop; dispute open never blocked.
- Binding: `Email` section + raw `ADMIN_NOTIFICATION_EMAIL` env override
  (`AddMakablesInfrastructure.cs:225-240`); deliberately not `ValidateOnStart` (documented in
  `EmailOptions.cs`). Not a secret; not logged with PII.

## Check 8 — Outbox payload PII: PASS

- `OrderDisputedAdminEmailPayload`: order ids, dispute id, category, description, source, language,
  admin action URL — NO customer email/name/phone (data minimization holds; description is necessary
  for triage).
- `OrderRefundedCustomerEmailPayload` / `OrderDisputeResolvedCustomerEmailPayload`: carry
  `order.ContactEmail` + `ContactName` — the recipient's own snapshot, per the established
  T-0067/T-0083 precedent. No maker PII, no payment refs.

## Check 9 — Webhook surface: PASS (unchanged)

No new inbound webhooks, no new `[AllowAnonymous]`. `DisputeShipment` is dispatched only by the
Functions host (carrier sweep), now wired to the real dispute domain with server-side
`DisputeSource.Carrier` and a canned description (no external input in the email path beyond the
carrier ref). Comgate refund is outbound-only.

## LOW advisories (no fold required)

- **A2 — concurrent partial-refund record drift.** `Order` has no optimistic-concurrency token; two
  racing partial refunds can each load `RefundedAmountMinor=0` and last-write-wins under-records the
  cumulative total in the DB (never over-refunds — Comgate caps gateway-side; reconcilable from the
  Comgate console). Single-admin MVP makes this unlikely; revisit with T-0102 ledger work.
- **A3 (UX, noted in passing for PM):** the Refunded resolution outcome sends the customer two
  emails (dispute-resolved + order-refunded). Not a security issue.

## Fold list

| # | Severity | Item | Where |
|---|----------|------|-------|
| F1 | MEDIUM | Neutralize `{{` in substitution values (or single-pass substitution); update the stale SECURITY comment; document the SendGrid double-stache rule + "user-submitted content" labeling for `description`/`resolution_notes` | `backend/src/Makables.Core.AppServices/Features/Email/IEmailSendService.cs:827-838` + `docs/security/` |

Advisories A1/A2 may land as backlog tickets; they do not block this bundle.
