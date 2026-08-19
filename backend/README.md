# Makables — backend

.NET 10, ASP.NET Core, MediatR, FluentValidation, EF Core 10, PostgreSQL 16. Clean Architecture with four per-audience API hosts (Customer, Maker, Admin, Public) plus Azure Functions for background jobs.

**This is the system of record.** Money, state transitions, invariants, validation, invoicing, payouts and every third-party integration live here. The frontend never decides anything.

Solution: [`src/Makables.Api.slnx`](./src/Makables.Api.slnx) — 17 projects.

## Layers

Dependencies point inward. Only.

| Project | Contains | May not reference |
|---|---|---|
| `Makables.Core.Domain` | aggregates, value objects, domain services, repository **interfaces**, policies, specifications | any third-party package — no EF Core, no MediatR |
| `Makables.Core.AppServices` | use cases (MediatR handlers), validators, DTOs, mappers | `Microsoft.EntityFrameworkCore` |
| `Makables.Infra.*` | implementations of `Core.Domain` interfaces: EF Core, HTTP clients, blob storage, PDF | — |
| `Makables.Config` | shared host wiring: auth, DI extensions, middleware, observability | — |
| `Makables.Web.*` | thin hosts; controllers are one-liners over `Mediator.Send` | `Infra.*` directly |

`Makables.Functions` hosts the eleven Azure Functions v4 workers: outbox drain, email send, invoice + label generation, auto-deliver, shipment-status sync, expired-payment cancellation, dispute auto-escalation, weekly payout batch, registry-cache eviction, data-retention cleanup. `Makables.Tools.Seeder` builds the local CZ dataset.

Aggregate roots: `Order`, `Maker`, `Product`, `User`, `Invoice`, `PayoutBatch`, `Dispute`, `Category`. [`Orders/Order.cs`](./src/Makables.Core.Domain/Orders/Order.cs) is the reference implementation — read it before modelling anything new.

## Hosts

| Host | Local URL | Audience |
|---|---|---|
| `Makables.Web.Customer` | http://localhost:5001 | customer JWT audience |
| `Makables.Web.Maker` | http://localhost:5002 | maker JWT audience |
| `Makables.Web.Admin` | http://localhost:5003 | admin JWT audience |
| `Makables.Web.Public` | http://localhost:5104 | anonymous + provider webhooks |

Each exposes `/openapi/v1.json`, which NSwag turns into the frontend's TypeScript client (ADR 0022). A JWT minted for one audience is rejected by the others — that rejection is covered by integration tests, not by review.

## Running it

```powershell
pwsh ../scripts/run-dev.ps1                                   # all four hosts
pwsh ../scripts/run-dev.ps1 -Host Customer                    # one host
dotnet run --project src/Makables.Tools.Seeder -- --migrate   # migrate + seed
```

Needs Postgres 16 on `localhost:5432` (`makables_dev`, `postgres`/`postgres`). Azurite only matters for blob upload and outbox queue dispatch. Migrations are **not** applied automatically at host start — run the seeder with `--migrate`, or `dotnet ef database update` with an explicit connection string. Full procedure and the known traps: [`../docs/deployment/local-dev.md`](../docs/deployment/local-dev.md).

## Tests

```bash
dotnet test src/Makables.Api.slnx                        # everything
dotnet test src/Makables.Tests                           # unit — no infrastructure needed
dotnet test src/Makables.IntegrationTests                # WebApplicationFactory + Testcontainers Postgres
```

`Makables.IntegrationTests` spins Postgres via Testcontainers, or reuses an external instance when Docker is unavailable. The must-cover list — every error code, every legal and illegal state transition, webhook re-delivery, audience rejection — is in [`../agents/knowledge/testing.md`](../agents/knowledge/testing.md).

## Patterns

[`../docs/architecture/patterns.md`](../docs/architecture/patterns.md) Section A is the source of truth for every backend shape: layering, CQRS, `BusinessResult<T>`, pipeline behaviors, `CountryConfiguration`, retry policy, provider adapters via keyed services, per-audience hosts, custom auth, money as `long` minor units, EF Core query filters, idempotent webhooks, NSwag. The decisions behind them are in [`../docs/adr/`](../docs/adr/).
