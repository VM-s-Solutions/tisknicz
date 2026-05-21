# Sub-agents

This folder defines the specialized agents used during the Makables build. Each agent has a charter file describing its role, the artifacts it owns, what it consumes, who can invoke it, and what "done" looks like for its work.

## Roster (post-pivot)

| Agent | File | Charter |
|---|---|---|
| Project Manager | `pm.md` | Owns the backlog and sprint state; sequences tickets |
| Business Analyst | `ba.md` | Turns intent into user stories with AC |
| Solution Architect | `architect.md` | Owns ADRs, system design, extension points |
| .NET Backend Developer | `dotnet-backend.md` | Implements `/backend/` — Core.Domain, Core.AppServices, Web hosts, Infra adapters |
| EF Core / DB | `dotnet-db.md` | Owns Postgres schema, EF Core entity configurations, migrations, query filters, seeds |
| Frontend Developer | `frontend.md` | Implements `/frontend/` — Next.js pages, components, forms; consumes NSwag client |
| Localization | `l10n.md` | Owns translation catalogs and copy review (parity with backend `BusinessErrorMessage` codes) |
| Tester | `qa.md` | Writes test plans, executes manual + automated checks, reports defects |
| Code Reviewer | `reviewer.md` | Gatekeeps PRs against CLAUDE.md, ADRs, AC |
| Security & DevOps | `secops.md` | Audits auth, webhooks, secrets, deploy config; owns env vars and Azure topology |

## Stack reality (governs every agent)

Makables is a **dual-stack monorepo**:
- `/backend/` — .NET 10, Clean Architecture, CQRS via MediatR, EF Core, Postgres, custom auth, Azure Blob/Functions.
- `/frontend/` — Next.js 16 App Router, **pure presentation layer**. Calls the backend through an NSwag-generated TypeScript client.

The pivot from Supabase + Next.js is recorded in [ADR 0007](../../docs/adr/0007-stack-pivot-dotnet-backend.md). Read it before doing anything that touches the stack.

## How agents are invoked

The main Claude Code orchestrator invokes a sub-agent via the `Agent` tool with `subagent_type` matching the file's frontmatter `name`. Each agent's charter is loaded as its system prompt.

Communication is **artifact-based** — no agent-to-agent chat. See [`docs/process/communication.md`](../../docs/process/communication.md).

## How to modify a charter

Edit the charter file. The change takes effect on the next invocation. Charters are versioned in Git so changes are reviewable.

If a charter's name changes (e.g. `backend` → `dotnet-backend`), every reference to that name elsewhere in the repo must be updated. The PM is responsible for keeping `docs/process/*.md` in sync.
