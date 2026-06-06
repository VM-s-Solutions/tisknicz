# /audit — Parallel codebase audit across subsystems × dimensions

Fan out 16 specialist passes (4 subsystems × 4 dimensions) over the Makables codebase, rank every finding, and consolidate everything ≥ medium severity into draft tickets. This command lands the capability; the first real run is deferred until Phase 4 + 5 ship per user decision in the operating contract.

## When to use

- After a phase boundary (Phase 4 + 5 close out is the planned first run) to catch drift before it compounds.
- Before a release candidate, to harden the four production subsystems (identity, catalog, orders, platform).
- When the team suspects accumulated debt, missing conventions, or silent regressions that per-ticket review missed.
- When a new persona or country is on the horizon and you need a clean baseline of gaps, conventions, security posture, and performance hotspots.

Do **not** use `/audit` for a single-ticket review (use `reviewer` directly) or for a single subsystem spot-check (invoke the relevant agent inline).

## Subsystems × dimensions matrix

The audit runs as a 4 × 4 grid. Every cell is one parallel agent invocation.

| Subsystem \ Dimension | gaps (`ba`) | conventions (`reviewer`) | security (`secops`) | perf (`optimizer`) |
|---|---|---|---|---|
| **identity** — auth + users + makers | gaps-identity | conv-identity | sec-identity | perf-identity |
| **catalog** — categories + products + maker listings | gaps-catalog | conv-catalog | sec-catalog | perf-catalog |
| **orders** — `Order` + `OrderPricing` + `OrderNumberGenerator` + `OrderAttachment` + payment + invoice | gaps-orders | conv-orders | sec-orders | perf-orders |
| **platform** — `CountryConfiguration` + `Outbox` + Blob + Email + Comgate / Packeta / ARES / SendGrid | gaps-platform | conv-platform | sec-platform | perf-platform |

### Dimension definitions

- **gaps (`ba`)** — Acceptance criteria not implemented, user stories without code paths, personas blocked, copy/i18n holes against `lib/i18n/cs-CZ.ts`, missing happy / sad paths against `docs/user-stories/**`.
- **conventions (`reviewer`)** — Drift from `docs/architecture/patterns.md` (A.1–A.21 backend, B.1–B.19 frontend), CLAUDE.md self-check items, ADR conformance, file shape (one-file CQRS feature, named exports, primary-constructor DI), dead code, manual edits to `frontend/src/lib/api-client/`.
- **security (`secops`)** — CLAUDE.md §Security S1–S10: `[Authorize]` coverage, JWT audience per host, webhook origin/signature verification, cron secret checks, no client-bundle secrets, server-side file validation, server-side payment verification, secrets via Configuration only, Argon2id parameters, refresh-token rotation.
- **perf (`optimizer`)** — Pagination on list endpoints (`DataRangeRequest` / `PagedData<T>`), index coverage on WHERE / ORDER BY / JOIN columns, `.AsNoTracking()` on read-only queries, Server Components by default, `next/image` with dimensions, lazy-loaded heavy client components, no N+1, money math hot paths.

## Steps

1. **Confirm Phase 4 + 5 are shipped.** Read `docs/status/sprint-N.md` for the latest sprint. If Phase 4 (orders + payment) and Phase 5 (maker listings + payouts) are not both `done`, stop and tell the user the audit is deferred per the operating contract. Otherwise continue.
2. **Read the inputs every cell needs.** Open `CLAUDE.md`, `docs/architecture/patterns.md`, `docs/architecture/overview.md`, `docs/architecture/extension-points.md`, `docs/architecture/money.md`, `docs/architecture/multi-country.md`, the ADR set under `docs/adr/`, the role cards under `docs/architecture/roles/`, the persona file `docs/personas.md`, and the user-story trees under `docs/user-stories/customer/`, `docs/user-stories/maker/`, `docs/user-stories/admin/`. Each agent will re-read the slice it needs; this step makes sure nothing is missing on disk.
3. **Verify the audit scaffolding exists.** Ensure `docs/audits/_template.md` is present (Severity / Subsystem / Dimension / Finding / Evidence / Impact / Recommended fix / Suggested ticket). Ensure `docs/audits/INDEX.md` exists as the consolidation target. If either is missing, create it before fan-out — the parallel agents will write into these paths and must not race to create the template.
4. **Launch 16 agents in parallel.** One Task per cell of the matrix. Each agent gets:
   - Its subsystem scope (file globs under `backend/src/**` and `frontend/src/**`).
   - Its dimension brief (the bullet list above).
   - The output path `docs/audits/<subsystem>-<dimension>.md` (e.g. `docs/audits/orders-security.md`).
   - The instruction: produce a ranked list of findings using `docs/audits/_template.md`, severities **critical / high / medium / low**, each finding with evidence (file + line range), impact, and a recommended fix sized to one ticket.
5. **Wait for all 16 outputs.** Do not consolidate partial results — the grid is the unit of work. If any cell errors, re-run that cell with the same brief before moving on.
6. **PM consolidates.** Invoke `pm` to read every `docs/audits/<subsystem>-<dimension>.md`, merge into `docs/audits/INDEX.md` sorted by severity (critical first), then by subsystem (identity, catalog, orders, platform), then by dimension (security, gaps, conventions, perf). Each row in the index links back to the source audit file and to any draft ticket created in the next step.
7. **Draft tickets for every finding ≥ medium.** For each such finding, `pm` creates a draft ticket file under `docs/tickets/T-NNNN-<slug>.md` using `docs/tickets/template.md`, with frontmatter `state: draft`, `priority` mirroring the finding severity, `source: audit-<date>`, and a link back to the audit row. The ticket goes through normal Definition of Ready before `pm` promotes it to `ready`.
8. **Surface to the user.** `pm` appends an entry to the current sprint status doc summarising: counts per severity, the top five critical findings, the new draft-ticket IDs, and any blockers. Do not push tickets into the active sprint without user sign-off — the audit produces backlog candidates, not in-flight work.

## Output contract

- Every cell writes exactly one file: `docs/audits/<subsystem>-<dimension>.md` conforming to `docs/audits/_template.md`.
- `docs/audits/INDEX.md` is the single severity-sorted view across all 16 cells.
- Tickets created from the audit carry `source: audit-YYYY-MM-DD` in frontmatter so we can trace backlog → audit run.
- Findings below medium stay in the per-cell file only; they are not promoted to tickets but remain searchable.

## Guardrails

- **No fixes during audit.** Agents document; they do not patch. Fixes land through normal ticket flow so reviewer / secops / qa gates apply.
- **No new ADRs from `/audit`.** If an audit finding implies an architectural change, the draft ticket records the open question and routes through `architect`.
- **Respect the contract.** If a cell wants to flag a NSwag-generated file under `frontend/src/lib/api-client/`, that is a backend-contract issue — file the finding under the backend cell, not frontend.
- **Czech-only scope.** Multi-country findings (missing `CountryConfiguration` lookups, hard-coded `"CZ"`) are conventions findings under `platform`, not gaps — we are not adding countries here, only protecting the extension point.

## See also

- `CLAUDE.md` — non-negotiable rules and the §Self-Check the conventions cell anchors on.
- `docs/architecture/patterns.md` — pattern catalog (A.1–A.21 backend, B.1–B.19 frontend) the conventions cell scores against.
- `docs/architecture/overview.md`, `docs/architecture/extension-points.md`, `docs/architecture/money.md`, `docs/architecture/multi-country.md` — system shape inputs.
- `docs/architecture/roles/` — the 29 RDD CRC cards each subsystem cell consults.
- `docs/process/quality-gates.md` — Gate 1–5 the reviewer cell mirrors per file.
- `docs/process/ticket-lifecycle.md` — how draft tickets from this command enter the backlog.
- `docs/review/checklist.md` — the per-PR checklist the conventions cell extends to the whole codebase.
- `docs/tickets/template.md`, `docs/tickets/INDEX.md` — ticket shape and backlog index used in step 7.
- `docs/status/sprint-N.md` — where `pm` reports audit results in step 8.
- `.claude/agents/ba.md`, `.claude/agents/reviewer.md`, `.claude/agents/secops.md`, `.claude/agents/optimizer.md`, `.claude/agents/pm.md` — the agent charters this command orchestrates.
