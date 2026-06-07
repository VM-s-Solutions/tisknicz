# Must-cover tests

The canonical list of code categories in Makables that **must** ship with tests. Reviewer's Gate 5 walks this list against every PR diff. New code in any category below without a test commit in the same PR is a **HARD FAIL** — no exceptions, no "follow-up ticket."

This list is enforced from **T-0067 onward** per the TDD policy (see [tdd-policy.md](./tdd-policy.md)). T-0001–T-0066 are grandfathered against the strict policy but are still expected to add tests when touched.

Cross-links:
- [tdd-policy.md](./tdd-policy.md) — when TDD is hard-required vs. recommended
- [quality-gates.md](./quality-gates.md) — Gate 5 (Tests) sits on top of this list
- [../review/checklist.md](../review/checklist.md) — Section H is the reviewer's row-by-row enforcement
- [../../.claude/agents/reviewer.md](../../.claude/agents/reviewer.md) — reviewer charter

---

## Coverage shorthand

Each row below specifies the **minimum** coverage. "Happy path + 1 failure" means at least two tests, named explicitly for the case under test. More is welcome; less is a hard fail.

| Shorthand | Meaning |
|---|---|
| `H` | Happy path (one canonical correct call) |
| `IT-N` | N illegal transitions / inputs (each in its own test) |
| `RC` | One race-condition / concurrency test (deterministic, no `Thread.Sleep`) |
| `RB` | One rollback test (failure mid-transaction leaves no partial state) |
| `FA` | First-allocation / cold-start test (empty table, no prior row) |
| `NEG-CODE` | One negative-path test per `BusinessErrorMessage` code the handler can surface |

---

## 1. Money + MoneyFormatter

**Code:** `Core.Domain/ValueObjects/Money.cs`, `Core.Domain/Services/MoneyFormatter.cs`
**Precedent:** [T-0005](../tickets/T-0005-money-value-object.md)
**Minimum:** `H` + rounding tests for every documented half-up edge case + currency mismatch throws + CZK haléř-stripping for display + negative amount handling per [docs/architecture/money.md](../architecture/money.md).

Anchor cases (non-exhaustive — see T-0005 test file as the precedent):
- `100.005 CZK` → `100.01 CZK` (half-up)
- `100.004 CZK` → `100.00 CZK`
- Adding `CZK + EUR` throws `InvalidOperationException` (or returns `BusinessResult` failure where the design says so)
- Formatting `123456_minor` CZK → `1 234 Kč` (whole CZK, NBSP thousands)
- Formatting `0_minor` → `0 Kč`

## 2. *NumberGenerator (Order, Invoice, Payout, …)

**Code:** `Core.AppServices/Services/Numbering/OrderNumberGenerator.cs` and every future `*NumberGenerator`
**Precedent:** [T-0062](../tickets/T-0062-order-number-generator.md)
**Minimum:** `H` + `RC` + `RB` + `FA`.

- `H` — sequential allocation across same `CountryCode` + year produces `YYYY-NNNNNN` with monotonic counter
- `RC` — two concurrent `Allocate()` calls under serializable isolation both succeed without duplicate emission (use `Task.WhenAll` against a real Postgres test container; no `lock` shortcuts)
- `RB` — failure after allocation but before downstream commit (simulate by throwing in the handler) leaves the counter row consistent with the next successful allocation
- `FA` — first allocation against an empty `numbering_state` row produces sequence `000001` (not `000000`, not `000002`)

Every new `*NumberGenerator` (`InvoiceNumberGenerator`, `PayoutNumberGenerator`, …) repeats this exact matrix.

## 3. OrderPricing + PricingService

**Code:** `Core.AppServices/Features/Order/Pricing/OrderPricing.cs`, `Core.AppServices/Services/PricingService.cs`
**Precedent:** [T-0061](../tickets/T-0061-order-pricing.md)
**Minimum:** `H` + rounding cases per [docs/architecture/money.md](../architecture/money.md).

Anchor cases:
- VAT 21% applied to `100_00_minor` CZK net → `121_00_minor` gross (basis-points math, half-up)
- VAT applied to `99_99_minor` net → gross rounded half-up
- Discount applied before VAT (or after, whichever the ADR fixes — test both directions per the design)
- Shipping fee with VAT, free-shipping threshold edge (`subtotal == threshold` and `threshold - 1_minor`)
- Multi-line order: per-line rounding sums to header total within ±0 minor units (no drift)

## 4. Order state-machine transitions

**Code:** `Core.Domain/Entities/Order.cs` (state transition methods) and `Core.AppServices/Features/Order/*` handlers that drive them
**Precedent:** [T-0060](../tickets/T-0060-order-state-machine.md)
**Minimum:** **every** legal transition has a passing test; **every** illegal transition has a failing test that surfaces the correct `BusinessErrorMessage` code.

There is no shortcut here. If the state machine has N legal edges and M illegal edges, the test file has N+M tests. Reviewer counts.

- One test per legal edge (`Pending → Paid`, `Paid → Shipped`, `Shipped → Delivered`, etc.)
- One test per illegal edge (`Delivered → Pending`, `Cancelled → Paid`, etc.) returning the documented `BusinessErrorMessage.Order*` code
- Idempotent re-application of a terminal-state transition returns success without state change (per [T-0066](../tickets/T-0066-webhook-idempotency.md) interaction)

## 5. FluentValidation `Validator` classes

**Code:** every `class XxxValidator : AbstractValidator<XxxCommand>` (or `XxxQuery`) under `Core.AppServices/Features/**/`
**Precedent:** any feature ticket from T-0050 onward
**Minimum:** `H` + one test per `RuleFor(...)` clause's failure mode + one test per cross-field rule.

- Validators are pure — no excuse for skipping. Use `TestValidate(...)` from `FluentValidation.TestHelper`.
- If a rule emits a `BusinessErrorMessage` code, the test asserts the code (`result.ShouldHaveValidationErrorFor(x => x.Field).WithErrorCode("...")`).
- Adding a new `RuleFor` clause without a new test = hard fail.

## 6. `*Specification` classes

**Code:** every `class XxxSpecification : Specification<TEntity>` under `Core.Domain/Specifications/` or `Infra.Database/Specifications/`
**Precedent:** any read-side ticket that introduces a spec
**Minimum:** `H` + one test per filter clause + one test that the spec composes correctly with `&&` / `||` if it exposes composition.

- Test against an in-memory list using `spec.IsSatisfiedBy(entity)` for domain specs.
- Test against the real Postgres test container for query specs that emit `Expression<Func<T, bool>>` (verify the SQL doesn't trip EF Core translation).

## 7. Authz / ownership in scoped repositories

**Code:** `Infra.Database/Repositories/ForCustomer/*Repository.cs`, `Infra.Database/Repositories/ForMaker/*Repository.cs`, `Infra.Database/Repositories/Unscoped/*Repository.cs` per [ADR 0013](../adr/0013-scoped-repository-pattern.md)
**Precedent:** any ticket that adds a scoped repository
**Minimum:** `H` (owner reads own row) + cross-tenant denial (customer A reads customer B's row → empty result, **not** 403; the query filter is the security boundary) + role boundary (a `ForCustomer` repository cannot return data the maker scope would have seen, and vice versa).

- These tests run against a seeded Postgres test container with at least two tenants of each scope class.
- `Unscoped` repositories are tested for the absence of the query filter — they MUST return cross-tenant rows when called from `Web.Admin` (this is the whole point of the scope).
- Tests live next to the repository, not in the handler test file. Authz is the repository's responsibility per ADR 0013.

## 8. Webhook idempotency

**Code:** every controller under `Web.Public/Controllers/Webhooks/*` and the handlers they call
**Precedent:** [T-0066](../tickets/T-0066-comgate-webhook.md), [T-0067](../tickets/T-0067-webhook-idempotency-hardening.md)
**Minimum:** `H` + paid-twice + refId mismatch + signature/IP failure.

| Case | Expected |
|---|---|
| First valid webhook for `provider_ref=X` | 200, state transition, audit row |
| Second valid webhook for same `provider_ref=X` already in target state | 200, **no** state transition, **no** duplicate audit row |
| `provider_ref` not found in our DB | 404 (or per ADR — verify against [ADR 0019](../adr/0019-webhook-handling.md)) |
| `refId` mismatch (our internal ref ≠ provider's reference for the looked-up payment) | 401 |
| Signature invalid / origin IP not in allowlist | 401, **no** DB read of the payment row |
| Malformed body (missing required field) | 400, **no** signature verification attempted past the parse failure |

All six cases live in a `WebApplicationFactory<Program>` integration test, not a unit test. The webhook is a transport-layer contract; unit tests of the handler are insufficient.

## 9. Every `BusinessErrorMessage` code has a negative-path test

**Code:** every handler under `Core.AppServices/Features/**/Handler` that returns `BusinessResult<T>.Failure(BusinessErrorMessage.Xxx)`
**Precedent:** standing rule from [CLAUDE.md](../../CLAUDE.md) §Errors
**Minimum:** for every `BusinessErrorMessage.Xxx` referenced in a handler, **at least one** test exercises the negative path and asserts `result.Error.Code == BusinessErrorMessage.Xxx`.

Reviewer enforcement:
- Grep the PR diff for new `BusinessErrorMessage.` references on `Failure(...)` calls.
- For each, grep the test diff for a corresponding `Should().Be(BusinessErrorMessage.Xxx)` (or equivalent assertion).
- No match = hard fail.

This is the single highest-volume rule in this document. A handler that returns six distinct error codes ships with six negative-path tests, period.

## 10. Adapter parse + verify (payment, shipping, registry, geocoder, email)

**Code:** every adapter implementation under `Infra.Clients/<Provider>/` that exposes `ParseAndVerifyXxxAsync(...)` or equivalent
**Precedent:** [T-0066](../tickets/T-0066-comgate-webhook.md) `ComgatePaymentProvider.ParseAndVerifyWebhookAsync`
**Minimum:** `H` + malformed body + spoofed origin (IP / signature) + refId mismatch.

Anchor for `ComgatePaymentProvider.ParseAndVerifyWebhookAsync` per T-0066:
- `H` — valid Comgate POST with correct signature from allowlisted IP → parsed `WebhookPayload` returned
- Malformed body (missing `transId`, garbled JSON, empty form) → failure result with code; **no** DB call
- IP spoofed (request from non-allowlisted IP, signature valid) → failure result; **no** DB call
- `refId` in payload doesn't match our `Payment.ProviderRef` lookup → failure result with the exact `BusinessErrorMessage.Webhook*` code per T-0067

Every new provider adapter (`PacketaShippingProvider.ParseAndVerifyWebhookAsync`, future `StripePaymentProvider`, etc.) inherits this matrix.

## 11. Set-once entity invariants

**Code:** any property on an `Auditable` entity that is allowed to transition from `null` → value exactly once and is then immutable
**Precedent:** [T-0060](../tickets/T-0060-order-state-machine.md), [T-0067](../tickets/T-0067-webhook-idempotency-hardening.md)
**Minimum:** `H` (first set succeeds) + second-set rejected with the documented `BusinessErrorMessage` code + null/empty-string rejected on the first set.

Known set-once invariants (this list grows; reviewer keeps it current):

| Entity | Property | Set by | Source ticket |
|---|---|---|---|
| `Payment` | `PaymentProviderRef` | Comgate webhook first-receive | T-0066 |
| `Payment` | `PaymentMethod` | Comgate webhook first-receive | T-0067 |
| `Order` | `ShippingCarrierRef` | Packeta label-issued callback | T-0067 |
| `Invoice` | `PdfBlobPath` | T-0068b `InvoiceService.IssueAsync` after blob upload | T-0068a |

Add a row here when a new set-once property lands. The test sits next to the entity in `Core.Domain.Tests/Entities/`.

---

## How reviewer enforces

**Gate 5 walks this list vs. the PR diff. New code in any category above without a test commit in the same PR is a HARD FAIL.**

The reviewer's procedure ([checklist.md](../review/checklist.md) Section H, [reviewer.md](../../.claude/agents/reviewer.md)):

1. Pull the PR diff.
2. For each section 1–11 above, grep the production diff for additions in scope (new `Money` operation, new `*NumberGenerator`, new state-machine edge, new `Validator`, new `Specification`, new scoped repository method, new webhook controller, new `BusinessErrorMessage` reference in a handler, new adapter `ParseAndVerifyXxxAsync`, new set-once property).
3. For each hit, grep the test diff for matching coverage at the minimum specified above.
4. Any miss → request changes with the row quoted verbatim and the missing test named.
5. Reviewer does **not** write the test. The implementing agent does, then re-requests review.

"It's a small change," "I'll add the test in a follow-up," and "the code is obviously correct" are not accepted. The TDD policy ([tdd-policy.md](./tdd-policy.md)) is the deeper rationale; this document is the enforcement surface.
