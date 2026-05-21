# Makables — Engineering Process

This `/docs` tree is the source of truth for **how** Makables is built, alongside the codebase which is the source of truth for **what** is built.

Audience: the sub-agent team (defined in `.claude/agents/`) and any human collaborator.

## Folder map

| Folder | Purpose | Owned by |
|---|---|---|
| `process/` | How we work: discovery, ticket lifecycle, quality gates, communication rules | PM |
| `personas.md`, `glossary.md` | Who the users are, what terms mean | BA |
| `user-stories/` | Stories per persona with acceptance criteria | BA |
| `adr/` | Architecture Decision Records (numbered, immutable once accepted) | Architect |
| `architecture/` | Living architecture docs: overview, extension points, money, multi-country | Architect |
| `tickets/` | Sized, sequenced work items with dependencies | PM |
| `test-plans/` | Manual & automated test plans, per feature | QA |
| `security/` | RLS audits, webhook verification, secret hygiene | SecOps |
| `review/` | Review checklists, definition of done | Reviewer |
| `status/` | Sprint status reports (PR-only checkpoints for the user) | PM |
| `questions/open.md` | **Open questions escalated to the user.** Batched, reviewed at checkpoints. | All agents append |

## Process phases

1. **Phase 0 — Setup** (this commit): agents defined, process docs in place.
2. **Phase 1 — Discovery**: BA runs a structured interview with the user; outputs personas, stories, ADRs.
3. **Phase 2 — Backlog freeze**: PM writes tickets with AC, dependencies, sizing; user signs off.
4. **Phase 3 — Autonomous build**: PM dispatches tickets; agents work to artifact contracts; user reviews PRs only.

See `process/discovery.md`, `process/ticket-lifecycle.md`, `process/quality-gates.md`, `process/communication.md`.

## Reading order for a new agent

1. `.claude/agents/<your-role>.md` — your charter
2. `docs/process/communication.md` — how to talk to other agents
3. `docs/process/ticket-lifecycle.md` — where you fit
4. `docs/architecture/overview.md` — the system at a glance
5. The ADRs that apply to your work
