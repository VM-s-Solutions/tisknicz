---
id: T-0001
title: Scaffold .NET solution skeleton (15 projects)
status: done
size: M
owner: dotnet-backend
created: 2026-05-22
updated: 2026-05-22
depends_on: []
blocks: [T-0002, T-0003, T-0004, T-0005, T-0006, T-0007, T-0008, T-0009]
user_stories: []
adrs: [0001, 0007, 0008]
phase: 1
---

# T-0001 — Scaffold .NET solution skeleton

## Context

First ticket of the build phase. Stand up the .NET 10 solution under `/backend/src/` with the project structure prescribed by [ADR 0001](../adr/0001-layering.md) (four-layer architecture) and [ADR 0007](../adr/0007-stack-pivot-dotnet-backend.md) (per-audience hosts), so subsequent tickets have somewhere to add code.

No business logic in this ticket — just the empty projects, csprojs, package references, namespaces, project-to-project references, and a working `dotnet build` of the whole solution.

## Scope

Create the following projects under `/backend/src/`:

**Solution file:** `Makables.Api.sln`

**Core (no third-party packages allowed except those listed):**
- `Makables.Core.Domain` — class library, .NET 10, no packages
- `Makables.Core.AppServices` — class library; references `Core.Domain`; packages: `MediatR`, `FluentValidation`

**Config / shared startup:**
- `Makables.Config` — class library; references `Core.AppServices`; packages: `Microsoft.AspNetCore.App` (FrameworkReference), `Microsoft.Extensions.Hosting`, `Asp.Versioning.Mvc.ApiExplorer`, `Swashbuckle.AspNetCore`

**Infrastructure:**
- `Makables.Infra.Common` — references `Core.Domain`; minimal packages (`Microsoft.Extensions.Configuration.Abstractions`)
- `Makables.Infra.Database` — references `Core.Domain`; packages: `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`
- `Makables.Infra.Clients` — references `Core.Domain`; packages: `Microsoft.Extensions.Http`
- `Makables.Infra.Azure.Storage.Blobs` — references `Core.Domain`; package: `Azure.Storage.Blobs`

**API hosts (per ADR 0005):**
- `Makables.Web.Customer` — ASP.NET Core Web API; references `Config`, `Core.AppServices`, all `Infra.*`
- `Makables.Web.Maker` — same shape
- `Makables.Web.Admin` — same shape
- `Makables.Web.Public` — same shape

**Background jobs:**
- `Makables.Functions` — Azure Functions Isolated Worker (.NET 10); references `Config`, `Core.AppServices`, all `Infra.*`; packages: `Microsoft.Azure.Functions.Worker`, `Microsoft.Azure.Functions.Worker.Sdk`

**Tests:**
- `Makables.Tests` — unit tests; packages: `xunit`, `FluentAssertions`, `NSubstitute`, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`
- `Makables.IntegrationTests` — integration tests; packages: `xunit`, `FluentAssertions`, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql`
- `Makables.TestUtilities` — shared test helpers; referenced by both test projects

**Configuration files:**
- `/backend/src/global.json` pinning the SDK to 10.0.x
- `/backend/src/Directory.Packages.props` with `ManagePackageVersionsCentrally=true` listing every package version exactly once
- `/backend/src/Directory.Build.props` setting `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`

## Out of scope

- Entity types (T-0002+)
- DbContext (T-0002)
- MediatR pipeline behaviors (T-0003)
- BusinessResult / Error / ICommand / IQuery (T-0004)
- Anything else from Phase 1 — separate tickets

## Acceptance criteria

- **AC-1** `dotnet build backend/src/Makables.Api.sln` succeeds with zero warnings (`TreatWarningsAsErrors`).
- **AC-2** Every project's namespace matches its folder path (`Makables.Core.Domain`, `Makables.Web.Customer`, etc.).
- **AC-3** Project references point inward only per ADR 0001: `Core.Domain` references nothing; `Core.AppServices` references only `Core.Domain` (plus MediatR + FluentValidation); `Infra.*` references `Core.Domain`; `Web.*` references `Config` + `Core.AppServices` + `Infra.*` (NOT `Core.Domain` directly — go through `Core.AppServices`).
- **AC-4** `Directory.Packages.props` centralizes every package version. No `Version` attribute on `<PackageReference>` in individual csprojs.
- **AC-5** `global.json` pins the .NET SDK to 10.0.x.
- **AC-6** Each `Web.*` project has a minimal `Program.cs` that calls `WebApplication.CreateBuilder(args)` and `app.Run()` — enough to start, no controllers yet.
- **AC-7** `Makables.Functions` has a stub program entry suitable for the Functions isolated worker. No actual functions yet.
- **AC-8** Test projects run `dotnet test` cleanly (zero tests, zero failures).

## Technical notes

- Use `dotnet new classlib`, `dotnet new webapi`, `dotnet new xunit` etc. to scaffold.
- Resist the urge to add `Microsoft.AspNetCore.Mvc.Versioning` here — Asp.Versioning.Mvc is added in T-0012.
- Don't add EF Core migrations runner here — T-0002 owns it.
- Each `Web.*` `Program.cs` is single-line minimum; we'll fill it in T-0008/T-0009.

## Files touched (expected)

- `backend/src/Makables.Api.sln`
- `backend/src/global.json`
- `backend/src/Directory.Build.props`
- `backend/src/Directory.Packages.props`
- `backend/src/Makables.Core.Domain/Makables.Core.Domain.csproj`
- `backend/src/Makables.Core.Domain/AssemblyReference.cs`
- `backend/src/Makables.Core.AppServices/Makables.Core.AppServices.csproj`
- `backend/src/Makables.Core.AppServices/AssemblyReference.cs`
- `backend/src/Makables.Config/Makables.Config.csproj`
- `backend/src/Makables.Config/AssemblyReference.cs`
- `backend/src/Makables.Infra.Common/Makables.Infra.Common.csproj`
- `backend/src/Makables.Infra.Database/Makables.Infra.Database.csproj`
- `backend/src/Makables.Infra.Clients/Makables.Infra.Clients.csproj`
- `backend/src/Makables.Infra.Azure.Storage.Blobs/Makables.Infra.Azure.Storage.Blobs.csproj`
- `backend/src/Makables.Web.Customer/{Makables.Web.Customer.csproj, Program.cs, appsettings.json, Properties/launchSettings.json}`
- `backend/src/Makables.Web.Maker/...`
- `backend/src/Makables.Web.Admin/...`
- `backend/src/Makables.Web.Public/...`
- `backend/src/Makables.Functions/{Makables.Functions.csproj, Program.cs, host.json, local.settings.json}`
- `backend/src/Makables.Tests/Makables.Tests.csproj`
- `backend/src/Makables.IntegrationTests/Makables.IntegrationTests.csproj`
- `backend/src/Makables.TestUtilities/Makables.TestUtilities.csproj`

## Test plan reference

No test plan ticket. `dotnet build` clean + `dotnet test` clean from AC-1 and AC-8 are the verification.

## Status log

- 2026-05-22 `draft → ready → in_progress` by PM, owner `dotnet-backend` (started immediately by orchestrator)
- 2026-05-22 `in_progress → done`. All ACs satisfied:
  - **AC-1** Build clean: 0 warnings, 0 errors, `TreatWarningsAsErrors=true` globally
  - **AC-2** Namespaces match folder paths; verified via three `AssemblyReference.cs` marker files
  - **AC-3** Inward project references only; verified by inspection of every csproj
  - **AC-4** Central package management; zero per-csproj `Version=` attributes
  - **AC-5** `global.json` pins SDK 10.0.201
  - **AC-6** Smoke test: `Makables.Web.Customer` started on port 18001, root endpoint returned `"Makables Customer API — alive."`
  - **AC-7** `Makables.Functions/Program.cs` is a minimal `FunctionsApplication.CreateBuilder(...).Build().Run()` (App Insights wiring deferred to T-0014)
  - **AC-8** `dotnet test` exit 0; both test projects load (template stubs deleted; no production tests yet)

  Notes on deviations / discoveries during execution:
  - .NET 10 SDK creates `.slnx` (XML solution) by default rather than `.sln`. Used as-is.
  - Solution shape ended up with 15 projects (not 13) because `Makables.TestUtilities` was always intended but the spec text in this ticket missed counting it; and the cmd already names 13 source projects + 3 test projects = 16; effective code projects: 12 + 3 test = 15.
  - Added `System.Security.Cryptography.Xml` 10.0.8 to `Directory.Packages.props` as a transitive vulnerability override for two GHSA advisories that EF Core 10.0.0 chains into via 9.0.x.
  - Added `Microsoft.AspNetCore.OpenApi` to central package versions (Web template includes it; we'll use it from T-0012 onward).
  - Set per-host launchSettings ports: Customer 5001/7001, Maker 5002/7002, Admin 5003/7003, Public 5004/7004.
  - Added `backend/**/local.settings.json` to `.gitignore` (Azure Functions developer-local file).
  - Removed all template stubs: `Class1.cs`, `WeatherForecast.cs`, `UnitTest1.cs`, `*.http`.
