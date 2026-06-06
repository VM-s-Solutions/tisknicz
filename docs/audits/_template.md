---
id: AUDIT-YYYYMMDD-<subsystem>-<dimension>
subsystem: <backend | frontend | infra | contract | cross-stack | <feature-slug>>
dimension: <architecture | security | performance | i18n | accessibility | money | error-codes | nswag-parity | dead-code | test-coverage>
ran_on: YYYY-MM-DD
ran_by: <pm | architect | ba | dotnet-backend | dotnet-db | frontend | l10n | qa | reviewer | secops | optimizer>
state: open   # open | consolidated | archived
related_audits: [AUDIT-YYYYMMDD-..., AUDIT-YYYYMMDD-...]
adrs: [0001, 0007]
---

# AUDIT-YYYYMMDD-&lt;subsystem&gt;-&lt;dimension&gt; — &lt;Short title&gt;

One paragraph: why this audit was run, what surface it covers, what the reader should take away. Keep it factual — defenses go in the Defense block, alternatives in Alternatives Considered.

## Scope

Bulleted list of exactly what was inspected. Be specific — paths, layers, feature folders, ADRs in play. A reader should be able to reproduce the audit boundary without guessing.

- `backend/src/Core.Domain/Orders/` (entity invariants, set-once semantics)
- `backend/src/Core.AppServices/Features/Orders/` (handlers, validators, responses)
- `frontend/src/app/(public)/objednavka/` (Server Components only — no client islands in scope)
- ADR 0014 (audit + UoW pipeline) — pipeline behavior conformance
- Pattern catalog rows A.4 (BusinessResult), A.7 (Money), B.6 (apiFetch wrapper)

State explicitly what is **out of scope** so the reader does not mistake silence for clearance:

- NOT inspected: payment-provider clients (`Infra.Clients/Comgate/`) — separate audit
- NOT inspected: generated `lib/api-client/` — pre-commit hook owns parity

## Method

How the audit was performed. Mix of automated + manual. List the exact tools, scripts, and prompts so the next run is reproducible.

- `scripts/audit/consistency.mjs --subsystem=<subsystem> --dimension=<dimension>` (Node 20+, no external deps)
- Manual grep passes: `Grep("dynamic", glob="**/*.cs")`, `Grep("any", glob="**/*.ts")`, `Grep("console\\.", glob="**/*.{ts,tsx}")`
- Pattern-catalog cross-walk: every changed file mapped to its catalog row(s); deviations recorded as findings.
- Diff base: `master` at commit `<sha>` (record the SHA — audits are point-in-time snapshots).

Note any caveats — false-positive patterns the script skipped, sampling boundaries (e.g. "spot-checked 12 of 47 handlers"), environment quirks (e.g. "EF migrations not applied — schema lint based on `Up()` source only").

## Findings

Findings are recorded once, in one table. One row per distinct defect. Group related rows by leaving a blank line between groups if helpful, but keep the table single.

**Severity legend** (matches `docs/review/checklist.md`):

- **blocker** — ships a known broken contract, security hole, or money bug. Must be fixed before merge / before next release.
- **major** — architectural violation, dead code in a hot path, missing i18n on a customer-facing surface. Fix this sprint.
- **minor** — hygiene, naming, missing comment. Fix opportunistically; bundle into the next touching ticket.
- **info** — observation worth recording for context. No ticket required.

| severity | finding | evidence (file:line) | proposed fix | proposed ticket | size | layers |
| --- | --- | --- | --- | --- | --- | --- |
| blocker | `<one-line description of the defect>` | `backend/src/Core.AppServices/Features/Orders/MarkOrderPaid.cs:142` | `<concrete fix in one sentence>` | T-NNNN | S | backend |
| major | `<...>` | `frontend/src/app/(public)/katalog/page.tsx:33` | `<...>` | T-NNNN | M | frontend |
| major | `<...>` | `backend/src/Core.Domain/Orders/Order.cs:201`, `:248` | `<...>` | T-NNNN | M | backend, db |
| minor | `<...>` | `docs/architecture/patterns.md:A.12` | `<...>` | — | S | docs |
| info | `<...>` | `backend/src/Infra.Database/Migrations/20260603110319_Orders.cs:36` | `<no action — recorded for the next migration author>` | — | — | db |

Column rules:

- **severity** — lower-case, one of `blocker / major / minor / info`. No emoji.
- **finding** — one declarative sentence, present tense, no hedging. "X does Y" not "X might do Y."
- **evidence** — at least one `file:line` (or `file:line-line` for a span). Multiple permitted, comma-separated. Paths are repo-relative; never absolute. If the defect is structural (no single line), cite the directory + the catalog row it violates: `docs/architecture/patterns.md:A.7`.
- **proposed fix** — one sentence. If the fix is non-trivial, do NOT inline the design here — point at the proposed ticket and write the design in that ticket per `docs/tickets/template.md`.
- **proposed ticket** — `T-NNNN` if a ticket should be opened, `—` if no ticket (info rows, docs-only nits the auditor can fix inline). For findings that consolidate into an existing ticket, use that ticket's ID.
- **size** — `S / M / L` matching `docs/tickets/template.md`. `—` for info rows.
- **layers** — comma-separated from {`backend`, `db`, `frontend`, `infra`, `contract`, `docs`, `tests`, `ops`}. Drives which agent picks the ticket up.

## Proposed tickets

For every distinct `T-NNNN` cited in the Findings table, expand here with one block. This is the bridge from "raw finding" to "ready for PM to file." Keep each block short — the full design lives in the ticket itself once filed.

### T-NNNN — &lt;short imperative title&gt;

- **Severity rolled up from findings:** `<blocker | major | minor>`
- **Layers:** `<comma list>`
- **Findings rolled in:** rows 1, 3, 5 (reference by row position in the Findings table above)
- **Why now:** one sentence — what breaks or rots if this slips.
- **Scope sketch:** 2–4 bullets. Not a full ticket; enough for PM to estimate.
- **Out of scope sketch:** what an eager reader might assume is included but is NOT.
- **Suggested owner:** `<agent name>` (e.g. `dotnet-backend`, `frontend`, `secops`).
- **Depends on:** `[T-NNNN]` if any.
- **ADRs touched:** `[0014, 0020]` if any.
- **Test plan hint:** one sentence — the smallest verifiable proof of the fix (a failing test that turns green, a CI lint that goes from red to green, a manual repro that stops reproducing).

### T-NNNN — &lt;next ticket&gt;

…

## Alternatives Considered

For each non-obvious finding (every blocker and most majors), record at least one alternative fix that was weighed and rejected. The user is the challenger — this block defends the proposed fix against the obvious "why not X?" question. Keep each entry to two or three sentences.

- **Finding row 1** — Considered: leave the field nullable and patch at read time. Rejected: pushes the invariant out of the domain into every reader; set-once at the entity is one line and pins the contract.
- **Finding row 3** — Considered: add a feature flag to disable the new validator. Rejected: the validator IS the fix; a flag means the broken code path stays reachable in prod.

## Defense

A short paragraph (3–6 sentences) anticipating the strongest pushback against this audit's conclusions and answering it. Examples of pushback to address:

- "These are all hygiene, why are you blocking the release?" — answer with the specific blocker(s) and their blast radius.
- "We agreed to defer i18n to phase 4." — answer with the actual scope this audit covered vs. the deferred slice.
- "Aren't half of these false positives?" — answer with the sampling rate and the manual verification pass.

This block exists because deliberation is CHEAP (per user decision 2): every audit pre-stages its own defense rather than waiting to be challenged.

## Consolidation

When two or more audits cover overlapping surface, the later auditor SHOULD consolidate. Record the consolidation here:

- **2026-MM-DD** — consolidated rows 2, 4, 7 into AUDIT-YYYYMMDD-&lt;newer&gt; row 1. State flipped to `consolidated`. Original rows retained for traceability; the newer audit owns the ticket(s).

When this audit is fully superseded, flip frontmatter `state: consolidated` (or `archived` if the surface no longer exists) and add a final line:

- **2026-MM-DD** — fully consolidated into AUDIT-YYYYMMDD-&lt;newer&gt;. No open rows remain.

## Status log

- YYYY-MM-DD `created` by &lt;agent&gt;. Findings raw — no tickets filed yet.
- YYYY-MM-DD `tickets filed` by pm. Row 1 → T-NNNN, row 3 → T-NNNN, row 5 folded into T-MMMM.
- YYYY-MM-DD `state: open → consolidated` by &lt;agent&gt;. See Consolidation block.
- YYYY-MM-DD `state: consolidated → archived` by pm. All rolled-up tickets done; surface re-audited clean in AUDIT-YYYYMMDD-&lt;newer&gt;.

---

## How to use this template

1. Copy `docs/audits/_template.md` to `docs/audits/AUDIT-<YYYYMMDD>-<subsystem>-<dimension>.md`. Use UTC date.
2. Fill the frontmatter. `ran_by` is the agent name — `optimizer` for the consistency sweeps, `secops` for security passes, `reviewer` for Gate 5 deep-dives, etc.
3. Run `scripts/audit/consistency.mjs` (Node 20+, no external deps) if the subsystem has script coverage; paste the relevant rows into Findings. Manual rows are equally valid — annotate evidence honestly.
4. Open one ticket per distinct `T-NNNN` cited. The Proposed tickets block is the PM's hand-off — keep it short and ticket-shaped.
5. Leave `state: open` until every cited ticket is `done` or every row is consolidated. Then flip per the Consolidation block.
6. Never edit a finding after the audit is filed — append to Status log + Consolidation instead. Audits are point-in-time records.
