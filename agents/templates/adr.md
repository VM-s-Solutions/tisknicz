# ADR-NNNN — <decision title>

- **Status:** proposed   <!-- proposed | accepted | superseded | rejected -->
- **Date:** YYYY-MM-DD
- **Supersedes:** —       <!-- ADR number, if any -->
- **Superseded by:** —
- **Applies to:** backend | frontend | cross-cutting

## Context
The forces at play: the requirement, the constraint, the seam under pressure. What makes this a
decision rather than an obvious choice. State whether the pressure lands on the .NET backend
(`backend/src/Makables.Api.slnx`), the Next.js frontend (`frontend/`), or both. If the decision
touches an extension point — payments (Comgate), shipping (Packeta), registry (ARES), email
(SendGrid), geocoder (Mapbox) — name it, because the reviewer will check the adapter seam holds.

## Decision
The single decision (one ADR = one decision). The interface sketch / pattern, with the catalog
section it adopts or adapts (cite `docs/architecture/patterns.md §A.N` for backend, `§B.N` for
frontend). If the decision varies by country, it drives `CountryConfiguration` — never branch on
country directly. Money stays `long` minor units + `string Currency`; VAT rates stay basis points.

## Alternatives considered
Mandatory: at least **2** alternatives, each with a one-line "what it was" + one-line "why
rejected". This captures the deliberation trail so a future reader does not re-litigate a settled
choice. See [deliberation.md](../../docs/process/deliberation.md).

- **Option A** — why not.
- **Option B** — why not.

## Consequences
What gets cheaper and what gets more expensive because of this. New obligations on developers.
If the decision touches per-audience hosts, say which of the four are affected: `Web.Customer`
(5001), `Web.Maker` (5002), `Web.Admin` (5003), `Web.Public` (5104), or the Azure Functions jobs.

## How a reviewer verifies compliance
Concrete checks the [reviewer](../../.claude/agents/reviewer.md) / [architect](../../.claude/agents/architect.md)
runs to confirm a change honors this ADR. Cite the pattern section (`§A.N` / `§B.N`) so the check
is mechanical. If a contract changed, the check includes NSwag client parity
(`frontend/src/lib/api-client/` regenerated in the same PR — see
[ADR 0022](../../docs/adr/0022-nswag-pipeline.md)).

## Roles affected
Role files in `docs/architecture/roles/` created or updated by this decision (per the
Responsibility-Driven Design discipline in [ADR 0015](../../docs/adr/0015-responsibility-driven-design.md)).
Every new aggregate, value object, domain service, repository interface, or adapter interface this
ADR introduces gets a CRC-card role file.

## Defense
*Populated only if the decision is challenged.* Append-only log of rebuttals, concessions, or
escalations per [deliberation.md](../../docs/process/deliberation.md). One `### Challenge` block per
challenge; the author responds `rebut | concede | escalate`; evidence beats opinion.
