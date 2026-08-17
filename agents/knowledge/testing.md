# Testing Strategy — What Must Be Tested, and Where

Tests are evidence, not theater. This catalog fixes **what** gets tested, at **which layer**, and the
**must-cover** list for a marketplace that handles real orders, real payments, and real money. `qa`
owns execution; every developer writes the tests for the logic they add; `reviewer` enforces **Gate 5**
against this doc. The bar: "would I let this run unattended in production handling real customers and
real money."

This doc is the agent-facing *how we test* companion. It does not restate the pattern catalog — the
canonical shapes live in [docs/architecture/patterns.md](../../docs/architecture/patterns.md), the
enforcement surface in [docs/process/must-cover-tests.md](../../docs/process/must-cover-tests.md), and
the test-first rule in [docs/process/tdd-policy.md](../../docs/process/tdd-policy.md). When those and
this doc disagree, **the process docs win** — flag the drift so this one is brought back in sync.

Backend test projects that exist: `Makables.Tests` (xUnit unit), `Makables.IntegrationTests`
(`WebApplicationFactory<Program>` per host), `Makables.TestUtilities` (fixtures/builders). Frontend:
Vitest specs (`@testing-library/react` + `jest-axe`), run via `npm test` in `frontend/`. Coverage today
is **expanding** — hardening it is part of going to PROD.

---

## TDD — write the test first (the default development approach)

We develop **test-first**. A ticket defines *what correct looks like*; the test encodes it
**executably**; the code makes it pass. Writing the test first forces you to nail the contract before
the implementation, catches the failure branches you'd otherwise forget, and gives you a regression net
the moment the feature exists. For money and lifecycle code this isn't optional polish — it's how we
get accuracy. The full policy, its precedents (T-0061 pricing, T-0062 order numbering), the
grandfather window (T-0067 onward), and the carve-outs live in
[tdd-policy.md](../../docs/process/tdd-policy.md); this section is the working summary.

### The loop (red → green → refactor)
1. **Red** — write a failing test that states the desired behavior from the ticket's acceptance
   criteria. Run it; confirm it fails *for the right reason* (the behavior is missing, not a typo).
2. **Green** — write the **minimum** code to make it pass. No extra scope, no "while I'm here".
3. **Refactor** — clean up with the test green: extract helpers, remove duplication, apply the
   canonical pattern from [patterns.md](../../docs/architecture/patterns.md). The test stays green
   throughout.
4. Repeat per acceptance criterion / per failure branch until the ticket's AC are all covered.

### Where TDD is strict vs. pragmatic
- **Strict red-green-refactor (mandatory)** for **pure logic** — the `Money` value object and
  `MoneyFormatter`, `OrderPricing` / `PricingService`, `*NumberGenerator`, FluentValidation validators,
  `Order` state transitions, `*Specification` predicates, refund/VAT (basis-point) math, any algorithm.
  This is where TDD pays off most and is easiest; there is no excuse to write these after. `reviewer`
  expects the test to predate the implementation (visible in commit order / the ticket's status log) —
  after-the-fact tests on pure logic are a **Gate 5 HARD FAIL** from T-0067 onward.
- **Test-first at the contract** for **command/query handlers** — write the handler's unit test
  (mock repos; assert `IsSuccess` and each `BusinessResult` `Error.Code`) against the intended
  `Command`/`Response` shape before the handler body. Write the route integration test (incl. the
  auth/ownership rejection) against the controller signature before wiring it. A handler may land in
  the same commit as its contract test *only* if it is pure orchestration; a handler that inlines pure
  logic instead of delegating to a domain service **is** pure logic and loses the carve-out — extract
  first.
- **Pragmatic test-alongside** for **UI** (Next.js Server/Client Components) — pure TDD on markup is
  low-value. Here: write the **logic** test first (the data-state mapping empty/loading/error, the
  error-code → i18n-message mapping, any client-side derived state — these are logic), then build the
  component to that tested state. The view itself is verified by `qa` against the AC on the Vercel
  preview, not by a unit test of markup. Accessibility is asserted with `jest-axe` where a spec exists.

### How the ticket shows TDD happened
A ticket implemented test-first shows it: the **test appears before (or with) the implementation** in
the diff/commits, the status log notes "red: <test> failing → green", and each AC item maps to a test
case. Proof is one of two forms (per [tdd-policy.md](../../docs/process/tdd-policy.md)): the test commit
is strictly before the implementation commit (`git log --reverse` on the feature branch), or the
ticket's status log names the failing test and the assertion it tripped on. A PR where the
implementation lands first and tests are bolted on at the end (or not at all) for pure logic **fails
Gate 5** — `reviewer` asks for it to be redone test-first, because after-the-fact tests systematically
miss the branches the author didn't think to handle.

### When you're changing existing untested code
Some of the codebase predates the strict policy (grandfathered T-0001–T-0066). When you touch an
untested unit to change it: write a **characterization test first** that pins the *current* behavior,
confirm it passes, then TDD the change on top. This stops you silently breaking behavior you didn't know
existed. Grandfathering is one-way — a grandfathered file **modified** in a T-0067+ PR is fair game for
Gate 5 on the changed lines.

---

## Which layer tests what

| Layer | Test type | What it proves | Where |
|---|---|---|---|
| Pure domain/app logic | **Unit** | a calculation, validator, spec, or state transition is correct in isolation | `Makables.Tests` |
| A handler with mocked repos | **Unit** | the handler's happy path + each `BusinessResult.Failure` branch | `Makables.Tests` (mock `IXxxRepository`) |
| A route end-to-end | **Integration** | controller → MediatR → validation behavior → handler → DB behaves, incl. auth + audience | `Makables.IntegrationTests` (`WebApplicationFactory<Program>` for the specific host) |
| A component's logic | **Vitest** | data-state transitions, error-code→i18n mapping, the three UI states, a11y | `frontend/**/__tests__/*.test.tsx` |
| A background job | **Integration** | the Azure Function's happy path + retry/idempotency behavior | `Makables.IntegrationTests` |

**Rule:** new **pure logic** (no I/O) → a unit test is mandatory. A new **endpoint** → at least one
integration test covering the happy path and the most important failure (auth/ownership/audience). A new
**validator rule** → a unit test asserting the rule's `BusinessErrorMessage` code fires. Integration
tests target the **correct host** — the four hosts are `Web.Customer` (5001), `Web.Maker` (5002),
`Web.Admin` (5003), `Web.Public` (5104); a customer JWT replayed against the maker API must be rejected,
and that rejection is a **test**, not just a code review.

## The must-cover list (non-negotiable before PROD)

These are the areas where a bug costs money, breaks the law, or leaks data. Each needs explicit tests.
This is the readable overview; the row-by-row enforcement matrix (with coverage shorthand `H` / `IT-N`
/ `RC` / `RB` / `FA` / `NEG-CODE`) is [must-cover-tests.md](../../docs/process/must-cover-tests.md) and
**that** is what `reviewer` greps the diff against.

1. **Money & `MoneyFormatter`** — half-up rounding on every documented edge case (`100.005 → 100.01`,
   `100.004 → 100.00`), currency-mismatch rejection (`CZK + EUR`), CZK haléř-stripping for display
   (`123456_minor → 1 234 Kč`, NBSP thousands), negative-amount handling. No floating-point surprises —
   money is `long` minor units + `string Currency`, VAT as basis points.
2. **Pricing** — `OrderPricing` / `PricingService`: VAT applied via basis points (21% on `100_00_minor`
   net → `121_00_minor` gross, half-up), discount ordering relative to VAT per the ADR, shipping fee +
   free-shipping-threshold edges (`subtotal == threshold` and `threshold − 1_minor`), and multi-line
   per-line rounding that sums to the header total with **zero** drift.
3. **Order lifecycle** — **every** legal transition (`Pending → Paid → Shipped → Delivered`, plus
   `→ Cancelled`) has a passing test, and **every** illegal transition (cancel a delivered order, pay a
   cancelled one, `Delivered → Pending`, …) has a test that surfaces the correct
   `BusinessErrorMessage.Order*` code. N legal edges + M illegal edges = N+M tests; `reviewer` counts.
4. **Numbering** — every `*NumberGenerator` (`OrderNumberGenerator`, future `InvoiceNumberGenerator`,
   `PayoutNumberGenerator`) covers happy path (`YYYY-NNNNNN`, monotonic), concurrency (two `Allocate()`
   under serializable isolation, no duplicate — real Postgres test container, `Task.WhenAll`, no `lock`
   shortcut), rollback (failure after allocation leaves the counter consistent), and first-allocation
   (`000001`, not `000000`/`000002`).
5. **Country configuration routing** — per-country behavior is selected by looking up the
   `CountryConfiguration` row (default payment/shipping/registry/geocoder/email provider), never by
   branching on the country string. Tests assert the correct adapter is chosen for the configured
   country and that a change to the config row changes the routed provider — **customer checkout is
   never blocked** by a downstream integration (fiscal/registry) failure; those retry.
6. **Authorization, ownership & audience boundaries** — for resource-by-id endpoints, a **cross-user**
   and **cross-tenant** access attempt is rejected (the scoped-repository query filter returns an empty
   result / 404, **not** the resource and **not** a 403 — the filter is the security boundary, per
   ADR 0013). `ForCustomer` / `ForMaker` scopes cannot see each other's rows; `Unscoped` (Admin) can.
   Plus: a JWT minted for one host is rejected by another. These are tests, not just code review.
7. **Webhook idempotency** — side-effecting inbound webhooks (Comgate payment, Packeta shipping label)
   are safe to re-deliver: verify origin/signature and allowlisted IP first (a spoofed origin does
   **no** DB read of the payment row), look up by `provider_ref`, and if already in the target state
   return 200 with **no** second state transition and **no** duplicate audit/effect. Simulate the
   re-delivery. Set-once fields (`Payment.PaymentProviderRef`, `Order.ShippingCarrierRef`, …) reject the
   second set with the documented code.
8. **Invoices & payouts** — invoice generation and numbering (gap-free where required), invoice PDF
   render + blob attach (`Invoice.PdfBlobPath` set-once), payout batch open/close, CSV artifact attach,
   and the approve / mark-paid / cancel transitions — each transition legal and illegal covered.
9. **Every `BusinessErrorMessage` path** — a validator/handler that can return a given error code has a
   test that triggers it and asserts `result.Error.Code == BusinessErrorMessage.Xxx`. This keeps the
   error contract the frontend i18n depends on real (every code has a parallel `cs-CZ` key). This is the
   highest-volume rule: a handler returning six distinct codes ships with six negative-path tests.
10. **Adapter parse + verify** — every `Infra.Clients/<Provider>/` adapter exposing
    `ParseAndVerifyXxxAsync` (Comgate, Packeta, ARES, Mapbox, SendGrid) covers happy path + malformed
    body + spoofed origin (IP/signature) + `refId` mismatch, each returning a failure result with the
    exact code and doing **no** DB call on a rejected request.

## How to write them (match the codebase)

- **Handler unit test:** construct the handler with mocked `IXxxRepository`/services (per the DI shape
  in [patterns.md](../../docs/architecture/patterns.md)), call `Handle`, assert `result.IsSuccess` or
  `result.Error!.Code == BusinessErrorMessage.X`. Use the builders in `Makables.TestUtilities`; don't
  hand-roll entity graphs inline.
- **Validator unit test:** `new XxxValidator(...).TestValidate(command)` (FluentValidation
  `TestHelper`) → assert `ShouldHaveValidationErrorFor(x => x.Field).WithErrorCode("...")` for each
  rule, and a clean pass for valid input.
- **Specification unit test:** domain specs via `spec.IsSatisfiedBy(entity)` against an in-memory list;
  query specs that emit `Expression<Func<T,bool>>` against the real Postgres test container so EF Core
  translation is exercised, not mocked away.
- **Integration test:** spin the correct `Web.*` host with `WebApplicationFactory<Program>`,
  authenticate as the relevant audience, hit the route, assert HTTP status + body. Cover the
  auth/ownership/audience rejection explicitly. Webhook idempotency (§7) and adapter verify (§10) live
  here, not in a unit test — the webhook is a transport-layer contract.
- **Frontend:** test the **logic** (data-state mapping, error-code → `cs-CZ` i18n message) over the
  component where possible; assert the three states (empty/loading/error) render and, where a spec
  exists, that `jest-axe` finds no violations. No test asserts business math on the frontend — there
  isn't any; the frontend is a pure presentation layer.
- **Background jobs:** Azure Functions get an integration test for the happy path plus retry/idempotency
  (a re-invocation does not double the effect), mirroring §7.

## Running the suites

```bash
cd backend/src
dotnet test Makables.Tests/Makables.Tests.csproj                     # unit, no infrastructure
dotnet test Makables.IntegrationTests/Makables.IntegrationTests.csproj
cd ../../frontend && npm run test                                     # Vitest + jest-axe
```

`Makables.IntegrationTests` needs a real Postgres 16. By default `PostgresHarness`
starts a `postgres:16-alpine` Testcontainer — that is what CI uses and the
production-parity guarantee rests on it. **Without a Docker daemon every
Postgres-backed test fails at fixture construction** (`Docker is either not
running or misconfigured`), which reads like 188 broken tests and is not.

On a machine without Docker, point the harness at an already-running Postgres 16
instead — it creates a throwaway `makables_test_<guid>` database per fixture and
drops it afterwards, so it never touches the dev data:

```bash
~/.makables-dev/start-pg.sh   # the durable local cluster
MAKABLES_TEST_POSTGRES="Host=localhost;Port=5432;Username=postgres;Database=postgres" \
  dotnet test Makables.IntegrationTests/Makables.IntegrationTests.csproj
```

Leave the variable unset in CI. A run that skipped the Postgres leg is not
evidence — say so rather than reporting the unit counts alone.

## Anti-patterns (`reviewer` rejects — and Gate 0 governs *how* findings are reported)

- A test that asserts a method exists or returns non-null but checks no behavior (theater).
- All-happy-path with no failure/edge cases — money and state machines especially must test the sad
  paths.
- Tests coupled to incidental detail (exact log strings, private fields, generated-client internals)
  that break on any refactor.
- Asserting on a hardcoded expected string instead of the `BusinessErrorMessage` constant.
- Skipping the cross-user / cross-tenant / cross-audience authorization test "because the code looks
  right."
- Any monetary assertion against a `decimal` or floating-point expected value instead of `long` minor
  units.
- Reporting a "failing test" as a defect without the Gate 0 discipline
  ([quality-gates.md](../../docs/process/quality-gates.md)): refute by default, cite file:line for both
  the defect and the missing guard, state the concrete trigger, and check for the guard (pipeline
  behavior, query filter, idempotency short-circuit, `[Authorize]`) before asserting a BLOCKER.

## Related

- [docs/process/must-cover-tests.md](../../docs/process/must-cover-tests.md) — the enforcement matrix `reviewer` greps
- [docs/process/tdd-policy.md](../../docs/process/tdd-policy.md) — when test-first is hard-required vs. pragmatic
- [docs/process/quality-gates.md](../../docs/process/quality-gates.md) — Gate 0 (evidence) + Gate 5 (tests)
- [docs/architecture/patterns.md](../../docs/architecture/patterns.md) — canonical shapes (handlers, validators, specs, errors, money)
- [docs/process/ticket-lifecycle.md](../../docs/process/ticket-lifecycle.md) — where the status log that proves red→green lives
- [.claude/agents/qa.md](../../.claude/agents/qa.md) — execution owner · [.claude/agents/reviewer.md](../../.claude/agents/reviewer.md) — Gate 5 walker · [.claude/agents/dotnet-backend.md](../../.claude/agents/dotnet-backend.md) — implementer
