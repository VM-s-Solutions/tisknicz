<!-- BEGIN:nextjs-agent-rules -->
# This is NOT the Next.js you know

This version has breaking changes — APIs, conventions, and file structure may all differ from your training data. Read the relevant guide in `node_modules/next/dist/docs/` before writing any code. Heed deprecation notices.
<!-- END:nextjs-agent-rules -->

## Makables — agent rules

Full specification: **[CLAUDE.md](./CLAUDE.md)**. This file is the condensed contract for any coding agent working in this repo. Where the two differ, CLAUDE.md wins.

Czech marketplace platform (makables.cz, operator JVM YORE s.r.o.). Dual-stack monorepo: **.NET 10 backend** (`backend/`) + **Next.js 16 frontend** (`frontend/`). Production-grade discipline — once live, changes are expensive.

---

### 1. How to communicate

- **Answer in bullets, ≤ 10.** No preamble, no postamble, no restating the request, no re-explaining the diff.
- **No code blocks in the reply** unless asked, or it's a command to run.
- **File references as markdown links**: [maker-card.tsx](frontend/src/app/(public)/katalog/maker-card.tsx).
- **Mirror the user's language** — Czech in, Czech out.
- Report shape: `Změny` (bullets) → `Ověřeno` (evidence) → `Pozor / zbývá` (only if non-empty).

### 2. How to work

- **Finish the whole chain without stopping:** domain → tests → handler → contract → NSwag regen → UI → build → verify in the running app → commit → PR → CI green → merge (if authorized). Don't ask "shall I continue".
- **A green build is not a finished ticket.**
- **Sweep repo-wide.** A design/token/pattern change applies to every occurrence in the same PR, not just the file named. Grep; list anything deliberately left.
- **Own hygiene** — never hand back a lint error, type error, merge conflict, or red CI.
- Stop only for: irreversible ops, missing credentials, or a business rule where guessing wrong costs money.
- If you must ask: 2–3 concrete options + a recommendation. Otherwise invent the sensible default and state the assumption in one line.

### 3. "Done" needs evidence

Never write "fixed" / "works" without naming the proof. Compiling is not proof.

- **UI** → app running, actual route loaded, checked at 375 / 768 / 1280, in **Chrome and WebKit/Safari** (Safari-only bugs have hit us twice).
- **Auth/session** → login → protected page → hard refresh → still logged in.
- **Upload** → upload a real file *and* confirm it renders afterwards.
- **Validation** → test a real *valid* input, not only an invalid one.
- **Perf** → measured before → after, in ms.
- **Backend** → `dotnet test` on `Makables.Tests` and `Makables.IntegrationTests`, counts reported.
- **Contract** → NSwag regenerated, `npm run check:api` green.
- Cannot verify locally? Say so explicitly. Never imply it works.

### 4. Architecture — DDD first

Reference implementation: [Order.cs](backend/src/Makables.Core.Domain/Orders/Order.cs).

- **Layers, dependencies inward only.** `Core.Domain` (no third-party packages, no EF, no MediatR) ← `Core.AppServices` (MediatR + FluentValidation, no EF Core) ← `Infra.*` / `Web.*`. `Web.*` never references `Infra.*` directly.
- **Aggregates own their invariants.** Rich behavior methods (`MarkAsPaid`, `Ship`, `Cancel`) returning `BusinessResult`; all setters `private set`; construction via `static Create(...)`. Handlers never assign a property.
- **One aggregate per transaction.** Cross-aggregate and external effects go through the **Outbox**, idempotently.
- **State machines are aggregate methods** — every legal transition a method, every illegal one a documented error code.
- **Value objects** for money (`long` minor units + currency — never decimal/double), IČO, DIČ, email, slug, phone, rating (bp).
- **CQRS via MediatR** — one file per use case: `Core.AppServices/Features/<Aggregate>/<UseCase>.cs` with nested `Command`/`Query`, `Response`, `Validator`, `Handler`. Handlers orchestrate only; no business `if`.
- Pipeline behaviors run automatically (`ValidationPipelineBehavior` → `UnitOfWorkPipelineBehavior`). **Handlers never call `SaveChangesAsync()`.** Controllers are one-liners over `Mediator.Send`.
- **Repository per aggregate root**, interface in `Core.Domain`. Read models via `IXxxQueries` returning `record` DTOs with `.AsNoTracking()`. No `IQueryable` leaves `Infra.*`.
- **Error codes only from `BusinessErrorMessage`.** No inline strings.
- **`Auditable`** on every transactional entity; soft delete by default.
- **`CountryConfiguration`** drives per-country variation — never branch on the country string. Provider adapters (payments/shipping/registry/email/geocoder) via keyed DI.
- **No `HttpClient` outside `Infra.Clients/<Provider>/`.**
- **Idempotent webhooks:** verify origin/signature before any DB access; look up by `provider_ref`; already-in-target-state ⇒ 200, no second transition.

### 5. Backend standards (.NET)

- Strict nullability. No `dynamic`. No `!` to silence the compiler.
- `record` DTOs/value objects, `sealed` by default, primary constructors for DI.
- `async` throughout with a threaded `CancellationToken`. No `.Result`, no `.Wait()`, no `async void`.
- `ILogger<T>` with structured templates; never interpolate into the message; never `Console.WriteLine`; **never log PII or secrets**.
- `IClock` for time, `IIdGenerator` for ids.
- No dead code, no commented-out code, no TODO without an owner (open questions → [docs/questions/open.md](./docs/questions/open.md)).

### 6. Frontend standards (Next.js 16 / React 19)

- **Server Components by default;** `'use client'` only for real interactivity, pushed as low as possible.
- **No data fetching in `useEffect`.** No business logic, no pricing math, no validation rules, no state machines.
- **No DB SDK imports.** Only data path is `lib/api-client/` (NSwag-generated, never hand-edited) via `lib/runtime/api-fetch.ts` → `Result<T, ApiError>`. Multipart uploads must pass an explicit long `timeoutMs`.
- **No global state libraries** (Redux/Zustand/Jotai). URL holds filter/pagination state.
- Zero `any`, zero unsafe `!`, named exports, stable keys (never indices), no derived state in `useState`.
- Primitives from `components/ui/` — never re-implement a button/badge/dropdown/date picker inline. Semantic color tokens only; no arbitrary Tailwind values; no layout `style={}`.
- Responsive at 375 / 768 / 1280, verified. `next/image` with explicit dimensions; heavy widgets via `next/dynamic`.
- A successful save must be visibly confirmed in-viewport.
- **All strings from `lib/i18n/cs-CZ`.** Every `BusinessErrorMessage` code gets a parallel `cs-CZ` key in the same PR. Currency `1 234 Kč`, dates `9. 5. 2026`.
- Design language (hairline buttons, no gradients, no icons in badges, contrast floor, static by default) applies **site-wide**.

### 7. Performance is a gate

- Measure and report before → after in ms. No "should be faster".
- **No N+1.** Every list endpoint paged; every WHERE/ORDER BY/JOIN column indexed (verify, don't assume); `.AsNoTracking()` on reads.
- Parallelize independent I/O with `Task.WhenAll`. Cache stable hot data behind an interface with explicit invalidation.
- External calls get timeout + bounded retry with jitter (Azure throws transient 5xx and cold starts).

### 8. Security

- `[Authorize]` or middleware on every protected endpoint; **JWT audience enforced per host**; ownership enforced by the scoped repository (cross-tenant read returns empty, not 403).
- Webhooks verify origin/signature/IP before any side effect. Cron endpoints check `CRON_SECRET`.
- Payments verified server-side against the provider — never trust redirect params.
- Uploads validated server-side by **file signature** + size; all file access proxied by the backend.
- Rate-limit auth endpoints; uniform failure messages (never reveal account existence).
- Secrets via Configuration/Key Vault; only `NEXT_PUBLIC_*` reaches the client; never commit `.env*`.
- Dev conveniences must be environment-gated and unreachable in production.
- Full rules: [agents/knowledge/security-rules.md](./agents/knowledge/security-rules.md).

### 9. Tests are part of the change

Policy: [agents/knowledge/testing.md](./agents/knowledge/testing.md).

- **New pure logic → unit test written first** (money, pricing, numbering, validators, state transitions, specs).
- **New endpoint → integration test** for happy path + auth/ownership/audience rejection, against the correct host.
- **Every returnable `BusinessErrorMessage` code → a test asserting the constant.**
- **State machines:** every legal *and* illegal transition tested. **Webhooks:** re-delivery test.
- Frontend: test logic (state mapping, error-code → i18n), not markup; a11y via `jest-axe`.
- Money assertions in `long` minor units only. Run the suites; report the counts.

### 10. Cross-stack

- Czech-only at launch; multi-country-ready architecture.
- **NSwag is the contract** — a backend contract change regenerates the client for every affected host in the **same PR**; CI verifies parity.
- One PR per ticket; cross-stack changes ship atomically.
- No mocks during the build phase — missing endpoints stay loudly broken.
- Docs updated in the same PR when architecture, env vars, or deployment change.
- Never reference files outside this repository.
