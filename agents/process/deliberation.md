# Deliberation — Defense Panels for Stories & Decisions

Stories and architectural decisions are not produced by a lone specialist and handed off. They are
**defended in front of challenging colleagues** and only **finalized by consensus**. An author must
*defend* their work; challengers are tasked to *attack* it; a lead *adjudicates*. Nothing reaches
developers until no challenge survives unanswered.

This is the spec-first heart of the system: a story/ADR that has survived adversarial defense is a far
better spec than one written once and shipped. It costs more up front and saves far more downstream.

> This is the **panel** mechanism — the expensive pattern. The cheap default (user-as-challenger, no
> separate meeting) lives in [docs/process/deliberation.md](../../docs/process/deliberation.md). That
> doc's §"When cheap is not enough" is the on-ramp to this one. Use the panel when the stakes clear the
> bar there (money, security, an extension-point seam) or when the user asks for maximum rigor.

## When a panel convenes
**Every user story and every architectural decision** that clears the stakes bar (the user's standing
instruction for load-bearing work — maximum rigor). The `ba` panel deliberates stories/business-logic;
the `architect` panel deliberates ADRs/decisions. Pure mechanical tickets that introduce **no** new
behavior or decision (a magic-number fix, a consistency-cleanup `T-*`) carry a one-line "no-decision"
note from the `pm` and skip the panel — but anything that defines *what the system does* or *how it's
structured* goes through it. See [docs/process/ticket-lifecycle.md](../../docs/process/ticket-lifecycle.md)
§"Documentation weight" for the proportionality rule: pay for the panel where the decision is
load-bearing, not on every mechanical edit.

## The roles (assigned at spawn time — same charter, different mode)
The `pm` spawns instances of the existing [`ba`](../../.claude/agents/ba.md) /
[`architect`](../../.claude/agents/architect.md) charter in one of three **modes**, named in the
invocation:
- **Author** — drafts the artifact and **owns** it. Must defend every part of it.
- **Challenger** (2–3 of them) — tasked to **attack**: poke holes in the AC, surface missing edge
  cases and lifecycle states, dispute the business logic, challenge the seam/trade-off, find the
  unstated assumption. A challenger that finds nothing says so explicitly *and* names what they
  checked (silence is not assent).
- **Lead** — adjudicates. A challenge stands only if the author failed to defend it convincingly or
  conceded. Declares consensus reached (or escalates to the user if it can't be).

The author and lead must be **different instances**. Challengers must be **different instances** from
the author. (Same charter, parallel instances — DRY.)

Security-touching stories/decisions pull [`secops`](../../.claude/agents/secops.md) in as a mandatory
challenger (per the Gate-3 list in
[docs/process/quality-gates.md](../../docs/process/quality-gates.md) and the routing rule in
[routing](../../docs/process/routing.md)). A decision that touches copy or a new
`BusinessErrorMessage` code pulls [`l10n`](../../.claude/agents/l10n.md) in as a challenger on the
i18n surface.

## The loop (author defends → challengers attack → lead rules)
```
1. AUTHOR drafts the story/ADR (grounded in real code + the audit findings).
        │
2. CHALLENGERS attack it (in parallel). Each writes, in the artifact's `## Challenge` section:
     - the specific hole (AC gap, missing state, wrong business rule, broken seam, unstated assumption)
     - why it matters (cite the code / lifecycle / a persona scenario)
        │
3. AUTHOR DEFENDS each challenge in writing in the `## Defense` section, one of:
     - REBUT (the challenge is wrong — here's the evidence), or
     - CONCEDE + REVISE (fold the fix into the artifact), or
     - ESCALATE (a real business decision only the user can make → docs/questions/open.md)
        │
4. CHALLENGERS re-check the revised artifact. New holes → repeat from 2.
        │
5. LEAD adjudicates every open point: each challenge is RESOLVED (defended or fixed) or it BLOCKS.
     Consensus = zero blocking challenges remain. The lead records the verdict + the key decisions.
        │
6. FINALIZED. The artifact is locked; the pm may now create tickets / the architect may accept the ADR.
```
Cap at a sensible number of rounds; if consensus can't be reached, the lead escalates the *specific
disagreement* to the user via [docs/questions/open.md](../../docs/questions/open.md) rather than
letting it loop.

## What "defended" means (the bar)
- A REBUT must cite evidence (code at file:line, the documented lifecycle, a persona scenario from
  [docs/personas.md](../../docs/personas.md)) — "I disagree" is not a defense.
- A CONCEDE must actually change the artifact, not just acknowledge the point.
- An AC that a challenger showed is ambiguous or unobservable does not survive — it gets rewritten or
  the challenge blocks.
- A decision with a real trade-off must have its **alternatives and why-not** in the record (the
  challenge surfaces them; the defense answers them). This is what makes the ADR trustworthy later —
  and it is exactly what the ADR template's `## Alternatives considered` section (≥2 alternatives)
  requires; the panel is where that section is *earned*, not backfilled.

## The output handed to developers
A finalized story/ADR carries its **deliberation trail**: the `## Challenge` / `## Defense` / `##
Verdict` sections stay in the artifact (append-only — never edited in place, only appended to, matching
the Defense-loop rule in [docs/process/deliberation.md](../../docs/process/deliberation.md)).
Developers (and testers, reviewers, secops, optimizers) read not just the conclusion but *why it's the
conclusion and what was rejected* — which prevents them re-litigating settled points and tells them the
edges that were considered. The story's AC are then the **TDD targets**
([docs/process/tdd-policy.md](../../docs/process/tdd-policy.md),
[docs/process/must-cover-tests.md](../../docs/process/must-cover-tests.md)): the tests encode what
survived the defense.

## Parallel documentation (non-negotiable, happens during deliberation)
Each role keeps its own **living documentation**, updated *as part of* finalizing — not a later chore:
- **`ba`** owns the domain business-logic docs under
  [docs/architecture/roles/](../../docs/architecture/roles/) — the responsibility of each object in
  prose **+ Mermaid diagrams** (flows, state machines, decision trees) + the living story map for the
  domain (per [ADR 0015 — Responsibility-Driven Design](../../docs/adr/0015-responsibility-driven-design.md)).
  When a story is finalized, the affected role doc(s) are updated in the same step so they never drift.
- **`architect`** owns the ADRs in [docs/adr/](../../docs/adr/) (the **immutable, numbered** record)
  plus their *evolving* companion notes under [docs/architecture/](../../docs/architecture/) (the
  trade-off space and current shape — e.g. [overview.md](../../docs/architecture/overview.md),
  [extension-points.md](../../docs/architecture/extension-points.md)). Updated when a decision is
  finalized; a superseded ADR gets a new one with `supersedes:` per the
  [ADR template](../../docs/adr/template.md), never an in-place rewrite.
- **Implementers** (`dotnet-backend`, `dotnet-db`, `frontend`, `l10n`) keep their implementation
  notes in sync with the canonical [docs/architecture/patterns.md](../../docs/architecture/patterns.md).
  When a ticket lands, the implementer updates the relevant pattern/role pointer.

See [docs/process/communication.md](../../docs/process/communication.md) for the artifact-not-chat rule
and [docs/README.md](../../docs/README.md) for the doc tree. The rule: **the documentation is updated
in parallel with the work, by the role that owns it — a finalized artifact with stale docs is not
finalized.**

## Why this isn't just bureaucracy
Makables is going to production and is large — and once it is live, changes are expensive (the user has
made that explicit; see [ADR 0007](../../docs/adr/0007-stack-pivot-dotnet-backend.md)). A story written
once and shipped carries the author's blind spots straight into code; an ADR decided alone carries one
person's trade-off preference. The load-bearing patterns we are locking now — CQRS one-file feature,
`BusinessResult<T>`, `CountryConfiguration`-driven per-country variation, the provider adapter seam
(Comgate / Packeta / ARES / SendGrid / Mapbox), per-audience hosts (`Web.Customer` 5001, `Web.Maker`
5002, `Web.Admin` 5003, `Web.Public` 5104) + Azure Functions, and the outbox — are the ones a bad early
call is most expensive to reverse. The defense panel converts *individual judgment* into
*surviving-the-best-objections judgment* — which is the difference between "it compiled" and "it's
right." The cost is paid in tokens up front; the saving is paid in defects, rework, and reverts not
happening after launch.

## Related

- [docs/process/deliberation.md](../../docs/process/deliberation.md) — the cheap default; this doc is its escalation
- [docs/process/communication.md](../../docs/process/communication.md) — artifacts, not chat
- [docs/process/ticket-lifecycle.md](../../docs/process/ticket-lifecycle.md) — where finalized artifacts become tickets
- [docs/process/quality-gates.md](../../docs/process/quality-gates.md) — Gate-3 (secops) and Gate-4 (architect) enforce the trail
- [routing](../../docs/process/routing.md) — which agents a signal pulls onto the panel
- [docs/process/tdd-policy.md](../../docs/process/tdd-policy.md) — AC that survived the panel become the TDD targets
- [docs/adr/template.md](../../docs/adr/template.md) — the `## Alternatives considered` section the panel earns
- [docs/questions/open.md](../../docs/questions/open.md) — where an ESCALATE lands
- Agent charters: [`.claude/agents/{architect,ba,secops,l10n,pm}.md`](../../.claude/agents/)
