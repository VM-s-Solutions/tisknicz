# Communication Protocol

The team coordinates through **Git-tracked artifacts only**. There is no agent-to-agent chat, no
shared memory, no verbal hand-off. If a decision isn't written to a file, it didn't happen.

This is deliberate: every hand-off is reviewable, every decision is traceable to a commit, and the
whole history of *why* the system looks the way it does is reconstructable. For a self-running
marketplace that is about to go to production and will be expensive to change later, that
traceability is the whole point.

---

## The channels

| What | Channel (file) | Written by | Read by |
|---|---|---|---|
| A unit of work + its state | `docs/tickets/T-NNNN-*.md` | PM (state), devs (notes) | everyone on the ticket |
| The backlog at a glance | `docs/tickets/INDEX.md` | PM | PM, owner |
| Requirements / behavior | `docs/user-stories/<persona>/*.md` | BA | PM, devs, QA |
| Architecture decisions | `docs/adr/NNNN-*.md` | Architect | everyone |
| How we build (patterns) | `docs/architecture/patterns.md`, `docs/architecture/roles/*.md` | Architect | every developer |
| Progress for the owner | `docs/status/sprint-N.md` | PM | owner |
| Blockers & questions | `docs/questions/open.md` | any agent | owner (answers), PM |
| Test plans & results | `docs/test-plans/T-NNNN.md` | QA | PM, devs |
| Review verdicts | the ticket's `## Review` section + `docs/review/runs/*.md` | Reviewer / SecOps / Optimizer | PM, devs |
| Audit findings | `docs/audits/*.md` | BA / Architect / Reviewers | PM (→ tickets) |

A developer hands off to a reviewer by **finishing the diff and writing an implementation note in
the ticket**. The PM, seeing the ticket in `in_review`, invokes the reviewer, who writes a verdict
in the ticket's `## Review` section (with the working notes in `docs/review/runs/T-NNNN-draft.md`).
No one messages anyone.

Two hard gates are contract-specific to this stack and travel *inside* the artifact, not in a chat
turn:

- **NSwag parity.** When a ticket changes the API contract (`dotnet-backend` regenerates the
  TypeScript client under `frontend/src/lib/api-client/`), the regenerated client ships in the
  **same PR**. A bundle that touches more than one host (`Web.Customer`, `Web.Maker`, `Web.Admin`,
  `Web.Public`) regenerates **every** affected client, not just the primary. The signal that this
  happened lives in the PR body and the ticket, never in a hand-off message.
- **i18n parity.** Every new `BusinessErrorMessage` code needs a matching key in
  `frontend/src/lib/i18n/cs-CZ`. `l10n` and `frontend` co-author that in the same PR. Reviewer
  rejects a PR where one side shipped without the other.

---

## Escalation: how the team asks the owner

When an agent hits something it cannot decide — a business rule it doesn't know, an ambiguous
requirement, a decision with lasting cost — it does **not** guess silently and it does **not** stop
the world. It:

1. Appends a question to [`../../docs/questions/open.md`](../../docs/questions/open.md) using the
   format below.
2. Marks it `blocking: yes` only if work genuinely cannot proceed without the answer; otherwise
   `blocking: no` and the agent proceeds with the **most defensible default**, documenting the
   assumption in the ticket.
3. The PM surfaces open blocking questions to the owner at the next checkpoint.
4. When the owner answers, the answer is moved to `answered.md`, and the decision is locked into
   the relevant artifact (ADR, story AC, or charter) so it never has to be asked again.

```markdown
### Q-0007 — [blocking: yes] Comgate refund precedence on partially shipped orders
- **Raised by:** dotnet-backend (T-0042)
- **Date:** 2026-07-08
- **Question:** When a maker cancels one line of a multi-line order after part of it has shipped
  via Packeta, does the Comgate refund cover the whole order or only the unshipped lines?
- **Why it matters:** Determines the refund-amount calculation in the payment adapter; changing it
  later means recomputing historical invoices and payout ledgers.
- **Default taken (if non-blocking):** —
- **Answer:** _(owner fills this in)_
```

> **Escalate up, not sideways.** An agent never asks another agent to make a business decision —
> business decisions go to the owner via `docs/questions/open.md`. Agents *do* defer technical
> decisions to the Architect by leaving a note in the ticket and having the PM invoke the Architect.
> A per-country variation is **never** a question to the owner and **never** an `if (country == "CZ")`
> branch — it is a row in `CountryConfiguration`. See [`../../CLAUDE.md`](../../CLAUDE.md) and
> [ADR 0004](../../docs/adr/0004-country-configuration.md).

---

## Batching status to the owner

The PM is the **only** agent that reports progress to the owner, and it **batches**. The owner is
not pinged mid-ticket. Status surfaces at:

- a **sprint checkpoint** (the PM writes/updates `docs/status/sprint-N.md`), or
- when a ticket has been `blocked` longer than a checkpoint, or
- when `docs/questions/open.md` has an unanswered `blocking: yes` entry, or
- when a ticket has a **Manual deployment steps** section the owner must action before PM can
  advance it past a named transition (a vendor account, a Comgate merchant onboarding, a SendGrid
  domain verification, a DNS record, an Azure Key Vault secret, a Bicep parameter in `infra/bicep/`).

Everything else stays in the artifacts until the owner asks. This keeps the owner's attention for
the decisions that actually need a human.

---

## Anti-patterns (rejected on sight)

- An agent describing what it "told" or "asked" another agent — there is no such channel.
- A decision that exists only in a chat turn and not in a file.
- A developer silently inventing a business rule instead of raising a question.
- A frontend agent embedding pricing, VAT, or state-machine logic that belongs in the backend —
  the frontend is a pure presentation layer, and "I decided the rounding" is not a hand-off, it's a
  bug.
- Shipping a backend contract change without the regenerated NSwag client in the same PR, or a new
  `BusinessErrorMessage` code without its `cs-CZ` key.
- The PM pinging the owner for routine progress instead of batching into the sprint doc.
- Two agents editing the same shared-lane file concurrently without the PM sequencing them (the PM
  owns ordering to avoid write races; when true parallel file edits are unavoidable, isolate them).
  Shared lanes here include `CountryConfiguration` seed data, `BusinessErrorMessage`, the i18n
  catalog, and the NSwag-generated client — all magnets for silent write races.

---

## Cross-references

- Who picks up a ticket at each state: [`routing.md`](./routing.md)
- States and transitions: [`ticket-lifecycle.md`](./ticket-lifecycle.md)
- What each agent verifies at PR-open: [`quality-gates.md`](./quality-gates.md)
- Agent charters: [`../../.claude/agents/`](../../.claude/agents/) — `architect`, `ba`,
  `dotnet-backend`, `dotnet-db`, `frontend`, `l10n`, `optimizer`, `pm`, `qa`, `reviewer`, `secops`
- The non-negotiable rules every artifact must respect: [`../../CLAUDE.md`](../../CLAUDE.md)
