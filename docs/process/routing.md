# Routing

`ticket-lifecycle.md` says **what** states a ticket moves through. This doc says **who** picks it up at each state and why. The signal → agent table below is deterministic: same signal, same primary agent, every time. PM uses it to assign; reviewer uses it to verify the right agents touched the work.

Agent charters live in `.claude/agents/`. Quality gates live in `quality-gates.md`. Escalation rules live in `communication.md`.

## Signal → agent table

| Signal | Primary | Secondary |
|---|---|---|
| New user-facing capability, ambiguous AC | `ba` | `architect` if extension-point |
| New ADR / pattern / cross-cutting concern | `architect` | `ba` if affects stories |
| Schema (entity / column / index / migration) | `dotnet-db` | `dotnet-backend` repo consumer |
| CQRS feature (Command / Query / Handler / Validator) | `dotnet-backend` | `dotnet-db` if schema |
| Adapter (Comgate / Packeta / ARES / SendGrid / Mapbox / Blob) | `dotnet-backend` | `secops` on auth / secret / webhook |
| Page / component / form / Server Component | `frontend` | `l10n` if new copy |
| New copy / new `BusinessErrorMessage` code | `l10n` | parallel with implementer |
| Security-touching change (see Gate 3 in `quality-gates.md`) | `secops` | mandatory pair with implementer |
| Hot path / external call / heavy UI / new package | `optimizer` | `reviewer` always |
| ANY PR | `reviewer` | PARALLEL from `in_progress` + final at PR-open |
| ANY AC needs proof | `qa` | PARALLEL writing test plan |

Read the table top-down for each ticket. A single ticket usually hits multiple rows — that's expected. PM lists every triggered row in the ticket's **Routing** block so reviewer can verify nothing was skipped.

## Sequencing rules

These are not preferences. Break them and the ticket fails Gate 6 (contract parity) or Gate 1 (architecture) at PR-open.

1. **Schema before code, code before UI.** `dotnet-db` → `dotnet-backend` → NSwag regen → `frontend`. The frontend cannot start until the controller signature is locked in the ticket. "Locked" means the ticket body contains the route, request DTO, response DTO, and status codes, or an ADR in `docs/adr/` does.
2. **`l10n` parallels `frontend` on the same ticket.** Every new `BusinessErrorMessage.*` code in the backend PR needs a matching key in `frontend/src/lib/i18n/cs-CZ.ts`. Same PR. Reviewer rejects the PR if one side ships without the other.
3. **`reviewer` parallels every implementing agent from `in_progress`.** Preliminary notes go in `docs/review/runs/T-NNNN-draft.md` while work is in flight. Final review against `docs/review/checklist.md` happens at PR-open and supersedes the draft. The draft is not a quality gate — it's an early-warning system so reviewer doesn't discover an architectural problem at the end.
4. **`secops` is a mandatory pair, not an optional reviewer.** On security-touching tickets (Gate 3 list), `secops` engages from `in_progress` alongside the implementer and signs off before merge. PM does not move the ticket to `in_review` without `secops` engaged.
5. **`optimizer` is pinged by `reviewer`, not by PM.** When reviewer's draft notes flag a hot path, new external call, heavy client bundle, or new package dependency, reviewer adds `optimizer` to the ticket. `optimizer` then writes a perf note inline in the ticket; the implementer addresses or defers with a follow-up ticket.
6. **Quality gates run last.** `quality-gates.md` Gates 1–7 fire at PR-open, after all implementing agents have finished. They are not a substitute for routing — a ticket that routed to the wrong agent fails the gates anyway, just later and more expensively.
7. **`manual_steps` block PM at a named transition.** If a ticket has a `Manual steps` section (vendor account setup, DNS, secret in Key Vault, Comgate merchant onboarding), PM does not advance the ticket past the named transition until the user signals the step is done. The block goes in `docs/questions/open.md` as a Q-entry with `Blocking: yes`.

## Orchestration shape — fan out for breadth, stay direct for depth

The signal→agent table says *which* agents engage. This section says *whether
the orchestrator should spawn parallel sub-agents at all, or do the work
directly in the main loop*. Getting this wrong wastes tokens on ceremony (or
loses parallelism where it would have helped).

The deciding question is the **shape of the task**, not its size:

- **Fan out (parallel sub-agents) for BREADTH** — work that decomposes into
  independent pieces with no shared mutable state, where you want the
  *conclusion* not the file-dumps:
  - Multi-gate review (reviewer + secops + architect + optimizer, each a lens).
  - Bug-hunts / audits sweeping many files (each finder a distinct angle).
  - Broad searches across naming conventions / subsystems (`Explore`).
  - Independent design attempts to compare (judge panel).
  Each sub-agent is blind to the others; that independence is the value.

- **Stay direct (main loop) for DEPTH** — one coherent change with internal
  dependencies, where context must carry from step to step:
  - Implementing a single feature/fix (groom → code → test is *sequential*; a
    sub-agent per step just re-loads context and loses the thread).
  - A focused edit you already hold the context for.
  - Anything where step N needs the *reasoning* (not just the output) of N−1.

  **Delegating depth work is the common over-orchestration mistake**: spawning
  an agent to "implement T-0NNN" when you already have the file contents loaded
  costs a full context re-hydration for no parallelism gain. Do it directly.

- **Hybrid (the usual answer for a substantive task):** scout/decompose
  directly first (list the files, scope the diff, find the work-list), *then*
  fan out over that list, *then* fold the results directly. You stay in the
  loop between phases; each fan-out is one well-scoped breadth step.

Rule of thumb: **if the sub-tasks could run in any order and not see each other,
fan out. If they form a chain where each needs the last one's reasoning, stay
direct.** When a task would merely *benefit* from an extra perspective but isn't
genuinely parallel, a single verification pass beats a fleet.

This composes with **Gate 0 (evidence discipline)** in `quality-gates.md`: the
wider you fan out a finder swarm, the more false positives you import, so the
fold step MUST verify each finding before acting on it.

## Cross-stack ticket — concrete trace

A typical M-sized cross-stack ticket, in routing order:

```
ba           — confirms AC unambiguous, freezes copy slugs
architect    — engages only if new extension point or new ADR
dotnet-db    — migration + entity config + repository
dotnet-backend
             — Feature: Command/Query + Validator + Handler + Controller
             — regenerates NSwag client (contract change)
frontend     — page + components + form, consumes regenerated client
l10n         — parallel with frontend, adds cs-CZ keys
reviewer     — parallel from in_progress (draft notes), final at PR-open
secops       — parallel from in_progress if security-touching
optimizer    — pinged by reviewer if hot path / heavy / new package
qa           — writes test plan during dev, executes against preview at PR-open
```

PM is not in the routing chain — PM picks the ticket, names the agents, and watches for blockers. PM does not implement.

## Alternatives considered

- **Route by file path glob (e.g. `/backend/**` → `dotnet-backend`).** Rejected. Plenty of changes span paths (a new `BusinessErrorMessage` code touches backend AND the cs-CZ dictionary; a new payment provider touches `Infra.Clients/` AND `CountryConfiguration` seed data). Path globs miss the cross-cutting agents (`l10n`, `secops`, `optimizer`).
- **Single "lead engineer" agent who decomposes and routes.** Rejected. Adds a hop, hides the routing rationale, and gives one agent veto power over the others' charters. The table above is the contract; no one agent owns it.
- **Route purely by ticket size.** Rejected. An S ticket can still be security-touching and demand `secops`. Size affects scheduling, not which agents engage.
- **Make `reviewer` PR-open only (no parallel drafts).** Rejected by user decision: both. Catching an architectural mistake at PR-open means re-doing days of work. Parallel drafts cost reviewer a few extra reads and save the implementer a rewrite. See `docs/review/runs/` for the draft notes convention.
- **Make `optimizer` engage on every ticket.** Rejected. Too noisy. `optimizer` engages on the four signals in the table (hot path / external call / heavy UI / new package) and `reviewer` is the trigger because reviewer is reading the diff anyway.

## Defense

The routing table is deterministic so two things become true:

1. **PM can assign without judgment calls.** Same signal, same agent, every time. PM's job is to read the table, not to negotiate.
2. **`reviewer` can verify nothing was skipped.** If the ticket touched a webhook and `secops` is not in the **Routing** block, reviewer rejects the PR before reading the diff. No "I didn't know I needed SecOps for that" — the table said so.

Cheap-deliberation principle from `CLAUDE.md`: user is the challenger. If the table is wrong, the user changes the table — they don't argue routing per ticket. Anyone who wants to change a row writes an ADR in `docs/adr/`; until that ADR is `accepted`, the table here is the law.

## Cross-references

- States and transitions: `ticket-lifecycle.md`
- What each agent reviews at PR-open: `quality-gates.md`, `../review/checklist.md`
- How agents hand off (artifacts not chat): `communication.md`
- Agent charters: `../../.claude/agents/{architect,ba,dotnet-backend,dotnet-db,frontend,l10n,optimizer,pm,qa,reviewer,secops}.md`
- Extension points that trigger architect routing: `../architecture/extension-points.md`
- Patterns that handlers / pages must follow: `../architecture/patterns.md`

## Bundling related tickets into one PR

A "bundle" is 3-6 tightly-coupled tickets in the same subsystem that ship as a single PR. Bundling reduces PR count and review overhead when tickets share a dep chain and a subsystem boundary.

### When to bundle

- Same subsystem (e.g., "shipping pipeline" = order-accept + ship-zasilkovna + ship-pickup + label-fetch + label-download).
- Same domain (orders, payments, identity, etc.).
- Dep chain is sequential and tight (each ticket depends on the previous; no external blockers between them).
- Total bundle size ≤ ~3000 LOC of production code + ~1500 LOC of tests.

### When NOT to bundle

- Tickets span multiple subsystems (e.g., one shipping + one auth — too much blast radius).
- A ticket has external blockers (waiting on a third-party API change, design approval, etc.).
- Bundle would exceed ~6 tickets or ~3000 LOC — split into two bundles.

### Bundle workflow

1. **Grooming:** PM grooms ALL tickets in the bundle BEFORE implementation starts. User answers all AskUserQuestion deliberations up front (batched across tickets). Each ticket's `## Locked design decisions` section is populated; `status: ready`.
2. **Branch:** single feature branch (e.g., `feat/shipping-pipeline-bundle`).
3. **Implementation:** dotnet-backend (or relevant implementer) processes tickets sequentially in the same branch. One `feat(T-NNNN):` commit per ticket (or per logical sub-feature within a ticket). TDD-with-commit-order still applies per ticket. **NSwag regen must cover EVERY host whose controllers changed in the bundle, not just the primary one; verify with `npm run check:api` before PR-open.** (Codifies the admin-drift lesson from payout-settlement: a bundle that touches Customer + Maker + Admin controllers must regen all three clients, or Gate 6 contract-parity fails late.)
4. **Parallel reviewer:** runs ONCE for the whole bundle from `in_progress` state. Draft notes at `docs/review/runs/<bundle-name>-draft.md`. Per-ticket draft notes are NOT required for bundles.
5. **Final review + Gate 8 + Gate 9:** single pass over the full bundle diff at PR-open. Reviewer reads ALL ticket files in the bundle + all modified source files.
6. **Fold:** single `chore(<bundle>): fold reviewer findings` commit.
7. **PR:** one PR for the entire bundle. PR description summarizes which tickets are included + AC traceability + test counts before/after.
8. **L-split rule:** still triggers per ticket. L tickets split into a/b at grooming; both halves can join the bundle.

### Charter overrides

- **`/feature` command:** when invoked with bundle scope (e.g., `/feature shipping-pipeline`), the workflow grooms all tickets in parallel and writes a single bundle plan instead of N per-ticket plans.
- **`/execute` command:** picks up the next ready bundle from INDEX if one exists; else falls back to single-ticket execution.
- **DoR check:** PM verifies every ticket in the bundle satisfies DoR before transitioning the bundle to in_progress.
