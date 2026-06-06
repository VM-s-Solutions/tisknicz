# Deliberation protocol

Every ADR and every user story is one path through a tree of options. The rejected paths matter as much as the chosen one — they are the trail that lets the next reader (human or agent) understand **why** we picked what we picked, and stop the same question from being re-litigated for free.

This doc describes how we deliberate cheaply, how we record the trail, and when cheap is not enough.

## Why we need this

Once Makables is live, changes are expensive — the user has made that point explicit (see [ADR 0007](../adr/0007-stack-pivot-dotnet-backend.md) context). The patterns we adopt now (CQRS one-file feature, `BusinessResult`, `CountryConfiguration`, adapter pattern, per-audience hosts, outbox) are load-bearing. A future reader needs to know:

1. **What else we considered** — so they don't waste a week re-discovering the same alternative.
2. **Why we rejected it** — so they can tell whether the rejection still holds under new constraints.
3. **Whether the decision was challenged** — and if so, what the rebuttal was.

Without that trail, every decision degrades into "someone picked this once, nobody remembers why" within two sprints.

## The cheap pattern (default)

The **user is the challenger**. Agents draft; the user attacks; the artifact records.

```
Author drafts proposal       ── (ADR / story / ticket)
   ↓
User reads + challenges      ── ("why not X?", "what about Y?")
   ↓
Author REBUTS (evidence)
     or CONCEDES + revises
     or ESCALATES to architect
   ↓
Artifact persists the trail  ── ## Alternatives Considered + ## Defense
```

There is no separate "deliberation meeting." The challenge happens in the same pass that produces the artifact, and the trail lives in the artifact itself. No side channels, no chat-only context — per [communication.md](./communication.md) §"artifacts, not chat".

### Who challenges what

| Artifact | Default challenger | Escalation |
|---|---|---|
| ADR | user | architect spawns sub-agents (see "when cheap is not enough" below) |
| User story (Given/When/Then) | user via BA | architect if a role boundary is in play |
| Ticket | user via PM | architect if extension point is touched; secops if security-touching |
| Sprint plan | user via PM | — |
| Open question (`docs/questions/open.md`) | user | — (that file IS the challenge channel) |

## Required artifact sections

### ADRs — [docs/adr/template.md](../adr/template.md)

Every ADR MUST carry:

- **`## Alternatives considered`** — at least **2** alternatives with a one-line "what it was" + one-line "why rejected". The template already mandates this; deliberation.md makes the minimum count explicit.
- **`## Defense`** — appended if and only if the ADR was challenged. Format:

  ```markdown
  ## Defense

  ### Challenge — <YYYY-MM-DD> — <who>
  > <quoted challenge>

  **Author response:** rebut | concede | escalate
  - <evidence / link to ADR / link to roles doc / link to ticket>
  - <if concede: what the revision is>
  - <if escalate: which architect sub-agent took it>
  ```

  Multiple challenges → multiple `### Challenge` blocks, append-only. The Defense section is the audit log; it is never edited in place, only appended to.

ADRs without `## Alternatives considered` fail Reviewer's Gate-4 architecture check.

### User stories — [docs/user-stories/template.md](../user-stories/template.md)

Every Given/When/Then story MUST carry:

- **`## Alternatives considered`** — at least **1** alternative shape for the capability (different AC framing, different role split, different out-of-scope cut), with one-line rationale for the rejection. Stories ship more often than ADRs and the budget is lower; one alternative is the minimum.
- **`## Defense`** — appended if challenged, same shape as ADR.

The story template's `## Open questions` section is the **pre-deliberation** channel — anything still open at story-acceptance time gets a `Q-NNNN` entry in [docs/questions/open.md](../questions/open.md) per [communication.md](./communication.md) §questions/open.md.

### Tickets — [docs/tickets/template.md](../tickets/template.md)

A ticket carries `## Alternatives considered` **when the ticket made a non-trivial pick** that is not already locked in an ADR or a story. Examples of triggers:

- The ticket picked between two ways to wire a feature (e.g. payload pre-bake vs. lookup-at-send).
- The ticket cut something the reader would expect to be in scope.
- The ticket chose a migration name, column shape, or test-count baseline that future tickets must respect.

[T-0067](../tickets/T-0067-mark-order-paid-outbox.md) is the canonical example: it carries an explicit `### User decisions captured upfront (research workflow + synthesis)` block with **Q1–Q4** — each a non-trivial pick with rationale. Sprint-7 (T-0060 onwards) is the precedent for embedding the deliberation trail directly in the ticket scope.

When in doubt: if a future implementer might ask "why did this ticket do X instead of Y?", write `## Alternatives considered`.

## The Defense loop

```
Author drafts
   ↓
Challenger attacks (user, or escalated agent)
   ↓
Author chooses ONE:
   ─ REBUT     → cite evidence; append to ## Defense; original decision stands
   ─ CONCEDE   → revise decision; append concession to ## Defense; revise ## Decision section
   ─ ESCALATE  → architect spawns sub-agents (only for high-stakes ADRs)
   ↓
Artifact persists every round, append-only
```

Rules:

1. **Append-only.** Defense entries are never edited or deleted. If the decision is later superseded, write a new ADR with `supersedes: NNNN` per [template](../adr/template.md).
2. **Evidence beats opinion.** A rebuttal cites a roles doc, an ADR, a ticket, or a code path. "Because I think so" is a concession in disguise — revise.
3. **One challenge, one response.** Don't bundle three rebuttals into one block; each `### Challenge` gets its own `**Author response:**`.
4. **No silent rewrites.** If the user's challenge forces a `## Decision` change, the old decision text is not erased — it is moved into `## Defense` under the concession entry so the trail survives.

## When cheap is not enough

High-stakes ADRs MAY upgrade to a more expensive pattern: the **architect spawns 2–3 sub-agents** with explicit author/challenger prompts, runs the debate, then synthesises into the ADR's `## Defense`.

Stakes that qualify:

| Domain | Why high-stakes |
|---|---|
| Money | Per-row minor-units schema, VAT rounding, payout math. [ADR 0003](../adr/0003-money-and-currency.md). |
| Security | Auth flow, JWT audience separation, webhook origin verification. [ADR 0012](../adr/0012-authentication.md), [ADR 0016](../adr/0016-payments-comgate.md). |
| Extension points | Anything listed in [docs/architecture/extension-points.md](../architecture/extension-points.md) — payment provider, shipping provider, registry, email, geocoder. |

**Out of scope for MVP.** No T-0001..T-0067 ticket triggered this path. The mechanism exists so it isn't reinvented when the first hard call comes (likely payouts, refunds, or the SK/PL/HU country onboarding).

When it does fire, the architect:

1. Writes the author prompt (the proposal as stated).
2. Writes the challenger prompt (the strongest opposite case).
3. Spawns sub-agents with each prompt.
4. Synthesises winners + losers into `## Alternatives considered` + `## Defense`.
5. Signs the ADR.

The user remains the final ratifier — sub-agent debate produces the trail, not the ratification.

## Grandfathering

- **ADRs 0001–0024** — written before this protocol. They carry `## Alternatives considered` per the template but no `## Defense` retroactive backfill is required. If one is challenged from this point on, the `## Defense` section is added then.
- **Tickets T-0001–T-0067** — written before this protocol. They are NOT required to retro-add `## Alternatives considered`; the precedent (T-0067's Q1–Q4 block) is what new tickets follow.
- **Tickets T-0068 onward** — MUST follow the rules in this doc.
- **User stories** — backfill `## Alternatives considered` on next edit if missing; no separate migration sprint.

## How this fits the rest of the process

- **[discovery.md](./discovery.md)** — discovery produces the first wave of ADRs + stories. Each one ships with `## Alternatives considered` from day one; the user's sign-off at Step 6 is the first formal challenge round.
- **[ticket-lifecycle.md](./ticket-lifecycle.md)** — `## Alternatives considered` is captured at `draft → ready` (PM-owned). Challenges that arrive during `in_progress` go to the author; challenges that arrive during `in_review` go to the reviewer per [quality-gates.md](./quality-gates.md) Gate-4.
- **[communication.md](./communication.md)** — deliberation is artifact-bound. Verbal context does not count. If the rationale is not in `## Alternatives considered` or `## Defense`, it didn't happen.
- **[quality-gates.md](./quality-gates.md)** — Gate-4 (Architect) verifies that ADRs touching extension points carry `## Alternatives considered` with ≥2 alternatives. Reviewer enforces.

## What this doc does NOT do

- It does not require a deliberation meeting, a synchronous review, or a back-channel.
- It does not mandate that every ticket carry `## Alternatives considered` — only those that made a non-trivial pick (T-0067 is the bar, not the floor).
- It does not retroactively rewrite ADRs 0001–0024 or tickets T-0001–T-0067.
- It does not replace the [Reviewer gates](./quality-gates.md) — it feeds them.

## Related

- [docs/process/discovery.md](./discovery.md)
- [docs/process/communication.md](./communication.md)
- [docs/process/ticket-lifecycle.md](./ticket-lifecycle.md)
- [docs/process/quality-gates.md](./quality-gates.md)
- [docs/adr/template.md](../adr/template.md)
- [docs/tickets/template.md](../tickets/template.md)
- [docs/user-stories/template.md](../user-stories/template.md)
- [docs/tickets/T-0067-mark-order-paid-outbox.md](../tickets/T-0067-mark-order-paid-outbox.md) — sprint-7 precedent
- [docs/questions/open.md](../questions/open.md)
