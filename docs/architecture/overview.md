# Architecture overview

> Living index. Points to ADRs and other architecture docs. Decisions are made in ADRs; this file orients newcomers.

## Stack

| Layer | Technology |
|---|---|
| Backend | .NET 10, ASP.NET Core, MediatR, FluentValidation, EF Core 10 |
| Database | PostgreSQL 16 (Azure Flexible Server) |
| Backend hosts | 4 × ASP.NET Core (Customer / Maker / Admin / Public APIs) |
| Background jobs | Azure Functions v4 on Docker |
| File storage | Azure Blob Storage |
| Auth | Custom (Argon2id + JWT + refresh tokens), `IAuthService` interface |
| Frontend | Next.js 16 (App Router), React 19, Tailwind 4 |
| API contract | OpenAPI, NSwag-generated TypeScript client |
| Cloud | Azure (West Europe) |
| Observability | Application Insights, OpenTelemetry via .NET Aspire defaults |

The pivot from Supabase + Next.js is recorded in [ADR 0007](../adr/0007-stack-pivot-dotnet-backend.md).

## System shape

```
                        ┌──────────────────────┐
                        │  Azure DNS           │
                        │  makables.cz         │
                        └──────────┬───────────┘
                                   │
        ┌──────────────────────────┼──────────────────────────────────┐
        │                          │                                  │
┌───────▼─────────┐  ┌─────────────▼──────────┐  ┌─────────────────▼──────────┐
│ Frontend        │  │ Customer API           │  │ Maker / Admin / Public APIs │
│ Next.js 16 SSR  │  │ ASP.NET Core (.NET 10) │  │ ASP.NET Core (.NET 10)      │
│ App Service     │  │ App Service            │  │ App Services                │
│ makables.cz     │  │ api-customer.makables.cz│ │ api-{maker,admin}.makables.cz │
│                 │  │                        │  │ api.makables.cz             │
└───────┬─────────┘  └─────────────┬──────────┘  └─────────────────┬──────────┘
        │                          │                                  │
        └──────────────────────────┴──────────────────────────────────┘
                                   │
                                   ▼
                        ┌──────────────────────┐
                        │ Postgres Flexible    │
                        │ Server 16            │
                        └──────────┬───────────┘
                                   │
              ┌────────────────────┼──────────────────────┐
              │                    │                      │
     ┌────────▼────────┐  ┌────────▼────────┐  ┌─────────▼─────────┐
     │ Azure Functions │  │ Azure Blob      │  │ Azure Key Vault   │
     │ (Docker)        │  │ Storage         │  │                   │
     └─────────────────┘  └─────────────────┘  └───────────────────┘
```

## Backend layers

```
Web.Customer / Web.Maker / Web.Admin / Web.Public / Functions
        │
        ▼
   Makables.Config           ← shared startup (auth, CORS, MediatR, rate limit, middleware)
        │
        ├──► Makables.Core.AppServices    ← MediatR handlers, validators, services
        │           │
        │           ▼
        │     Makables.Core.Domain        ← entities, value objects, repo interfaces, BusinessResult, Money
        │
        ├──► Makables.Infra.Database               ← EF Core DbContext, migrations, repositories
        ├──► Makables.Infra.Clients                ← Comgate, Packeta, ARES, Resend, Mapbox HttpClients
        ├──► Makables.Infra.Azure.Storage.Blobs    ← Blob wrapper
        └──► Makables.Infra.Common                 ← shared infra utilities
```

- `Core.Domain` references **no** third-party packages.
- `Core.AppServices` references `Core.Domain` + MediatR + FluentValidation.
- `Infra.*` references `Core.Domain` (to implement interfaces) + the relevant SDKs.
- `Web.*` references `Config` + `Core.AppServices`, never `Infra.*` directly.

See [ADR 0001](../adr/0001-layering.md) and [patterns.md §A.1](./patterns.md#a1-layered-architecture).

## Frontend layers

```
app/(public|auth|customer|maker|admin)/  ← Server Components, pages
        │
        ▼
   components/                            ← ui/, layout/, forms/, catalog/, dashboard/, shared/
        │
        ▼
   lib/api-client/                        ← NSwag-generated TypeScript client (DO NOT EDIT)
        │
        ▼
   lib/runtime/api-fetch                  ← attaches auth, parses errors, returns Result<T>
        │
        ▼
   .NET backend (HTTPS)
```

The frontend has **no business logic** and **no database access**. Pages render data fetched through the API client.

See [patterns.md §B](./patterns.md#b--frontend-patterns-nextjs).

## Key principles

- **Backend is the system of record.** Money math, state transitions, validation, invoice generation, payouts — all in .NET.
- **Frontend is presentation only.** Format, display, submit. No business decisions.
- **Adapter pattern at every external boundary.** Domain code doesn't know whether the payment provider is Comgate or Stripe.
- **Money is `long` minor units.** Currency-aware. See [ADR 0003](../adr/0003-money-and-currency.md).
- **Country is a first-class concept** on every transactional entity. See [ADR 0004](../adr/0004-country-configuration.md).
- **Multi-country variation lives in `CountryConfiguration`,** not in code branches.
- **Per-audience APIs.** Customer, Maker, Admin, Public hosts have different auth policies, CORS, rate limits.
- **Custom auth, no vendor lock.** `IAuthService` wraps the implementation; refresh tokens rotated; JWT audience enforced per host.
- **Idempotent webhooks.** Comgate/Packeta callers may retry; we handle re-entry safely.
- **Numbering is namespaced per country.** Order numbers, invoice numbers, payout batches.

## Non-functional requirements

To be filled in during discovery Batch 5.

- Performance budget: TBD
- Scale assumptions: TBD
- Availability target: TBD
- Browser/device support: TBD
- Accessibility: TBD
- Observability detail: TBD

## Index of architecture docs

- [`patterns.md`](./patterns.md) — every pattern with code samples (dual-stack)
- [`extension-points.md`](./extension-points.md) — seams where we expect variation
- [`money.md`](./money.md) — money handling specifics
- [`multi-country.md`](./multi-country.md) — what changes when we add country #2
- ADRs: [`../adr/`](../adr/)
