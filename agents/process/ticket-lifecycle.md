# Ticket Lifecycle

A **ticket** is the atomic unit of coordinated work. One ticket = one shippable change with its
own acceptance criteria. Tickets live in [`../../docs/tickets/`](../../docs/tickets/) as
`T-NNNN-<slug>.md` and are indexed in [`../../docs/tickets/INDEX.md`](../../docs/tickets/INDEX.md).

The **PM owns every state transition.** No other agent edits a ticket's `status` field.

---

## State machine

```
        ┌──────────────────────────────────────────────────────────┐
        │                                                          │
draft ──► ready ──► in_progress ──► in_review ──► qa ──► done       │
            │            │              │          │                │
            │            └──────────────┴──────────┴──► blocked ────┘
            │                                              │
            └──────────────────────────────────────────────┘
                              (unblocked → back to prior state)
```

| State | Meaning | Who moves it out |
|---|---|---|
| `draft` | Captured but not yet specced. Needs AC, scope, sizing. | PM (after BA/architect input) |
| `ready` | Passes the **Definition of Ready** (below); `depends_on` satisfied; safe to start. | PM (when picking it up) |
| `in_progress` | An implementing instance is building it. A reviewer runs alongside. | PM (when work + review converge) |
| `in_review` | Implementation done; reviewer + (if needed) secops/optimizer verifying. | PM (when review passes or requests changes) |
| `qa` | Review passed; QA executing the test plan against the running app. | PM (when QA passes) |
| `done` | Merged, verified, status logged. | — terminal |
| `blocked` | Cannot proceed: unanswered blocking question, failed dependency, owner decision needed. | PM (when the blocker clears) |

A ticket that fails review or QA does **not** go backwards in the index; it stays in
`in_progress`/`in_review` with the reviewer's change-requests appended, and the same implementing
instance fixes it. Only a *dependency failure* or an *owner decision* sends it to `blocked`.

---

## Ticket frontmatter (canonical shape)

```yaml
---
id: T-0042
title: Maker payout batch admin UI
status: in_progress            # PM owns this field
size: M                        # S | M | L  (L must be split)
owner: frontend                # the charter currently working it
created: 2026-06-01
updated: 2026-06-01
depends_on: [T-0040, T-0041]   # ticket ids that must be `done` first
blocks: [T-0050]               # tickets waiting on this one
user_stories: [US-maker-0007]  # user stories this satisfies
adrs: [0003, 0016]             # ADRs in force for this work
layers: [dotnet-db, dotnet-backend, frontend]  # which stacks it touches → which agents run
security_touching: false       # true → SecOps gate is mandatory
manual_steps: [ef-migration, nswag-regen]   # owner-only steps this ticket needs
phase: 4
---
```

`layers` values are the agent charters that must run: `dotnet-db`, `dotnet-backend`, `frontend`,
`l10n`, `secops`, `optimizer` (plus the always-on `architect`/`ba`/`qa`/`reviewer`). `manual_steps`
values are owner-only actions: `ef-migration`, `nswag-regen`, `vendor-account`, `secret-rotation`,
`deploy-trigger`.

The body holds: **Context**, **Scope**, **Alternatives Considered**, **Acceptance Criteria**
(Given/When/Then), **Out of scope**, **Technical notes**, and a **Status log** (one line per
transition). See [`../../docs/tickets/template.md`](../../docs/tickets/template.md).

---

## The cross-stack flow

A typical feature ticket touches several layers. The PM sequences the layers and runs review in
parallel:

```
0. DELIBERATION (before any ticket exists) — per agents/process/deliberation.md:
     ba PANEL       (author + 2-3 challengers + lead) defends the user story  → consensus
     architect PANEL (author + 2-3 challengers + lead) defends the decision/ADR → consensus
     each panel's owning role updates its living doc (docs/user-stories/, docs/adr/)
        │  (only a FINALIZED story/ADR becomes a ticket)
        ▼
1. ba        — (already done in the panel) story is finalized with AC + deliberation trail
2. architect — (already done in the panel) ADR accepted, living decision doc updated
        │
        ▼  (contract locked: entity shape, API DTOs, error codes)
   qa        — drafts the test plan from the AC, in parallel (becomes the developers' TDD target)
3. dotnet-db      — migration + entity config + repository           ┐
4. dotnet-backend — test-first: failing test from AC → handler       │ each implementing step
5. frontend       — test-first: facade spec → component + i18n keys   │ runs with a `reviewer`
6. l10n           — cs-CZ keys for every new BusinessErrorMessage code ┘ instance IN PARALLEL
        │                                                                (red → green → refactor)
        ▼
7. secops    — mandatory iff `security_touching: true`
8. optimizer — for perf-sensitive or hot-path changes
9. qa        — execute the test plan against the running app
        │
        ▼
10. PM       — all gates green → merge → status: done → log → pick next
```

### Parallelism rules

- **Reviewer always runs in parallel with the implementing instance**, not after. The implementer
  produces the change; the reviewer instance reads the same ticket + diff and produces a verdict
  concurrently. The PM merges the two before transitioning state. Review happens *alongside*
  implementation, not as a serial gate — the reviewer parallels **every** implementing agent from
  the moment the ticket enters `in_progress`.
- **Backend and frontend may run in parallel** once the API contract is locked (the ADR / ticket
  fixes the controller signature + DTO shape). Until the contract is locked, frontend waits.
- **NSwag regeneration is a hard gate between `dotnet-backend` and `frontend`** for any contract
  change: the frontend consumes the regenerated `frontend/src/lib/api-client/`, never a hand-edited
  one. See [routing](./routing.md).
- **DB must finish before backend** when a migration changes the shape backend code compiles
  against — EF Core cannot compile against missing entities.
- **Independent tickets fan out freely** — the PM may have `dotnet-backend #1` on T-0042 and
  `dotnet-backend #2` on T-0048 at the same time, each paired with its own reviewer.
- **L10n** can proceed any time after AC are fixed.
- **QA** writes the test plan during dev and executes it against the running app after the PR is
  open.

---

## Definition of Ready (a ticket can't go `ready` without this)

A `draft` only becomes `ready` when **all** hold — this stops half-specced tickets from wasting an
implementer's run and stops the backlog from rotting as it grows:

1. **Not a duplicate.** The PM searched [`INDEX.md`](../../docs/tickets/INDEX.md) and
   [`docs/audits/`](../../docs/audits/) first; this isn't already captured by an open ticket or an
   audit finding. (If it overlaps one, merge instead of forking.)
2. **AC are present and observable** (Given/When/Then, verifiable outcomes — not "make it nicer").
3. **Sized** S/M/L, and any `L` is **split** before it goes ready.
4. **Dependencies known** (`depends_on` listed and either `done` or themselves tracked).
5. **`manual_steps` assessed** — does it need an EF migration or NSwag regen? If so they're listed
   and the owner is flagged.
6. **`security_touching` and `layers` set** so the PM routes and gates correctly. When
   `security_touching: true`, reviewer + secops concur before the ticket goes `ready`.
7. **The canonical archetype is identified** — which pattern in
   [`docs/architecture/patterns.md`](../../docs/architecture/patterns.md) applies — so the
   implementer mirrors the right existing feature.

A ticket failing any of these stays `draft`; the PM completes it (invoking `ba`/`architect` as
needed) before promoting it.

### Bundle DoR

When related tickets are bundled into a single PR (per [routing](./routing.md)
§"Bundling related tickets into one PR"), the bundle as a whole satisfies DoR when:

1. **Every ticket in the bundle individually satisfies the 7 DoR items above.** The PM does not skip
   per-ticket DoR for bundled tickets.
2. **Bundle scope is named** in the branch name (e.g. `feat/shipping-pipeline-bundle`) and called
   out in each ticket's `## Context`.
3. **Bundle order is documented** in each ticket's `## Context`: which ticket comes first, which
   last, why.
4. **No external blockers between tickets in the bundle.** If a middle ticket blocks on external
   work, split the bundle.
5. **A single parallel-reviewer artifact** lives at `docs/review/runs/<bundle-name>-draft.md`, not
   per-ticket.
6. **The L-split rule still triggers per ticket.** L tickets in a bundle split into a/b at grooming;
   both halves can join the bundle.

The PM blocks `draft → ready` on the bundle's first ticket until **all** bundle tickets are
individually ready.

## Sizing

| Size | Effort | Files | ADR? | Rule |
|---|---|---|---|---|
| **S** | < ~4h | 1–3 | no | Single concern. May skip the ba/architect steps. |
| **M** | ~4–16h, often cross-layer | several | maybe | The default. Full flow. |
| **L** | > ~16h, many files, multiple layers, new patterns | many | likely | **Must be split** into S/M tickets before going `ready`. |

If a ticket is discovered mid-flight to be an `L`, the implementer stops, writes a note in the
status log, and the PM splits it. We never let an `L` run as one ticket — it destroys traceability
and review quality.

---

## Documentation weight — tier by importance, not uniformly

Match the doc weight to the work, not to the number of files touched:

- **INDEX.md row = ONE line.** Title + a short hook (≤~25 words). The full context lives in the
  ticket file, not the index. The index is a ledger you scan, not a place to re-explain the work.
- **Full ticket file** (Context / Scope / Alternatives / AC / Technical notes) **is for load-bearing
  tickets** — anything touching money, state machines, auth/security, schema, a provider seam
  (Comgate, Packeta, ARES, SendGrid, Mapbox), or a cross-cutting concern. These earn the
  deliberation record.
- **Lightweight tickets** (hygiene, a contained fix, a doc tweak, a single non-load-bearing edit)
  get a short ticket: Context + Scope + AC, and skip the Alternatives prose unless a real decision
  was made.
- **Folds:** update the doc that actually changed. A review fold that's a code tweak updates the
  review-run doc and the ticket status log — not every doc in the tree. Touch ADRs / patterns /
  launch-checklist only when the fold genuinely changes those.

The rule is **proportionality**: the audit trail is valuable, but pay for it where the decision is
load-bearing, not on every mechanical edit.

---

## "Done" means

A ticket is `done` only when **all** of these hold:

1. AC each have verifiable evidence (a test, a screenshot, a log line, or a reviewer confirmation).
2. The reviewer approved (and secops/optimizer approved if they were in scope).
3. QA executed the test plan and recorded the result.
4. Any `manual_steps` are flagged to the owner (the agents do **not** run EF migrations or NSwag
   regen — those are owner-only).
5. The [`INDEX.md`](../../docs/tickets/INDEX.md) row and the phase status doc are updated, and the
   status log has a line for the final transition.

Anything short of this stays out of `done`. We do not mark work complete on hope.

### When the in-workflow gate did not run (hand-gating)

A final-report (StructuredOutput) failure can kill a ticket's in-workflow reviewer lane while the
work itself landed fine on disk. Such a ticket may still reach `done`, but ONLY when both hold (see
[quality-gates](./quality-gates.md) §"A final-report failure ≠ a work failure"):

1. The ticket's `## Review` carries a **MANUAL-GATE block** recording the concrete evidence the
   orchestrator inspected by hand: the files it read, the commands it ran itself (with exit codes
   and pass/fail counts), and which AC each piece of evidence covers. "The work looked fine" is not
   a MANUAL-GATE block — it is narration the gates forbid.
2. The [`INDEX.md`](../../docs/tickets/INDEX.md) row carries a **manual-gate provenance marker**
   (e.g. `done (manual-gate)`), so nobody later mistakes a hand-gated ticket for one whose reviewer
   lane actually ran.

A ticket with neither is not `done` — it is `in_review` with a dead reviewer lane, and the PM
re-runs the gate or hand-gates it properly.

---

## Branch & PR conventions

- Branch: `feat/T-NNNN-short-slug` or `fix/T-NNNN-short-slug`.
- PR title: `T-NNNN: <ticket title>`.
- PR body: link the ticket, summarize the change, list AC items addressed, link any new ADR, and
  **flag whether the NSwag client was regenerated** if the API contract changed.
- **One ticket = one PR.** No mega-PRs.
- Cross-stack tickets that touch both `backend/` and `frontend/` are still **one PR** — the contract
  change (across `Web.Customer` / `Web.Maker` / `Web.Admin` / `Web.Public`) and its consumer ship
  atomically. CI ([`.github/workflows/ci.yml`](../../.github/workflows/ci.yml)) verifies NSwag spec
  parity before merge.
