---
id: T-0010
title: Country + CountryConfiguration entity + initial migration (CZ seed)
status: done
size: M
owner: dotnet-backend
created: 2026-05-23
updated: 2026-05-23
depends_on: [T-0002, T-0006, T-0007, T-0008, T-0009]
blocks: [T-0011]
adrs: [0004, 0013]
phase: 1
---

# T-0010 — Country + CountryConfiguration + initial migration

## Scope
- `Core.Domain.Configuration/Country` entity (inherits Auditable). Two flags: `IsActive` (admin visibility) and `IsServiced` (open for business).
- `Core.Domain.Configuration/CountryConfiguration` entity — full per-country control plane per ADR 0004: currency, language, timezone, VAT (basis points), invoicing mode, platform fee rate, tax-ID label/format, VAT-ID label/format, registration-number label/format, four default provider codes, free-form `LegalRequirementsJson`.
- `Core.Domain.Configuration/InvoicingMode` enum (`None | StandardVat | ReverseCharge | StrictFiscalReporting`).
- `Core.Domain.Configuration/ICountryConfigurationRepository` interface.
- `Infra.Database.Repositories/CountryConfigurationRepository` impl (read-only, AsNoTracking).
- EF Core entity configurations for both entities + numbering sequence (`Configurations/CountryConfiguration.cs`).
- `Infra.Database/Seeding/CountrySeed.cs` — factory for the CZ row.
- `Infra.Database/Migrations/20260523105147_InitialSchema.cs` — initial EF Core migration generated via `dotnet ef`. Creates `countries`, `country_configuration`, `numbering_sequence` tables and seeds CZ via raw SQL inside the migration.
- Registered `ICountryConfigurationRepository → CountryConfigurationRepository` in `AddMakablesInfrastructure`.
- `Microsoft.EntityFrameworkCore.Design` added to `Makables.Web.Customer.csproj` (Design-time dependency for EF migrations).

## Side-deliverables: T-0009 reviewer follow-ups
Reviewer of T-0009 returned CHANGES_REQUESTED with 2 MAJORs + 2 MINORs + 1 NIT. All addressed in this commit:

- **MAJOR #1**: `AddMakablesInfrastructure` connection-string guard now uses `IsNullOrWhiteSpace` instead of `?? throw` (which an empty string slipped past).
- **MAJOR #2**: Added two integration tests per host that exercise middleware wiring: `Host_Cors_Middleware_Is_Active` (CORS preflight returns Access-Control-Allow-Origin header), `Host_RateLimiter_Options_Are_Registered` (resolves `IOptions<RateLimiterOptions>`), `Host_Authentication_Services_Are_Registered` (resolves `IAuthenticationSchemeProvider`).
- **MINOR #3**: Removed orphan `Swashbuckle.AspNetCore` package version from Directory.Packages.props (kept a comment explaining the drop).
- **MINOR #4**: Public host's `AddMakablesAuth` call kept; documented as acceptable since no `[Authorize]` exists on Public endpoints. (Adding a no-op auth registration is harmless; removing the conditional makes Program.cs more uniform across hosts.)

Test infrastructure also extended:
- WebHostStartupTests' `BuildFactory()` now supplies a placeholder `ConnectionStrings:Postgres` via in-memory configuration so the host startup passes the new MAJOR #1 guard. The SQLite swap still happens; the placeholder string is never used because the test removes the Postgres registration before any DbContext is constructed.

## Acceptance criteria
- **AC-1** Build clean (0 warnings, 0 errors).
- **AC-2** 94 unit + 20 integration = 114 tests pass.
- **AC-3** `CountryConfiguration.Create` validates country code (2 chars), currency code (3 chars), VAT rates in range, platform fee rate in range, reduced VAT ≤ standard.
- **AC-4** `Country.IsActive` and `Country.IsServiced` are independent flags per ADR 0004.
- **AC-5** `EF Core` migration creates `countries`, `country_configuration`, `numbering_sequence` tables; seeds Czechia with realistic defaults (CZK, 21% VAT, 12% reduced, 15% platform fee, IČO/DIČ formats, comgate/packeta/ares/resend providers).
- **AC-6** `ICountryConfigurationRepository.GetByCodeAsync("CZ")` returns the seeded row.
- **AC-7** Updates (`UpdateVatRates`, `UpdateInvoicingMode`, `UpdatePlatformFeeRate`, `UpdateProviders`) validate bounds and update fields atomically.

## Status log
- 2026-05-23 done. 114 tests passing (94 unit + 20 integration).
