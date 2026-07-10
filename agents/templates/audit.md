# Audit — <subsystem / area>

- **Auditor:** <ba | architect | reviewer | secops | optimizer>
- **Date:** YYYY-MM-DD
- **Scope:** which projects/folders/features were examined
- **Method:** what was compared against what (patterns catalog, stories, ticket lifecycle in `docs/process/ticket-lifecycle.md`, ADRs, code)

## Summary
One paragraph: overall health of the area, the biggest risk, the highest-value fix.

## Findings
Ranked by impact. Each finding is directly convertible to a ticket per [`ticket.md`](./ticket.md).

### F1 — <title>   [severity: blocker | major | minor]   [type: gap | bug | spaghetti | hardcoded | perf | security | contract-drift]
- **Where:** `backend/src/...` or `frontend/src/...` file:line / area — always repo-relative, never absolute
- **What:** the concrete problem
- **Why it matters:** customer/maker/business/security/cost impact
- **Proposed fix:** the long-term-correct resolution (not a workaround). If it touches a per-country rule, fix it in `CountryConfiguration`, never with `if (countryCode == "CZ")`.
- **Proposed ticket:** `<imperative ticket title>`  size: S/M/L  layers: [...]

### F2 — ...

## Not-issues considered
Things that looked wrong but are intentional (cite the reason — the ADR in `docs/adr/`, the pattern-catalog row, or the `docs/questions/open.md` entry) — so they aren't re-flagged later.

---

## How to fill this in

This is the **lightweight, per-finding** audit template — the fast path for a single auditor writing up an on-demand sweep or a Gate-5 deep-dive. When a sweep is large enough to warrant its own audit-trail file (frontmatter, defense block, consolidation log, status log), promote it to the fuller [`docs/audits/_template.md`](../../docs/audits/_template.md) and register it in [`docs/audits/INDEX.md`](../../docs/audits/INDEX.md). Both share the same finding vocabulary; this template is the one-pager, that one is the ledger.

### Auditor role

The `Auditor` is a Makables agent name — never a stack label. Pick the one whose dimension the audit covers:

| Auditor | Dimension it owns | Charter |
|---|---|---|
| `ba` | gaps — missing stories, missing AC, requirements the code silently does not satisfy | [`.claude/agents/ba.md`](../../.claude/agents/ba.md) |
| `reviewer` | conventions — drift from `docs/architecture/patterns.md`, CLAUDE.md violations, dead code, layering | [`.claude/agents/reviewer.md`](../../.claude/agents/reviewer.md) |
| `secops` | security — missing `[Authorize]`, audience confusion, secret leakage, webhook-signature gaps, EF query-filter holes, input validation, rate limits | [`.claude/agents/secops.md`](../../.claude/agents/secops.md) |
| `optimizer` | perf — N+1 queries, missing indexes on WHERE/ORDER BY/JOIN columns, missing `.AsNoTracking()`, unbounded list endpoints, client-bundle bloat | [`.claude/agents/optimizer.md`](../../.claude/agents/optimizer.md) |
| `architect` | cross-cutting — extension-point erosion, ADR conformance, provider-seam integrity | [`.claude/agents/architect.md`](../../.claude/agents/architect.md) |

`dotnet-backend`, `dotnet-db`, `frontend`, `l10n`, and `qa` may **contribute** findings to any audit but do not own a dimension. If a contributor disagrees with the owner's call, escalate via [`docs/questions/open.md`](../../docs/questions/open.md) — never edit the finding past the owner.

### Scope discipline

State what was inspected **and what was not** — silence is not clearance. Name the hosts explicitly. Makables has four API hosts plus Azure Functions:

- `Web.Customer` (5001), `Web.Maker` (5002), `Web.Admin` (5003), `Web.Public` (5104)
- Azure Functions v4 (background jobs)

A finding that spans hosts belongs in the **most upstream** subsystem — the one whose change propagates to the others. If unclear, file it under `platform`.

### Method — make the run reproducible

List the exact comparisons so the next auditor can rerun it:

- **Diff base:** `master` at commit `<sha>` — audits are point-in-time snapshots; record the SHA.
- **Pattern-catalog cross-walk:** map each inspected file to its row(s) in `docs/architecture/patterns.md` (A.* backend, B.* frontend); record deviations as findings.
- **Manual grep passes:** e.g. `Grep("dynamic", glob="**/*.cs")`, `Grep("\\bany\\b", glob="**/*.ts")`, `Grep("console\\.", glob="**/*.{ts,tsx}")`, `Grep("if \\(countryCode ==", glob="**/*.cs")`.
- **Contract parity:** if the audit touches the API surface, note whether the NSwag-generated `frontend/src/lib/api-client/` matches the backend controllers (per [ADR 0022](../../docs/adr/0022-nswag-pipeline.md)).

Note caveats honestly — sampling boundaries ("spot-checked 12 of 47 handlers"), false-positive patterns skipped, migrations not applied.

### Severity legend

Matches [`docs/review/checklist.md`](../../docs/review/checklist.md) and the audit-index scale:

- **blocker** — ships a known-broken contract, a security hole, or a money bug. Must be fixed before merge / before the next release. Money-math, auth, and webhook-signature findings start here by default and may only be downgraded with an explicit defense.
- **major** — architectural violation, dead code in a hot path, missing i18n on a customer- or maker-facing surface. Fix this phase.
- **minor** — hygiene, naming, missing comment. Fix opportunistically; bundle into the next touching ticket.

### Finding-type tags

`gap` (missing capability / AC) · `bug` (wrong behavior) · `spaghetti` (tangled or duplicated code) · `hardcoded` (value that belongs in `CountryConfiguration`, an error code that belongs in `BusinessErrorMessage`, or a string that belongs in `frontend/src/lib/i18n/cs-CZ`) · `perf` · `security` · `contract-drift` (backend controllers and the NSwag client have diverged).

### Proposed ticket

Each finding is directly convertible to a ticket. Fill:

- **title** — imperative, one line.
- **size** — `S / M / L` per `docs/process/ticket-lifecycle.md §Sizing`. Any `L` splits before it goes `ready`.
- **layers** — comma-separated from `{ domain, appservices, infra, web, frontend, database, docs, tests, ops }`. Drives which agent picks it up per [`docs/process/routing.md`](../../docs/process/routing.md).

Do not inline a full ticket design here — write the one-liner, then let the design live in the ticket file per [`ticket.md`](./ticket.md). When several findings roll into one ticket, cite them all in that ticket's Context.

### Communication is artifact-based

This audit **is** the hand-off — there is no agent-to-agent chat. PM reads the Findings table and files tickets; the owning agent advances each finding's state (open finding → ticket draft → merged into backlog → resolved). A finding closed without a ticket records its reason inline (`closed: duplicate`, `closed: wontfix — see ADR-NNNN`, `closed: invalid — evidence misread`). Silent deletion of a finding is a process violation. See [`communication.md`](../process/communication.md).

### Defense in the artifact

Deliberation is cheap: the user is the challenger. For every blocker and most majors, pre-stage the strongest pushback and answer it — "aren't half of these false positives?" (cite your sampling rate), "we deferred i18n to a later phase" (cite the actual scope covered vs. deferred). Put the alternative fixes you weighed and rejected in a short list under each non-obvious finding, so the reasoning is visible before it is questioned rather than after.
