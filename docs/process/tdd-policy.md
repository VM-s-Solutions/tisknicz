# TDD policy

Pure logic without a test is permanent debt. Math, state machines, and authorization rules survive every refactor; the test is what proves they still mean what they meant on day one. Reviewer enforces this at Gate 5.

## Why this exists

Two precedents shaped the rule:

- **T-0061** — `OrderPricing` + `PricingService`. Money math (line subtotal, VAT basis points, half-up rounding, CZK haléř stripping). A regression here is invisible until an invoice goes out wrong; by then it's a refund letter.
- **T-0062** — `OrderNumberGenerator`. State + concurrency (per-year sequence, gap handling, collision). A regression here corrupts the human-facing identifier on every order.

Both shipped test-first. Both caught real bugs in the red phase. That is the bar.

After-the-fact tests on pure logic pass green by construction — they encode the implementation that already exists. They prove nothing. Reviewer Gate 5 treats them as a hard fail from T-0067 onward.

## What counts as "pure logic" (must-cover)

The canonical list lives in [must-cover-tests.md](./must-cover-tests.md). It mirrors this table — keep them in sync when adding rows.

| Area | Concrete targets | Precedent |
|---|---|---|
| Money | `Money` value object, `MoneyFormatter` (CZK 1 234 Kč, haléř strip, basis-point VAT) | [T-0005](../tickets/T-0005-money-value-object.md) |
| Numbering | `OrderNumberGenerator`, future `InvoiceNumberGenerator`, any `*NumberGenerator` | [T-0007](../tickets/T-0007-numbering.md), [T-0062](../tickets/T-0062-order-number-generator-rescope.md) |
| Pricing | `OrderPricing`, `PricingService`, any pure pricing service | [T-0061](../tickets/T-0061-order-pricing-domain-service.md) |
| State machine | Legal **and** illegal transitions on `Order.cs` (and every future aggregate with explicit states) | [T-0060](../tickets/T-0060-order-entity-state-machine.md) |
| Validation | Every `FluentValidation` `Validator` class — one positive + one negative per rule | A.7 in [patterns.md](../architecture/patterns.md) |
| Specifications | Every `*Specification` class (predicate + EF translation) | A.12 in [patterns.md](../architecture/patterns.md) |
| Authz / ownership | `ForCustomer`, `ForMaker`, `Unscoped` repository scoping — wrong audience returns empty / 404 | [ADR 0013](../adr/0013-scoped-repositories.md) |
| Webhook idempotency | Replay of same `provider_ref` is a no-op; signature mismatch rejects | [T-0066](../tickets/T-0066-comgate-webhook.md), [T-0067](../tickets/T-0067-mark-order-paid-outbox.md) |
| Error surface | Every `BusinessErrorMessage` code referenced in a Handler has ≥ 1 negative-path test that asserts the code | A.4 in [patterns.md](../architecture/patterns.md) |

If a change adds a new class of pure logic not covered above, propose a row in the PR and update [must-cover-tests.md](./must-cover-tests.md) in the same commit.

## The rule

A PR that touches pure logic (from the table above) must prove test-first in one of two ways:

1. **Git log proof.** The test commit is **strictly before** the implementation commit on the feature branch. `git log --reverse feat/T-NNNN-* -- <test-files> <impl-files>` shows red before green.
2. **Ticket status-log proof.** The ticket's status log (in `docs/tickets/T-NNNN-*.md`) shows a `red` entry — tests written and failing — before the `green` entry. This carve-out exists for trunk-style commits but the status log must name the failing test and the assertion it tripped on.

One of the two must be present. "I wrote them together" is not acceptable for pure logic.

After-the-fact tests on pure logic = **Gate 5 HARD FAIL**. Reviewer rejects the PR and asks for the work to be redone test-first on a fresh branch. The grandfather window does not extend retroactively.

## Carve-outs

Not every line needs the strict commit dance. Two carve-outs:

| Carve-out | Rule | Why |
|---|---|---|
| **UI tests** (`/frontend/components/**`, page components) | Pragmatic-alongside is fine. Manual test plan against preview (Gate 5) + post-hoc component test is acceptable. | UI behavior is verified visually; the test-first ROI is low until the interaction logic stabilizes. |
| **Handler unit tests** (`Core.AppServices/Features/<Entity>/<UseCase>.cs`) | Test-first **at the contract** — write the test against the intended `Command`/`Response` shape from the ticket. May land in the same commit as the handler if the handler is behind a feature flag or the endpoint is not yet wired into a host. | Handlers are mostly orchestration; the pure logic they orchestrate (the table above) is already covered upstream. The contract-shape test is the value. |

A handler that contains pure logic inline (instead of delegating to a domain service) is itself pure logic and the carve-out does **not** apply. Extract first, then this policy aims at the extracted service. See A.7 in [patterns.md](../architecture/patterns.md).

## Scope: T-0067 onwards

| Ticket range | Status |
|---|---|
| T-0001 — T-0066 | **Grandfathered.** No retro-fail. If a grandfathered ticket gets reopened for a real change, the changed surface is held to this policy. |
| T-0067 onward | **Enforced.** Reviewer Gate 5 walks the git log. |

Grandfathering is one-way: a grandfathered file that is **modified** in a T-0067+ PR is fair game for Gate 5 on the changed lines. Reviewer is reasonable about this — touching a comment doesn't drag the whole class into the policy, but adding a new rounding branch does.

## Enforcement

| Who | What they do |
|---|---|
| **dotnet-backend** | Charter ([.claude/agents/dotnet-backend.md](../../.claude/agents/dotnet-backend.md)) references this doc. Writes the failing test commit before the implementation commit on every pure-logic ticket. |
| **qa** | Charter ([.claude/agents/qa.md](../../.claude/agents/qa.md)) references this doc. Owns [must-cover-tests.md](./must-cover-tests.md). Flags a PR pre-review if the must-cover list grew without a test. |
| **reviewer** | Charter ([.claude/agents/reviewer.md](../../.claude/agents/reviewer.md)) walks `git log --reverse` on the feature branch as part of Gate 5 in [quality-gates.md](./quality-gates.md). Hard-fails after-the-fact tests on pure logic. |
| **PM** | Sizes pure-logic tickets with the test-first commit in mind. A pure-logic ticket is never **S** unless the test fits in the same hour as the implementation. |

## Alternatives considered

- **Strict TDD everywhere, including UI.** Rejected. The signal-to-noise on Server Component snapshots is low and would slow frontend without catching the bugs we actually ship. Manual + i18n key check + Gate 1 hygiene catches the real failures.
- **No commit-order enforcement, just "tests must exist."** Rejected. That is the current industry default and it is exactly how T-0061 would have been written without test-first — a green file that encodes the bug. The whole point is the red phase.
- **Grandfather nothing; backfill T-0001–T-0066.** Rejected. The backfill cost is real and the ROI is low — most of those tickets are scaffolding, wiring, or one-shot infra. We hold the bar going forward.
- **Allow same-commit test-with-implementation for pure logic.** Rejected. Indistinguishable from after-the-fact at review time. The status-log carve-out exists for the trunk-style case and requires the failing assertion to be named.

## Defense

The cost of this policy is one extra commit per pure-logic ticket and a small amount of reviewer time walking the log. The cost of **not** having it is paid in production: a wrong VAT rate, a duplicate order number, a state machine that lets a refunded order be paid again, a webhook that double-credits. Each of those is a customer-trust event and a manual-reconciliation event in a marketplace that we have committed (per [CLAUDE.md](../../CLAUDE.md)) to running with minimal manual intervention.

T-0061 and T-0062 already paid this tax and caught bugs in the red phase that would have shipped silently otherwise. We are formalizing what already worked, not adding ceremony.

## Related

- [must-cover-tests.md](./must-cover-tests.md) — the live list of pure-logic surfaces
- [quality-gates.md](./quality-gates.md) — Gate 5 enforcement
- [ticket-lifecycle.md](./ticket-lifecycle.md) — where the status log lives
- [.claude/agents/reviewer.md](../../.claude/agents/reviewer.md) — Gate 5 walker
- [.claude/agents/dotnet-backend.md](../../.claude/agents/dotnet-backend.md) — implementer charter
- [.claude/agents/qa.md](../../.claude/agents/qa.md) — must-cover owner
- [patterns.md](../architecture/patterns.md) — A.4 (errors), A.7 (validators), A.12 (specifications)
