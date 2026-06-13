# Reviewer recurring findings — harvest log

> Pattern-evolution loop. The reviewer logs every finding that has now been raised more than once across the repo. The architect sweeps this log every 2–3 sprints, promotes anything at **count ≥ 3** into either [`docs/architecture/patterns.md`](../architecture/patterns.md) (a new §A.N / §B.N row) or [`scripts/check-consistency.mjs`](../../scripts/check-consistency.mjs) (a new mechanical check), and marks the row codified.

This file is **append-only during a sprint**. Only the architect edits prior rows — and only to flip `status` or add a link to the codifying ADR / patterns row / script check.

## Purpose

Reviewer feedback is cheap to write and expensive to scale. The same nit appearing in three tickets is no longer a nit — it is a missing pattern. This log is the bridge between **"reviewer caught it again"** and **"the catalog now forbids it"**:

1. **Reviewer** appends a row the second time a finding lands (count starts at **2**).
2. **Reviewer** increments `files` / `tickets` on every subsequent repeat — no new row.
3. **Architect** sweeps every 2–3 sprints. Anything at **count ≥ 3** earns one of:
   - **codified-in-patterns** — a new pattern row in [`patterns.md`](../architecture/patterns.md) §A or §B, optionally backed by an ADR if the rule is contentious.
   - **codified-in-script** — a new check in [`scripts/check-consistency.mjs`](../../scripts/check-consistency.mjs) so CI catches the next instance before review.
   - **wontfix** — the finding is real but not worth a rule (e.g. taste-only, or the cost of enforcement exceeds the cost of the nit). Architect leaves a one-line defense.
4. Once a finding is codified, **the reviewer stops logging it** here — CI or the catalog owns it. New violations become "violates §A.N" in PR comments, not new rows.

Cross-references:
- [`docs/architecture/patterns.md`](../architecture/patterns.md) — destination for codified rules. The **Evolution loop** note at the top of that file points back here.
- [`.claude/agents/reviewer.md`](../../.claude/agents/reviewer.md) — the reviewer's **harvest duty** lives in the workflow section: after every PR review, append or increment rows here.
- [`docs/review/checklist.md`](./checklist.md) — the per-PR checklist. Codified rows often graduate into a new checklist line.

## Log

| # | Finding | Files seen (count) | Tickets seen | Proposed rule | Status |
|---|---|---|---|---|---|
| 1 | *(example)* Test method names redundant with class name (e.g. `OrderTests.OrderTests_Create_Succeeds`) | 3 | T-0060, T-0061, T-0067 | "Test names describe behavior, not the method under test. Prefer `Create_WithValidInput_ReturnsOrderId` over `CreateOrder_Succeeds`." | pending |
| 2 | Ticket/code ships a new `BusinessErrorMessage` constant without its parallel `cs-CZ` i18n key — caught only at Gate 9 contract/i18n parity, never at authoring time | 3 | order-cleanup bundle, order-dashboards bundle, payout-core bundle (`csvPathAlreadySet`) | "Every new `BusinessErrorMessage` constant MUST ship with a matching `cs-CZ` i18n key in the same PR. Enforce mechanically: a pre-commit or CI check asserting each `BusinessErrorMessage` code has a parallel `cs-CZ` key (Architect/secops to design). **Standing automated-gate candidate** — third strike fired at payout-core; this is no longer a checklist line, it wants a script." | pending |

<!--
Column legend:
- # — running id. Never renumber. If a row is wontfix, leave it in place.
- Finding — one sentence. Include the smell, not the fix.
- Files seen (count) — integer. Bump by 1 per new file. Do not list paths here (PR diff already has them); the count is what matters for sweep triage.
- Tickets seen — ticket IDs, comma-separated, oldest first.
- Proposed rule — the candidate pattern, phrased as a rule the catalog could quote verbatim.
- Status — one of:
    - pending          — awaiting architect sweep
    - codified-in-patterns   — promoted to patterns.md §A.N / §B.N (link the section)
    - codified-in-script     — promoted to scripts/check-consistency.mjs (link the check id)
    - wontfix          — architect declined; reason on the next line
-->

## Workflow — reviewer

After every PR review:

1. **For each finding you left in the PR**, search this log (Ctrl-F on the finding's core noun is usually enough).
2. If a matching row exists:
   - Bump `Files seen (count)` by the number of new files in this PR that triggered it.
   - Append the ticket id to `Tickets seen` (skip if already listed).
3. If no matching row exists **and this is the first repeat** (you remember calling out the same thing in a previous PR but it is not logged yet):
   - Append a new row with `count = 2` and both ticket ids.
4. If this is the **first time** you have ever raised the finding, do **not** log it. Single occurrences are noise; the log is for repeats.
5. Commit the log update on the **same branch** as your review comments. The reviewer's harvest is part of the review, not a follow-up task.

> Rule of thumb: if you find yourself typing the same sentence into PR #N that you typed into PR #N-7, the rule is missing — log it.

## Workflow — architect

Every 2–3 sprints (or whenever the log gains 5+ pending rows, whichever comes first):

1. **Triage `pending` rows** newest to oldest.
2. For each row at **count ≥ 3**, decide:
   - **Codify in patterns.md?** Pick a section (§A.N for backend, §B.N for frontend). Write the rule + a one-line **Verification** clause (how a reviewer or CI proves it). Renumber subsequent sections if needed and update the patterns.md TOC.
   - **Codify in script?** Add a check to [`scripts/check-consistency.mjs`](../../scripts/check-consistency.mjs) — a mechanical check is strongly preferred over a checklist line when feasible (grep-able, AST-walkable, or directory-shape based). Tag the check with the finding `#` so the script output points back to this row.
   - **Both?** Patterns.md states the rule; the script enforces a subset of it. Most rules want this.
   - **Wontfix?** Add a one-line reason on the row below the table (so the table stays scannable). The next reviewer should not re-propose the same rule.
3. Flip `Status` and append the link to the codifying section / check id.
4. **Do not delete codified rows.** They are the audit trail showing why §A.N exists.
5. If the rule is contentious (changes how a layer is built, adds a new constraint to `Core.Domain`, etc.) — write a superseding **ADR** under [`docs/adr/`](../adr/) and link it from the patterns row. Cheap deliberation: capture `## Alternatives Considered` and `## Defense` in the ADR.

## Wontfix reasons

<!-- Append one-line reasons here, keyed by row #. Keep the table above scannable. -->

*(none yet)*
