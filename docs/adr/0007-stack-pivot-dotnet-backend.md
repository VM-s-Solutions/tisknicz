---
id: 0007
title: Stack pivot — .NET backend + Next.js frontend; drop Supabase
status: accepted
date: 2026-05-21
deciders: [Architect, user]
supersedes_in_part: [0001, 0004, 0005, 0006]
---

# 0007 — Stack pivot: .NET backend + Next.js frontend; drop Supabase

## Context

ADRs 0001–0006 were drafted assuming the stack documented in `TISKNI_MVP_SPEC.md`: a Next.js 16 application using Supabase for database, auth, and storage. The patterns we adopted (CQRS, `Result`, pipeline middleware, `CountryConfiguration`, enforcement modes, adapter pattern, idempotent webhooks) were translated from the user's existing Clean Architecture .NET experience into TypeScript on top of Supabase.

After Batch 2, the user made a deliberate pivot:

> "We have to completely remove Supabase and we will build our own .NET backend, so that it can be easily expanded in the future. All proposals have to be projected in the way that it's a long win for us in the future. Once we're live, it's difficult to make changes in a free way because the product is running."

The motivation: post-launch flexibility. The patterns the user trusts (CQRS, MediatR, pipeline behaviors, FluentValidation, EF Core, `BusinessResult`, per-audience APIs, custom auth) are most natural in .NET, where the user has production experience and a working template (`dotnet-template`, `Cleansia`). Continuing to translate them into TypeScript on top of a managed BaaS would have introduced friction in two directions: less idiomatic .NET patterns AND a vendor coupling that is expensive to undo once orders, invoices, and payouts are flowing.

## Decision

Pivot the architecture to a **dual-stack monorepo**:

- `/backend/` — a .NET 10 Clean Architecture solution following the user's `dotnet-template` + Cleansia conventions, with multiple per-audience API hosts, EF Core + Postgres, custom JWT auth, Azure Blob Storage, and Azure Functions for background jobs.
- `/frontend/` — the existing Next.js 16 App Router application, repurposed as a pure presentation layer that calls the .NET API through an NSwag-generated TypeScript client. No server-side database access from Next.js.
- `/docs/` and `/.claude/agents/` — shared, governing both codebases.

Supabase is removed entirely. No DB, no Auth, no Storage, no SDK. All Supabase artifacts in the repository are deleted as part of this pivot.

### Stack summary

| Concern | Pre-pivot (rejected) | Post-pivot (accepted) |
|---|---|---|
| Database | Supabase Postgres | Self-managed Postgres 16 on Azure Flexible Server |
| ORM / data access | `@supabase/supabase-js` queries from Next.js | EF Core 10 in .NET; repositories in `Infra.Database` |
| Auth | Supabase Auth (managed) | Custom .NET auth: user table, Argon2 password hashing, JWT issuance, refresh tokens; `IAuthService` interface |
| File storage | Supabase Storage | Azure Blob Storage; accessed only through the .NET backend (no direct client → storage links) |
| Realtime | Supabase Realtime | Out of scope for MVP. Polling is acceptable for order status; if realtime is needed later, SignalR in .NET. |
| API contract | Implicit (Next.js calls Supabase directly) | Explicit: OpenAPI spec emitted by .NET; NSwag generates `frontend/src/lib/api-client/` |
| Auth transport | Supabase session cookie | JWT in `Authorization: Bearer`; refresh token in HttpOnly cookie |
| Background jobs | Vercel Cron | Azure Functions (Docker), Cleansia-style; timer-triggered + queue-triggered |
| Cloud | Vercel + Supabase | Azure (App Service, Postgres Flexible Server, Blob Storage, Functions, Key Vault, Application Insights) |
| RLS | Postgres RLS (Supabase) | EF Core global query filters scoped by country + ownership; defense in depth at the API auth layer |

### Per-audience API hosts (Cleansia parity)

The .NET solution exposes four separate API hosts on different ports, each with its own auth policy, CORS, rate limit, and audience:

| Host project | Audience | URL (prod) |
|---|---|---|
| `Makables.Web.Customer` | Authenticated customers | api-customer.makables.cz |
| `Makables.Web.Maker` | Authenticated makers | api-maker.makables.cz |
| `Makables.Web.Admin` | Admins | api-admin.makables.cz |
| `Makables.Web.Public` | Public (catalog read, ARES proxy, webhooks, cron) | api.makables.cz |

All four share `Makables.Core.Domain`, `Makables.Core.AppServices`, `Makables.Config`, `Makables.Infra.*`.

### Monorepo layout

```
makables/
├── backend/
│   └── src/
│       ├── Makables.Api.sln
│       ├── Makables.Core.Domain/          # entities, value objects, repo interfaces, BusinessResult, AppError
│       ├── Makables.Core.AppServices/     # MediatR handlers (Features/<Entity>/<UseCase>.cs), validators, services
│       ├── Makables.Config/               # shared startup, middleware, auth, MediatR + pipeline behaviors
│       ├── Makables.Infra.Database/       # EF Core DbContext, migrations, repositories
│       ├── Makables.Infra.Common/         # shared infra utilities
│       ├── Makables.Infra.Clients/        # Comgate, Packeta, ARES, Resend, Mapbox clients
│       ├── Makables.Infra.Azure.Storage.Blobs/
│       ├── Makables.Web.Customer/         # Customer API host
│       ├── Makables.Web.Maker/            # Maker API host
│       ├── Makables.Web.Admin/            # Admin API host
│       ├── Makables.Web.Public/           # Public API host (catalog, webhooks, cron)
│       ├── Makables.Functions/            # Background jobs (Docker, Azure Functions v4)
│       ├── Makables.Tests/                # unit tests
│       ├── Makables.IntegrationTests/     # integration tests
│       └── Makables.TestUtilities/
├── frontend/
│   ├── src/
│   │   ├── app/
│   │   │   ├── (public)/                  # marketing, catalog, product detail, jak-to-funguje, vop, gdpr
│   │   │   ├── (auth)/                    # login, register, password reset, magic link
│   │   │   ├── (customer)/                # customer dashboard, order placement, order tracking
│   │   │   ├── (maker)/                   # maker dashboard, products, orders, payouts
│   │   │   └── (admin)/                   # admin dashboard, payouts, makers, invoices
│   │   ├── components/                    # ui/, layout/, forms/, catalog/, dashboard/, shared/
│   │   └── lib/
│   │       ├── api-client/                # NSwag-generated TypeScript client (DO NOT EDIT)
│   │       ├── auth/                      # token storage, refresh logic
│   │       ├── runtime/                   # Result<T> for client-side, error handling, formatters
│   │       └── utils/                     # pure helpers (formatting, validation mirrors)
│   ├── public/
│   ├── package.json
│   └── tsconfig.json
├── docs/                                  # process, ADRs, user stories, tickets, architecture
├── .claude/agents/                        # sub-agent charters
├── deploy/                                # Bicep / Terraform / pipeline definitions
└── README.md
```

## Alternatives considered

- **Stay on Supabase + Next.js with the planned TypeScript translation of CQRS** — rejected by user. Argument: post-launch flexibility outweighs short-term velocity. Once orders flow, migrating away from Supabase Auth (user records), Postgres (managed), and Storage (URLs in customer emails) is multi-week work. Doing it now while the DB is empty is essentially free.
- **Hybrid: .NET backend + Supabase Postgres-as-database only** — rejected. Would keep us on Supabase's pricing and operational quirks for the database while gaining little. Azure Postgres Flexible Server gives equivalent capability with the same operator (Azure) as the rest of the stack.
- **Keep current Next.js code as the API layer, swap Supabase for Prisma + custom Postgres** — rejected. Doesn't solve the underlying problem: the patterns the user wants are most idiomatic in .NET, not Next.js Route Handlers.
- **Single .NET host (no per-audience split)** — rejected. Per-audience hosts are a Cleansia pattern the user has validated in production. Splitting now is much cheaper than splitting later (different auth policies, CORS, rate limits accumulate).

## Consequences

### Positive

- **Long-term flexibility.** Postgres is portable. JWT is portable. Blob storage adapters are portable. The Cleansia-pattern adapter layer means any third party (Comgate, Packeta, Resend, Mapbox, ARES) can be swapped by adding an `Infra.Clients` implementation and a `CountryConfiguration` row update.
- **Idiomatic patterns.** The user is fluent in CQRS, MediatR, FluentValidation, EF Core, `BusinessResult`. The team's velocity will be higher when the patterns match the language.
- **Owned auth.** No vendor cost per MAU. No "Supabase changed their pricing" risk. GDPR delete is a SQL statement, not a vendor API call. Password reset flows are ours to design.
- **Clearer test boundary.** The .NET unit-testable layers (`Core.AppServices` handlers) become the system of record for business logic. Frontend tests focus on UI. The API contract is the seam.
- **Per-audience APIs map to real auth, CORS, rate-limit differences.** Admin API can be locked down to office IPs in production; customer API stays open. Public API gets aggressive rate limits on ARES proxy.

### Negative

- **More moving parts to deploy.** Four .NET hosts + Functions + Postgres + Blob + Frontend = 7+ deployable units. Mitigation: Bicep or Terraform encodes the topology; one `azd up` (or equivalent) brings it all up.
- **Frontend pages currently break.** All `dashboard/`, `katalog/`, `produkt/`, `pro-makery` registration form, and `auth/` pages reach into `src/lib/supabase/*` today. They stop working the moment Supabase is removed. **Accepted by user (option 1):** no mock layer. Pages stay broken until the corresponding .NET API endpoint exists. This makes it impossible to silently skip a missing endpoint.
- **Auth is real work.** Argon2 password hashing, email confirmation, magic link issuance, refresh-token rotation, JWT validation, lockout policies, GDPR-compliant deletion — all need careful implementation and testing. ~2–3 weeks of focused work, much of it boilerplate that the user has prior art for.
- **Two deploy targets.** Frontend on App Service (Node.js) + four .NET hosts on App Service (Linux). Routing across `api.makables.cz`, `api-customer.makables.cz`, etc. requires DNS + cert management.
- **Realtime is gone.** Supabase Realtime gave us live order status without code. Replacement is polling for MVP; SignalR if/when needed.

### Neutral

- **NSwag adds a build step.** Whenever the backend's OpenAPI spec changes, regenerate the TypeScript client. The frontend's CI verifies the generated client matches the backend's spec on every PR.
- **EF Core migrations** replace Supabase SQL migrations. The user is familiar with EF migrations.
- **Application Insights** replaces whatever observability Supabase provided (which was minimal).

## ADRs superseded in part by this one

| ADR | Previous decision | Status under pivot |
|---|---|---|
| 0001 — Four-layer architecture | `src/lib/{domain,features,infra,runtime}` in Next.js | **Validated and strengthened.** Backend is now a real Clean Architecture .NET solution with `Core.Domain` / `Core.AppServices` / `Infra.*`. Frontend remains layered but is much thinner (presentation + API client). A new ADR (0008) refines the backend layering specifically. |
| 0002 — Command/Query, Result, pipeline middleware | TypeScript implementation | **Validated.** Now lives in .NET as `ICommand` / `IQuery` / `BusinessResult` / MediatR pipeline behaviors (Cleansia pattern). The frontend keeps a small TypeScript `Result<T>` for API response handling, but the system of record for command/query patterns is the backend. |
| 0003 — Money as integer minor units | TypeScript `Money` value object | **Validated.** Now a .NET `Money` record with `long AmountMinor` and `string Currency`. Same rules, same VAT-as-basis-points, same CZ display rule. Frontend mirrors the type via NSwag-generated DTOs. |
| 0004 — CountryConfiguration | Postgres table read from Next.js | **Validated.** Now an EF Core entity in `Core.Domain.Configuration`. Same shape (currency, language, VAT, provider defaults, invoicing mode). Admin UI calls the Admin API to read/edit. |
| 0005 — Per-audience route groups | Next.js route groups for customer/maker/admin | **Refined and strengthened.** Frontend route groups remain. Backend additionally splits into four API hosts (Customer / Maker / Admin / Public). The two levels of separation (frontend route groups + backend hosts) reinforce each other. |
| 0006 — Lightweight DI container (tsyringe) | Per-request scope on Next.js | **Superseded entirely.** No DI on the frontend; the frontend has no business logic to wire. Backend uses .NET's built-in `Microsoft.Extensions.DependencyInjection`. See ADR 0008. |

## Compliance / verification

- **No Supabase remnants:** grep across the repo returns zero hits for `@supabase`, `supabase.from`, `supabase.auth`, `supabase.storage`. CI fails if any return.
- **No direct DB access from frontend:** ESLint rule blocks imports of `pg`, `prisma`, `@supabase/*`, or any database SDK in `/frontend/src/`. The only data path is `frontend/src/lib/api-client/`.
- **Generated client is generated:** `frontend/src/lib/api-client/` is regenerated by NSwag on every API change. Generated files carry a banner comment and are excluded from manual edits via a CODEOWNERS or pre-commit rule.
- **Backend layers respect dependency direction:** `Core.Domain` references no `Microsoft.EntityFrameworkCore` package or any third-party SDK. CI fails if it does.
- **Per-audience hosts share startup:** `Makables.Config` provides shared `AddMakablesXxx()` extension methods used by all four `Web.*` projects. No duplicated startup logic.
- **Single source of truth for auth:** every request to a `Web.*` host goes through the same JWT validation middleware from `Makables.Config`. No host implements its own.

## Open follow-ups

- **Auth strategy ADR** — to be written in Batch 3: password policy, refresh-token lifetime, magic-link expiration, OAuth roadmap (Google in v1.1).
- **EF Core query filter strategy** — to be written in Batch 3: how `country_code` and ownership scoping are enforced at the data layer (replaces RLS).
- **API client generation pipeline** — to be written in Batch 4: when NSwag runs, how the generated diff is reviewed, how breaking changes are detected.
- **Deployment topology** — Bicep templates and the pipeline definitions; SecOps + Architect collaboration in a later batch.

## Related

- Patterns: `docs/architecture/patterns.md` (to be updated in this pivot)
- ADR 0008 — .NET dependency injection (supersedes 0006)
- Cleansia precedent for: per-audience APIs, CQRS handlers, pipeline behaviors, `CountryConfiguration`, custom auth, Azure deployment topology
