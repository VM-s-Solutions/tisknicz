# Makables — backend

.NET 10, ASP.NET Core, MediatR, FluentValidation, EF Core 10, PostgreSQL 16. Clean Architecture with four per-audience API hosts (Customer, Maker, Admin, Public) plus Azure Functions for background jobs.

## Status

Not yet scaffolded. The pivot from Supabase + Next.js is recorded in [`../docs/adr/0007-stack-pivot-dotnet-backend.md`](../docs/adr/0007-stack-pivot-dotnet-backend.md). The solution will be scaffolded in Phase 0.6 after the remaining domain and integration ADRs (Batches 3 and 4) are accepted.

## Expected solution layout

See [`../docs/architecture/patterns.md`](../docs/architecture/patterns.md) Section A.1 and the [`dotnet-backend` charter](../.claude/agents/dotnet-backend.md) for the full solution layout: `Makables.Core.Domain`, `Makables.Core.AppServices`, `Makables.Config`, `Makables.Infra.*`, `Makables.Web.{Customer,Maker,Admin,Public}`, `Makables.Functions`, plus test projects.

## Patterns

Read [`../docs/architecture/patterns.md`](../docs/architecture/patterns.md) Section A for every backend pattern with C# code samples: layering, CQRS, `BusinessResult<T>`, pipeline behaviors, `CountryConfiguration`, enforcement modes, retry policy, provider adapters (keyed services), per-audience hosts, custom auth, money, EF Core query filters, idempotent webhooks, NSwag.
