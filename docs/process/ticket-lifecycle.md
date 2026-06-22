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

## Definition of Ready

PM blocks transition from `draft → ready` until all of the following are satisfied:

1. **not-duplicate** — PM verifies the ticket is not a duplicate of an existing ready/in_progress/qa/done ticket (check `docs/tickets/INDEX.md` + recent ADRs).
2. **observable G/W/T AC** — Each acceptance criterion has explicit Given/When/Then phrasing; outcomes are testable, not vague.
3. **sized S/M/L (L split)** — Ticket is sized in `size:` field. Any L-sized ticket is split or has an explicit multi-sprint plan and owner sign-off.
4. **depends_on satisfied or unblocker noted** — `depends_on:` list is empty (all dependencies are done), OR the ticket explicitly documents who will unblock it and when (see `docs/process/discovery.md` for unblocker nomination).
5. **manual_steps populated** — If the ticket requires deploy-time manual steps (database seed changes, config overrides, secret provisioning, DNS updates, feature flags), they are listed under a **## Manual deployment steps** section in the ticket with step-by-step instructions and rollback plan.
6. **security_touching boolean** — Ticket front-matter includes `security_touching: true | false`. True if the ticket touches auth, encryption, secrets, permission checks, rate limits, or webhook signatures. Reviewer + SecOps must concur before ready.
7. **layers populated** — Ticket documents which technical layers it affects: `layers: [domain | appservices | infra | web | frontend | config | database]`. Guides router to the correct implementing agent(s).

### Bundle DoR

When tickets are bundled into a single PR (3-6 tickets per `docs/process/routing.md §"Bundling related tickets into one PR"`), the bundle as a whole satisfies DoR when:

1. **Every ticket in the bundle individually satisfies the 7 DoR items above.** PM does not skip per-ticket DoR for bundled tickets.
2. **Bundle scope is named** in the branch name (e.g., `feat/shipping-pipeline-bundle`) and called out in each ticket's `## Context` section.
3. **Bundle order is documented** in each ticket's `## Context`: which ticket comes first, which last, why this ordering.
4. **No external blockers between tickets in the bundle.** If a ticket in the middle of the bundle blocks on external work, split the bundle.
5. **Single parallel-reviewer artifact** lives at `docs/review/runs/<bundle-name>-draft.md` (not per-ticket).
6. **L-split rule still triggers per ticket.** L tickets in a bundle split into a/b at grooming; both halves can join the bundle.

PM blocks `draft → ready` on the bundle's first ticket until ALL bundle tickets are individually ready.

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

## Documentation weight — tier by importance, not uniformly

Lesson from the build: documentation weight scaled with *every* change rather
than with its importance — INDEX rows grew to multi-hundred-word paragraphs and
every fold touched 4–5 docs, so a typo-fix carried the same prose tax as a
payments decision. Going forward, match the doc weight to the work:

- **INDEX.md row = ONE line.** Title + a short hook (≤~25 words). The full
  context lives in the ticket file, not the index. (Existing fat rows are
  grandfathered — don't rewrite them; just don't add new ones.) The index is a
  ledger you scan, not a place to re-explain the work.
- **Full ticket file (Context / Scope / Alternatives / AC / Technical notes)
  is for load-bearing tickets** — anything touching money, state machines,
  auth/security, schema, a provider seam, or a cross-cutting concern. These earn
  the deliberation record.
- **Lightweight tickets** (hygiene, a contained fix, a doc tweak, a single
  non-load-bearing edit) get a short ticket: Context + Scope + AC, and skip the
  Alternatives/Defense prose unless a real decision was made. A trivial change
  does not need a page.
- **Folds:** update the doc that actually changed. A review fold that's a code
  tweak updates the review-run doc and the ticket status log — not every doc in
  the tree. Touch ADRs/patterns/launch-checklist only when the fold genuinely
  changes those.

The rule is **proportionality**: the audit trail is valuable, but pay for it
where the decision is load-bearing, not on every mechanical edit.

## Parallelism rules

- `dotnet-backend` and `frontend` can work in parallel **only after** the API contract (controller + DTO shape) is locked in the ticket or an ADR.
- `dotnet-db` must finish before `dotnet-backend` starts (no schema drift; EF Core can't compile against missing entities).
- `l10n` can run any time after AC is locked.
- `qa` writes test plans during dev, executes after the PR is open.
- `reviewer` parallels **every** implementing agent from the moment the ticket enters `in_progress`. Reviewer reviews the branch continuously, not just at PR open — this allows early feedback on architecture, test coverage, and ADR alignment. See [docs/process/routing.md](routing.md) for reviewer assignment rules.
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
