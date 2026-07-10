# Conventions & Quality Bars

The shared "what clean means here" reference, across both stacks. Every developer reads this plus
the canonical pattern catalog. The Reviewer enforces it. Where this references concrete
.NET / Next.js patterns, [`../../docs/architecture/patterns.md`](../../docs/architecture/patterns.md)
holds the code samples — this file complements it with *how we build*, not *what the shapes are*.

The canonical *architecture* description lives in
[`../../docs/architecture/`](../../docs/architecture/) (`overview.md`, `patterns.md`,
`multi-country.md`, `money.md`, `extension-points.md`) and the numbered decisions in
[`../../docs/adr/`](../../docs/adr/). Those are the source of truth for *how the system is built*;
this file is the source of truth for *how we write code in it*. When they conflict, fix one and note
it — they must not drift.

---

## Reuse the real types — do not reinvent (the prime directive)

This codebase has established base types, shared components, and idioms. **Before writing anything,
open [`../../docs/architecture/patterns.md`](../../docs/architecture/patterns.md) and the nearest
existing feature of the same kind, and reuse the exact types named there.** Inventing a parallel base
class, result type, table wrapper, HTTP call, or state container when one already exists is the
single most-rejected mistake — the Reviewer treats it as a hard fail.

- **Backend (`dotnet-backend` / `dotnet-db`):** `BusinessResult`/`BusinessResult<T>` + `AppError` +
  `BusinessErrorMessage`, `ICommand`/`ICommand<T>`/`IQuery<T>` + the matching handler interfaces,
  `DataRangeRequest`/`PagedData<T>` + `<Entity>Specification` (in `Core.Domain/Specifications/`) +
  `<Entity>Sort` (in `Core.Domain/Sorting/`), the real `MakablesApiController` + `HandleResult`,
  `BaseRepository<TEntity>`, `IUserSessionProvider`, `Money` (long minor units + currency), the
  `Auditable` base entity. No new result type, no `ErrorType` enum, no hand-rolled paging, no ad-hoc
  money struct.
- **Frontend (`frontend` / `l10n`):** the `Result<T, ApiError>` type + `apiFetch` (the single HTTP
  chokepoint in `lib/runtime/api-fetch.ts`) + the hand-written per-endpoint wrappers in
  `lib/api-client-helpers/` — route code imports **only** the helpers, never the NSwag-generated
  `lib/api-client/`. Server Components by default; UI primitives from `components/ui/`; the `cs-CZ`
  dictionary in `lib/i18n/cs-CZ.ts`. No hand-rolled `fetch`, no raw HTML form controls, no edited
  generated files, no `useEffect` data fetching.

If a genuinely new abstraction is needed, that's an **Architect** decision (an ADR), not an ad-hoc
invention inside a feature. Raise it via the ticket; don't fork the pattern silently.

## One way to do each thing

Reuse isn't only about base types; it's about doing **the same operation the same way every time**.
Before writing a paged query, a create/update/delete command, a list page, or a form, read the
canonical form for that archetype in [`../../docs/architecture/patterns.md`](../../docs/architecture/patterns.md)
and match it. Doing the same operation a *different* way than the rest of the codebase — even if it
"works" — is the spaghetti we are actively removing before PROD, and the Reviewer treats a new
deviation as a hard fail. Known existing deviations are tracked in
[`../../docs/audits/consistency-violations.md`](../../docs/audits/consistency-violations.md); the
mechanical enforcer is `node scripts/check-consistency.mjs` (see
[`../process/enforcement.md`](../process/enforcement.md)).

## Global rules

- **No hardcoded user-facing strings.** Backend → `BusinessErrorMessage` codes (dot notation,
  e.g. `order.invalid_status`). Frontend → `cs-CZ.ts` i18n keys. Every backend error key has a
  matching frontend `errors.*` key. Czech (`cs-CZ`) only at launch; the architecture is
  multi-country-ready, so the copy lives behind a key, never inline.
- **No `any` (TS) / no `dynamic` (C#).** Use real types, enums, and generics.
- **No magic numbers/strings.** Constants live in an authz/policy class, an enum, a theme token, or a
  `CountryConfiguration` row — never inline. Fee rates, VAT (basis points), commission, lead times,
  window durations, max lengths, status codes all come from a named home. **Never branch on country
  directly** (`if (countryCode == "CZ")`) — look up the `CountryConfiguration` row (ADR 0004).
- **No inline `style={}` for layout** in the frontend (Tailwind utilities + `components/ui/`
  primitives); no arbitrary Tailwind values where a token exists.
- **CancellationToken propagation** through every async IO path (backend).
- **No dead code.** Delete unreferenced methods/classes; for DB columns, never delete in code —
  flag a migration `manual_step`.
- **Comment discipline — see the dedicated section below.** The default is *no comment*; the code is
  the documentation.

## File length & method length (backend, as a smell test, not a hard cap)

- Handler file < ~200 lines; `Handle()` method < ~80 lines.
- Service file < ~400 lines; service method < ~100 lines.
- Controller file < ~250 lines (and each action is a one-liner over `Mediator.Send`).
- Validators: any length (declarative).

Over the line usually means too many responsibilities — extract into a domain service, not a bigger
handler.

## Duplication

Extract when the *same* 3+ lines appear in 3+ places **and** genuinely mean the same thing.
Premature unification is worse than duplication: two methods that look the same but must diverge
later become a silent bug when "deduplicated". Confirm intent before merging call sites.

## Comments — write almost none

**The default is no comment. The code is the documentation.** Self-documenting code — clear names,
small methods, real types — replaces the vast majority of comments. A reviewer who sees a comment on
every few lines treats it as a smell, not as diligence.

**Only comment genuinely non-obvious *critical* logic** — the *why* a reader cannot recover from the
code itself:
- a non-obvious ordering/atomicity requirement, a race the code is defending against, or a
  correctness subtlety (e.g. "this UPDATE is conditional so two webhook deliveries can't both settle
  the same payment");
- a deliberate, surprising deviation from the obvious approach, with the reason;
- a domain/legal/fiscal rule the code encodes but doesn't state (e.g. a rounding, VAT, or
  invoice-numbering sequence rule).

**Never write:**
- **WHAT comments** — `// update the order`, `// loop over line items`, `// return the result`. If a
  line needs a label to be understood, rename the variable/method instead.
- **Restating the signature** — `// takes an id and returns the order`.
- **Ticket / review / issue numbers in code** — no `// T-0123`, `// PR review #4`, `// AC2`,
  `// TODO(JIRA-x)`, `// fix from sprint 3`. These rot into dangling pointers the moment the tracker
  moves; a future reader cannot resolve them. The *reason* belongs in the comment; the *traceability*
  belongs in the commit message and the ticket, never in a source comment. (A bare `// TODO:` with a
  concrete next action and no tracker id is acceptable only as a short-lived marker.)
- **Section-divider noise** — `// ─── helpers ───`, banners, ASCII art, decorative rules.
- **Commented-out code** — delete it; git remembers.

When you fix or change a line, **delete any now-stale comment on it** rather than leaving it. A
comment that no longer matches the code is worse than none.

> Rationale: comments are unversioned-against-the-code duplication. Every comment is a second thing
> that must be kept true; most add risk (drift) without adding understanding. Spend the effort on the
> name instead.

## Harvest good patterns back into the catalog

The pattern catalog ([`../../docs/architecture/patterns.md`](../../docs/architecture/patterns.md))
and this conventions doc are **living** documents, not fixed inputs. When, while building, you
discover a genuinely better or more-consistent way to do a recurring thing — a cleaner idiom, a
reusable helper, a safer default that the rest of the codebase would benefit from — **don't keep it
to yourself in one feature:**

1. **Apply it** in the change you're making.
2. **Propose it into the catalog** so it becomes the canonical form everyone follows next time:
   - a *small* clarification/addition to an existing rule (a better example, a sharper "why", a newly
     observed footgun) → the developer edits the relevant `patterns.md` / conventions entry in the
     same change, and notes it in the ticket's `## Review` so the Reviewer sanity-checks it.
   - a *new canonical archetype* or anything that changes "the one way to do X" across the codebase →
     this is an **Architect** call (it may warrant an ADR and a canonicalization ticket to migrate
     the existing call sites). Raise it via the ticket; don't unilaterally redefine the standard.
3. If the new pattern supersedes an old one, mark the old form as a deviation in
   [`../../docs/audits/consistency-violations.md`](../../docs/audits/consistency-violations.md) (and
   file the canonicalization follow-up) so the codebase converges instead of carrying both.

The bar: a pattern earns a catalog entry when it would make **future** changes cheaper or the
codebase **more consistent**, not because it's merely a preference. Reviewer and Architect are the
guardrails against catalog bloat — the same "earns its place" test as any abstraction.

## Naming (canonical)

| Thing | Backend (C#) | Frontend (Next.js / TS) |
|---|---|---|
| Files | PascalCase | kebab-case (`order-list.tsx`); route files per Next.js (`page.tsx`, `layout.tsx`, `route.ts`) |
| Command | `CreateOrder.cs` (static class; inner record ends `Command`) | — |
| Query | `GetMyOrders.cs` (inner record ends `Query`; paged uses `IRequest<PagedData<T>>`) | — |
| DTO | `OrderListItem` / `OrderDetail` (record) | mirrored TS interface (NSwag-generated, `readonly`) |
| Repo | `IOrderRepository` / `OrderRepository` | — |
| Service | `IOrderService` / `OrderService` | function module (functions over classes for cross-feature services) |
| Controller / Component | `OrderController` (thin) | `OrderList` (Server Component by default) |
| State | — | local component state / URL state; **no Redux/Zustand/Jotai** (server state lives in the backend) |
| Export | — | named exports (except Next.js `page`/`layout`/`route` defaults) |

> **Critical naming trap (backend):** the `UnitOfWorkPipelineBehavior` commits only when the request
> is a command (its type ends `Command`). Misname a command record (e.g. `.Request`) and the row is
> **silently not saved**. Always end command record types with `Command`.

### Deployment / infra naming (ADR 0015 + ADR 0023)

Azure resource names are **immutable** — getting the seam in at clean-slate is free; retrofitting it
later forces a recreate of live resources. So from day one:

- **A stage token in every resource / RG / Key Vault name.** The Bicep orchestrator
  ([`infra/bicep/main.bicep`](../../infra/bicep/main.bicep)) derives a `makables-${envSlug}` prefix,
  so names carry the stage: `api-makables-<audience>-staging`, `web-makables-customer-staging`,
  `rg-makables-staging`, `makables-db-staging`, `kv-makables-staging`, … A name **without** a stage
  token is a finding — `staging` and `production` resources cannot coexist without it.
- **`location` / `postgresLocation` are Bicep parameters** (default `westeurope`; Postgres may differ
  when a subscription is offer-restricted in the main region). The single region is a launch
  simplification, not a hard-coded assumption — the parameter is the seam a second region would use,
  not a rename of the live ones.
- **Parameter files are per-stage** — [`infra/bicep/envs/weu.dev.bicepparam`](../../infra/bicep/envs/weu.dev.bicepparam)
  and [`infra/bicep/envs/weu.prod.bicepparam`](../../infra/bicep/envs/weu.prod.bicepparam). The
  same modules deploy twice with different SKUs (Burstable B1ms / B1 App Service on staging;
  larger on production per ADR 0023 §7) — no per-stage module forks.
- **Deploy workflows are per-stage** — [`.github/workflows/deploy-staging.yml`](../../.github/workflows/deploy-staging.yml)
  (auto on merge to master) and [`.github/workflows/deploy-production.yml`](../../.github/workflows/deploy-production.yml)
  (protected: manual approval). CI is [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml).

The litmus test: *would a config change (new SKU, a second region, a Postgres relocation) rename or
recreate a live resource, or restructure a workflow?* The answer must be **no** — it must be a new
param value or a new stage token, never a rename of the live `staging` / `production` resources.
Tenancy/country is an app-level concern (`CountryConfiguration` + the country scoping filter), never
baked into a resource name.

## Owner-only steps (agents flag, never run)

- **EF Core migrations** — flag `manual_step: ef-migration`, describe the schema delta. Agents do
  not run `dotnet ef migrations add` / `database update`.
- **NSwag client regeneration** — flag `manual_step: nswag-regen` whenever a backend DTO/endpoint or
  `BusinessErrorMessage` code changes; hold dependent frontend work until the owner confirms. NSwag
  is the contract (ADR 0022): the regen covers **every** affected host client (Web.Customer,
  Web.Maker, Web.Admin, Web.Public), then `npm run check:api` + the frontend prod build verify
  parity. See [`../process/quality-gates.md`](../process/quality-gates.md) §Gate 7 / Gate 8.
- **DB seed edits** — seeds carry ids matched to dev tooling and the migrated `CountryConfiguration`
  row; don't touch without explicit owner approval.
- **Real secrets** — never in `appsettings*.json`, **and never in Bicep, a `.bicepparam`, or a
  workflow YAML** (ADR 0015). Infra-as-code carries Key Vault secret **names** + reference URIs
  (`@Microsoft.KeyVault(SecretUri=...)`) only; the **values** are owner/CI-populated into Key Vault
  (the Postgres admin *password*, the Comgate / Packeta / ARES / SendGrid / Mapbox keys, the JWT
  signing key are supplied at deploy time via `getSecret` / a CI secret, never a literal). The
  Postgres admin *login name* and SKUs/regions are non-secret and may sit in the param file. A
  literal secret in any of these is a blocking finding. User-secrets on dev, env vars / Key Vault on
  staging + production. Only `NEXT_PUBLIC_*` is allowed in the frontend bundle.
- **Committing / pushing** — leave changes uncommitted unless the owner explicitly asks.

## Localization (cs-CZ at launch, multi-country-ready)

The single Czech dictionary is [`frontend/src/lib/i18n/cs-CZ.ts`](../../frontend/src/lib/i18n/cs-CZ.ts).
Adding a user-facing string means adding a key there, and **every `BusinessErrorMessage` code has a
parallel `errors.*` key** — that parity is `l10n`-enforced. Currency displays as `1 234 Kč` (whole
CZK, space thousands separator, haléře stripped); dates as Czech short format `9. 5. 2026`. Tone:
vykání (V form) for customers, tykání (T form) for makers — pending the confirmation in
[`../../docs/questions/open.md`](../../docs/questions/open.md); until it's answered the developer adds
a placeholder and flags it, never invents the wording silently. The architecture is multi-country /
multi-locale ready (ADR 0004), but only `cs-CZ` ships at launch — a second locale is a catalog + a
`CountryConfiguration.DefaultLanguageCode`, not a code change.

## The "production-ready, long-term" bar

This is the bar for every change, because the platform is going live and will be costly to change:
- Solve the root cause, not a symptom. No "temporary" workarounds that become permanent.
- Prefer the design that makes the *next* change cheap (preserve seams, adapters, config-driven
  variation) over the one that's shortest today. The provider adapters (Comgate for payments, Packeta
  for shipping, ARES for the company registry, SendGrid for email, Mapbox for geocoding) each live
  only behind their interface in `Infra.Clients/<Provider>/`, selected via `CountryConfiguration` —
  keep them that way. The future Stripe Connect escrow pivot is recorded in ADR 0027 and is **not
  built**; Comgate (ADR 0016) is the launch payment provider.
- If a change reveals a deeper structural problem, raise it as an audit finding / ticket rather than
  papering over it.
- "It works on the happy path" is not done. Empty, loading, error, and edge states are part of the
  work.
- **Develop test-first (TDD).** Write the failing test from the AC, make it pass minimally, refactor.
  Strict for pure logic (fee/commission math + override precedence, money/refunds/payouts, VAT,
  half-up rounding, invoice numbering, validators, state machines); test the facade/component logic
  first for UI. After-the-fact tests on pure logic are rejected. Webhook idempotency and per-host JWT
  audience boundaries are non-negotiable. Full rules:
  [`../../docs/process/quality-gates.md`](../../docs/process/quality-gates.md) §Gate 6 and
  [`../../docs/process/must-cover-tests.md`](../../docs/process/must-cover-tests.md).
