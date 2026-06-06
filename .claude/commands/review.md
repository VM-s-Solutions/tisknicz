# /review — Manual reviewer pass over current diff or a specific PR

Trigger the **reviewer** agent against the current working diff, a named PR, or a path-scoped slice. The reviewer walks `docs/review/checklist.md` and the seven gates in `docs/process/quality-gates.md`, then writes a verdict to `docs/review/runs/T-NNNN.md`.

## When to use

- Before opening a PR — sanity check your own diff against CLAUDE.md, the ticket's AC, and the ADRs it cites.
- After CI goes green on an open PR, to get the formal Gate 1–7 verdict before requesting human merge.
- When iterating on a PR after fixes — re-run to confirm prior BLOCKERs are cleared.
- When you want a focused review on a slice (`--paths`) without re-reviewing the whole PR.
- When a ticket is security-touching (Gate 3) or perf-sensitive — chain `--security` and/or `--perf` so SecOps and the optimizer file findings into the same run document.

Do **not** use this for a quick "looks good" — the reviewer agent is strict by design and will reject under-specified work. If the ticket is still on the bench, run `/groom` first.

## Steps

1. **Resolve the target.**
   - Default: review the current working tree diff vs. `master`.
   - `--pr=<number>`: fetch the PR diff via `gh pr view <number> --json files,headRefName,body,title,baseRefName` and check out the head branch read-only.
   - `--paths=<glob>`: restrict the diff to files matching the glob (e.g. `--paths=backend/src/Makables.Core.AppServices/Features/Product/**`). Useful for chunked review of a large PR.

2. **Identify the ticket the PR closes.**
   - Parse `Closes T-NNNN` / `Refs T-NNNN` from the PR body, or the branch name (`feat/T-NNNN-*`, `fix/T-NNNN-*`).
   - Open `docs/tickets/T-NNNN-*.md`. Read the AC, the Test Plan, and the frontmatter `adrs:` list. If no ticket is referenced, halt and emit a single BLOCKER: "PR has no ticket reference — Gate 2 cannot be evaluated."

3. **Read referenced ADRs.**
   - For every ADR id in the ticket frontmatter, read `docs/adr/<id>-*.md` end-to-end. Note constraints you must enforce (e.g. ADR 0003 forbids `decimal` for stored money; ADR 0007 forbids reintroducing Supabase SDKs).
   - If the diff touches money, country branching, payments, shipping, address, or auth and the relevant ADR is **not** in the ticket frontmatter, file a Medium finding asking PM/Architect to add the linkage.

4. **Walk `docs/review/checklist.md` section by section.**
   - Sections A (CLAUDE.md self-check), B (Architecture), C (Domain & extension points), D (Security), E (UI/UX), F (AC traceability), G (Tests & docs).
   - For each failing row quote the row verbatim — do not paraphrase. Cite `path/to/file.ext:line` and a one-line fix.
   - Skip sections that do not apply (e.g. E for a backend-only PR) but state so explicitly.

5. **Walk `docs/process/quality-gates.md` gates 1–7.**
   - Gate 1 — CLAUDE.md self-check (Backend or Frontend variant by diff scope).
   - Gate 2 — AC traceability: every AC line in the ticket maps to a hunk in the diff. Missing AC → BLOCKER.
   - Gate 3 — Security (mandatory if the ticket is security-touching per the gates list). If `--security` was passed, also invoke the **secops** agent and inline its findings.
   - Gate 4 — Architecture (mandatory if an extension point in `docs/architecture/extension-points.md` is touched). If touched without ADR coverage → BLOCKER, request **architect** sign-off.
   - Gate 5 — Tests. TDD hard rule applies for T-0067+: pure logic without a prior failing test → BLOCKER. T-0001..T-0066 are grandfathered (call this out as a Nit only).
   - Gate 6 — Contract parity: if `backend/**/Controllers/**` or any DTO record changed, verify `frontend/src/lib/api-client/**` was regenerated in the **same PR**. Manual edits to `lib/api-client/**` → BLOCKER.
   - Gate 7 — Docs: architecture / process / env / extension-point changes require the matching doc update in the same PR.

6. **Verify RDD parity** (per reviewer charter, ADR 0015).
   - Every new aggregate, value object, domain service, repository interface, or adapter interface in the diff has a role file under `docs/architecture/roles/`.
   - Handlers depend on ~5 collaborators or fewer. More → Medium finding ("split the handler or extract a domain service").

7. **Classify findings.**
   - **BLOCKER** — checklist row fails, gate red, AC missing, security hole, money in `decimal`, country branched outside an adapter, manual `lib/api-client/` edit, missing test for new pure logic on T-0067+.
   - **High** — non-blocking correctness risk (e.g. missing index on a column used in WHERE, missing `.AsNoTracking()` on a hot read query).
   - **Medium** — design smell, oversized handler, doc drift, missing ADR linkage when one would help.
   - **Low** — small refactor opportunity, naming, log level.
   - **Nit** — style, ordering, optional comment. Never a reason to reject.
   - Each finding: `path/to/file.ext:line — <verbatim checklist quote or gate name> — <specific fix>`.

8. **Render the verdict.**
   - **APPROVE** — zero BLOCKER, zero High.
   - **APPROVE_WITH_NITS** — zero BLOCKER, zero High; Medium/Low/Nit present.
   - **REQUEST_CHANGES** — one or more BLOCKER or High.
   - Per reviewer charter: never approve under pressure; "small change" is not a reason.

9. **Write the run document** to `docs/review/runs/T-NNNN.md` (create the directory if missing). Append the timestamp if a prior run exists for the same ticket: `T-NNNN-YYYYMMDD-HHMM.md`. The file MUST contain:
   - Frontmatter: `ticket`, `pr`, `commit`, `reviewer_run_at`, `verdict`.
   - `## Scope` (paths reviewed; note any `--paths` filter).
   - `## Gates` (table: Gate 1–7, pass/fail/n-a, one-line reason).
   - `## Findings` (grouped by severity; each with file:line + fix).
   - `## Verdict` (one of the three above, with the gating reason).
   - `## Next steps` (who unblocks: implementing agent for BLOCKER/High; PM for AC gaps; Architect for extension-point design; SecOps for Gate 3).

10. **If `--inline` is set**, also stream the verdict back to the caller (skip the file write only when the user explicitly asked for transient output). Otherwise just emit the run-file path.

11. **If `--security` is set**, invoke the **secops** agent on the same diff before rendering the verdict and merge its findings into the run document under a `### SecOps` subsection.

12. **If `--perf` is set**, invoke the **optimizer** agent on the same diff and merge its findings under `### Optimizer`. Perf findings are High at most unless they violate an ADR (then BLOCKER).

## Flags

- `--pr=<number>` — review an open PR instead of the working tree.
- `--paths=<glob>` — restrict the diff to a glob; verdict scope notes the filter.
- `--security` — also run **secops** on the same diff.
- `--perf` — also run **optimizer** on the same diff.
- `--inline` — also print the verdict to the caller (in addition to writing the run file).

## Output

- File: `docs/review/runs/T-NNNN.md` (or timestamped variant on re-run).
- Console: the verdict line + path to the run file.

## See also

- `docs/review/checklist.md` — the canonical checklist this command walks.
- `docs/process/quality-gates.md` — the seven gates a PR must clear before merge.
- `docs/process/ticket-lifecycle.md` — where review fits between in-progress and merged.
- `.claude/agents/reviewer.md` — reviewer charter and style rules.
- `.claude/agents/secops.md` — invoked when `--security` is passed or Gate 3 applies.
- `.claude/agents/architect.md` — invoked when Gate 4 fires.
- `docs/architecture/extension-points.md` — the list that triggers Gate 4.
- `docs/adr/0015-responsibility-driven-design.md` — RDD parity rule enforced in step 6.
- `CLAUDE.md` — the Self-Check section enforced in Gate 1.
