# Makables Agent Operating System

> A team of specialized AI sub-agents that pick up tasks, analyze them, write user stories,
> implement across .NET backend and Next.js frontend, localize, test, review, harden, and keep the
> docs honest — coordinating entirely through Git-tracked artifacts.

This folder is the **operating system** for the Makables engineering team of agents. It is the
single source of truth for *how the team works*. The agents themselves (their system prompts) live
in [`.claude/agents/`](../.claude/agents/); the canonical *process, architecture, and backlog*
knowledge lives in [`docs/`](../docs/). This `agents/` tree holds the machinery that stitches them
together — the roster, the scaffolding for role-owned living docs, and the working backlog state.

If you are a human: start with [`docs/WAY-OF-WORKING.md`](../docs/WAY-OF-WORKING.md).
If you are an agent: read your charter in `.claude/agents/<your-name>.md`, then the process docs
under [`agents/process/`](./process/), then the ticket you were handed.

---

## The mental model in one paragraph

You (the owner) type a request in natural language, or drive one of the slash commands in
[`.claude/commands/`](../.claude/commands/) (`/plan`, `/feature`, `/execute`, `/sync`, `/review`,
`/audit`). The **orchestrator** (the main Claude Code session) hands it to the **PM**, who turns it
into one or more **tickets** in [`docs/tickets/`](../docs/tickets/). Each ticket has a state machine
(`draft → ready → in_progress → in_review → qa → done`). The PM routes each ticket to the right
**specialist** (ba, architect, dotnet-db, dotnet-backend, frontend, l10n). A **reviewer** runs
**in parallel** with every developer. **secops** and **qa** gate the merge. Nothing is verbal —
every decision, hand-off, and status change is a file in Git. When the team is blocked, it writes a
question to [`docs/questions/open.md`](../docs/questions/open.md) and surfaces it to you.

---

## Roster

The team is defined as **one charter per role** (DRY). The orchestrator/PM spawns **multiple
concurrent instances** of the same charter when work fans out — e.g. three `dotnet-backend` agents
on three independent features, each with a `reviewer` running alongside. Concurrency is a *runtime*
decision; the charter is the *definition*.

| Agent | Charter | Owns | One-line role |
|---|---|---|---|
| **Orchestrator** | *(the main session)* | routing | Receives your request, invokes the PM, relays status. The only agent you talk to. |
| **PM** | `pm.md` | `docs/tickets`, `docs/status`, ticket state | Owns the backlog & sprint state; sequences work; the only agent that reports progress up. |
| **BA** | `ba.md` | `docs/user-stories`, `docs/questions/open.md` | Turns intent into user stories with Given/When/Then acceptance criteria. |
| **Architect** | `architect.md` | `docs/adr`, `docs/architecture/*`, `docs/architecture/roles` | Owns Architecture Decision Records, the pattern catalog, and the responsibility map (CRC cards). |
| **.NET Backend Dev** | `dotnet-backend.md` | `backend/src/Makables.Core.*`, `Makables.Web.*`, `Makables.Infra.Clients`, `Makables.Functions` | Implements .NET 10 / CQRS / MediatR features, controllers, provider adapters, and background jobs. |
| **EF Core / DB** | `dotnet-db.md` | `backend/src/Makables.Infra.Database`, migrations, entity configs | Owns the Postgres schema, EF Core configurations, migrations, query filters, indexes, seeds. |
| **Frontend Dev** | `frontend.md` | `frontend/` (App Router pages, components, forms) | Implements Next.js 16 / React 19 / Tailwind 4; consumes the NSwag-generated client. |
| **Localization** | `l10n.md` | `frontend/src/lib/i18n/cs-CZ`, copy review | Owns Czech translation catalogs; keeps a key for every `BusinessErrorMessage` code. |
| **QA** | `qa.md` | `docs/test-plans` | Writes test plans, executes against running hosts / preview, adds automated tests, reports defects. |
| **Reviewer** | `reviewer.md` | review verdicts, `docs/review/runs` | Gatekeeps every change against CLAUDE.md, the conventions, ADRs, and AC. Runs in parallel with devs. |
| **SecOps** | `secops.md` | `docs/security`, env vars, Azure topology | Audits auth, ownership, PII, tenancy, idempotency, webhooks, secrets, rate-limits; owns deploy config. Gates security-touching work. |
| **Optimizer** | `optimizer.md` | optimization reports | Hunts performance & cost: N+1s, bundle size, render churn, slow queries, allocations. Files cleanup tickets; never blocks a PR. |

### Why one charter per role (and not `dotnet-backend-1`, `dotnet-backend-2`)

A CQRS rule changes once → it changes in one file. Named duplicates drift: `dotnet-backend-1.md`
and `dotnet-backend-2.md` slowly disagree, and the reviewer can't tell which is canonical. We get
parallelism from **spawning N instances of the one charter at runtime**, not from copying the
charter N times. Where a role genuinely splits by surface (e.g. `frontend` across `Web.Customer`,
`Web.Maker`, `Web.Admin`, `Web.Public`), the charter documents each surface and the PM scopes each
instance to one.

---

## Folder map

The operating system spans two trees. `docs/` is the **canonical knowledge** the whole team reads
and cites; `agents/` is the **working machinery** — role-owned living docs and churning backlog
state that would pollute the published architecture docs if mixed in.

```
agents/
├── README.md                 # this file — the roster & map
├── analysts/                 # BA living docs: business logic + Mermaid diagrams, per domain
├── architecture/
│   └── decisions/            # architect living decision docs (immutable ADRs stay in docs/adr/)
├── backlog/
│   ├── questions/            # working escalation notes (canonical inbox is docs/questions/open.md)
│   └── status/               # working sprint state (canonical reports are docs/status/sprint-N.md)
└── templates/
    └── story.md              # user-story template the BA fills

docs/                         # the canonical, cited knowledge base (source of truth)
├── WAY-OF-WORKING.md         # human-facing guide to the whole flow (read this first)
├── README.md                 # the /docs tree map
├── process/
│   ├── ticket-lifecycle.md   # state machine + Definition of Ready + parallelism
│   ├── deliberation.md       # defense panels: author defends story/ADR vs challengers → consensus
│   ├── quality-gates.md      # the gates a change passes before "done"
│   ├── communication.md      # artifact-based protocol; escalation; no agent chat
│   ├── routing.md            # how the PM decides which agent gets the work
│   ├── discovery.md          # the structured interview that seeds personas/stories/ADRs
│   ├── tdd-policy.md         # when tests come first (Gate 5)
│   └── must-cover-tests.md   # the must-cover list (payments, order lifecycle, authz, money…)
├── architecture/
│   ├── overview.md           # system shape: four hosts + Functions
│   ├── patterns.md           # the canonical pattern catalog (backend C# + frontend TS)
│   ├── multi-country.md      # CountryConfiguration-driven variation; CZ-only at launch
│   └── roles/                # responsibility map (CRC cards) per aggregate / service / adapter
├── adr/                      # NNNN-*.md — immutable architecture decisions
├── personas.md               # who the users are (customer, maker, admin)
├── user-stories/            # US-<persona>-NNNN-*.md — user stories with AC, per persona folder
├── tickets/
│   ├── INDEX.md              # the manifest — every ticket, one row, current state
│   └── T-NNNN-*.md           # one file per unit of work
├── test-plans/               # QA test plans & results
├── security/                 # audit checklists & findings (secops)
├── review/                   # review checklist + per-run verdicts under review/runs/
├── status/                   # sprint-N.md — progress reports for the owner
└── questions/open.md         # the escalation inbox — open questions surfaced to you
```

> **Why split `agents/` from `docs/`?** `docs/` is the published, cited knowledge base — the
> architecture, ADRs, patterns, and the frozen backlog everyone links to. The `agents/` tree is
> internal churn: role-owned working docs and live backlog state. Keeping them apart means a link to
> an ADR or a pattern is stable, while the day-to-day machinery stays out of the way. The canonical
> *architecture* knowledge lives in [`docs/architecture/*.md`](../docs/architecture/); anything under
> `agents/` **references** it rather than duplicating it (one source of truth).

---

## How an agent is invoked

The orchestrator or PM invokes a sub-agent via the `Agent` tool with `subagent_type` matching the
charter's frontmatter `name`. The charter is loaded as that agent's system prompt. The agent then
reads, in order:

1. Its own charter (`.claude/agents/<name>.md`)
2. [`CLAUDE.md`](../CLAUDE.md) (project guardrails — the non-negotiable rules)
3. [`docs/architecture/patterns.md`](../docs/architecture/patterns.md) — the pattern catalog for its stack
4. The ticket it was handed (and any ADRs / stories it links)

Communication is **artifact-based** — agents never chat with each other. See
[`agents/process/communication.md`](./process/communication.md).

---

## The contract that governs the whole flow

Two rules bind the two stacks together and are enforced by every gate:

- **NSwag is the contract.** The backend's OpenAPI surface generates the TypeScript client in
  `frontend/src/lib/api-client/`. Any backend contract change (controller signature, request DTO,
  response DTO, or `BusinessErrorMessage` code) regenerates that client in the **same PR** — run
  `/sync`. The pre-commit hook blocks manual edits to that folder. CI verifies parity.
- **One PR per ticket.** Cross-stack changes ship atomically. A ticket that moves the schema, the
  contract, and the UI is one branch, one PR, one review pass.

The provider integrations the frontend must **never** call directly — Comgate (payments), Packeta
(shipping), ARES (company registry), SendGrid (email), Mapbox (geocoder) — all live behind adapters
in `backend/src/.../Infra.Clients/<Provider>/`, selected via `CountryConfiguration`. The future
Stripe Connect escrow pivot is recorded in [ADR 0027](../docs/adr/0027-marketplace-escrow-payments-stripe-connect.md)
and is **not built**; Comgate ([ADR 0016](../docs/adr/0016-payments-comgate.md)) is the launch
provider.

---

## Modifying the team

Edit a charter or a process doc; the change takes effect on the next invocation. Everything is in
Git, so every change to *how the team works* is reviewable like code. If you rename a charter, the
PM is responsible for updating every reference in `agents/process/`, `docs/tickets/`, and this roster.
