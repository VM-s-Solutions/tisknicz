# Workflow improvements — portable retrospective (for the Cleansia agent)

> **Audience:** the Claude agent working on the **Cleansia** project.
> **Purpose:** these are process/workflow lessons learned on the **Makables
> (tisknicz)** build that are *project-agnostic* — they apply to any
> multi-agent, ticket-to-PR Claude workflow. Read them, then audit Cleansia's
> own workflow (`CLAUDE.md`, `.claude/agents/`, `docs/process/`, ticket/question
> conventions) and apply whichever fit. **Do not copy tisknicz-specific content**
> (its ADRs, its stack rules, its ticket IDs) — copy the *patterns and the
> reasoning*, then re-express them in Cleansia's own terms.
>
> Each item below names: the **symptom** observed, the **fix** applied in
> tisknicz (with the file it lives in), and **how to adapt it to Cleansia**.
> The actual implementations are in this repo's
> `docs/process/quality-gates.md`, `docs/process/routing.md`,
> `docs/process/ticket-lifecycle.md`, `docs/questions/open.md`, and the agent
> charters under `.claude/agents/` — read those for the full text.

---

## 1. Make finder-agents PROVE findings — "evidence discipline" (the most important one)

**Symptom.** When you fan out sub-agents to hunt bugs / review code / audit
security, they **systematically over-report**. On a single Makables bug-bash,
4 of 5 "BLOCKER/HIGH" findings were confident, well-formatted, and **completely
wrong** once traced against the actual code: a misread idempotency short-circuit
reported as money corruption; a "host won't boot" that was really a config-binder
default; a "double-send race" that an existing ordering already prevents; a
"lost payout" that was a deliberate rollback-and-rerun design. An agent that
emits 4 false BLOCKERs is **worse than no agent**, because its output gets
trusted and you may "fix" working code and introduce real bugs.

**Fix (tisknicz: `docs/process/quality-gates.md` → "Gate 0 — Evidence
discipline", referenced from every finder charter).** A meta-rule governing
*how every finding is reported*. Every reported finding must satisfy ALL of:
1. **REFUTED by default** — treat your own hypothesis as false until traced
   through the code. Can't complete the trace → report a *question*, not a
   finding.
2. **File:line evidence** — cite the defect location AND the location of the
   guard you confirmed is missing/insufficient.
3. **Concrete trigger** — the exact input/sequence that reaches the bug. No
   repro = not confirmed.
4. **Guard check** — before reporting, look for the guard that already prevents
   it (a state check, an idempotency key, an authz attribute, a config default,
   a DB constraint). **Most "bugs" die here.** If a guard exists → REFUTED.
5. **Severity honesty** — BLOCKER = exploitable / money-losing / illegal-state
   in production *as written, today* — not "in a hypothetical future topology."

And the orchestrator's posture: **verify before acting** on any finding; never
"fix" on an unverified one.

**Adapt to Cleansia.** Add the same Gate 0 to Cleansia's quality-gates doc (or
create one). Reference it from every Cleansia agent that *reports findings*
(reviewer, QA/tester, security, performance, and any ad-hoc exploration agent).
The guard-list in step 4 should name Cleansia's *own* guard mechanisms (whatever
its equivalents of authz, idempotency, constraints are). This single rule has
the highest payoff — it changes fan-out from a liability into an asset.

---

## 2. Calibrate orchestration: fan out for BREADTH, stay direct for DEPTH

**Symptom.** A blanket "use multi-agent orchestration for everything" instinct
leads to spawning a sub-agent for sequential, single-context work (e.g. "an
agent to implement this one feature"). That **re-loads context per step and
loses the thread** — pure ceremony cost, zero parallelism gain.

**Fix (tisknicz: `docs/process/routing.md` → "Orchestration shape").** Decide by
the *shape* of the task, not its size:
- **Fan out (parallel agents) for BREADTH** — independent pieces, no shared
  state, you want the conclusion not the file-dumps: multi-lens review, bug
  audits, broad searches, independent design attempts.
- **Stay direct (main loop) for DEPTH** — one coherent change with internal
  dependencies where context must carry step-to-step: implementing a feature,
  a focused edit you already hold context for.
- **Hybrid (the usual answer):** scout/decompose *directly* → fan out over the
  work-list → fold results *directly*.
- Rule of thumb: *if the sub-tasks could run in any order without seeing each
  other, fan out; if each needs the previous one's reasoning, stay direct.*

**Adapt to Cleansia.** If Cleansia has a routing/orchestration doc, add this
section. If Cleansia runs under an "always orchestrate" directive, soften it to
"orchestrate for breadth; work directly for a single coherent change." Pair it
with Gate 0 — the wider you fan out, the more false positives you import, so the
fold step must verify.

---

## 3. Add the regression guard at the FIRST occurrence of a bug, not the third

**Symptom.** The same class of bug shipped **twice** before it got a guard (a
dead-link bug from one route-group mistake recurred in a later batch). The
codebase already had a "harvest recurring findings at the 3rd occurrence" rule —
but waiting for three is reactive; the cheap guard should land with the *first*
fix.

**Fix (tisknicz: `.claude/agents/reviewer.md` → "First-occurrence guard duty").**
When a PR *fixes* a bug, the reviewer asks "what cheap static check makes this
class unrepeatable?" and requires it in the **same PR**. A static guard = a
consistency-linter rule, a test that scans for the pattern, a type that makes
the bad state unrepresentable, or a DB constraint. If no cheap guard is
feasible, the PR says so explicitly.

**Adapt to Cleansia.** Add this to Cleansia's reviewer/QA charter. Cleansia's
"cheap guard" menu depends on its stack (a lint rule, a unit test, a schema
constraint, a CI check). The principle is stack-agnostic: **a fix without a
guard invites the bug back.**

---

## 4. Every open question carries an OWNER and a RESOLVE-BY deadline

**Symptom.** Questions accumulated far faster than they closed; several
deferred *silently* and became launch surprises (two security questions sat
open for ~3 weeks until they were finally the blocking work). A `blocking:
yes/no` flag isn't enough — "no" questions still need a deadline or they drift.

**Fix (tisknicz: `docs/questions/open.md` → "Triage discipline" header).**
Every open question must carry:
- **Owner** — who decides (`user` for a business/legal/product call; an agent
  for a technical default the user ratifies).
- **Resolve-by** — `pre-launch` (blocks go-live) | `v1.1` | `backlog`. A
  question with no Resolve-by may not stay `open`.
- A live **launch-blocking index** at the top of the file lists *only* the
  `pre-launch` questions, so nothing launch-critical hides in a long file.
- The PM/orchestrator escalates any still-open `pre-launch` question at every
  checkpoint, and it also gets a line in the launch checklist.

**Adapt to Cleansia.** Add Owner + Resolve-by to Cleansia's open-questions
template, and put a launch/milestone-blocking index at the top. Whatever
Cleansia's milestones are (not necessarily "launch"), the buckets become
`<next-milestone>` | `later` | `backlog`. The point: **no question is allowed to
be open without a deadline and an owner.**

---

## 5. Tier documentation weight by importance — don't tax every change uniformly

**Symptom.** Documentation weight scaled with *every* change, not its
importance. Index rows grew into multi-hundred-word paragraphs; every small fold
touched 4–5 docs. A typo-fix paid the same prose tax as a payments decision. The
audit trail is genuinely valuable — but it's over-invested when applied flat.

**Fix (tisknicz: `docs/process/ticket-lifecycle.md` → "Documentation weight").**
Proportionality:
- **Index row = ONE line** (title + short hook). Full context lives in the
  ticket, not the index.
- **Full deliberation record** (Context / Scope / Alternatives / Defense) is for
  **load-bearing tickets** — money, state machines, auth/security, schema, a
  provider seam, a cross-cutting concern.
- **Lightweight tickets** (hygiene, a contained fix) get Context + Scope + AC
  and skip the Alternatives prose unless a real decision was made.
- **Folds** update only the doc that actually changed, not the whole tree.

**Adapt to Cleansia.** If Cleansia has heavy per-ticket docs, introduce the same
tiering. The test for "does this earn a full deliberation record?" is: *is the
decision load-bearing and expensive to reverse later?* If not, keep it short.

---

## 6. Don't track session-managed / operator-local config in git

**Symptom.** Claude Code's `.claude/settings.json` is rewritten by the session
(model, permissions, etc.) on every run, so it was **perpetually dirty**. Being
git-tracked, it repeatedly **blocked `git checkout` and `git rebase`** and forced
stash gymnastics, and every commit needed manual vigilance to exclude it. The
single most repeated friction of the whole build.

**Fix (tisknicz: `.gitignore`).** `git rm --cached .claude/settings.json` +
gitignore it (and `.claude/settings.local.json`). The **shared** Claude config
that *should* be versioned and reviewed — `CLAUDE.md`, `.claude/agents/`,
`.claude/commands/` — stays tracked. Result: worktree is clean, no more dance.

**Adapt to Cleansia.** Check `git status` in Cleansia: if `.claude/settings.json`
(or any session-rewritten file) shows as perpetually modified, gitignore it the
same way. Keep the *shared* config tracked; ignore the *operator-local /
session-managed* config. (Note: this changes a tracked file for other
contributors — a harmless one-time heads-up.)

---

## 7. Decide the test pyramid — especially END-TO-END — at the START, not at the end

**Symptom (NOT yet fixed in tisknicz — flagged as a real gap).** The whole test
strategy was unit + integration. There is **no automated end-to-end layer**: the
one launch-blocking bug (a dead link on a critical CTA) was the kind only a
rendered-route / E2E check catches, and the entire critical revenue path
(browse → pay → confirm → fulfil → settle) is verified only by a **manual**
checklist. The gap between "integration tests pass" and "the critical path
actually works in a browser" is the riskiest uncovered area.

**Recommendation (applies to BOTH projects).** Decide the E2E layer at the
project's **Phase 0**, not as a pre-launch scramble. Even a *thin* end-to-end
smoke of the single most important user/revenue path, running in CI against a
seeded stack, would have caught the dead CTA *and* given the critical path
automated coverage. This is real work (a test harness + a CI job), not a doc
edit — so it's a *ticket*, not a charter change.

**Adapt to Cleansia.** Audit Cleansia's test pyramid now. If there's no E2E
layer, file it as an early ticket rather than discovering the gap near a
milestone. Scope it thin: one happy-path smoke of the most critical flow, in CI,
against seeded data.

---

## How to use this document (instructions for the Cleansia agent)

1. **Read the source files** referenced above in the tisknicz repo for the full
   text and reasoning — this summary is the index, not the whole story:
   - `docs/process/quality-gates.md` (Gate 0)
   - `docs/process/routing.md` (orchestration shape)
   - `docs/process/ticket-lifecycle.md` (doc tiering + DoR)
   - `docs/questions/open.md` (triage header)
   - `.claude/agents/reviewer.md`, `qa.md`, `secops.md`, `optimizer.md`
     (how the evidence discipline + guard duty read in a charter)
2. **Audit Cleansia's equivalents** — find Cleansia's `CLAUDE.md`, agent
   charters, process docs, ticket and question conventions.
3. **Apply what fits, re-expressed in Cleansia's terms.** Items 1–6 are
   workflow/governance edits you can make directly. Item 7 (E2E) is a ticket to
   propose, not a silent change.
4. **Do not copy tisknicz domain content** — no Makables ADRs, stack rules, or
   ticket IDs. Copy the *patterns*, name them in Cleansia's own vocabulary.
5. **Be honest about scope:** these lessons come from the artifacts and the
   later phases of the tisknicz build, generalized. Sanity-check each against
   Cleansia's actual conventions before applying — some may already be covered,
   and a rule Cleansia already has shouldn't be duplicated.

---

*Source: Makables (tisknicz) build retrospective, 2026-06-21. The corresponding
process changes shipped on the `chore/workflow-hardening` branch.*
