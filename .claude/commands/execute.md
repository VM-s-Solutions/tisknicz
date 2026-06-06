# /execute — drive a ready Makables ticket through merge

## When to use

Use `/execute` when the ticket is already **ready** (AC complete, dependencies done, sized) and you want the agent team to actually build, review, and merge it. This command does not refine, decompose, or design — it picks up a ticket that has already passed DoR and runs it through the lifecycle in `../../docs/process/ticket-lifecycle.md` to **done**.

Distinct from:
- `/feature` — creates a new ticket (draft) from a user request or story.
- `/plan` — decomposes a large or vague ticket into smaller ready tickets.
- `/execute` — runs an already-ready ticket end-to-end.

Forms:
- `/execute` — PM picks the next ready ticket whose `depends_on` are all `done`, in priority order from `../../docs/tickets/INDEX.md`.
- `/execute T-NNNN` — run the named ticket; if it is not `ready` or has unmet dependencies, PM stops and explains what is missing (does not silently promote a `draft` ticket).

## Steps

1. **PM selects the ticket.**
   - If a `T-NNNN` was given, load `../../docs/tickets/T-NNNN-*.md` and verify state is `ready`, AC is non-empty, sizing is set, and every entry in `depends_on` resolves to a ticket whose state is `done`. If any check fails, stop and report.
   - If no ticket was given, PM scans `../../docs/tickets/INDEX.md` for the highest-priority ticket where state is `ready` and all `depends_on` are `done`. Ties broken by sprint goal in `../../docs/status/sprint-N.md` (current sprint), then by ticket number.
   - Record the selection in the ticket file under `## Execution log` with a timestamp.

2. **PM transitions `ready` → `in_progress`.**
   - Update the ticket's front-matter `state:` field, append a log line, and update `../../docs/tickets/INDEX.md` in the same edit. Create the branch name `feat/T-NNNN-<slug>` (or `fix/...`) per `../../docs/process/ticket-lifecycle.md` §Branch & PR conventions.

3. **PM dispatches reviewer in parallel with implementers.**
   - The reviewer agent runs **preliminary, non-blocking** review as work lands, writing notes to `../../docs/review/runs/T-NNNN-draft.md` (create the file if absent). These draft notes are advisory — they catch direction problems early but do not gate merge.
   - The implementer agents (selected per step 4) run concurrently with reviewer's draft pass. Implementers may read the draft notes between commits and adjust.

4. **PM routes work to implementing agents per the ticket's touched paths.**
   - Routing follows `../../docs/process/ticket-lifecycle.md` §Cross-stack tickets and the agent charters in `../../.claude/agents/`:
     - schema or migration changes → `dotnet-db` first (must finish before `dotnet-backend` starts).
     - feature handler, controller, validator, or adapter → `dotnet-backend`.
     - new pages, components, or consumption of regenerated `lib/api-client/` → `frontend` (may run parallel to `dotnet-backend` only after the controller signature is locked in the ticket).
     - new `BusinessErrorMessage` codes or user-facing strings → `l10n`.
     - extension-point or new ADR territory → `architect`.
     - ambiguous AC discovered mid-flight → `ba` clarifies; PM updates the ticket; reviewer rechecks.
     - auth, webhooks, blob storage, secrets, cron, or PII columns → `secops` engaged at PR open per Gate 3.
     - test plan authored during implementation, executed at PR open → `qa`.
   - `optimizer` engages only when reviewer's draft flags a perf hot path or a duplicated abstraction worth consolidating before PR open.

5. **NSwag contract gate.** If the backend contract changed, `dotnet-backend` regenerates `frontend/src/lib/api-client/` in the same PR and notes the affected `<host>-api.ts` file in the PR body. The pre-commit hook will block manual edits to that directory. No frontend consumption work merges without the regenerated client.

6. **PM transitions `in_progress` → `in_review` and opens the PR.**
   - PR title `T-NNNN: <ticket title>`, body links the ticket, lists AC items addressed, links any new ADR, and flags NSwag regeneration if applicable.
   - Reviewer now runs the **final** review pass — this is the gating one. It supersedes the draft notes in `../../docs/review/runs/T-NNNN-draft.md`; the final write-up lives in `../../docs/review/runs/T-NNNN.md`.

7. **Gates run per `../../docs/process/quality-gates.md`.**
   - Gate 1 (CLAUDE.md self-check) — reviewer.
   - Gate 2 (AC verification with proofs) — qa + reviewer.
   - Gate 3 (security) — secops, mandatory for security-touching tickets.
   - Gate 4 (architecture) — architect, mandatory if an extension point was touched.
   - Gate 5 (tests) — qa. Includes the TDD hard rule for pure-logic code on T-0067+ (tickets T-0001–T-0066 are grandfathered); reviewer rejects after-the-fact tests on in-scope tickets.
   - Gate 6 (contract parity) — reviewer confirms the regenerated client matches `openapi/v1.json`.
   - Gate 7 (docs) — author of the change updates architecture, process, extension-points, or env-var docs as applicable.

8. **PM transitions `in_review` → `qa` → `done` on merge.**
   - On merge to `master`, PM:
     - Flips the ticket state to `done` and appends the merge commit SHA to the execution log.
     - Updates `../../docs/tickets/INDEX.md` (state + closed date).
     - Updates the current `../../docs/status/sprint-N.md` — moves the ticket under **Done**, notes the merge SHA, and refreshes the sprint burndown line.
     - Deletes the draft review file `../../docs/review/runs/T-NNNN-draft.md` once the final `T-NNNN.md` is in place.

9. **Continue or stop.** After `done`, PM evaluates whether to continue:
   - **Continue** if another `ready` ticket exists whose dependencies are now satisfied AND no `manual_step` block applies AND the user has not typed `stop`. Loop back to step 1.
   - **Stop** if any of the following:
     - PR is open and awaiting external merge (CI, human approver, deploy preview not yet up).
     - The next ticket carries a `manual_step:` field requiring user action (e.g., a secret to provision in Azure Key Vault, a domain DNS change, a Comgate merchant config).
     - The user has explicitly typed `stop` or interrupted the run.
     - Backlog is empty — no `ready` ticket with satisfied dependencies remains.
   - On stop, PM reports the stop reason, the current state of every ticket touched in this run, and the next suggested action (e.g., "T-0071 needs `manual_step`: rotate `CRON_SECRET` in Key Vault before /execute can resume").

10. **Capture deliberation cheaply.** Per the user-locked decision on cheap deliberation, any non-obvious call made during execution (alternative considered and rejected, a perf trade-off, a contract shape decision) is recorded in the ticket under `## Alternatives Considered` and `## Defense`. The user is the challenger — these sections exist so they can interrogate the choice later. Do not open a separate ADR unless the decision touches an extension point.

## See also

- `../../docs/process/ticket-lifecycle.md` — state machine, branch conventions, parallelism rules
- `../../docs/process/quality-gates.md` — the seven gates enforced at PR open
- `../../docs/process/communication.md` — who reports what, when
- `../../docs/process/discovery.md` — how `ready` is earned (read this if `/execute T-NNNN` rejects the ticket)
- `../../docs/tickets/INDEX.md` — priority order and dependency graph
- `../../docs/tickets/template.md` — ticket file shape, including `depends_on` and `manual_step`
- `../../docs/review/checklist.md` — reviewer's working checklist
- `../../docs/architecture/patterns.md` — A.1–A.21 backend + B.1–B.19 frontend patterns referenced by reviewer
- `../../docs/architecture/extension-points.md` — triggers Gate 4
- `../../.claude/agents/pm.md`, `../../.claude/agents/reviewer.md`, `../../.claude/agents/dotnet-backend.md`, `../../.claude/agents/dotnet-db.md`, `../../.claude/agents/frontend.md`, `../../.claude/agents/qa.md`, `../../.claude/agents/secops.md`, `../../.claude/agents/architect.md`, `../../.claude/agents/ba.md`, `../../.claude/agents/l10n.md`, `../../.claude/agents/optimizer.md` — agent charters
- `/feature` — author a new ticket from scratch
- `/plan` — decompose a large or vague ticket into ready tickets
