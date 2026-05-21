# Ticket lifecycle

PM owns this. Every unit of work is a ticket file in `docs/tickets/T-NNNN-*.md`.

## States

```
draft → ready → in_progress → in_review → qa → done
                            ↓
                        blocked (with reason)
```

- **draft** — written but not refined; may lack AC or dependencies
- **ready** — AC complete, dependencies satisfied, sized, assigned
- **in_progress** — at least one implementing agent is working on it
- **in_review** — PR open, Reviewer + SecOps engaged
- **qa** — Reviewer approved; QA executing test plan against preview
- **done** — merged to master; user can see it in next status report
- **blocked** — any state can transition to blocked; needs a reason and an unblocker

## Workflow per ticket

1. **PM picks** the next ready ticket whose dependencies are done.
2. **BA reviews** AC for ambiguity (only if the ticket touches user-facing behavior).
3. **Architect engages** if the ticket touches an extension point or has no ADR coverage.
4. **dotnet-db engages** if the ticket needs schema changes; produces EF Core migration, entity configuration, repository, query-filter setup.
5. **dotnet-backend engages** to implement features (Command/Query, Validator, Handler), adapters, controllers; opens feature branch `feat/T-NNNN-<slug>`. Regenerates the NSwag OpenAPI surface if the contract changes.
6. **frontend engages** to implement pages/components and to consume the regenerated API client (parallel to dotnet-backend once the controller signature is locked).
7. **l10n engages** to add translation keys for any new `BusinessErrorMessage` codes and user-facing strings.
8. **qa writes test plan** while implementation is in flight.
9. **PR opened** → reviewer reviews against CLAUDE.md + AC + ADRs; secops reviews if security-touching.
10. **qa executes** test plan against the preview environment.
11. **Merge** when reviewer + secops (if applicable) + qa all green.
12. **PM updates** status; closes ticket; updates sprint status doc.

## Cross-stack tickets

Most feature tickets span both stacks. The standard sequence:

```
architect (ADR if needed)
   ↓
dotnet-db (migration + entity config + repository)
   ↓
dotnet-backend (Feature: Command/Validator/Handler + Controller)
   ↓ (regenerate NSwag client)
frontend (page + components + form + i18n keys)
   ↓
qa (manual + integration tests)
   ↓
reviewer + secops
   ↓
merge
```

When the API contract is small and stable (e.g. a read-only list endpoint), `dotnet-backend` and `frontend` can run in parallel once the controller signature is locked in the ticket.

## Ticket file structure

See `docs/tickets/template.md`.

## Parallelism rules

- `dotnet-backend` and `frontend` can work in parallel **only after** the API contract (controller + DTO shape) is locked in the ticket or an ADR.
- `dotnet-db` must finish before `dotnet-backend` starts (no schema drift; EF Core can't compile against missing entities).
- `l10n` can run any time after AC is locked.
- `qa` writes test plans during dev, executes after the PR is open.
- NSwag regeneration is a hard gate between `dotnet-backend` and `frontend` for any contract change.

## Sizing

- **S** — < 4 hours of agent work, single file domain, no new ADR
- **M** — 4–16 hours, multi-file, may touch one ADR
- **L** — > 16 hours, new ADR likely, split if possible

Anything > L must be split.

## Branch & PR conventions

- Branch: `feat/T-NNNN-short-slug` or `fix/T-NNNN-short-slug`
- PR title: `T-NNNN: <ticket title>`
- PR body: link ticket, summarize change, list AC items addressed, link any new ADR, **flag whether NSwag client was regenerated** if the API contract changed
- One ticket = one PR. No mega-PRs.
- Cross-stack tickets that touch both `/backend/` and `/frontend/` are still **one PR** — the contract change and its consumer should ship atomically.
