# Quality Gates

A change does not reach `done` until every **applicable** gate passes. The Reviewer enforces gates
1–2 and 6–7 on every ticket; SecOps (gate 3), Architect (gate 4), and Optimizer (gate 5) are
conditional. The PM will not merge until the gates that apply are green.

These gates exist because this platform is going to production and is already large. A bug or a
leak that ships now is expensive to undo later. The bar is "would I let this run unattended in
production handling real customers and real money" — not "does it compile."

---

## The gates

### Gate 0 — Evidence discipline (every reviewing/finding agent: reviewer, secops, optimizer, qa, and any ad-hoc audit/exploration)

This is a **meta-gate**: it governs *how* every finding from every other gate is reported. It exists
because automated finders **systematically over-report** — they pattern-match to a scary scenario and
assert a defect without tracing the guard that already prevents it. This was observed on this very
codebase: agents reported "the tree won't build" on a transient mid-flight state, and security/review
passes flagged "bugs" that an existing `[Authorize]` gate, idempotency key, or query filter already
prevented. **A finder that emits confident false findings is worse than no finder, because its output
gets trusted — and you may "fix" working code and introduce a real bug.**

Therefore, every reported finding MUST satisfy ALL of:

1. **REFUTED by default.** Treat your own hypothesis as false until you have *traced it through the
   actual code*. If you cannot complete the trace, report it as a **question** ("is X guarded?"), not
   a finding.
2. **File:line evidence.** Cite the exact location of the defect AND the location of the guard you
   confirmed is missing/insufficient. "Could happen if…" with no traced path is not a finding.
3. **Concrete trigger.** State the exact input/sequence/request that reaches the bug. If you can't
   describe the repro, you haven't confirmed it.
4. **Guard check (most "bugs" die here).** Before reporting, look for the guard that already prevents
   it. In this codebase the guard menu is: an `[Authorize]` / policy authz attribute with the correct
   per-host JWT audience; a deterministic **idempotency key** / `ProcessedMessage` / outbox claim on a
   webhook or side-effecting command; a **FluentValidation** rule (every `*Command` has a `Validator`,
   `Cascade.Stop`); an EF **query filter** (soft-delete / country scoping) or DB **constraint** (unique
   index, FK Restrict); a **rate-limit window**; the **UnitOfWorkPipelineBehavior** commit-only-on-success
   pipeline; a domain **state-transition guard** (`CanTransitionTo`); or a `CountryConfiguration` /
   options default. If a guard exists, the finding is **REFUTED** — say so and move on.
5. **Severity honesty.** A blocker = exploitable / money-losing / illegal-state in production *as
   written, reachable today* — not "in a hypothetical future topology." Downgrade or refute everything
   else. (A genuine *latent* multi-country / go-live blocker is real, but label it as such — dormant on
   the current CZ-only path, blocking before that capability ships — not as a live crash.)

When the orchestrator consumes finder output, the posture is **verify before acting** — never "fix" on
an unverified finding. A clean area reported honestly ("traced X/Y/Z, no defect, guard at file:line")
is a valid, valuable result; manufacturing findings to look thorough is the failure mode this gate
prevents. This is the report-side complement to the build-side **verify-not-trust** rule in Gate 8.

### Gate 1 — Conventions self-check (always)
The change conforms to [`CLAUDE.md`](../../CLAUDE.md) §Self-Check and the canonical pattern catalog
[`docs/architecture/patterns.md`](../../docs/architecture/patterns.md) for the touched side (backend C#
or frontend TypeScript). Concretely:
- **No hardcoded user-facing strings.** Backend errors use `BusinessErrorMessage` codes; the frontend
  reads keys from `frontend/src/lib/i18n/cs-CZ.ts`. Every new `BusinessErrorMessage` code has a parallel
  `cs-CZ` key (Czech-only at launch; multi-country-ready).
- **No `any` in TypeScript. No `dynamic` in C#.** Proper types, records for DTOs, enums.
- **Architecture obeyed**: CQRS one-file-per-use-case structure intact, controllers are one-liners over
  `Mediator.Send`, no business logic in components/Server Components, no raw HTML form controls (UI
  primitives from `components/ui/`).
- **No magic numbers/strings** — constants in the right home (a policy/authz class, an enum, the theme,
  or a `CountryConfiguration` row).
- **Naming + file layout** match the canonical tables in `patterns.md`.

### Gate 2 — Acceptance criteria (always)
Every AC item in the ticket has **verifiable evidence**: an automated test, a screenshot from the
running app, a log line, or an explicit reviewer confirmation tied to a file:line. "Looks done" is
not evidence. An AC with no evidence fails the gate.

### Gate 3 — Security (mandatory iff `security_touching: true`)
The SecOps reviewer walks the checklists under
[`docs/security/`](../../docs/security/) against the diff. A ticket is **security-touching** if it
adds/changes any of: an endpoint, auth/authorization, a resource-by-id operation, a response DTO,
country/soft-delete scoping, a side-effecting command (payment, payout, invoice, email, referral),
file upload, logging of user data, cron endpoints, or rate-limited routes. The verdict names the
**specific risk**, not a category — e.g. "customer A can read customer B's order because the handler
doesn't check ownership at `GetOrderById.cs:31`", not "missing authorization". A JWT minted for one
host's audience must not be replayable against another host's API — call that out explicitly when the
change touches auth.

### Gate 4 — Architecture (mandatory iff a new pattern or extension point is touched)
The Architect confirms the change preserves the seams listed in
[`docs/architecture/extension-points.md`](../../docs/architecture/extension-points.md): no country
branching in handlers (read `CountryConfiguration`), no provider-specific code outside its adapter
(Comgate / Packeta / ARES / SendGrid / Mapbox / Blob each live only in `Infra.Clients/<Provider>/`),
no infra leaking into `Core.Domain` / `Core.AppServices`, `Core.Domain` free of third-party packages.
If the change needed a new pattern, an ADR exists in [`docs/adr/`](../../docs/adr/) and is cited.

### Gate 5 — Performance, cost & runtime readiness (for hot paths, external calls, jobs, heavy UI)
The Optimizer checks: no N+1 queries, `AsNoTracking()` on read paths, indexes for new
WHERE/ORDER/JOIN columns, no over-fetching DTOs, every list endpoint paginated
(`DataRangeRequest` / `PagedData<T>`); on the frontend, Server Components by default, `next/image` with
explicit dimensions, no bundle bloat from a heavy client import, heavy client components lazy-loaded via
`next/dynamic`, no needless re-renders. **Plus runtime readiness** when the change touches an external
service (Comgate, Packeta, ARES, SendGrid, Mapbox, Blob), an Azure Function / queue, or a hot path:
structured logging + correlation id, error classification, **graceful degradation** (the core action
is not blocked by a non-core dependency outage), durable side effects, idempotency, and a visible
dead-end for failures. Applied when the ticket touches a list view, a paged query, a hot endpoint, an
external integration, a background job, or adds a dependency.

### Gate 6 — Tests, written test-first (always, proportional to risk) — per [`docs/process/must-cover-tests.md`](../../docs/process/must-cover-tests.md)
- Development is **TDD by default**: the test is written **before** the implementation (red → green →
  refactor). For **pure logic** (pricing, fee/commission math + override precedence, validators, state
  machines, fiscal/rounding rules, numbering, refunds/payouts) this is **strict and mandatory** — the
  Reviewer expects the test to predate the code (commit order / status-log "red→green"), and **rejects
  after-the-fact tests on pure logic** (they miss the branches the author didn't think of). Money math
  (`long` minor units, basis-point VAT, half-up rounding) and state transitions are non-negotiable.
- Handlers: the unit test (mocked repos, asserting `IsSuccess` + each `Error.Code`) and the route
  integration test (incl. the auth/ownership rejection and the per-host audience check) are written
  against the intended contract first. UI: the pure-logic test (money formatting, validation mirror)
  is written first; the component follows.
- Changing existing **untested** code: write a **characterization test** pinning current behavior
  first, then TDD the change.
- New **endpoints** have an integration test covering the happy path and the key failure
  (auth/ownership rejection — a real test, not just review).
- The change covers its slice of the **must-cover list** in `must-cover-tests.md` (fee/commission calc,
  order lifecycle, money/refunds/payouts, invoice numbering, authz boundaries, webhook idempotency,
  every `BusinessErrorMessage` path).
- Cross-layer behavior has an integration test where the project's harness supports it.
- The QA test plan exists and was executed; results recorded.

### Gate 6.5 — Behavioral non-stub (when the AC assert behavior)
A green suite proves nothing if it would stay green with the feature deleted. This gate exists because
it happened: a "spine" ticket shipped with a green suite whose tests never exercised the real path — the
implementation could have been a no-op and nothing would have gone red. For any ticket whose AC assert
**behavior** — auth decisions, money math, state transitions, webhook idempotency, and anything named
*spine / foundation / middleware / skeleton* (tickets whose whole point is that the real path works) — at
least one test must **FAIL if the implementation body is replaced with the empty/default value** (return
default, no-op, empty collection). The reviewer **names that test** in the verdict (e.g. "Gate 6.5:
`RefundKey_DoubleSubmit_SingleComgateCall` goes red against a no-op seam"). If no such test can be named,
the suite is asserting the scaffolding, not the behavior — the gate fails, however green the run. The
cheap mental check: *delete the method body — does anything go red?* Routing flags these tickets up
front ([`routing.md`](./routing.md) §"Spine tickets gate harder") so the dev writes to this gate, not
just past it.

### Gate 7 — Contract & docs parity (when the surface changed)
- If a backend DTO/endpoint changed, the ticket carries a `MANUAL_STEP: nswag-regen` flag for the
  owner. The agents do **not** regenerate clients.
- If a schema changed, the ticket carries a `MANUAL_STEP: ef-migration` flag. The agents do **not**
  run migrations.
- If shipped behavior changed, the docs are updated in the same ticket (or a linked docs ticket): the
  relevant page under [`docs/`](../../docs/) — `docs/architecture/*` for architecture, `docs/adr/` for
  a new decision, `docs/architecture/extension-points.md` for a new extension point, the deployment env
  var list for a new configuration value.

### Gate 8 — Mechanical checks pass (always; this is what makes the rules real)
Deterministic beats diligent. Before a ticket reaches `done`, the **mechanical** checks for the
touched stacks pass, and the Reviewer/PM record the result on the ticket as evidence. These are the
**same checks CI enforces** (so a green local check is a green PR), and CI is the structural safety
net — the gate must not depend on a human remembering to run a suite:
- **Backend touched:** `dotnet build backend/src/Makables.Api.slnx` + **both** test projects succeed —
  `Makables.Tests` (unit) and **`Makables.IntegrationTests` (real Postgres via Testcontainers)**. The
  integration suite is the one that catches country-scoping, FK, migration, and webhook bugs that
  mocked unit tests cannot — run it, do not skip it. CI [`ci.yml`](../../.github/workflows/ci.yml)
  runs both.
- **Frontend touched:** `next build` (production) **and the frontend test run** succeed. Lint runs
  in CI. CI [`ci.yml`](../../.github/workflows/ci.yml) runs build + tests + lint. Because the frontend
  is a pure presentation layer, a green build is the primary signal that the generated client and the
  pages that consume it still line up.
- **Contract parity:** if the API surface changed, `npm run check:api` (parity against the backend's
  `openapi/v1.json`) and `npm run check:api-client` (no manual edits to `lib/api-client/`) report clean.
- **Any stack:** `node scripts/check-consistency.mjs --paths=<changed dirs>` reports **no new**
  violation. A pre-existing baseline violation the change merely sits near is noted, not blocking —
  unless the ticket *is* its canonicalization ticket. See [`enforcement.md`](./enforcement.md) for the
  tool, the baseline, and the rollout to fully-automatic CI gating.

> **Verify-not-trust (the lesson that caught every shipped bug in the rework):** when work fans out
> across agents, the orchestrator re-runs the **combined-tree** suites itself before accepting — it does
> not trust per-agent "PASS" reports or per-lane isolation runs. Agents repeatedly reported "the tree
> won't build" on a transient mid-flight state, and reported PASS where a real-DB run failed. The
> authoritative gate is a clean rebuild of the merged tree, not the agent's word.
>
> This posture is a **required artifact, not a habit**: the ticket's `## Review` must contain the
> orchestrator's OWN combined-tree run — the command, its exit code, and the counts (tests passed /
> failed / skipped, build errors). A Gate 8 entry showing only a dev-reported PASS with no independent
> run recorded is **itself a FAIL** — the gate did not run, it was narrated.

**Absent toolchain ⇒ DEFERRED-TO-CI, never PASS.** If a touched stack's toolchain is absent from the
execution environment (Docker down so the Testcontainers integration suite cannot start; `az`/Bicep
where not installed for an `infra/bicep/` change), the mechanical check for that stack is
**DEFERRED-TO-CI** and is recorded on the ticket verbatim as **UNVERIFIED-LOCALLY** — naming the check
that did not run and the CI job that will run it. An agent may **NEVER** report PASS for a check it
could not execute; "it should pass" is a prediction, not evidence. The ticket may advance on the checks
that DID run, but the deferred check stays visibly open until CI goes green.

A ticket whose mechanical checks fail cannot be `done`, regardless of how good the review reads. If a
check is failing for a reason genuinely unrelated to the change (a flaky test, a pre-existing
baseline item), the Reviewer says so explicitly with evidence — it is never waved through silently.

---

## Owner-only steps (the agents never do these)

Per [`CLAUDE.md`](../../CLAUDE.md), two steps are **owner-only**. Agents detect the need, flag it as a
`manual_steps` entry on the ticket, and **block the dependent work** until the owner confirms it's done:

- **EF Core migrations** — `dotnet ef migrations add` / `database update`. Agents describe the
  schema delta; the owner creates & applies the migration.
- **NSwag client regeneration** — `npm run generate:api`. Agents flag it; the owner regenerates
  the TypeScript client(s) before dependent frontend work begins.

A ticket that needs either and hasn't had it confirmed cannot reach `done`.

### Batch the owner-only handoffs (don't interleave them mid-wave)
The owner-only rule is sound, but interleaving each `dotnet ef` / regen mid-wave is lossy: it leaves
the tree half-broken (a missing migration trips EF `PendingModelChangesWarning` on **every**
integration test; a stale client breaks the build) and forces a slow per-step round-trip. Instead, a
batch should produce **one MANUAL_STEPS bundle at the end** — "run these N migrations + these M
regens, then tell me" — so there is a single fat handoff, not many thin ones. The PM collects the
bundle from the batch's tickets; the orchestrator re-verifies once after the owner confirms the whole
bundle.

### After an NSwag regen, build every affected host client before pushing
Regenerating one host's client (e.g. an added required DTO field) commonly breaks an **untouched**
consumer that shares that DTO. Makables has four API hosts — Web.Customer (5001), Web.Maker (5002),
Web.Admin (5003), Web.Public (5104) — and a bundle that touches controllers on more than one of them
must regenerate **every** affected host's client, not just the primary one. The owner-only regen
guardrail stays, but the follow-through is: **after any regen, run `npm run check:api` and the frontend
production build, and fix the consumers before pushing.** The blocking frontend prod-build CI catches
this too, but catching it locally avoids a red PR. (No dedicated client-drift CI job: the build +
`check:api` gates already fail on the drift symptom.)

### Match agent count to task risk (don't fan out mechanical work)
Multi-agent fan-out earns its overhead on **wide, parallel, or risky** work (a many-file migration, a
consistency sweep, anything needing independent verification). For **narrow, deterministic** work
(delete N lines, rename a symbol, a one-line consumer fix), a single direct edit + the mechanical
checks is faster and cheaper than dispatching a dev+reviewer — and avoids the rate-limit / collision
cost of pushing parallelism past what the task needs. Heuristic: **fan out for breadth or risk; act
directly for mechanical certainty.**

### Serialize shared-file lanes — and NEVER `git restore` a shared file in a parallel batch
When a batch fans out in parallel, tickets that touch the **same shared file** must be **serialized**
(one writer at a time), not run concurrently. The shared-file clusters that bite are:
`docs/tickets/INDEX.md`, the `frontend/src/lib/i18n/cs-CZ.ts` dictionary, the `BusinessErrorMessage`
catalog, and the authz policy cluster (policy definitions and their registration must move together or
startup validation fails at boot). The PM sequences these into a single lane — never two instances
editing one of them at once. The cluster list is maintained as **data** (verified paths, per-cluster
rationale) in [`shared-file-lanes.md`](./shared-file-lanes.md) — the PM validates every parallel batch's
lane assignments against that file before dispatch.

When true parallelism on adjacent (not identical) regions is unavoidable, each agent is told to **edit
only its own hunks** and is **forbidden from running `git restore` (or `git checkout --`, or a
wholesale revert) on a shared file** — even to "clean scope contamination." A blanket restore of a
shared file silently wipes a *sibling ticket's* committed deliverable. If an agent believes a shared
file is contaminated, it **reports it to the PM** (leaves a note), it does **not** revert the file
itself. The orchestrator's combined-tree re-verify is the backstop that catches a wiped deliverable —
but the structural fix is to serialize the lane and ban the shared-file `git restore` up front.

### A final-report (StructuredOutput) failure ≠ a work failure — gate the working tree by hand
A dev agent's **final report call can error** (retry cap exceeded) while its actual work **completed on
disk** — the new feature file / handler / validator + specs, the `cs-CZ` keys, a clean prod-build and a
green suite all landed, but the report call failed (often an oversized, escaping-heavy evidence string
tripping schema serialization). The work was fine; only the *report* failed. Rules: **(1)** a
final-report failure does **not** mean the work failed — the orchestrator **inspects the working tree
and gates the on-disk result by hand** (build clean? tests green? secret-scanned? country filter and
per-host audiences untouched?). **(2)** keep the evidence field **concise** to avoid the
schema-serialization failure — a terse summary (counts + a one-line verdict + the key file:line), never
the raw build log or diff. The authoritative evidence lives in the ticket status log and the working
tree anyway; the report field is a pointer, not the artifact.

---

## How a reviewer writes a verdict

In the ticket's `## Review` section, for each gate that applies:

```markdown
## Review — reviewer (2026-07-09)

- Gate 1 Conventions: PASS
- Gate 2 AC: FAIL — AC#3 ("admin sees override badge") has no evidence; the badge component
  isn't wired in the maker fee-config page. Add it + screenshot.
- Gate 6 Tests: FAIL — the fee-calculation override precedence has no unit test. Add one
  covering maker-override-wins and fallback-to-CountryConfiguration.
- Gate 8 Mechanical: PASS — `dotnet build Makables.Api.slnx` 0 errors; Makables.Tests 214/214,
  Makables.IntegrationTests 61/61 (combined-tree run, exit 0). `next build` clean.

Verdict: CHANGES REQUESTED. Re-invoke me after fixes.
```

Be specific (file:line + the fix expected), be kind (reject the code, not the author), and never
approve under time pressure. "It's a small change" is not a reason to skip a gate.

---

## Cross-references

- States and transitions: [`ticket-lifecycle.md`](./ticket-lifecycle.md)
- Who picks up a ticket at each state, and orchestration shape: [`routing.md`](./routing.md)
- How agents hand off (artifacts not chat) and escalation: [`communication.md`](./communication.md)
- The consistency tool + baseline: [`enforcement.md`](./enforcement.md)
- Shared-file lane clusters: [`shared-file-lanes.md`](./shared-file-lanes.md)
- What each agent reviews at PR-open: [`../../docs/review/checklist.md`](../../docs/review/checklist.md)
- Canonical pattern catalog: [`../../docs/architecture/patterns.md`](../../docs/architecture/patterns.md)
- Extension points that trigger Architect routing: [`../../docs/architecture/extension-points.md`](../../docs/architecture/extension-points.md)
- Agent charters: [`../../.claude/agents/`](../../.claude/agents/) — `architect`, `ba`, `dotnet-backend`, `dotnet-db`, `frontend`, `l10n`, `optimizer`, `pm`, `qa`, `reviewer`, `secops`.
