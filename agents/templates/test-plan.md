# Test Plan — T-NNNN <ticket title>

- **Author:** qa
- **Date:** YYYY-MM-DD
- **Story:** US-<persona>-NNNN
- **Surface(s) under test:** customer / maker / admin / public / api
  (Makables ships four API hosts — `Web.Customer` :5001, `Web.Maker` :5002, `Web.Admin` :5003, `Web.Public` :5104 — plus Azure Functions. Name the host(s) the ticket touches; there is no mobile surface.)

Write this plan from the ticket's AC in parallel with implementation, then execute it once the PR opens against the Vercel preview deploy. Canonical output lives at `docs/test-plans/T-NNNN.md` (this template is the shape; the [`docs/test-plans/template.md`](../../docs/test-plans/template.md) frontmatter is the on-disk header). See the [qa charter](../../.claude/agents/qa.md) for evidence discipline (Gate 0) and priorities.

## Cases
One row per AC item, plus edge & negative cases. Order by the qa priority ladder: **AC verification → money math → state transitions → security/authorization → UI states (375/768/1280)**.

| # | Type | Given / When / Then | Expected | Result | Evidence |
|---|---|---|---|---|---|
| 1 | AC1 (happy) | ... | ... | PASS/FAIL | screenshot / log / test |
| 2 | edge | ... | ... | | |
| 3 | negative (authz) | cross-user access attempt (customer JWT replayed at maker host; or foreign `orderId` on own host) | rejected — 404 NotFound via the IDOR WHERE-predicate, or 401 at the per-audience host gate; **no existence leak** | | |
| 4 | money | pricing / fee / VAT / payout calc + rounding | exact `_minor` value; VAT in basis points; half-up (`AwayFromZero`) rounding; CZK display strips haléře | | |
| 5 | state | order state-machine transition (e.g. `PendingPayment → Paid` via Comgate webhook; auto-deliver; autocancel) | only the legal transition fires; idempotent replay returns the current state, no double side-effect | | |

Notes for filling the table:
- **Money** columns must reconcile against an independent aggregate — never trust the handler's own number. Every monetary value is `long` minor units + `string` currency; VAT rates are basis points. See [patterns.md](../../docs/architecture/patterns.md).
- **Provider-touching** cases run in test mode: Comgate (payments) via `COMGATE_TEST=true`, Packeta (shipping), ARES (registry), SendGrid (email), Mapbox (geocoder). All providers are backend adapters selected via `CountryConfiguration` — never called from the frontend. Note the flag/sandbox in Preconditions.
- **Webhooks** (Comgate, Packeta status sync): verify signature/origin, look up by `provider_ref`, assert idempotent replay is a no-op 200.
- **i18n**: Makables is `cs-CZ`-only at launch. Every asserted error surfaces a `BusinessErrorMessage` code with a parallel `cs-CZ` key — verify parity. Flag any hardcoded Czech to the [l10n](../../.claude/agents/l10n.md) agent.

## Automated tests added
- `backend/…/Tests/…/<Name>Tests.cs` — what it covers.

Pure logic (pricing, VAT/fee math, numbering, formatting, validation predicates, state-machine guards) is **TDD-enforced** — see [must-cover-tests.md](../../docs/process/must-cover-tests.md) and [tdd-policy.md](../../docs/process/tdd-policy.md). List every must-cover row and its matching passing test; a handler ships only when each must-cover item has proof (assert the red→green ordering in git log). Backend solution: `backend/src/Makables.Api.slnx`.

## Regression spot-checks
- Adjacent features touched by the shared code, and their result. Name the specific query/DTO/flow the shared change could perturb and confirm it still holds (e.g. a catalog DTO field that a new stat now populates; a state-machine caller that must not gain a new transition).
- If the API contract changed, confirm the NSwag-generated client under `frontend/src/lib/api-client/` was regenerated in the **same PR** and not hand-edited (`git diff --stat`). Contract parity is CI-enforced; see [routing.md](./routing.md) and CLAUDE.md.

## Defects found
- Repro steps, expected vs actual; raised to pm as <finding/ticket>.
- Obey **Gate 0** ([quality-gates.md](../../docs/process/quality-gates.md)): REFUTED-by-default. Cite file:line for the defect **and** the guard you confirmed is missing (state machine, idempotency key, options default, DB constraint, pipeline behavior), a concrete repro trigger, and the guard-check that would prevent it. A defect you cannot repro is a *question* for [docs/questions/open.md](../../docs/questions/open.md), not a finding. "Examined X/Y/Z, guard at file:line, no defect" is a valid, honest result — manufacturing findings to look thorough is the failure this gate exists to stop.
