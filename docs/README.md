# Makables — project system of record

This `/docs` tree is the source of truth for **how** Makables is built and **why**; the codebase is the source of truth for **what** is built.

Audience: the sub-agent team (charters in [`../.claude/agents/`](../.claude/agents/)) and any human collaborator. Start with [`WAY-OF-WORKING.md`](./WAY-OF-WORKING.md).

## Folder map

| Folder | Purpose | Owned by |
|---|---|---|
| [`adr/`](./adr/) | 27 Architecture Decision Records — numbered, immutable once accepted; superseded by a new ADR, never edited | Architect |
| [`architecture/`](./architecture/) | Living architecture: [`overview.md`](./architecture/overview.md), [`patterns.md`](./architecture/patterns.md) (the canonical pattern catalog), role catalog | Architect |
| [`tickets/`](./tickets/) | 146 tickets + [`INDEX.md`](./tickets/INDEX.md), the backlog manifest | PM |
| [`user-stories/`](./user-stories/), [`personas.md`](./personas.md), [`glossary.md`](./glossary.md) | Who the users are, what they need, what the terms mean | BA |
| [`test-plans/`](./test-plans/) | Per-feature manual + automated test plans | QA |
| [`security/`](./security/) | Scoping audits, webhook verification, secret hygiene | SecOps |
| [`deployment/`](./deployment/) | [`local-dev.md`](./deployment/local-dev.md), [`deploy-runbook.md`](./deployment/deploy-runbook.md), [`env-vars.md`](./deployment/env-vars.md), OAuth provider setup, incident post-mortems | SecOps |
| [`runbooks/`](./runbooks/) | Operating the live system: monitoring, backup/restore, secret rotation | SecOps |
| [`audits/`](./audits/) | Cross-cutting audit reports | Reviewer / Optimizer |
| [`review/`](./review/) | Review checklists, definition of done | Reviewer |
| [`status/`](./status/) | Sprint status reports | PM |
| [`l10n/`](./l10n/), [`meetings/`](./meetings/) | Copy decisions; deliberation records | L10n / all |
| [`questions/open.md`](./questions/open.md) | **Open questions escalated to the operator.** Batched, reviewed at checkpoints | all agents append |
| [`launch-checklist.md`](./launch-checklist.md) | Blocking pre-launch items that only the operator can resolve | PM |
| [`HANDOFF.md`](./HANDOFF.md) | The discovery-phase sign-off package (historical) | Architect + PM + BA |

**Process docs live in [`../agents/process/`](../agents/process/)**, not here — that tree is the canonical agent operating system (routing, ticket lifecycle, quality gates, deliberation, communication, enforcement, shared-file lanes), alongside [`../agents/knowledge/`](../agents/knowledge/) and [`../agents/templates/`](../agents/templates/).

## Where the project stands

Discovery and Phases 1–6 are behind us: 138 of 146 tickets are `done`. What remains is the end-to-end path ticket (T-0153, in progress), four post-launch capability tickets, three v1.1 candidates, and the operator inputs in [`launch-checklist.md`](./launch-checklist.md). Current state per ticket is always [`tickets/INDEX.md`](./tickets/INDEX.md) — sprint reports in [`status/`](./status/) are point-in-time snapshots and lag it.

## Reading order for a new agent

1. [`../CLAUDE.md`](../CLAUDE.md) — the working agreement and the quality bar
2. `../.claude/agents/<your-role>.md` — your charter
3. [`../agents/process/ticket-lifecycle.md`](../agents/process/ticket-lifecycle.md) — where you fit
4. [`architecture/patterns.md`](./architecture/patterns.md) — the shape your code must take
5. The ADRs that apply to your work, then your ticket
