# /feature — turn intent into a ticket-to-PR cycle

## When to use

Run `/feature` at the **start of any new piece of work** that does not yet have a ticket — a customer request, a maker pain-point, an architect's chunk of a roadmap milestone, a bug that needs more than a one-line fix, or any change that crosses a layer boundary (DB ↔ backend ↔ NSwag contract ↔ frontend).

Do **not** use `/feature` for:
- Pure hygiene PRs (typos, lint, formatting) — open a PR directly.
- Hot-fix incidents — use `/hotfix` so the post-mortem lane is wired in.
- Reviewer-only or QA-only follow-ups on an already-merged ticket — use `/followup`.

`/feature` codifies the workflow we ran implicitly across Sprint 1–7 so every contributor (human or agent) follows the same path: PM → BA → Architect → Backend (DB + .NET) → NSwag → Frontend → QA → Reviewer → SecOps → Optimizer → merge.

## Steps

1. **Anchor the context.** Read, in order:
   1. [CLAUDE.md](../../CLAUDE.md) — non-negotiable rules.
   2. [docs/architecture/patterns.md](../../docs/architecture/patterns.md) — the A.1–A.21 backend and B.1–B.19 frontend pattern catalogue. Pick the patterns you will reuse; flag any you intend to extend.
   3. [docs/process/quality-gates.md](../../docs/process/quality-gates.md) — the five gates the PR must pass.
   4. [docs/tickets/INDEX.md](../../docs/tickets/INDEX.md) — to claim the next ticket id, see related work, and avoid duplicates.

   If you cannot cite at least one A.x or B.x pattern that frames the work, stop and ask the architect for a new extension point before writing a ticket.

2. **PM expands the intent into a ticket.** Invoke the `pm` agent. PM copies [docs/tickets/template.md](../../docs/tickets/template.md), assigns the next id from `INDEX.md` (T-0068+ in the enforced range; T-0001–T-0067 are grandfathered for DoR), and fills the ticket through **Definition of Ready** per [docs/process/ticket-lifecycle.md](../../docs/process/ticket-lifecycle.md):
   - Linked user story (`docs/user-stories/<persona>/US-*.md`).
   - Acceptance criteria with verifiable proofs (UI screenshot, API sample, log line, or DB state).
   - Out-of-scope explicitly listed.
   - Security-touching flag set per Gate 3 criteria in `quality-gates.md`.
   - Touched layers declared: `db`, `backend`, `contract`, `frontend`, `infra`, `docs`.
   - Test plan stub under `docs/test-plans/T-XXXX.md` if any pure logic is in play.

   PM commits the ticket on a fresh branch `feat/T-XXXX-<slug>` and updates `INDEX.md` to `Status: in_design`.

3. **Run cheap deliberation on every design dimension.** For each open question (data model, error code, endpoint shape, i18n key, route layout, payment/shipping provider seam), use **AskUserQuestion** with the user as challenger. Capture every option, the chosen path, and the rejected alternatives directly in the ticket under:
   - `## Alternatives Considered` — one bullet per rejected option with the kill reason.
   - `## Defense` — one paragraph rebutting the strongest counter-argument.

   Follow [docs/process/deliberation.md](../../docs/process/deliberation.md). If a dimension touches money, state machines, or country variation, the defense must reference the relevant ADR in `docs/adr/`. If no ADR exists and the decision is load-bearing, draft one from [docs/adr/template.md](../../docs/adr/template.md) in the same branch.

4. **Route the work** per [docs/process/routing.md](../../docs/process/routing.md):
   - **Extension point touched?** (new provider seam, new audience host, new pipeline behaviour, new pattern not in A.x/B.x) → invoke `architect` first. Architect either updates `docs/architecture/extension-points.md` and `patterns.md`, or rejects the framing and sends PM back to step 2.
   - **Schema change?** (new table, column, index, FK, enum, seed) → invoke `dotnet-db`. `dotnet-db` lands the EF Core migration, updates `Infra.Database` configurations, and writes the `Auditable` columns + `*_minor` / `currency` pair for any monetary field.
   - **Backend feature?** → invoke `dotnet-backend` to **lock the signature first**: the `Core.AppServices/Features/<Entity>/<UseCase>.cs` file with nested `Command`/`Query`, `Response`, `Validator`, and `Handler` shells, plus the controller one-liner in the correct `Web.*` host. Signature lock means the public types compile before the handler body is written — this is the contract the frontend will consume.
   - **NSwag regen.** As soon as the backend signature compiles, run the NSwag pipeline. The generated `frontend/src/lib/api-client/` must be committed in the same PR. The pre-commit hook blocks manual edits to that folder.
   - **Frontend feature?** → invoke `frontend` **in parallel with `reviewer` (draft mode)**. Frontend wires Server Components by default, calls the generated client via `lib/runtime/api-fetch.ts`, sources strings from `lib/i18n/cs-CZ`, and never reaches for `useEffect` data fetching or a DB SDK. The draft reviewer pass runs while the work is `in_progress` so structural feedback lands before the PR opens, not after.
   - **L10n keys?** → invoke `l10n` to add the `cs-CZ` keys paired with every `BusinessErrorMessage` code introduced.

5. **Self-check before opening the PR.** Run the CLAUDE.md §Self-Check end-to-end on **both** sides of the diff. Specifically: no `dynamic` / `any`; no `Console.WriteLine` / `console.*`; no `SaveChangesAsync()` in handlers; no manual edits to `lib/api-client/`; every monetary column ends in `_minor` with a paired `currency CHAR(3)`; every protected endpoint carries `[Authorize]` or middleware; every user-facing string flows through `lib/i18n/cs-CZ`. Fix every failure yourself — do not punt hygiene to the reviewer.

6. **Open the PR.** Title: `T-XXXX <verb> <scope>`. Body links the ticket, the user story, the ADR (if any), and lists the AC items as a checklist. Set ticket status to `in_review` in `INDEX.md`. The PR description must call out: touched extension points, touched providers, security-touching yes/no, contract changed yes/no.

7. **Trigger the PR-open gate fan-out.** On PR open:
   - Invoke `qa` to execute the test plan (Gate 2 + Gate 5 from `quality-gates.md`) — TDD is a hard rule for pure logic on T-0067+; reviewer Gate 5 rejects after-the-fact tests on those tickets.
   - Invoke `reviewer` in **final mode** to run all five gates from `docs/review/checklist.md`. The earlier draft reviewer pass does not substitute for the final pass.
   - Invoke `secops` **iff** the ticket is security-touching per Gate 3 — auth, PII/financial columns, webhooks, file upload, secrets, cron, CORS/rate-limit.
   - Invoke `optimizer` **iff** the change lands on a hot path: list endpoints, search, marketplace browse, checkout, payout calculation, image pipeline, or any `Web.Public` SSR route. Optimizer verifies pagination, `.AsNoTracking()`, indexed predicates, `next/image` sizing, and Server-Component-first rendering.

8. **Resolve gate feedback in the same branch.** Every reviewer / QA / SecOps / Optimizer comment is closed by a follow-up commit, not by argument. If a comment exposes a missing ADR or pattern, update `docs/adr/` or `docs/architecture/patterns.md` in the same PR — do not defer.

9. **Merge and bookkeep.** On merge, PM:
   - Flips the ticket row in [docs/tickets/INDEX.md](../../docs/tickets/INDEX.md) to `Status: done` with the merge commit sha.
   - Appends a one-line entry to the current sprint file under [docs/status/](../../docs/status/) (e.g. `docs/status/sprint-N.md`) noting the ticket, the persona served, and any follow-up tickets spawned.
   - Files any deferred questions surfaced during the cycle into [docs/questions/open.md](../../docs/questions/open.md) with an owner.

   If the PR introduced a new pattern, primary-constructor handler shape, provider seam, or i18n convention, PM also nudges `architect` to backfill the pattern catalogue so the next `/feature` run can cite it in step 1.

## Bundling related tickets

When the work spans 3-6 tightly-coupled tickets in the same subsystem (e.g., `shipping-pipeline = T-0070 + T-0071 + T-0072 + T-0073 + T-0074 + T-0075`), use `/feature` with bundle scope.

**Workflow:**
1. PM grooms ALL bundle tickets in parallel. AskUserQuestion deliberations are batched across tickets (max 4 questions per round).
2. Each ticket's `## Locked design decisions` populated; all transition `draft → ready`.
3. Single feature branch (`feat/<bundle-name>`).
4. Implementer processes tickets sequentially in the same branch with TDD commit order.
5. Single reviewer pass + single Gate 8 + single Gate 9 at PR-open.
6. Single `chore(<bundle>): fold` commit at the end.
7. One PR for the entire bundle.

See `docs/process/routing.md §"Bundling related tickets into one PR"` for the full rule. See `docs/process/ticket-lifecycle.md §"Bundle DoR"` for the gating criteria.

## See also

- [CLAUDE.md](../../CLAUDE.md) — project-wide non-negotiables.
- [docs/architecture/patterns.md](../../docs/architecture/patterns.md) — A.1–A.21 backend, B.1–B.19 frontend patterns.
- [docs/architecture/extension-points.md](../../docs/architecture/extension-points.md) — when to bring in `architect`.
- [docs/process/ticket-lifecycle.md](../../docs/process/ticket-lifecycle.md) — Definition of Ready and Definition of Done.
- [docs/process/quality-gates.md](../../docs/process/quality-gates.md) — the five gates Reviewer enforces.
- [docs/process/deliberation.md](../../docs/process/deliberation.md) — Alternatives Considered / Defense capture rules.
- [docs/process/routing.md](../../docs/process/routing.md) — which agent owns which step.
- [docs/process/communication.md](../../docs/process/communication.md) — handoff format between agents.
- [docs/review/checklist.md](../../docs/review/checklist.md) — Reviewer's full pass.
- [docs/tickets/INDEX.md](../../docs/tickets/INDEX.md) — ticket ledger.
- [docs/tickets/template.md](../../docs/tickets/template.md) — ticket scaffold.
- [docs/adr/template.md](../../docs/adr/template.md) — ADR scaffold for load-bearing decisions.
- [.claude/agents/](../agents/) — per-role charters for `pm`, `ba`, `architect`, `dotnet-backend`, `dotnet-db`, `frontend`, `l10n`, `qa`, `reviewer`, `secops`, `optimizer`.
