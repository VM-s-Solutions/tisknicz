# Enforcement — Making Rules Mechanical, Not Advisory

A rule in a Markdown file is a strong suggestion; a build that fails is a law. This document is the
plan and the current state for turning the team's conventions into **machine-checked gates** so
consistency survives even when an agent (or human) doesn't read carefully. The principle:
**deterministic beats diligent.** Anything a tool can check, a tool should check.

The single source of truth for *what* the conventions are is
[`docs/architecture/patterns.md`](../../docs/architecture/patterns.md); this doc is about *how* they
get enforced. Where a rule here disagrees with patterns.md, patterns.md wins and this doc is stale.

## What's mechanical today

| Layer | Tool | Covers | Status |
|---|---|---|---|
| Backend build + tests | `dotnet build` + `dotnet test` on `Makables.Api.slnx` (CI: `.github/workflows/ci.yml` → `backend` job) | compile, unit + integration tests via `WebApplicationFactory<Program>` | **live in CI** |
| Backend warnings-as-errors | `backend/src/Directory.Build.props` (`TreatWarningsAsErrors=true`, `Nullable=enable`, `EnforceCodeStyleInBuild=true`) | nullability, unused usings, code-style analyzers — a warning fails the build | **live** |
| Frontend build + typecheck + lint | `next build` + `tsc --noEmit` + `eslint src` (CI: `ci.yml` → `frontend` job) | the Next.js app compiles, typechecks, and lints clean | **live in CI** |
| Frontend a11y + SEO | `vitest` (`npm run test:run`, jest-axe + SEO predicate tests, ADR 0023 §5/§6) | zero WCAG 2.1 AA violations on critical customer paths; SEO regressions | **live in CI** |
| Contract parity (NSwag) | `check:api` parity + `check:api-client` manual-edit guard (ADR 0022; CI `api-parity` job + husky pre-commit) | generated client matches every host's `/openapi/v1.json`; no hand edits to `lib/api-client/` | **live in CI + pre-commit** |
| IaC | `az bicep build` + `build-params` on `infra/bicep/` (CI: `bicep` job) | Bicep type/reference/param errors before a deploy runs them | **live in CI** |
| Project-specific rules | `scripts/check-consistency.mjs` | the T1–T7 patterns no linter/analyzer expresses (see below) | **live in CI + run by Reviewer** |

## The consistency checker — `scripts/check-consistency.mjs`

Dependency-free Node ESM (Node 20+; runs on the Windows dev box **and** the ubuntu CI runner). It
line-scans source for the project-specific rules in
[`docs/architecture/patterns.md`](../../docs/architecture/patterns.md) that neither ESLint nor the C#
analyzers can express:

- **T1 — one-file feature shape** (patterns.md §A.2/§A.7). Every
  `backend/src/Makables.Core.AppServices/Features/**/*.cs` file must declare exactly one top-level
  `public static class` wrapper containing the nested `record Command` **or** `record Query`, a
  `record Response`, a `class Validator`, and a `class Handler`. Multiple top-level types in one
  feature file is a violation — one use case per file.
- **T2 — no `console.*` in the frontend** (patterns.md §B.7). `console.{log,info,warn,error,debug,trace}`
  is banned in `frontend/src/**/*.{ts,tsx}`; inject the structured logger instead. Allow-list:
  `lib/runtime/api-fetch.ts` and `lib/logger.ts`.
- **T3 — no `SaveChangesAsync()` in AppServices** (patterns.md §A.5). Handlers never commit; the
  `UnitOfWorkPipelineBehavior` does. Any `SaveChangesAsync(` under `Makables.Core.AppServices/` is a
  violation.
- **T4 — type safety.** `dynamic` is banned in `backend/src/**/*.cs`; `: any` and `as any` are banned
  in `frontend/src/**/*.{ts,tsx}`. Model the contract; use `unknown` if genuinely opaque.
- **T5 — no inline error strings** (patterns.md §A.4). Any `Error.{Conflict,NotFound,Permanent,
  Validation,Unauthorized,Forbidden}(…)` whose args carry a raw string literal but no reference to
  `BusinessErrorMessage` is a violation. Every code comes from the centralized catalogue.
- **T6 — money column naming** (patterns.md §A.11, ADR 0003, ADR 0009). In
  `Infra.Database/Migrations/*.cs` and `Infra.Database/Configurations/*.cs`, any `bigint`/`long`
  column whose name matches `amount|price|total|fee|payout` must end in `_minor`.
- **T7 — no `useEffect` data fetching** (patterns.md §B.4). A `useEffect` whose body calls
  `fetch(`, `apiClient.`, or `await client.` in `frontend/src/**/*.{ts,tsx}` is a violation — fetch in
  a Server Component or an event handler.

```bash
node scripts/check-consistency.mjs                      # whole repo; exit 1 on any NEW finding
node scripts/check-consistency.mjs --paths='backend/src/Makables.Core.AppServices/Features/Orders/**/*.cs'  # scope to a diff
node scripts/check-consistency.mjs --json               # machine-readable output
node scripts/check-consistency.mjs --update-baseline    # re-snapshot the grandfathered set (see below)
```

Auto-generated paths are skipped so they never produce noise:
`frontend/src/lib/api-client/**`, the EF Core migration `*.Designer.cs` files, and
`MakablesDbContextModelSnapshot.cs`.

The checks are **heuristic and line-based** — a clean run is *necessary, not sufficient*; the
Reviewer still reads the diff. They are intentionally tuned to minimize false positives (e.g. T5
only flags an `Error.X(…)` call whose args contain a string literal *and* no `BusinessErrorMessage`
reference; T6 only flags `bigint`/`long` money-named columns, not every numeric column; T1 requires
the full nested set only for files under `Features/`).

### Baseline — the grandfathered set

`docs/audits/consistency-violations.md` is the checked-in baseline: one `path:line:ruleId` row per
known, pre-existing violation. **The gate fires on new/changed code, not the whole repo** — a finding
already in the baseline is noted for awareness and does not block; a finding *not* in the baseline
breaks the build. The rule is **shrink, never grow**: canonicalization tickets drive the count down,
and `--update-baseline` re-snapshots only after those land. **Existing violations do not block
unrelated work.**

The current baseline is dominated by two patterns worth clearing: read-side `Features/Admin/*`,
`Features/Catalog/*`, and `Features/Orders/*` files that don't carry the full T1 nested set (query-only
features and `I*` service interfaces living under `Features/`), and a run of T5 `Error.NotFound(…)`
callsites in `Products`, `Maker`, and `Categories` handlers that pass an inline string instead of a
`BusinessErrorMessage.X` constant. Each is a small canonicalization ticket.

## How the gate works (Reviewer + PM)

- For any ticket touching code, the **Reviewer runs `check-consistency.mjs` scoped to the changed
  area** (`--paths=`) and treats a **new** violation (one not in the baseline) as a hard fail — it
  names the rule (T1–T7). A *pre-existing* baseline violation the change merely sits near is noted,
  not blocked, unless the ticket *is* the canonicalization ticket for it. This is
  [`docs/process/quality-gates.md`](../../docs/process/quality-gates.md) **Gate 9**.
- The **PM does not mark a ticket `done`** until, per Gate 9 and the Definition of Done: the backend
  `dotnet build` + `dotnet test` pass (backend touched); `tsc --noEmit`, `eslint`, `vitest`, and
  `next build` pass (frontend touched); the NSwag parity check (`check:api`) is green if the contract
  changed; and the consistency checker reports no new violation for the changed area. See
  [`../../docs/process/ticket-lifecycle.md`](../../docs/process/ticket-lifecycle.md) for the state
  machine those gates fire against.

## Rollout plan (graduate to fully automatic)

The checker, the baseline, `Directory.Build.props` warnings-as-errors, and the husky pre-commit hook
are already in place, and `check-consistency.mjs` already runs in CI as Gate 9. The remaining work is
about **driving each rule's baseline to zero** so the gate can tighten from "no new violations" toward
"no violations at all":

1. **Now:** checker + baseline + `Directory.Build.props` + husky pre-commit are live; Reviewer runs
   the checker per change (Gate 9); the baseline is recorded and only shrinks.
2. **As canonicalization tickets land:** the baseline count in
   `docs/audits/consistency-violations.md` drops toward zero, rule by rule. Re-snapshot with
   `--update-baseline` only after a canonicalization ticket merges — never to launder a fresh
   violation.
3. **When a rule's baseline hits zero:** drop that rule's rows from the baseline and let the gate run
   in *strict* mode for it — any occurrence, old or new, fails. Track this per-rule (T5 and T1 are the
   two largest baselines today; the smaller rules can go strict first).
4. **C# analyzers, per-rule severity ratchet:** `Directory.Build.props` already sets
   `TreatWarningsAsErrors=true` and `EnforceCodeStyleInBuild=true`. Turn on additional analyzer rule
   ids (e.g. `EnableNETAnalyzers` + a raised `AnalysisLevel`) **one id at a time**, each only after its
   occurrences are at zero, so the build never breaks on day one. `WarningsNotAsErrors` is the escape
   hatch while an id is being driven down.
5. **Frontend lint ratchet:** promote the ESLint rules that currently warn to `error` as each reaches
   zero, mirroring T2/T4/T7 in the ESLint config so a violation fails `eslint src` in CI directly
   (not only the consistency checker).

> **Rule of thumb:** a check only becomes *blocking-on-everything in CI* once its baseline is zero for
> that rule — otherwise CI is red for reasons unrelated to the current change, and people learn to
> ignore it. Add enforcement behind the cleanup, never in front of it. Until then the gate blocks
> **new** violations only, which is enough to stop the debt from growing.

## When a new rule is needed

A new mechanical check is added **only** when a convention already exists in
[`docs/architecture/patterns.md`](../../docs/architecture/patterns.md) or a new ADR in
[`docs/adr/`](../../docs/adr/) — the checker enforces decisions, it doesn't invent them. Adding a
check is itself a small ticket (`layers:` names the affected stack) and the `architect` agent signs
off the rule. Wire the new rule as a `ruleT<N>` function in `scripts/check-consistency.mjs`, run it
against the whole tree once to seed the baseline, and only then flip it on in CI.

## Cross-references

- Convention source of truth: [`docs/architecture/patterns.md`](../../docs/architecture/patterns.md)
- Where the gate fires in the PR flow: [`docs/process/quality-gates.md`](../../docs/process/quality-gates.md) (Gate 9)
- Who runs which check when: [`../process/routing.md`](./routing.md) and the agent charters in [`.claude/agents/`](../../.claude/agents/) (`reviewer`, `optimizer`, `pm`)
- Ticket states the gates run against: [`docs/process/ticket-lifecycle.md`](../../docs/process/ticket-lifecycle.md)
- Open questions / blocking manual steps: [`docs/questions/open.md`](../../docs/questions/open.md)
- The live baseline: [`docs/audits/consistency-violations.md`](../../docs/audits/consistency-violations.md)
