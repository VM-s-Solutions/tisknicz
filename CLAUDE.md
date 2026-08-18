# Makables — Project Instructions for Claude Code

**Brand:** Makables — "Where Ideas Take Shape."
**Domain:** makables.cz
**Operator:** JVM YORE s.r.o.

---

## PART 0 — Working agreement (read first, applies to every turn)

These rules exist because they were learned the hard way. They override default behavior.

### 0.1 Answer in bullets. Spend tokens on code, not prose

- **Bullets by default.** Prose paragraphs only when a bullet genuinely can't carry the thought.
- **Budget: ≤ 10 bullets** for a normal reply. A large multi-file change may go to ~15. Never a wall of text.
- **No preamble, no postamble.** Don't restate the request. Don't open with "I'll now…". Don't close with "Let me know if…".
- **Never re-explain what the diff already shows.** Name the file + what changed in one line each.
- **No tables** unless comparing ≥ 3 things on ≥ 2 axes.
- **No code blocks in the reply** unless the user asked to see code, or it's a command they must run. The code is in the files.
- **File references as markdown links** — [maker-card.tsx:42](frontend/src/app/(public)/katalog/maker-card.tsx#L42) — never backticks.
- **Mirror the user's language.** Czech in → Czech out. English in → English out.

**Standard report shape after doing work** (this is the whole reply — nothing before, nothing after):

```
**Změny**
- <file> — <what, one line>
- <file> — <what, one line>

**Ověřeno**
- <concrete evidence: route loaded, test count, measured ms, browser>

**Pozor / zbývá**  ← only if non-empty
- <assumption taken, or what's deliberately left out and why>
```

### 0.2 Don't stop until it's actually finished

The user has typed "continue" 15+ times across sessions. That is a defect in how I work, not impatience.

- **Run the whole chain in one go:** domain → tests → handler → contract → NSwag regen → UI → build → **verify in the running app** → commit → PR → CI green → merge (when merging was authorized).
- **Do not stop to narrate progress.** Do not ask "shall I continue?" / "want me to also…?". If it's in scope, do it.
- **Do not stop at the first green build.** A green build is not a finished ticket.
- **Sweep the whole codebase, not the page that was named.** A token, primitive, pattern or design change applies to *every* occurrence in the same PR. Grep for all of them. If you deliberately leave some, list them under "zbývá" — don't wait to be told "also the rest of the site".
- **Stop only for:** irreversible/destructive ops (prod DB writes, force-push, deleting data), missing secrets or credentials only the user has, or a business rule where a wrong guess would cost real money.

### 0.3 Ask well, or don't ask

- "I don't understand what you need from me" was a real user reply. Never produce that.
- If you must ask: **2–3 concrete options, a recommendation, one sentence each.** Never open-ended.
- Otherwise: **invent the sensible default and state it in one line** under "Pozor". The user's own instruction was "invent it then".
- Non-blocking unknowns → append to [docs/questions/open.md](./docs/questions/open.md) with `blocking: no`, take the most defensible default, keep working.

### 0.4 "Done" requires evidence. Compiling is not evidence

Every one of these was reported broken *after* being declared fixed: hero animation (3×), logged-in state (2×), login, pagination, image upload, IČO validation, product detail design. The cause is always the same — shipped on a green build without opening the app.

**Never write "fixed" / "works" / "done" without naming the evidence.** Required proof by change type:

| Change | Required proof |
|---|---|
| Any UI | App running; the **actual route** loaded; checked at 375 / 768 / 1280 |
| Anything visual or JS-driven | **Chrome and WebKit/Safari.** Safari has bitten us twice (WebGL hero, `Secure` cookies on localhost) |
| Auth / session / cookies | Real cycle: login → navigate to a protected page → hard refresh → still logged in |
| Upload | Upload a real file **and confirm it renders afterwards** ("it uploaded but I just see an icon" = not done) |
| Validation | Test with a **real valid input**, not just an invalid one (IČO validation shipped rejecting the user's own real Czech IČO) |
| Query / page speed | Measured before → after, in ms, in the reply |
| Backend logic | `dotnet test` on `Makables.Tests` **and** `Makables.IntegrationTests`, counts in the reply |
| Contract change | NSwag regenerated + `npm run check:api` passes |

- **If you cannot verify locally, say so explicitly.** "Nešlo ověřit lokálně — chybí X" is acceptable. Implying it works is not.
- **Test the failing thing itself.** If the report is "no animation in Chrome", checking that the file compiles is not checking the animation.
- Local dev has traps — Postgres location, email confirmation, Azurite, upload timeouts. Consult your memory files before assuming an env failure is a code bug.

### 0.5 Non-negotiable quality bar on every change

Four things ship together, always. A PR missing any of them is not done:

1. **Correct architecture** — DDD domain rules (§2), Clean Architecture layering, no logic in the wrong layer.
2. **Tests** — unit tests for new pure logic, integration test for new endpoints. See [agents/knowledge/testing.md](./agents/knowledge/testing.md). Test-first for domain logic.
3. **Performance** — measured, not assumed. No N+1, paged, indexed. See §5.
4. **Security** — authorization, ownership scoping, input validation, no secret/PII leak. See §6.

You own hygiene. Never hand the user a lint error, a type error, a merge conflict or a red CI to fix.

---

## PART 1 — The project

You are part of a multi-agent team building a Czech marketplace platform with production-grade discipline. Every decision serves a self-running marketplace that needs minimal manual intervention. Once live, changes are expensive — bias toward long-term flexibility.

### Read these before touching code

1. **[docs/architecture/patterns.md](./docs/architecture/patterns.md)** — canonical pattern catalog (C# + TS). Single source of truth for *shapes*.
2. **[docs/architecture/overview.md](./docs/architecture/overview.md)** — system shape.
3. **[docs/adr/](./docs/adr/)** — every architectural decision, numbered. Especially [0007](./docs/adr/0007-stack-pivot-dotnet-backend.md).
4. **[agents/knowledge/testing.md](./agents/knowledge/testing.md)** — what must be tested, where, and the must-cover list.
5. **[agents/knowledge/security-rules.md](./agents/knowledge/security-rules.md)** — the S-rules.
6. **[.claude/agents/](./.claude/agents/)** — your charter if you are a sub-agent.
7. **[agents/WAY-OF-WORKING.md](./agents/WAY-OF-WORKING.md)** — request → shipped code.

### Agent operating system

`.claude/agents/` holds agent **charters**; `agents/` holds the **operating system**:

- **[agents/process/](./agents/process/)** — [routing](./agents/process/routing.md), [ticket-lifecycle](./agents/process/ticket-lifecycle.md), [quality-gates](./agents/process/quality-gates.md), [deliberation](./agents/process/deliberation.md), [communication](./agents/process/communication.md), [enforcement](./agents/process/enforcement.md), [shared-file-lanes](./agents/process/shared-file-lanes.md).
- **[agents/knowledge/](./agents/knowledge/)** — conventions, security S-rules, testing/TDD, runtime-readiness.
- **[agents/templates/](./agents/templates/)** — ticket / story / ADR / audit / test-plan.

Backlog and project state live under [docs/](./docs/). Entry point `/team <request>`; narrower: `/plan`, `/execute`, `/feature`, `/review`, `/audit`, `/sync`.

### Stack

| Layer | Stack |
|---|---|
| Backend | .NET 10, ASP.NET Core, MediatR, FluentValidation, EF Core 10, PostgreSQL 16 |
| Backend hosts | Per-audience: `Web.Customer` (5001), `Web.Maker` (5002), `Web.Admin` (5003), `Web.Public` (5104) |
| Background jobs | Azure Functions v4 (Docker) |
| File storage | Azure Blob Storage (only through the backend) |
| Auth | Custom: Argon2id + JWT + refresh tokens; `IAuthService` |
| Frontend | Next.js 16 (App Router), React 19, Tailwind 4 |
| Contract | OpenAPI → NSwag TypeScript client in `frontend/src/lib/api-client/` |
| Tests | `Makables.Tests` (xUnit unit), `Makables.IntegrationTests` (`WebApplicationFactory`), `Makables.TestUtilities`; frontend Vitest + Testing Library + jest-axe |
| Cloud | Azure (West Europe) |

**The backend is the system of record.** Money, state transitions, invariants, validation, invoicing, payouts, integrations — all .NET.
**The frontend is a pure presentation layer.** No DB access, no business logic.

### Repository layout

```
makables/
├── backend/             # /backend/src/Makables.Api.slnx
├── frontend/            # Next.js app
├── docs/                # ADRs, tickets, stories, architecture (project system of record)
├── agents/              # agent OS: process, knowledge, templates
├── infra/bicep/         # Azure IaC
├── scripts/             # run-dev.ps1, check-consistency.mjs
└── .claude/agents/ · .claude/commands/
```

---

## PART 2 — Architecture: DDD first

DDD is the primary architecture, expressed through Clean Architecture layers and CQRS. The `Order` aggregate ([Order.cs](backend/src/Makables.Core.Domain/Orders/Order.cs)) is the reference implementation — read it before modelling anything new.

### 2.1 Layering (hard boundaries)

- `Core.Domain` — entities, aggregates, value objects, domain services, repository **interfaces**, policies, specifications. **Zero third-party packages. No EF Core. No MediatR.**
- `Core.AppServices` — use cases (MediatR handlers), validators, DTOs, mappers. References `Core.Domain` + MediatR + FluentValidation. **No `Microsoft.EntityFrameworkCore`.**
- `Infra.*` — implements interfaces declared in `Core.Domain`. EF Core, HTTP clients, blob storage, PDF.
- `Web.*` — thin hosts. Reference `Config` + `Core.AppServices`, **never `Infra.*` directly**.

Dependencies point inward. Only. If a rule feels like it needs an outward reference, the model is wrong.

### 2.2 Aggregates

- **One aggregate per transaction.** Never mutate two aggregates in one handler — publish through the Outbox instead.
- **Rich behavior, not anemic data.** Aggregates expose intent-named methods (`MarkAsPaid`, `Ship`, `Cancel`, `RevertAcceptance`), not setters.
- **All setters `private set`.** Construction only via `static Create(...)`. A handler that assigns a property directly is a bug.
- **Invariants live in the aggregate.** A behavior method that would break an invariant returns `BusinessResult.Failure(BusinessErrorMessage.X)` and changes nothing. Validators check *input shape*; aggregates protect *truth*.
- **State machines are aggregate methods.** Every legal transition is a method; every illegal transition returns its documented error code. Never reachable by setting `State`.
- **Reference other aggregates by id** (`string MakerId`), not by navigation property, across aggregate boundaries.
- **Aggregate = consistency boundary.** Keep it small; if two things never change together, they are two aggregates.

Current aggregate roots: `Order`, `Maker`, `Product`, `User`, `Invoice`, `PayoutBatch`, `Dispute`, `Category`.

### 2.3 Value objects

- Money is `Money` — `long` minor units + `string Currency`. Never `decimal`, never `double`, ever.
- Model a concept as a value object when it has rules: `Money`, IČO, DIČ, email, slug, phone, rating (basis points). Immutable, structurally equal, self-validating, C# `record`.
- Ubiquitous language: domain types keep the Czech business vocabulary (maker, objednávka states, výrobek). Don't invent synonyms — grep the glossary first.

### 2.4 Repositories & specifications

- **One repository interface per aggregate root**, declared in `Core.Domain`, implemented in `Infra.Database`.
- Repositories return **aggregates**, not projections. Read models for lists go through `IXxxQueries` (CQRS read side) returning `record` DTOs with `.AsNoTracking()`.
- No `IQueryable` leaks out of `Infra.*`. Query intent is expressed as a `*Specification`.
- Scoped repositories (`ForCustomer` / `ForMaker` / `Unscoped`) are the **security boundary** — a cross-tenant read returns empty, not 403 (ADR 0013).

### 2.5 Use cases (CQRS via MediatR)

- One file per use case: `Core.AppServices/Features/<Aggregate>/<UseCase>.cs` with nested `Command`/`Query`, `Response`, `Validator`, `Handler`.
- **Handlers orchestrate, they don't decide.** Load aggregate → call one behavior method → return. Any `if` that encodes a business rule belongs in the domain.
- Pipeline behaviors run automatically: `ValidationPipelineBehavior` (all) → `UnitOfWorkPipelineBehavior` (commands). **Handlers never call `SaveChangesAsync()`.**
- `BusinessResult<T>` for expected failures; exceptions only for genuinely unexpected ones.
- Error codes always from `BusinessErrorMessage`. No inline strings.
- Controllers are one-liners over `Mediator.Send`. Never bypass it.

### 2.6 Domain events / integration events

- Cross-aggregate and external side effects (email, label generation, invoice render) go through the **Outbox** in the same transaction as the state change. Never inline HTTP or email from a handler.
- Outbox consumers are idempotent — a re-delivery produces no second effect.

### 2.7 Other backend invariants

- `Auditable` base entity on every transactional entity (`CountryCode`, `IsActive`, `CreatedBy/On`, `UpdatedBy/On`, `DeactivatedBy/On`). Soft delete by default.
- **`CountryConfiguration` drives per-country variation.** Never `if (countryCode == "CZ")` outside a per-country adapter — look up the row.
- **Provider adapter pattern** (payments, shipping, registry, email, geocoder) via keyed DI, selected by `CountryConfiguration.Default*Provider`.
- **No `HttpClient` outside `Infra.Clients/<Provider>/`.**
- **Idempotent webhooks:** verify origin/signature *first* (a spoofed origin does zero DB reads) → look up by `provider_ref` → already in target state ⇒ 200 with no second transition → transition in one transaction.
- Every monetary column: `*_minor BIGINT NOT NULL` + `currency CHAR(3) NOT NULL`. VAT as basis points, half-up rounding, CZK display strips haléře.

---

## PART 3 — Backend engineering standards (.NET)

- **Nullability strict.** No `dynamic`. No `object` where a concrete type fits. No `!` to silence the compiler — restructure.
- **`record` for DTOs and value objects**; `sealed class` for entities; `sealed` everything not designed for inheritance.
- **Primary constructors** for handler/validator/service DI.
- **`async` all the way down.** Every I/O method takes and honours a `CancellationToken`. No `.Result`, no `.Wait()`, no `async void`.
- **`ILogger<T>` with structured templates** — `_logger.LogWarning("Order {OrderId} rejected: {Code}", id, code)`. Never string interpolation into the message. Never `Console.WriteLine`.
- **Never log PII or secrets** — no emails, phone numbers, addresses, tokens, IČO in log messages.
- **`IClock` for time**, never `DateTimeOffset.UtcNow` inline (untestable).
- **`IIdGenerator` for ids.** No `Guid.NewGuid()` sprinkled in domain code.
- Prefer pure functions and immutability; prefer composition over inheritance; keep methods short and intent-named.
- Fail fast on programmer error (`ArgumentNullException`), `BusinessResult` on user/business error.
- **No dead code, no commented-out code, no TODO without an owner** (open questions → [docs/questions/open.md](./docs/questions/open.md)).

---

## PART 4 — Frontend engineering standards (Next.js 16 / React 19)

**Before writing Next.js code, read the relevant guide in `node_modules/next/dist/docs/`.** Next 16 has breaking changes vs. training data.

### Architecture

- **Server Components by default.** `'use client'` only for real interactivity, pushed as far down the tree as possible. A page is never a client component just because one button is.
- **No data fetching in `useEffect`.** Server Components fetch on render; Client Components call the API client from event handlers.
- **No business logic.** No pricing math, no validation rules, no state machines. Backend owns them.
- **No DB SDK imports** (`pg`, `prisma`, `@supabase/*`). The only data path is `lib/api-client/`.
- **Every API call through `lib/runtime/api-fetch.ts`** → `Result<T, ApiError>`, handling auth + 401 → refresh → retry. Multipart uploads must pass an explicit long `timeoutMs` (the 8 s default aborts them).
- **`lib/api-client/` is generated — never hand-edited.** A pre-commit hook blocks it.
- **No global state libraries** (Redux / Zustand / Jotai). Server state lives on the server; UI state is local. URL is the state container for filters and pagination (`searchParams` + `<Link>`).

### React quality

- Zero `any`. Zero unsafe `!`. Props explicitly typed; no `React.FC`.
- Named exports (except Next.js `page`/`layout`/`route` defaults).
- **Keys are stable ids**, never array indices.
- **No derived state in `useState`** — compute it during render. `useEffect` only for genuine external synchronization (subscriptions, DOM measurement), never for data or derivation.
- `useMemo`/`useCallback` only with a measured reason.
- Interactive elements are real `<button>` / `<a>`; `aria-*` where semantics need help; keyboard reachable. `jest-axe` clean where a spec exists.
- Forms: uncontrolled + Server Action or a single submit handler. Show pending state; disable double-submit; surface the backend's error code through i18n. A successful save must be visibly confirmed in-viewport (a toast off-screen is a bug — this was reported).
- Composition over prop-drilling; extract a component when a block has its own state or is used twice.

### Styling & design language

- Use primitives from `components/ui/`. Never re-implement a button, badge, dropdown, date picker or textbox inline.
- No inline `style={}` for layout. No arbitrary Tailwind values. Semantic tokens only (`--color-success/warning/error/info`), never stock Tailwind hues.
- Responsive at **375 / 768 / 1280** — verified, not assumed.
- The design language (palette, hairline buttons, no gradients, no icons in badges, contrast floor, static-by-default motion) is recorded in memory and enforced site-wide. Consult it before any visual change; a visual rule applies to **every** page, not the one named.
- `next/image` with explicit dimensions; heavy client components via `next/dynamic`.

### i18n

- All user-facing strings from `lib/i18n/cs-CZ`. Zero hardcoded Czech outside brand copy.
- Every `BusinessErrorMessage` code has a parallel `cs-CZ` key — shipped in the same PR.
- Currency `1 234 Kč` (NBSP thousands, whole CZK). Dates `9. 5. 2026`. Vykání for customers, tykání for makers.

---

## PART 5 — Performance (a gate, not an aspiration)

"Loading of katalog or profile is insanely slow — this is hilariously slow" was real user feedback. Perf regressions are defects.

- **Measure, then report.** Any query, page or endpoint you touch: state before → after in ms. No "should be faster".
- **No N+1. Ever.** Project in one query; `Include` deliberately; batch by id set.
- Every list endpoint paged (`DataRangeRequest` / `PagedData<T>`). Every column used in WHERE / ORDER BY / JOIN indexed — verify the index exists, don't assume.
- `.AsNoTracking()` on every read-only query. Read models are DTO projections, not full aggregates.
- No sequential awaits over independent I/O — `Task.WhenAll`.
- Cache what is stable and hot (`CountryConfiguration`, categories, ARES lookups) behind an interface, with explicit invalidation.
- Frontend: minimize client JS; `next/dynamic` for heavy widgets; explicit image dimensions; no layout shift. Budget: catalog LCP < 2.5 s on dev.
- Azure has cold starts and transient 5xx ("some actions have to be done multiple times before we get 200") — external calls get timeout + bounded retry with jitter; health endpoints stay warm.

---

## PART 6 — Security

- `[Authorize]` or middleware on **every** protected endpoint. No exceptions, no "internal" endpoint left open.
- **JWT audience enforced per host** — a customer JWT must be rejected by the maker API. That rejection is a test, not a code review.
- **Ownership is enforced by the scoped repository**, not by an `if` in the handler. Cross-user reads return empty.
- Webhooks verify origin/signature/IP **before any DB access or side effect**.
- Cron endpoints check `CRON_SECRET` / Functions key.
- All payments verified server-side against the provider. Never trust Comgate redirect params.
- **Dev conveniences must be environment-gated and provably unreachable in production** (the dev payment bypass is the pattern to follow).
- File uploads validated server-side: content type by **file signature**, size, extension. All file access proxied by the backend — no direct browser → blob URLs.
- Rate-limit auth endpoints (login, register, reset, resend). Uniform failure messages — never reveal whether an account exists.
- Secrets from Configuration / Key Vault only. Only `NEXT_PUBLIC_*` may reach the client bundle. Never commit `.env*`.
- GDPR: soft delete + documented erasure path; personal data deletable on request; no PII in logs or analytics.
- Full rule set: [agents/knowledge/security-rules.md](./agents/knowledge/security-rules.md).

---

## PART 7 — Testing (no ticket closes without it)

Full policy: [agents/knowledge/testing.md](./agents/knowledge/testing.md). The hard minimum:

- **New pure logic (money, pricing, numbering, validators, state transitions, specs) → unit test written first.** Red → green → refactor. After-the-fact tests on domain logic are a review failure.
- **New endpoint → integration test** covering happy path + the auth/ownership/audience rejection, against the correct host.
- **Every `BusinessErrorMessage` code a handler can return → a test that triggers it** and asserts the constant (never a hardcoded string).
- **State machines: every legal transition and every illegal transition** has a test.
- **Webhooks: re-delivery test** proving no second effect.
- **Frontend: test the logic** (state mapping, error-code → i18n), not the markup. a11y via `jest-axe`.
- Money assertions in `long` minor units. Never a float/decimal expected value.
- **Run the suites and put the counts in your reply.** "Tests pass" without numbers is not evidence.
- Changing untested legacy code → characterization test first, pinning current behavior.

---

## PART 8 — Cross-stack rules

- **Czech-only at launch.** Multi-country-ready architecture; CZ-only data and UI.
- **NSwag is the contract.** Any backend contract change ⇒ regenerate the TypeScript client in the **same PR**, for **every** affected host. CI verifies parity (`npm run check:api`).
- **One PR per ticket.** Cross-stack changes ship atomically.
- **No mocks during the build phase.** Missing endpoints stay loudly broken until built — this catches silent skipping.
- Docs updated in the same PR when architecture, env vars, or deployment change.

### What NOT to do

- Don't put business logic in the frontend, or call third-party APIs (Comgate, Packeta, ARES, Resend, Mapbox) from it.
- Don't bypass `Mediator.Send`, don't call `SaveChangesAsync()` in a handler, don't skip pipeline behaviors.
- Don't mutate an aggregate through property setters, and don't touch two aggregates in one transaction.
- Don't branch on country directly.
- Don't use the Pages Router, or add a global state library.
- Don't edit `lib/api-client/` manually.
- Don't commit secrets or `.env*`.
- Don't reference files outside this repository.

---

## PART 9 — Self-check before you say it's done

Walk this list. If any item fails, fix it — never hand it to the user.

#### Domain / backend

- Invariants in aggregates, not handlers; setters private; behavior methods return `BusinessResult`; one aggregate per transaction.
- `Core.Domain` third-party-free; `Core.AppServices` EF-free; no HTTP outside `Infra.Clients`; no `SaveChangesAsync()` in handlers; no direct country branching.
- Strict nullability; no `dynamic`; `CancellationToken` threaded; `IClock` used; no `Console.WriteLine`; no PII in logs; no unused usings; no dead code.
- Every error code from `BusinessErrorMessage`. Every money column `*_minor BIGINT` + `currency CHAR(3)`.
- `[Authorize]` on every protected route; audience enforced; ownership via scoped repository; webhooks verify before side effects.

#### Frontend

- Zero `any`, zero unsafe `!`, zero `console.*`, zero dead code, zero hardcoded Czech.
- Server Components default; no `useEffect` data fetching; all calls via `apiFetch`; no DB SDK import.
- `components/ui/` primitives; semantic tokens; no arbitrary values; no layout `style={}`; verified at 375/768/1280.
- Design-language rules applied **site-wide**, not just the touched page.

#### Tests & proof

- Unit tests for new logic (written first), integration test for new endpoints, negative-path test per error code — suites run, counts reported.
- App actually opened; the specific reported behavior actually reproduced-then-fixed; Chrome **and** WebKit for visual/JS work.
- Perf numbers measured where a hot path changed.
- NSwag regenerated if the contract moved; `cs-CZ` key added for every new error code.
- Every ticket AC has a named proof.

#### Reply

- Bullets. ≤ 10. No preamble, no postamble, no re-explaining the diff. Evidence named. User's language.

---

### Help and feedback

- `/help` — help with Claude Code
- Feedback: <https://github.com/anthropics/claude-code/issues>
