---
role: ManualOrderTransitionPolicy
kind: domain-service
status: accepted
---

# ManualOrderTransitionPolicy

## Responsibility

Pure, dependency-free allow-list deciding which order state transitions the admin escape hatch (`ChangeOrderStateManually.Command`, T-0107) may take manually, and which are blocked — with the blocking error code naming the sanctioned command instead. The tool is deliberately NOT a free-form state setter (user-locked Q4).

## Collaborators

- **Order** (the handler routes every allowed pair to an existing semantic domain method — `MarkAsPaid`, `Cancel`, `Accept`, `RevertAcceptance`, `MarkAsDelivered`; the entity's own guards stay as defence-in-depth)
- **ChangeOrderStateManually.Command** (admin host; `IAdminAuditableCommand` — mandatory ≥10-char reason → audit `Notes`; no outbox, no emails)
- **Sanctioned commands** it points to: `RefundOrder` (T-0105), `OpenDispute`/`ResolveDispute` (T-0106), `MarkPayoutBatchCompleted` (T-0103)

## Knows

`Evaluate(OrderState from, OrderState to, bool hasPaymentProviderRef)` returns a decision record (Allowed + route discriminator, or Blocked + error code). Deterministic precedence: same-state NoOp → from-Refunded `notAllowed` → from-Disputed `useResolveDispute` → to-Refunded `useRefundOrder` → to-Disputed `useOpenDispute` → Delivered→Completed `useMarkPayoutBatchCompleted` → Paid|Accepted→Cancelled `useRefundOrder` → allow-list pair (to-Paid additionally requires the provider ref) → `notAllowed`.

### The allow-list (Q4 corollary — the contract; the policy implements exactly this)

| From | Permitted manual targets | Routing |
|---|---|---|
| PendingPayment | Paid (only if `PaymentProviderRef` non-null — lost-webhook recovery); Cancelled (manual expiry) | `MarkAsPaid(clock, existing ref)`; `Cancel(clock, Admin)` |
| Paid | Accepted (redo after un-accept / maker unreachable) | `Accept(clock)` |
| Accepted | Paid (undo maker mis-click) | `RevertAcceptance(clock)` — NEW domain method |
| Shipped | Delivered (carrier-blind delivery) | `MarkAsDelivered(clock, AdminManual)` |
| Delivered | — (`Completed` reserved for T-0103 payout pipeline) | blocked: `useMarkPayoutBatchCompleted` |
| Completed | — (refund path = T-0105 with Q5 acknowledgement) | blocked |
| Cancelled | — (terminal; resurrect = new order) | blocked |
| Refunded | — (terminal, never out) | blocked: `notAllowed` |
| Disputed | — (resolution = T-0106) | blocked: `useResolveDispute` |

All pairs not listed as permitted are blocked. `Paid → Cancelled` and `Accepted → Cancelled` are blocked with `useRefundOrder` — money is captured; cancelling without refunding strands customer funds.

### The never-rules (user-locked Q4, non-negotiable)

- Never manually → `Paid` without `PaymentProviderRef` present on the row (`order.manualTransition.paidRequiresProviderRef` — without a ref there is no payment to point at; marking Paid would fabricate revenue with no provider trail and permanently break T-0105's refund path for that order).
- Never → `Refunded` (sanctioned: `RefundOrder` T-0105 — a manual flip without the Comgate refund call silently desynchronizes money state).
- Never FROM `Refunded` (terminal).
- Never → `Disputed` (sanctioned: `OpenDispute` T-0106 — a state flip without a `Dispute` row breaks resolution) and never FROM `Disputed` (sanctioned: `ResolveDispute`).
- Mandatory non-empty reason (≥10 chars — forces a real sentence; max 2000).
- Same-state target = Silent Success (no mutation; audit row records identical before/after with the reason).

### Blocked-code naming convention

Every blocked transition that has a sanctioned command returns a code **naming that command** (US-admin-0010 AC-2 — the hint saves the admin a support ticket); only sanctionless blocks use the generic code. The six `BusinessErrorMessage` codes, each with a parallel `cs-CZ` i18n key:

- `order.manualTransition.notAllowed`
- `order.manualTransition.useRefundOrder`
- `order.manualTransition.useOpenDispute`
- `order.manualTransition.useResolveDispute`
- `order.manualTransition.useMarkPayoutBatchCompleted`
- `order.manualTransition.paidRequiresProviderRef`

## Does NOT know

- Persistence, MediatR, HTTP — pure static logic in `Core.Domain`, table-driven-testable with no mocks
- Who is calling — `[Authorize]` admin-audience gate + fail-closed session check live in the host/handler
- The entity's own transition guards — the policy is the manual-tool layer ON TOP of them; if they ever drift, the entity refusal surfaces as a Conflict (safe)

## Lifecycle

- **Created by:** n/a (static class, no state)
- **Modified by:** any ticket that adds an `OrderState` value or a sanctioned command MUST reclassify the matrix — the exhaustive `OrderState × OrderState` test fails until every pair is classified

## Invariants

- Exactly 5 (from, to) pairs are Allowed (plus same-state NoOp); everything else is Blocked — pinned by the exhaustive matrix test
- Every Allowed pair routes to a semantic domain method that carries its side effects (timestamps, sources, set-once guards); there is no generic `State = target` setter on `Order`
- Blocked and validation-failed requests write no audit row; successful ones write exactly one (`action_code = "order.manualStateChange"`, before/after JSONB, `notes` = reason)

## Implementation pointer

- `backend/src/Makables.Core.Domain/Orders/ManualOrderTransitionPolicy.cs` (policy + nested decision record)
- `backend/src/Makables.Core.AppServices/Features/Orders/ChangeOrderStateManually.cs` (handler routing)
- `backend/src/Makables.Tests/Domain/Orders/ManualOrderTransitionPolicyTests.cs` (red-first table-driven matrix)

## Related

- Roles: `order` (the entity edges + `RevertAcceptance` + `OrderDeliverySource.AdminManual`), `dispute` (sanctioned dispute commands), `admin-audit-log-entry`
- ADRs: 0014 (admin audit pipeline)
- Tickets: T-0107 (this policy), T-0105/T-0106/T-0103 (the sanctioned commands), T-0118 (admin UI)
- Stories: US-admin-0010
