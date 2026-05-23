---
id: T-0009
title: Four Web hosts wired through AddMakables*; integration smoke tests
status: done
size: M
owner: dotnet-backend
created: 2026-05-23
updated: 2026-05-23
depends_on: [T-0001, T-0008]
blocks: [T-0010, T-0012, T-0013]
adrs: [0005, 0008]
phase: 1
---

# T-0009 — Web host wiring + integration smoke tests

## Scope
- Per-host `Program.cs` for `Web.{Customer,Maker,Admin,Public}` rewired through the `AddMakables*` extensions + `UseMakablesPipeline`. Each Program declares a `public partial class Program` in its host namespace so `WebApplicationFactory<TProgram>` can target it.
- Per-host `appsettings.json` + `appsettings.Development.json` with empty production `ConnectionStrings:Postgres` and dev fallback pointing at localhost; `Cors:AllowedOrigins:<audience>` arrays.
- `Makables.Functions/Program.cs` wired through `AddMakablesInfrastructure` + `AddMakablesMediator` + `AddMakablesClients` (no auth/CORS/rate-limit — Functions trigger from queue/timer/HTTP-with-key).
- `Makables.IntegrationTests/HostStartup/WebHostStartupTests.cs` — 8 tests: 4 hosts × (smoke / DI resolution).
- Swashbuckle.AspNetCore removed from `Makables.Config` due to incompatibility with .NET 10's `Microsoft.AspNetCore.OpenApi 10.0.0` (depends on Microsoft.OpenApi 2.x). OpenAPI generation goes through `Microsoft.AspNetCore.OpenApi` natively (T-0012/T-0013 will wire `/openapi/v1.json`).

## Acceptance criteria
- **AC-1** Build clean (0 warnings, 0 errors).
- **AC-2** Existing unit tests still pass (82) + 8 new integration tests pass.
- **AC-3** Each host responds to `GET /` with `Makables {HostName} API — alive.`.
- **AC-4** Each host resolves `MakablesDbContext`, `IClock`, `IIdGenerator`, three numbering generators, `ISender` from its DI container.
- **AC-5** Functions Program.cs wires through the same `AddMakables*` extensions (no auth/CORS/rate-limit).

## Status log
- 2026-05-23 done. 90 tests passing (82 unit + 8 integration).
