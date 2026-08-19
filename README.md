# Makables

**Where Ideas Take Shape.** — a marketplace for Czech makers (3D print, classic print, textile, laser/CNC, large format, handmade).

- Domain: [makables.cz](https://makables.cz) *(pre-launch)*
- Operator: JVM YORE s.r.o.
- Cloud: Azure, West Europe

## What this repo is

A **dual-stack monorepo**:

- The **.NET 10 backend is the system of record** — business logic, money math, state transitions, validation, invoicing, payouts, every third-party integration.
- The **Next.js 16 frontend is a pure presentation layer** — it calls the backend through an NSwag-generated TypeScript client. No database access, no business logic, no third-party API calls.

The pivot from a Next.js + Supabase monolith to this arrangement is recorded in [ADR 0007](./docs/adr/0007-stack-pivot-dotnet-backend.md).

## Repository layout

```
makables/
├── backend/src/            # Makables.Api.slnx — 17 projects
│   ├── Makables.Core.Domain/          # aggregates, value objects, repo interfaces (no third-party deps)
│   ├── Makables.Core.AppServices/     # MediatR use cases, validators, DTOs (no EF Core)
│   ├── Makables.Config/               # shared host wiring: auth, DI, middleware, observability
│   ├── Makables.Infra.{Common,Database,Clients,PdfRendering}/
│   ├── Makables.Infra.Azure.Storage.Blobs/
│   ├── Makables.Web.{Customer,Maker,Admin,Public}/   # four per-audience API hosts
│   ├── Makables.Functions/            # Azure Functions v4 — outbox, payouts, timers
│   ├── Makables.Tools.Seeder/         # realistic CZ dev dataset
│   └── Makables.{Tests,IntegrationTests,TestUtilities}/
├── frontend/               # Next.js 16 App Router — (public) (auth) (customer) (maker) (admin)
├── docs/                   # ADRs, tickets, stories, architecture, runbooks — project system of record
├── agents/                 # agent operating system: process, knowledge, templates
├── .claude/                # agent charters + slash commands
├── infra/bicep/            # Azure IaC (Postgres, App Services, Functions, Blob, Key Vault, App Insights)
├── deploy/load-tests/      # k6 load test
├── scripts/                # run-dev.ps1, check-consistency.mjs
└── .github/workflows/      # ci, deploy-staging, deploy-production, ops-diagnostics
```

## Getting started

**Prerequisites:** .NET 10 SDK · Node 20+ · Postgres 16 on `localhost:5432` (db `makables_dev`, `postgres`/`postgres`) · Azurite (optional — only uploads and outbox dispatch need it).

```bash
# 1. backend — all four hosts, each in its own window
pwsh scripts/run-dev.ps1

# 2. seed a realistic CZ dataset (55 makers, products in every category, orders in every state)
dotnet run --project backend/src/Makables.Tools.Seeder -- --migrate

# 3. frontend
cd frontend && npm install && npm run dev
```

| Host | URL | Serves |
|---|---|---|
| Customer | http://localhost:5001 | login, orders, checkout |
| Maker | http://localhost:5002 | maker dashboard |
| Admin | http://localhost:5003 | admin ops |
| Public | http://localhost:5104 | catalog, product pages, webhooks |
| Frontend | http://localhost:3000 | the app |

Each host exposes `/openapi/v1.json`. The frontend defaults to exactly these ports — no env vars needed locally. Full detail, including the traps: [`docs/deployment/local-dev.md`](./docs/deployment/local-dev.md).

## Tests and contract

```bash
dotnet test backend/src/Makables.Api.slnx     # unit + integration (integration needs Postgres)
cd frontend && npm run test                   # vitest + jest-axe
cd frontend && npm run generate:api           # regenerate the NSwag client from the running hosts
cd frontend && npm run check:api              # CI parity check (ADR 0022)
```

`frontend/src/lib/api-client/` is **generated** — a pre-commit hook blocks manual edits. Any backend contract change regenerates it in the same PR.

CI ([`.github/workflows/ci.yml`](./.github/workflows/ci.yml)) runs four jobs on every PR: backend build + tests, frontend typecheck/lint/test/build, NSwag spec parity against live hosts, and Bicep lint.

## Documentation entry points

- [`docs/WAY-OF-WORKING.md`](./docs/WAY-OF-WORKING.md) — one-page tour: request → shipped code
- [`docs/README.md`](./docs/README.md) — index of the whole `docs/` tree
- [`docs/architecture/patterns.md`](./docs/architecture/patterns.md) — canonical pattern catalog (C# + TypeScript), the source of truth for shapes
- [`docs/architecture/overview.md`](./docs/architecture/overview.md) — system shape
- [`docs/adr/`](./docs/adr/) — 27 numbered architectural decisions
- [`docs/tickets/INDEX.md`](./docs/tickets/INDEX.md) — the backlog manifest
- [`docs/deployment/deploy-runbook.md`](./docs/deployment/deploy-runbook.md) · [`docs/runbooks/`](./docs/runbooks/) — deploy, monitoring, backup/restore, secret rotation
- [`CLAUDE.md`](./CLAUDE.md) — guardrails for AI agents working on this codebase
- [`agents/README.md`](./agents/README.md) — how the agent team is wired

## Status

**Build phase, near feature-complete for launch.** 138 of 146 tickets are `done` — Phases 1–6 shipped: identity and auth, maker onboarding with ARES lookup, catalog, orders with the full state machine, Comgate payments, invoicing (QuestPDF), Packeta shipping, payouts, disputes, reviews, admin ops, GDPR erasure, observability.

Open work:

| Ticket | What | State |
|---|---|---|
| T-0153 | Complete the core marketplace path end-to-end | in_progress |
| T-0163 | Maker-proposed categories with admin approval | draft |
| T-0142 | Stripe Connect escrow — KYC, release-after-delivery, refunds | draft |
| T-0143 | Invoicing in the maker's name + per-maker VAT | draft |
| T-0148 | Maker SLA timers + three-tier sanctions | draft |
| T-0149 / T-0150 / T-0151 | Cart, quote calculator, newsletter — v1.1, **not** launch-blocking | draft |

Go-live is additionally gated on operator inputs that no code can supply — approved legal text, GitHub deploy secrets, and OAuth provider registration. The gating list is [`docs/launch-checklist.md`](./docs/launch-checklist.md).

## How the work gets done

A team of specialized AI sub-agents ([`.claude/agents/`](./.claude/agents/)) coordinates entirely through Git-tracked artifacts: tickets in [`docs/tickets/`](./docs/tickets/), decisions in [`docs/adr/`](./docs/adr/), questions in [`docs/questions/open.md`](./docs/questions/open.md). Entry point is `/team <request>`; narrower commands are `/plan`, `/feature`, `/execute`, `/review`, `/audit`, `/sync` (see [`.claude/commands/`](./.claude/commands/)). The machinery is documented in [`agents/`](./agents/).
