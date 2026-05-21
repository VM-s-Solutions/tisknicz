# Makables

**Where Ideas Take Shape.** — a marketplace for Czech makers (3D print, classic print, textile, laser/CNC, large format, handmade).

- Domain: [makables.cz](https://makables.cz) *(pre-launch)*
- Operator: JVM YORE s.r.o.

## Repository layout

```
makables/
├── backend/             # .NET 10, Clean Architecture, CQRS, EF Core, Postgres
├── frontend/            # Next.js 16 App Router, pure presentation layer
├── docs/                # ADRs, user stories, tickets, architecture, process
├── .claude/agents/      # Sub-agent charters (PM, BA, Architect, devs, QA, Reviewer, SecOps, L10n)
├── CLAUDE.md            # Project guardrails for AI agents
├── TISKNI_MVP_SPEC.md   # Original MVP spec (legacy domain reference)
└── PROJEKT-VIZE.md      # Vision document
```

The repo is a **dual-stack monorepo**:
- The .NET backend is the system of record. Business logic, money math, state transitions, validation, invoicing, payouts.
- The Next.js frontend is a **pure presentation layer**. It calls the backend through an NSwag-generated TypeScript client. No server-side database access.

The pivot from a Next.js + Supabase monolith to this dual-stack arrangement is recorded in [ADR 0007](./docs/adr/0007-stack-pivot-dotnet-backend.md).

## Documentation entry points

- [`docs/README.md`](./docs/README.md) — index of process, ADRs, user stories, tickets
- [`docs/architecture/patterns.md`](./docs/architecture/patterns.md) — canonical patterns (backend C# + frontend TypeScript)
- [`docs/architecture/overview.md`](./docs/architecture/overview.md) — system shape
- [`docs/adr/`](./docs/adr/) — every architectural decision, numbered and dated
- [`CLAUDE.md`](./CLAUDE.md) — guardrails for AI agents working on this codebase

## Status

Phase 0.5 — pivot bookkeeping. The backend solution is not yet scaffolded. The frontend is moved under `/frontend/` but **does not run** until the backend exposes the API endpoints the pages expect. This is intentional (option 1 in pivot planning): nothing is mocked, so missing pieces are loudly visible.

Sprint status: [`docs/status/sprint-0.md`](./docs/status/sprint-0.md).

## How agents collaborate

Discovery → ADRs → backlog → autonomous build. See [`docs/process/discovery.md`](./docs/process/discovery.md). Each agent has a charter in [`.claude/agents/`](./.claude/agents/).
