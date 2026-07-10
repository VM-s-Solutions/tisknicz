# Makables — The Way of Working

> Read this once and you'll understand the whole team. It's the human-facing companion to
> [`../.claude/agents/README.md`](../.claude/agents/README.md) (the roster) and the
> [`../docs/process/`](../docs/process/) docs (the rules).

You're building Makables — a Czech marketplace platform, `makables.cz`, operated by JVM YORE s.r.o.
It's a dual-stack monorepo: a .NET backend across four API hosts plus Azure Functions, and a Next.js
frontend that is pure presentation. It's not small, and it's meant to run itself once live — a
self-running marketplace with minimal manual intervention. You want to *delegate by typing* — hand a
task to a team of specialized agents that analyze it, spec it, build it, review it, test it, harden
it, and localize it, coordinating with each other and tracking status — without you having to
micromanage. This document is how that team works.

---

## 1. The one-screen picture

```
  YOU ──"natural language request"──►  ORCHESTRATOR (the main Claude session)
                                            │ hands the request to the PM
                                            ▼
                                          ┌────┐
                                          │ PM │  owns the backlog + state, sequences everything
                                          └─┬──┘
            ┌──────────────┬───────────────┼───────────────┬──────────────┐
            ▼              ▼                ▼               ▼              ▼
           BA         ARCHITECT     DOTNET-DB → DOTNET-    FRONTEND        L10N
      (user stories) (ADRs+patterns)   BACKEND           (Next.js)     (cs-CZ keys)
            │              │         (schema, CQRS)         │              │
            └──────────────┴───────┬────────┴──────────────┴──────────────┘
                                   │  ◄── a REVIEWER runs IN PARALLEL with every developer
                                   ▼
                         SECOPS · OPTIMIZER · QA   (the merge gates)
                                   │
                                   ▼
                         PM merges ──► ticket: done ──► picks the next
```

Everything they say to each other is a **file in Git**. There is no hidden chat. If a decision isn't
written down, it didn't happen. That's what makes a platform meant for real money — real customers,
real payouts, JVM YORE s.r.o.'s name on the invoice — safe to change with a team of agents.

---

## 2. The team (and why it's shaped this way)

| Agent | What it does for you |
|---|---|
| **Orchestrator** | The session you type into. Relays your request to the PM and reports back. The only one you talk to. |
| **PM** | Turns your request into tickets, decides the order, spawns the right specialists + a reviewer alongside each, runs the gates, tells you progress. The conductor. |
| **BA** | Writes the user story with crisp Given/When/Then criteria so "build X" can't be misinterpreted. Also finds *missing* functionality during audits. |
| **Architect** | Makes the decisions that are expensive to undo (ADRs) and owns the pattern catalog so everyone builds the same way. |
| **DB (dotnet-db)** | Owns the Postgres schema, EF Core configs, indexes, query filters, seeds. Describes migrations — **you** run them. |
| **Backend Dev (dotnet-backend)** | The .NET / CQRS / MediatR features across the four API hosts, the provider adapters (Comgate / Packeta / ARES / SendGrid / Mapbox), invoicing, payouts, Azure Functions. |
| **Frontend Dev (frontend)** | The Next.js 16 App Router app — Server Components, forms, the NSwag-generated API client. Pure presentation; no business logic, no DB. |
| **L10n** | Owns `lib/i18n/cs-CZ` — a Czech key for every user-facing string and every `BusinessErrorMessage` code. Czech-only at launch, multi-country-ready. |
| **QA** | Writes and runs test plans; adds automated tests for money/state logic; finds regressions. |
| **Reviewer** | Gatekeeps every change — runs **in parallel** with the developer, exactly as you asked. |
| **SecOps** | Hunts auth/ownership/PII/tenancy/idempotency holes; owns env vars, webhook verification, and the Azure topology. |
| **Optimizer** | Hunts N+1s, slow queries, bundle bloat, render churn. Files cleanup tickets; never blocks a PR. |

**Why one charter per role (not `dotnet-backend-1`, `dotnet-backend-2`):** you want multiple
developers of a role working in parallel so each can focus. We get that by **spawning multiple live
instances of the same charter at runtime** — the PM can run `dotnet-backend #1` on one feature and
`dotnet-backend #2` on another at the same time, each with its own reviewer. But the *definition*
stays in one file, so when a CQRS rule changes you edit it once and every instance gets it. Copies
would silently drift; instances don't.

**Why a reviewer runs in parallel with every developer:** you asked for this specifically. The
developer writes the change while a reviewer instance reads the same ticket and diff and writes a
verdict concurrently. The PM merges the two before the ticket advances. Review is a companion, not a
bottleneck.

The full charters live in [`../.claude/agents/`](../.claude/agents/); each has a CRC card in
[`../docs/architecture/roles/`](../docs/architecture/roles/). Routing rules — which agent owns
which ticket — live in [`../docs/process/routing.md`](../docs/process/routing.md).

---

## 3. How a request becomes shipped code

You type, for example:

> "Add the admin UI for per-maker fee-rate overrides, and make sure cancelling an order can't be done
> by the wrong user."

Here's what happens — every step is a file you can open:

1. **PM** reads the backlog, splits this into tickets:
   `T-NNNN per-maker fee-rate override admin UI` and `T-NNNN audit & fix order-cancel ownership`.
   Each gets a file in [`../docs/tickets/`](../docs/tickets/) and a row in
   [`INDEX.md`](../docs/tickets/INDEX.md).
2. For the fee-override ticket, behavior is slightly fuzzy → **BA** writes a user story in
   [`../docs/user-stories/`](../docs/user-stories/) with exact AC. The fee-override layering touches
   a seam → **Architect** confirms or writes an ADR so it composes over `CountryConfiguration`
   without recomputing settled history.
3. **DB** designs the schema delta for the override (flags `manual_step: ef-migration` — you run it).
   **Backend Dev** writes the command/query/validator/handler + DTO (flags `manual_step: nswag-regen`
   — you regenerate the client). A **Reviewer** instance reviews each in parallel.
4. Once the contract is locked and you've regenerated the client, **Frontend Dev** builds the admin
   tab, **L10n** adds the `cs-CZ` keys, with a Reviewer alongside.
5. The cancel-ownership ticket is `security_touching` → **SecOps** walks the auth/ownership rules
   across `CancelOrder`, names the exact hole if any ("maker X can cancel customer Y's order — no
   ownership check at line N"), Backend Dev fixes it, SecOps re-verifies.
6. **QA** writes and runs the test plans (including the cross-user cancel attempt → must be rejected).
7. **PM** confirms every gate is green, marks the tickets `done`, updates the sprint status, and —
   because there were manual steps — has already flagged them to you. Nothing is committed/pushed
   unless you ask.

You watched none of the mechanics. You read [`../docs/status/`](../docs/status/) when you want the
summary, and [`../docs/questions/open.md`](../docs/questions/open.md) if the team needed a decision
from you.

The full state machine a ticket walks — `draft → ready → in_progress → in_review → qa → done`
(or `blocked`) — and the shared-file lanes that keep parallel developers from colliding are in
[`../docs/process/ticket-lifecycle.md`](../docs/process/ticket-lifecycle.md) and
[`../docs/process/communication.md`](../docs/process/communication.md).

---

## 4. When the team needs *you*

The team is autonomous but not reckless. It escalates a decision to you (and only you) when it
genuinely can't be derived from the code, the docs, or a sensible default — by writing a question to
[`../docs/questions/open.md`](../docs/questions/open.md). Blocking questions surface at the next
checkpoint; non-blocking ones proceed on a documented default. When you answer, the decision is
locked into an ADR/story/charter so it's never asked again.

You're also the only one who runs the two **owner-only** steps (per your
[`CLAUDE.md`](../CLAUDE.md)): **EF Core migrations** and **NSwag client regeneration**. The agents
detect when these are needed, describe the exact delta, flag them on the ticket, and hold dependent
work until you confirm. NSwag is the contract — any backend contract change regenerates
`frontend/src/lib/api-client/` in the same PR, and CI verifies parity.

---

## 5. The quality bar (because you're going to PROD)

You said it plainly: not in production yet, so fix things *now*, for the long game, not with
throwaway patches. Once live, a schema migration costs downtime, a contract change costs a
regen-and-deploy of every host, and a money-rounding mistake costs trust. That bias toward long-term
flexibility is baked in:

> Would I run this unattended for a Czech marketplace handling real money, real customers, with
> JVM YORE s.r.o.'s name on the invoice?

That's the bar. It is not "does it compile" or "does the happy path work."

- **[`CLAUDE.md`](../CLAUDE.md)** sets that "would I run this unattended in production" bar, and
  forbids temporary workarounds, hardcoded strings, `any`/`dynamic`, and magic numbers. Money is
  `long` minor units with an explicit `Currency`; VAT rates are basis points; rounding is half-up;
  every monetary column ends `_minor` and is paired with `currency CHAR(3) NOT NULL`.
- **[`../docs/architecture/patterns.md`](../docs/architecture/patterns.md)** is the canonical pattern
  catalog — Clean Architecture layering, CQRS-per-file, `BusinessResult<T>`, pipeline behaviors,
  `Auditable` soft-delete, the provider adapter pattern, `CountryConfiguration` lookups. Everyone
  builds the same way because they all read the same catalog.
- **SecOps** enforces the security laws — auth/ownership on every protected endpoint, JWT audience
  enforced per host so a customer token can't be replayed against the maker API, webhooks that verify
  origin/signature before any side effect, idempotent webhook state transitions in a single
  transaction, no secrets in the client bundle.
- **[`../docs/process/quality-gates.md`](../docs/process/quality-gates.md)** is the checklist a
  change clears before `done`: self-check, AC evidence, security, architecture seams, performance,
  tests, contract/docs parity.
- The **Optimizer** keeps the running cost down (N+1s, indexes, bundle size) so scale doesn't hurt.

The seniority is encoded once, in the docs, and every developer agent reads it first. The
architectural decisions that got us here — especially the Supabase → .NET pivot in
[ADR-0007](../docs/adr/0007-stack-pivot-dotnet-backend.md) — are numbered and permanent under
[`../docs/adr/`](../docs/adr/).

---

## 6. The audit job

When you want to know what's missing, half-built, spaghetti, hardcoded, insecure, or slow before a
milestone, the PM fans an audit out **wide and in parallel**: one BA per subsystem looking for
functional gaps, the Reviewer / SecOps / Optimizer sweeping their dimensions, each writing ranked
findings to [`../docs/audits/`](../docs/audits/). The PM then converts every finding into a
prioritized ticket in [`INDEX.md`](../docs/tickets/INDEX.md), and the build-fix loop above takes
over. You get one audit report and a ready-to-execute backlog, not a wall of raw output. The
[`/audit`](../.claude/commands/audit.md) command drives it.

---

## 7. How you drive it day to day

- **To start work:** just describe what you want, in plain language. The Orchestrator hands it to the
  PM. Or be explicit with a command: [`/feature <intent>`](../.claude/commands/feature.md) to cut a
  new ticket, [`/plan <intent>`](../.claude/commands/plan.md) to shape an unprecedented choice
  without writing code first, or `continue with next ticket` / [`/execute next`](../.claude/commands/execute.md)
  to run the next `ready` ticket whose dependencies are `done`.
- **To check status:** read [`../docs/status/`](../docs/status/) and
  [`INDEX.md`](../docs/tickets/INDEX.md), or ask the PM.
- **When the contract moves:** [`/sync`](../.claude/commands/sync.md) regenerates the frontend client
  from the OpenAPI surface. A pre-commit hook blocks manual edits to `lib/api-client/`.
- **To fire a manual review:** [`/review T-NNNN`](../.claude/commands/review.md).
- **To answer the team:** edit [`../docs/questions/open.md`](../docs/questions/open.md).
- **To change how the team works:** edit a charter in [`../.claude/agents/`](../.claude/agents/) or a
  [`../docs/process/`](../docs/process/) doc — it takes effect on the next invocation, and the change
  is reviewable in Git like any code.

That's the whole system. Approve it, tweak any charter you'd run differently, or tell the PM to begin
the next ticket.
