# Makables — Project Instructions for Claude Code

**Brand:** Makables — "Where Ideas Take Shape."
**Domain:** makables.cz
**Operator:** JVM YORE s.r.o.

## Read these before touching any code

1. **[docs/architecture/patterns.md](./docs/architecture/patterns.md)** — the canonical pattern catalog (backend C# + frontend TypeScript). It is the single source of truth.
2. **[docs/architecture/overview.md](./docs/architecture/overview.md)** — system shape.
3. **[docs/adr/](./docs/adr/)** — every architectural decision, numbered. Especially [0007](./docs/adr/0007-stack-pivot-dotnet-backend.md) (the pivot from Supabase to .NET).
4. **[.claude/agents/](./.claude/agents/)** — your role-specific charter if you are a sub-agent.
5. **[agents/](./agents/)** — the agent operating system: how the team works together. Start at [agents/WAY-OF-WORKING.md](./agents/WAY-OF-WORKING.md) and [agents/README.md](./agents/README.md).

You are part of a multi-agent team building a Czech marketplace platform with production-grade discipline. Every decision serves a self-running marketplace that requires minimal manual intervention. Once we go live, changes are expensive — bias toward long-term flexibility.

## Agent operating system

The team runs as a deterministic, artifact-based flow. `.claude/agents/` holds the agent **charters** (system prompts); `agents/` holds the **operating system** everything they read, produce, and coordinate through:

- **[agents/WAY-OF-WORKING.md](./agents/WAY-OF-WORKING.md)** — the request → shipped-code walkthrough.
- **[agents/process/](./agents/process/)** — [routing](./agents/process/routing.md) (signal → agent), [ticket-lifecycle](./agents/process/ticket-lifecycle.md) (states + Definition of Ready), [quality-gates](./agents/process/quality-gates.md), [deliberation](./agents/process/deliberation.md) (defense panels), [communication](./agents/process/communication.md), [enforcement](./agents/process/enforcement.md), [shared-file-lanes](./agents/process/shared-file-lanes.md).
- **[agents/knowledge/](./agents/knowledge/)** — how-we-build guidance (conventions, security S-rules, testing/TDD, runtime-readiness) that complements the pattern catalog.
- **[agents/templates/](./agents/templates/)** — ticket / story / ADR / audit / test-plan templates.

The backlog itself (tickets, ADRs, questions, sprint status) lives under **[docs/](./docs/)** — that is the system of record for project state; `agents/` is the process layer.

Entry point: **`/team <request>`** hands work to the PM, who convenes a defense panel, files tickets, routes to specialists, runs a reviewer in parallel with every developer, and gates before `done`. Narrower commands: `/plan`, `/execute`, `/feature`, `/review`, `/audit`, `/sync`.

## Stack reality (dual-stack monorepo)

| Layer | Stack |
|---|---|
| Backend | .NET 10, ASP.NET Core, MediatR, FluentValidation, EF Core 10, PostgreSQL 16 |
| Backend hosts | Per-audience: `Web.Customer`, `Web.Maker`, `Web.Admin`, `Web.Public` |
| Background jobs | Azure Functions v4 (Docker) |
| File storage | Azure Blob Storage (accessed only through the backend) |
| Auth | Custom: Argon2id passwords + JWT + refresh tokens; `IAuthService` interface |
| Frontend | Next.js 16 (App Router), React 19, Tailwind 4 |
| API contract | OpenAPI → NSwag-generated TypeScript client in `frontend/src/lib/api-client/` |
| Cloud | Azure (West Europe) |

**The frontend is a pure presentation layer.** It has no database access, no business logic. It calls the backend through the generated client.

**The backend is the system of record.** Money math, state transitions, validation, invoicing, payouts, integrations — all in .NET.

## Repository layout

```
makables/
├── backend/             # /backend/src/Makables.Api.slnx (.NET solution)
├── frontend/            # Next.js app
├── docs/                # process, ADRs, user stories, tickets, architecture (project system of record)
├── agents/              # agent operating system: process, knowledge, templates
├── infra/bicep/         # Azure IaC (per-audience hosts, Postgres, Key Vault, Functions)
├── scripts/             # run-dev.ps1, check-consistency.mjs
├── .claude/agents/      # sub-agent charters   .claude/commands/ # slash commands
└── CLAUDE.md, README.md, TISKNI_MVP_SPEC.md, PROJEKT-VIZE.md
```

## Architectural rules (non-negotiable)

### Backend (.NET)

1. **Clean Architecture** layering. `Core.Domain` references no third-party packages. `Core.AppServices` references `Core.Domain` + MediatR + FluentValidation. `Infra.*` implements interfaces declared in `Core.Domain`. `Web.*` references `Config` + `Core.AppServices`, never `Infra.*` directly.
2. **CQRS via MediatR.** Every use case is one file: `Core.AppServices/Features/<Entity>/<UseCase>.cs` containing nested `Command`/`Query`, `Response`, `Validator`, `Handler`.
3. **`BusinessResult<T>`** for expected failures. Exceptions reserved for truly unexpected failures.
4. **Pipeline behaviors** run automatically: `ValidationPipelineBehavior` (all requests) → `UnitOfWorkPipelineBehavior` (commands only). Handlers **never** call `SaveChangesAsync()`.
5. **Centralized error codes** in `BusinessErrorMessage`. No inline error strings.
6. **`Auditable` base entity** on every transactional entity (`CountryCode`, `IsActive`, `CreatedBy/On`, `UpdatedBy/On`, `DeactivatedBy/On`). Soft delete by default.
7. **Money as `long` minor units** + `string Currency`. VAT rates as basis points. Half-up rounding. CZK display strips haléře.
8. **`CountryConfiguration`** drives per-country variation. Never branch on country directly — look up the row.
9. **Provider adapter pattern** for payments, shipping, registry, email, geocoder. Keyed services in DI; selection via `CountryConfiguration.Default*Provider`.
10. **No direct `HttpClient` calls** outside `Infra.Clients/<Provider>/`.
11. **Per-audience API hosts** share `Core.*` + `Config` + `Infra.*`. Each host's `Program.cs` is a flat list of `AddMakablesXxx()` calls.
12. **Idempotent webhooks.** Verify origin/signature; look up by `provider_ref`; if already in target state return 200; transition state in a single transaction.

### Frontend (Next.js)

1. **Server Components by default.** `'use client'` only for interactivity.
2. **No data fetching via `useEffect`.** Server Components fetch on render; Client Components call the API client in event handlers.
3. **No business logic.** No pricing math, no validation rules, no state machines on the frontend. They live in the backend.
4. **No DB SDK imports.** No `pg`, no `prisma`, no `@supabase/*`. The only data path is `lib/api-client/`.
5. **All API calls** go through `lib/runtime/api-fetch.ts` which returns `Result<T, ApiError>` and handles auth + 401 → refresh → retry.
6. **All user-facing strings** come from `lib/i18n/cs-CZ`. Every `BusinessErrorMessage` code has a parallel i18n key.
7. **The generated client (`lib/api-client/`) is not edited manually.** A pre-commit hook blocks edits.

### Cross-stack

- **Czech-only at launch.** Multi-country-ready architecture; CZ-only data and UI.
- **NSwag is the contract.** Any backend contract change requires regenerating the frontend client in the same PR. CI verifies parity.
- **One PR per ticket.** Cross-stack changes ship atomically.
- **No mocks during build phase.** Per user direction (option 1 in pivot planning), missing endpoints stay loudly broken until built. This catches "silently skipping" failures.

## Code quality

- **No `dynamic` in C#.** No `any` in TypeScript.
- **No `Console.WriteLine` / `console.*`.** Inject `ILogger<T>` (C#) or use the structured logger (TS).
- **No TODO without owner.** Open questions go in [docs/questions/open.md](./docs/questions/open.md).
- **No dead code, no commented-out code.**
- **Records for DTOs** (C#); `readonly` interfaces for TypeScript DTOs (generated by NSwag).
- **Named exports** in TypeScript (except Next.js page/layout/route defaults).
- **Primary constructors** for handler/validator DI in C# (C# 12+).
- **Functions over classes** in TypeScript for cross-feature services.

## Security

- **Auth check via `[Authorize]` or middleware** on every protected backend endpoint.
- **JWT audience enforced per host** — a customer JWT cannot be replayed against the maker API.
- **Webhooks verify origin/signature** before any side effect.
- **Cron endpoints** check `CRON_SECRET` (or equivalent Azure Functions key).
- **No secrets in client bundle.** Only `NEXT_PUBLIC_*` is allowed in the frontend.
- **File uploads** validated server-side (type + size). All file access through the backend; no direct browser → blob storage links.
- **All payments verified server-side.** Never trust the client-side redirect params from Comgate alone.

## i18n

- All UI strings via i18n keys. Czech (`cs-CZ`) only at launch.
- Currency: `1 234 Kč` (whole CZK display, space thousands separator).
- Dates: Czech short format `9. 5. 2026`.
- Tone: vykání (V form) for customers; tykání (T form) for makers — pending confirmation in [docs/questions/open.md](./docs/questions/open.md).

## Performance

- **Backend:** every list endpoint paginated (`DataRangeRequest` / `PagedData<T>`); every query indexed where used in WHERE / ORDER BY / JOIN; `.AsNoTracking()` for read-only queries.
- **Frontend:** Server Components by default (zero JS to client unless needed); `next/image` with explicit dimensions; lazy-load heavy client components via `next/dynamic`.

## What NOT to do

- Do not introduce Redux / Zustand / Jotai. Server state lives in the backend; client UI state is local.
- Do not use the Pages Router.
- Do not call third-party APIs from the frontend (Comgate, Packeta, ARES, Resend, Mapbox). They are all in `Infra.Clients/` on the backend.
- Do not bypass `Mediator.Send` in controllers. Controllers are one-liners.
- Do not branch on country directly. Look up `CountryConfiguration`.
- Do not put business logic in the frontend.
- Do not call `SaveChangesAsync()` in handlers.
- Do not edit `lib/api-client/` manually.
- Do not commit secrets or `.env*` files.
- Do not skip pipeline behaviors.
- Do not reference files outside this repository.

## Self-check before declaring a task done

After every code change, verify **all** of the following:

### Backend
- Type safety: strict nullability; no `dynamic`; no `object` where a concrete type works.
- Hygiene: no `Console.WriteLine`; no unused usings; no dead code.
- Architecture: `Core.Domain` has no third-party packages; `Core.AppServices` has no `Microsoft.EntityFrameworkCore`; no HTTP outside `Infra.Clients`; handlers happy-path only; no `SaveChangesAsync()` in handlers; no `if (countryCode == "CZ")` outside per-country adapters.
- Security: `[Authorize]` or middleware on every protected endpoint; webhooks verify origin; secrets via Configuration.
- Errors: every code from `BusinessErrorMessage`; no inline strings.
- Money: every monetary column ends in `_minor` (`BIGINT NOT NULL`) + `currency CHAR(3) NOT NULL`.

### Frontend
- Type safety: zero `any`; zero unsafe `!`; props typed.
- Hygiene: zero `console.*`; zero unused imports; zero dead code.
- Architecture: Server Components default; no `useEffect` for data fetching; all API calls via `lib/api-client/` + `apiFetch`; no DB SDK imports.
- Styling: no inline `style={}` for layout; UI primitives from `components/ui/`; responsive at 375/768/1280; no arbitrary Tailwind values.
- i18n: no hardcoded Czech (except brand copy); every error message uses a key matching backend `BusinessErrorMessage`.

### Cross-stack
- AC traceability: every AC item in the ticket has a verifiable proof.
- Contract parity: if the API contract changed, the NSwag-generated client is regenerated and committed in the same PR.
- Docs updated where needed (architecture, process, env vars, deployment).

If **any** item fails, fix it before closing the task. Do not ask the user to handle hygiene issues — own them.

## Help and feedback

If the user asks for help or wants to give feedback:
- `/help` — get help with using Claude Code
- Feedback: <https://github.com/anthropics/claude-code/issues>
