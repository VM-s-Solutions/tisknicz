---
id: T-0008
title: DI wiring — AddMakables{Infrastructure,Mediator,Auth,Cors,RateLimiting,Clients} extensions + UseMakablesPipeline
status: done
size: M
owner: dotnet-backend
created: 2026-05-22
updated: 2026-05-23
depends_on: [T-0001, T-0002, T-0003, T-0006, T-0007]
blocks: [T-0009]
user_stories: []
adrs: [0008]
phase: 1
---

# T-0008 — DI wiring

Per ADR 0008 / patterns §A.16. Each `AddMakablesXxx` extension method in `Makables.Config/Extensions/` registers a single concern. The four Web hosts (and `Makables.Functions`) call them in a flat list in `Program.cs` (wired in T-0009).

## Scope

- `AddMakablesInfrastructure(IConfiguration)` — `IClock`, `IIdGenerator`, `MakablesDbContext` (Npgsql + audit interceptor), `IUnitOfWork` alias, three numbering generators
- `AddMakablesMediator()` — MediatR assembly scan over `Core.AppServices`, the two pipeline behaviors in order (Validation → UnitOfWork), FluentValidation assembly scan
- `AddMakablesAuth(IConfiguration, string audience)` — `HttpContextAccessor`, `HttpContextUserSessionProvider` impl of `IUserSessionProvider`, JWT bearer with audience binding (signing key + issuer wiring deferred to T-0020)
- `AddMakablesCors(IConfiguration, string audience)` — per-audience CORS policy reading from `Cors:AllowedOrigins:<audience>` config; dev fallback to localhost
- `AddMakablesRateLimiting(string audience)` — per-audience fixed-window: Customer 100/min, Maker 60/min, Admin 30/min, Public 60/min
- `AddMakablesClients(IConfiguration)` — stub for future Comgate/Packeta/ARES/Resend/Mapbox adapters (each adapter ticket extends it)
- `UseMakablesPipeline(WebApplication)` — middleware order: CORS → AuthN → AuthZ → RateLimiter

## Out of scope

- Wiring these into the Web hosts' `Program.cs` (T-0009)
- AuthService impl, signing keys, refresh tokens (T-0020+)
- Any concrete external adapter (T-0028, T-0032, T-0065, T-0070, T-0031)
- Migrations runner (T-0010 brings the initial migration)

## Acceptance criteria

- **AC-1** Build clean.
- **AC-2** Existing 82 tests still pass.
- **AC-3** Makables.Config csproj now references the four Infra projects + Npgsql.EntityFrameworkCore.PostgreSQL + Microsoft.AspNetCore.Authentication.JwtBearer.
- **AC-4** Each `AddMakablesXxx` extension is single-purpose, configuration-driven where appropriate, and follows the patterns §A.16 sketch.

## Status log

- 2026-05-23 done. 82 tests still pass (no new tests; DI wiring exercised in T-0009 integration tests).
- Added `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.0 to central package versions.
