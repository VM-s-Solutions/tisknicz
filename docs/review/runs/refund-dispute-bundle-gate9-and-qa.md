# Refund-dispute bundle — Gate 9 + QA-plan authoring

> QA pass per the bundle workflow. Run date: 2026-06-12. Scope: branch
> `feat/order-cleanup-bundle` carrying T-0105 → T-0106 → T-0107 (the
> reviewer draft's naming note stands: review artifacts use
> **refund-dispute-bundle**; the branch name reuses `order-cleanup` —
> PM flagged in the draft, not re-litigated here).

## Part 1 — Gate 9 mechanical consistency

**Verdict: PASS.** `node scripts/check-consistency.mjs` → exit 0, output
`check-consistency: clean (125 tracked).` Matches the expected
118 (master re-key) + 6 T1 + 1 T5 = 125.

### Baseline diff audit (`docs/audits/consistency-violations.md` vs master)

`git diff master -- docs/audits/consistency-violations.md` shows exactly
**+7 added lines, 0 removed** — byte-for-byte the claimed set, nothing
else:

| # | Entry | Rule | False-positive class |
|---|---|---|---|
| 1 | `Features/Orders/ChangeOrderStateManually.cs:1` | T1 | one-file feature, static-class-wrapper heuristic (same class as the 50+ pre-existing T1 rows) |
| 2 | `Features/Orders/OpenCustomerDispute.cs:1` | T1 | same |
| 3 | `Features/Orders/OpenDispute.cs:1` | T1 | same |
| 4 | `Features/Orders/OpenMakerDispute.cs:1` | T1 | same |
| 5 | `Features/Orders/RefundOrder.cs:1` | T1 | same |
| 6 | `Features/Orders/ResolveDispute.cs:1` | T1 | same |
| 7 | `Features/Orders/ChangeOrderStateManually.cs:99` | T5 | **BlockedCode indirection — verified genuine false positive.** Line 99 is `Error.Conflict("state", decision.BlockedCode!)`; the T5 heuristic flags any non-`BusinessErrorMessage.X` second argument. Traced `ManualOrderTransitionPolicy.cs`: every `Decision.Blocked(...)` call site (lines 100–148) passes a `BusinessErrorMessage.OrderManualTransition*` constant — no inline string exists anywhere in the chain. |

No new T3 (`SaveChangesAsync`), T4 (`dynamic`/`any`), T6 (money column),
or T7 entries. `DisputeShipment.cs:1` (rewired, not new) was already in
the baseline — correctly NOT re-added. The reviewer draft's Gate 9
projection ("~124, ~6 T1") was off by the one T5 indirection; the +1 is
accounted for and legitimate.

## Part 2 — QA plans authored

The three tickets carry inline test plans ("no separate
docs/test-plans/T-NNNN.md"), but the bundle workflow requested
execution-grade plan files; written from the established stub format
(T-0085/T-0086a):

| Plan | Manual TCs | Edge cases | Automated must-cover mapping |
|---|---|---|---|
| `docs/test-plans/T-0105.md` | 11 (full happy, 2-partial accumulate, over-refund 409, Completed ack gate both arms, provider Permanent + Transient, Silent-Success re-refund, audience 401, no-ref/bad-state, hygiene) | 4 | OrderRefundTests / RefundOrderHandlerTests / RefundOrderIntegrationTests / ComgatePaymentProviderTests — red commit `f9eb0cb` precedes impl `9085897` |
| `docs/test-plans/T-0106.md` | 15 (open ×4 sources, blocked states, re-open, cross-tenant 404, resolve ×3 outcomes incl. Cancelled both branches, loud re-resolve, sweep exclusion, ADMIN_NOTIFICATION_EMAIL missing-config, message thread, hygiene) | 4 | OrderDisputeTests + 4 handler suites + 3 integration suites — red `f9eb0cb` precedes impl `bcedce4` |
| `docs/test-plans/T-0107.md` | 17 (5 allowed §A.2 rows, 8 blocked rows incl. precedence carve-outs, reason validation, same-state diagonal, audience 401, hygiene) | 4 | ManualOrderTransitionPolicyTests exhaustive matrix + RevertAcceptance + handler + integration — red `f9eb0cb` precedes impl `ad9e862` |

### Deliberate deviations from the workflow brief (ticket-over-brief)

1. **"double-open 409"** — ticket AC-5 locks re-open as **200
   Silent-Success returning the existing dispute id** (partial unique
   index is the concurrent-race backstop, not an API 409). T-0106 TC-6
   tests the locked behavior and notes the supersession.
2. **"admin-only 403"** — tickets AC-10 (T-0105) / AC-7 (T-0107) +
   ADR 0013 specify **401** (audience rejection at authentication, no
   existence leak). TC-9 / TC-16 expect 401.

### Flags raised in the plans

- **Two-email UX on resolve-Refunded** (T-0106 TC-9): dispute-resolved +
  order-refunded land near-simultaneously for one admin action — by
  design (reviewer HIGH-3.6), flagged for T-0118 revisit.
- **Comgate sandbox refund verification is a go-live manual item**
  (T-0105 preconditions): the integration suite uses
  `FakeComgatePaymentProvider`; real `/v1.0/refund` sandbox semantics
  must be hand-verified before production cutover (ticket Risk §3).
- **ADMIN_NOTIFICATION_EMAIL** precondition pinned in T-0106 (set on
  staging; TC-13 deliberately unsets it last to verify
  Configuration-class outbox failure + retry-after-fix per ADR 0020).

## Verdict

Gate 9 **PASS** (exit 0, 125 tracked; +7 baseline delta fully accounted
as 6 T1 wrapper false-positives + 1 verified T5 BlockedCode
indirection; zero unexplained entries). QA plans written for all three
tickets — 43 manual TCs + 12 edge cases total, every AC mapped, all
TDD must-cover rows matched to committed red-first test files.
Plans are executable once staging carries the bundle migrations, an
admin JWT, and `ADMIN_NOTIFICATION_EMAIL`.
