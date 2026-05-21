# Quality gates

A PR cannot merge until **every** applicable gate below is green. Reviewer enforces.

## Gate 1 — CLAUDE.md self-check (Reviewer)

Run the full self-check from [CLAUDE.md](../../CLAUDE.md) §Self-Check. Items differ per side of the PR:

### Backend PRs (`/backend/**`)
1. **Type safety**: strict nullability; no `dynamic`; no `object` where a concrete type works.
2. **Code hygiene**: no `Console.WriteLine`; no unused usings; no dead code; XML doc only where non-obvious.
3. **Architecture**:
   - `Core.Domain` has no third-party package references (only BCL).
   - `Core.AppServices` has no `Microsoft.EntityFrameworkCore` references; no direct DB access.
   - External HTTP calls only inside `Infra.Clients/<Provider>/`.
   - Handlers contain happy-path only; validation in FluentValidation `Validator`.
   - No `SaveChangesAsync()` in handlers — `UnitOfWorkPipelineBehavior` handles it.
   - No raw `if (countryCode == "CZ")` branches outside per-country adapter classes.
4. **Security**: auth check via `[Authorize]` or middleware on every protected endpoint; webhooks verify origin/signature; cron endpoints check `CRON_SECRET`; secrets only via Configuration, never inline.
5. **Errors**: every `Error.Code` comes from `BusinessErrorMessage`; no inline error strings.
6. **Money**: every monetary column ends in `_minor` (`BIGINT NOT NULL`); accompanying `currency CHAR(3) NOT NULL`; no `decimal` for stored amounts.

### Frontend PRs (`/frontend/**`)
1. **Type safety**: zero `any`; zero unsafe `!`; all props/params typed.
2. **Code hygiene**: zero `console.*`; zero TODO without owner; zero unused imports; zero dead code.
3. **Architecture**:
   - Server Components by default; `'use client'` only with justification.
   - No `useEffect` for data fetching.
   - All API calls go through `lib/api-client/` (NSwag-generated) via `apiFetch` wrapper.
   - No DB SDK imports (`pg`, `prisma`, `@supabase/*`).
   - No manual edits to `lib/api-client/*`.
4. **Styling**: no inline `style={}` for layout/spacing; UI primitives from `components/ui/`; responsive at 375/768/1280; no arbitrary Tailwind values.
5. **i18n**: every user-facing string from `lib/i18n/cs-CZ` (except brand copy).
6. **Error handling**: try/catch on async client calls; Czech user-facing errors via i18n; loading + error states present.

## Gate 2 — Acceptance criteria (QA + Reviewer)

Every AC item in the ticket has a verifiable proof:
- For UI: screenshot or recorded interaction
- For API: response sample, integration test, or HTTP client log
- For background job: log line, trace, or DB state change

## Gate 3 — Security (SecOps, mandatory for security-touching tickets)

A ticket is "security-touching" if it modifies any of:
- Auth flow, middleware, JWT validation, password handling
- New entities or columns containing PII or financial data
- Webhook endpoints
- File upload / Blob storage
- Secrets, env vars, Key Vault config, deploy pipeline
- Background job (cron) endpoints
- CORS, rate limit, IP allowlist configuration

SecOps verifies against `docs/security/*.md` checklists.

## Gate 4 — Architecture (Architect, mandatory if extension point touched)

If the ticket adds or modifies an extension point listed in `docs/architecture/extension-points.md`, Architect signs off that the change preserves the abstraction (e.g., a new payment provider does not leak Comgate-isms into the order domain).

## Gate 5 — Tests (QA)

- **Backend**: unit tests for non-trivial validators, services, specifications; integration test via `WebApplicationFactory<Program>` for any new endpoint.
- **Frontend**: manual test plan executed against preview environment; automated tests only where pure logic exists (money formatting, validation mirrors).
- Regression spot-check on adjacent features.

## Gate 6 — Contract parity (Reviewer)

If the PR changes the API contract:
- The NSwag-generated client in `/frontend/src/lib/api-client/` is regenerated and committed in the same PR.
- A short note in the PR description flags the contract change and which `lib/api-client/<host>-api.ts` file is affected.
- CI verifies the generated client matches the backend's `openapi/v1.json`.

## Gate 7 — Docs (writer of the change)

If the change affects:
- Architecture → update `docs/architecture/*`
- Process → update `docs/process/*`
- New extension point → add to `docs/architecture/extension-points.md`
- New configuration value → add to the appropriate `appsettings.*.json` template AND to the deployment env var list

## Definition of done

```
☐ All AC items verified (QA)
☐ CLAUDE.md self-check passed (Reviewer)
☐ Security gate passed if applicable (SecOps)
☐ Architecture gate passed if applicable (Architect)
☐ Contract parity green if applicable (Reviewer)
☐ Docs updated
☐ PR merged to master
☐ Ticket moved to done; sprint status updated (PM)
```
