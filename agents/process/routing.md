# Routing — how the PM decides who works a ticket

The PM reads a ticket's `layers` field and the nature of the change, then invokes the right
specialist(s) — and a `reviewer` alongside each developer. This table is the decision logic.

## By signal

| Signal in the ticket / change | Route to |
|---|---|
| New/changed user-facing behavior with fuzzy AC | `ba` (write/sharpen the story first) |
| New pattern, new extension point, cross-cutting decision | `architect` (ADR first) |
| New entity, column, index, EF migration, query filter, seed | `dotnet-db` |
| Command/query/handler/validator, DTO, mapper, service, provider integration (Comgate/Packeta/ARES/SendGrid/Mapbox), Azure Function | `dotnet-backend` |
| Next.js App Router: page, Server/Client Component, form, route handler, consumption of the NSwag client | `frontend` |
| i18n keys / copy in `cs-CZ`; parity with backend `BusinessErrorMessage` codes | `l10n` |
| Any diff in `in_review` | `reviewer` (always) |
| `security_touching: true` | `secops` (in addition to reviewer) |
| Spine / foundation / middleware / skeleton ticket (everything else stands on it) | the assigned dev + reviewer, flagged for **behavioral non-stub gating** (see [quality-gates](../../docs/process/quality-gates.md)) + an end-to-end test driving the real path |
| Hot path, list view, paged query, new dependency, SSR catalog page, heavy client component | `optimizer` |
| PR/diff ready for behavioral verification | `qa` |

## Sequencing rules (the PM applies these)

1. **Contract before consumers.** `architect` (if needed) → `dotnet-db` (if schema) →
   `dotnet-backend` locks the API DTO shape and regenerates the NSwag client. Only then does
   `frontend` start against that contract.
2. **Reviewer in parallel, always.** For every developer instance the PM spawns, it spawns a
   `reviewer` instance reading the same ticket. The PM reconciles both before moving state.
3. **Fan out independent tickets.** Multiple instances of the same charter run concurrently on
   *different* tickets (e.g. two `dotnet-backend` instances on two unrelated features). Never two
   instances editing the same files at once — the PM serializes those. This applies especially to the
   **shared-file clusters** — the per-locale i18n bundle (`frontend/src/lib/i18n/cs-CZ.ts`),
   the backend `BusinessErrorMessage` catalog, `docs/tickets/INDEX.md`, and the NSwag-generated
   client in `frontend/src/lib/api-client/` (never hand-edited — a pre-commit hook blocks edits;
   only `dotnet-backend`'s regen touches it): each gets a **single serialized lane**, and parallel
   agents must **edit only their own hunks and never `git restore` a shared file** (a blanket revert
   wipes a sibling ticket's work). See [quality-gates](../../docs/process/quality-gates.md) for the
   serialized-lane rule.
4. **Frontend after contract.** `frontend` runs off the locked, regenerated NSwag contract — never
   against an unbuilt or hand-mocked endpoint. Per the no-mocks build rule, a missing endpoint stays
   loudly broken until `dotnet-backend` builds it.
5. **Gates last.** `secops` / `optimizer` / `qa` run after implementation + review converge, before
   merge.
6. **Manual steps block.** If a ticket needs an EF migration or an NSwag regen, the PM flags it to the
   owner (or holds for the migration/regen step) and **holds** the dependent layer until confirmed.
   Frontend does not start against a contract the backend has not yet regenerated.
7. **Spine tickets gate harder.** A ticket that builds a *spine / foundation / middleware / skeleton*
   (the change everything else will stand on) is flagged at routing time as requiring a
   **behavioral non-stub gate** — at least one test fails if the implementation is stubbed to the
   empty/default value — **plus an end-to-end test that drives the real path**, not just the units
   around it. The PM writes the flag into the ticket so the dev builds to it and the reviewer gates on
   it (see [quality-gates](../../docs/process/quality-gates.md)).

## What the PM does NOT do

- Does not write code, ADRs, stories, or tests — it delegates.
- Does not approve its own merges — the reviewer (and secops/QA where applicable) gates.
- Does not run owner-only steps (EF migrations, NSwag regen sign-off, secret provisioning) — it flags
  them.
- Does not ping the owner for routine progress — it batches into the sprint doc.

## Fan-out budget

The PM scales instance count to the work, not to a fixed headcount. Guidance:
- Audit / sweep work (the first job): fan out **wide** — one `ba`/`reviewer` instance per subsystem,
  in parallel, because the subsystems are independent.
- Feature work: usually 1 instance per layer + its reviewer; add a second instance of a layer only
  when there are independent tickets to run.
- Keep the **reviewer-per-developer** invariant regardless of scale.

## Topology note

Makables has four per-audience API hosts — `Web.Customer` (5001), `Web.Maker` (5002),
`Web.Admin` (5003), `Web.Public` (5104) — plus Azure Functions for background jobs. A change scoped
to one audience routes to `dotnet-backend` against that host; cross-audience contract changes still
lock in `Core.AppServices` first (shared), then fan out per host. The backend solution is
`backend/src/Makables.Api.slnx`; the frontend is `frontend/`; infra-as-code lives in `infra/bicep/`
and CI in `.github/workflows/{ci,deploy-staging,deploy-production}.yml` — route infra/CI changes to
`secops`.
