# /sync — detect stale NSwag client and produce regen instructions

NSwag is the contract between backend and frontend (see [ADR 0022](../../docs/adr/0022-nswag-pipeline.md) and the scaffolding ticket [T-0013](../../docs/tickets/T-0013-nswag-pipeline.md)). When a Makables `Web.*` host changes its controllers or DTOs without a matching client regen + `.spec-hashes.json` update, the frontend silently compiles against a phantom contract. `/sync` is the gate that catches that drift **before** any frontend agent (the `frontend` charter) is invoked on a ticket that touched backend contract surface.

It is a read-only inspection plus a printed remediation plan. It does **not** mutate the working tree, does **not** run `npm run generate:api` for you, and does **not** spawn other agents.

## When to use

Run `/sync` in any of these situations:

- Before invoking the `frontend` agent on a ticket whose `## Touch list` includes anything under `backend/src/Makables.Web.*/Controllers/`, `backend/src/Makables.Core.AppServices/Features/**/*.cs`, or any DTO record reachable from a controller signature.
- Before opening a PR whose diff touches both `backend/` and `frontend/` — `/sync` confirms the generated client in the same PR matches the new spec.
- When `reviewer` Gate 4 (contract parity, per [docs/review/checklist.md](../../docs/review/checklist.md)) is about to run and you want to surface drift before the formal review.
- After merging `master` into a long-lived feature branch, to confirm no upstream contract change orphaned your local client.
- When CI fails with `NSwag client stale` (see [Failure mode](#failure-mode) below) and you need the exact regen recipe.

Do **not** run `/sync` for frontend-only or backend-internal changes (handlers, EF migrations, infra glue) — there is nothing to drift.

## Steps

1. **Scan backend contract surface in the current diff.**
   Run `git diff --name-only origin/master...HEAD` (or the configured base branch from the ticket) and filter for files matching any of:
   - `backend/src/Makables.Web.Customer/Controllers/**/*.cs`
   - `backend/src/Makables.Web.Maker/Controllers/**/*.cs`
   - `backend/src/Makables.Web.Admin/Controllers/**/*.cs`
   - `backend/src/Makables.Web.Public/Controllers/**/*.cs`
   - `backend/src/Makables.Core.AppServices/Features/**/*.cs` (the `Response`, `Command`, and `Query` records nested inside one-file features per pattern A.2 in [docs/architecture/patterns.md](../../docs/architecture/patterns.md))

   Build the set of **affected hosts** by mapping each hit:
   - A `Controllers/` change pins exactly one host.
   - A `Features/<Entity>/<UseCase>.cs` change affects every host whose controller references that feature (search controllers for `Mediator.Send(new <UseCase>.Command` or `new <UseCase>.Query`). If a feature is referenced by more than one host, all of them are affected.

   If the set is empty, print `sync: no backend contract surface touched — frontend agent is safe to invoke` and stop.

2. **Cross-reference the committed client artifacts.**
   For each affected host, compare:
   - The current SHA-256 of the host's live `/openapi/v1.json` (if a backend host is running locally) against the value stored in [`frontend/src/lib/api-client/.spec-hashes.json`](../../frontend/src/lib/api-client/.spec-hashes.json) under the keys `customer-api.v1`, `maker-api.v1`, `admin-api.v1`, `public-api.v1`.
   - The mtime of the matching generated file in [`frontend/src/lib/api-client/`](../../frontend/src/lib/api-client/) — `customer-api.v1.ts`, `maker-api.v1.ts`, `admin-api.v1.ts`, `public-api.v1.ts` — against the mtime of the controller / feature files flagged in step 1. Newer backend file with the same hash is the canonical drift signature.

   When backends are not running locally, fall back to the project's parity script: `cd frontend && npm run check:api`. That script is the same one `reviewer` Gate 4 invokes; treat a non-zero exit as drift.

3. **If drift is detected, print a per-host regen plan.**
   For every affected host, emit the exact command the developer must run from the repo root. The current shipping form is bulk (`npm run generate:api` regenerates all four hosts in one shot — see [`frontend/src/lib/api-client/README.md`](../../frontend/src/lib/api-client/README.md)), but `/sync` prints the per-host form mandated by the workflow so the developer regenerates only what changed and the diff stays minimal:

   ```bash
   # Customer host
   cd frontend && npm run generate:api -- --host customer

   # Maker host
   cd frontend && npm run generate:api -- --host maker

   # Admin host
   cd frontend && npm run generate:api -- --host admin

   # Public host
   cd frontend && npm run generate:api -- --host public
   ```

   Then print the follow-up checklist verbatim:
   - Restart the affected backend host(s) so the live `/openapi/v1.json` reflects the new contract before regen.
   - Re-run `cd frontend && npm run check:api` — it must exit 0.
   - Stage **both** the regenerated `*.v1.ts` files and the updated `.spec-hashes.json` in the same commit. The pre-commit hook [`.husky/pre-commit`](../../.husky/pre-commit) runs `frontend/scripts/check-api-client-manual-edits.mjs` and will reject a generated-file diff without a matching hash update.
   - If a frontend `apiFetch` consumer or a `lib/i18n/cs-CZ.ts` key needs to follow the contract change, do that work in the same PR (no follow-up tickets for contract debt).

4. **Gate the `frontend` agent.**
   While `/sync` reports drift, the `frontend` agent **must not be invoked** on this ticket. Document this in the ticket's `## Sequencing` section if the agent order is being negotiated: contract-change tickets are always backend → `/sync` clean → frontend. The `pm` and `architect` charters enforce this; `/sync` is the executable check.

5. **Re-run `/sync` after regen.**
   The command is idempotent. A second run after a clean regen must print `sync: client in step with backend contract — frontend agent unblocked`. Only then is the frontend handoff allowed.

## Failure mode

A PR that changes any file in step 1 **and** does not contain a matching diff in `frontend/src/lib/api-client/*.v1.ts` plus `frontend/src/lib/api-client/.spec-hashes.json` fails the reviewer Gate 4 with the exact message:

> **NSwag client stale.** Backend contract surface changed in this PR but `frontend/src/lib/api-client/` was not regenerated. Run `/sync` locally, follow the per-host regen plan it prints, and amend the PR.

`reviewer` will not waive this. There is no carve-out for "small" controller edits — even an added `[FromQuery]` parameter changes the generated method signature, and silently shipping the old client is exactly the "silently skipping" failure mode the project's no-mocks rule (root `CLAUDE.md`) is designed to surface loudly.

## Alternatives Considered

- **Auto-regen inside `/sync`.** Rejected: regen requires a running backend host, and `/sync` must work offline (e.g. during a code review on a laptop). Auto-regen would also hide the contract change from the developer's mental model — the explicit per-host command list is itself a teaching artifact.
- **Single bulk command (`npm run generate:api`) only.** Rejected as the printed remediation: regenerating all four hosts on every change pollutes diffs with unrelated client churn and makes contract-change blast radius harder to review. The bulk command stays available as an escape hatch (it is what `README.md` documents), but `/sync` prints the surgical form.
- **Run `npm run check:api` and stop.** Rejected: the parity script answers "is there drift?" but not "what do I do about it?". `/sync` is the developer-facing UX layered on top of that script.
- **Block via CI only, no slash command.** Rejected: catching drift in CI wastes a round trip and an agent invocation. `/sync` shifts the check left to the moment the developer is about to hand off to `frontend`.

## Defense

The pre-commit hook ([`.husky/pre-commit`](../../.husky/pre-commit)) and `reviewer` Gate 4 are the hard enforcement; `/sync` is the **soft, early** signal that prevents the team from discovering drift after the frontend agent has already produced work against a stale contract. It exists because the cost of regen mid-stream — re-running the frontend agent, re-doing i18n keys, re-doing `apiFetch` callers — is much higher than the thirty-second cost of running `/sync` before the handoff. The per-host regen plan is non-negotiable because it forces the developer to articulate, host by host, which contracts moved; that articulation is what makes contract changes reviewable in a four-host monorepo.

## See also

- [docs/adr/0022-nswag-pipeline.md](../../docs/adr/0022-nswag-pipeline.md) — pipeline decision (backend emits OpenAPI, frontend regenerates, CI enforces parity).
- [docs/adr/0007-stack-pivot-dotnet-backend.md](../../docs/adr/0007-stack-pivot-dotnet-backend.md) — the pivot that made NSwag the only frontend ↔ backend data path.
- [docs/tickets/T-0013-nswag-pipeline.md](../../docs/tickets/T-0013-nswag-pipeline.md) — the scaffold ticket that landed the generator scripts and `.spec-hashes.json`.
- [frontend/src/lib/api-client/README.md](../../frontend/src/lib/api-client/README.md) — generated-client folder rules and the bulk regen command.
- [.husky/pre-commit](../../.husky/pre-commit) — the hook that enforces "no manual edits to generated files without a hash update".
- [docs/architecture/patterns.md](../../docs/architecture/patterns.md) — patterns A.2 (one-file feature) and B.4 (frontend API client usage) — the surface `/sync` watches.
- [docs/review/checklist.md](../../docs/review/checklist.md) — Gate 4 (contract parity) is the hard enforcement `/sync` previews.
- [docs/process/quality-gates.md](../../docs/process/quality-gates.md) — where `/sync` sits in the gate sequence.
- [.claude/agents/frontend.md](../agents/frontend.md) — the agent that `/sync` gates.
- [.claude/agents/reviewer.md](../agents/reviewer.md) — the agent that enforces the failure mode above.
