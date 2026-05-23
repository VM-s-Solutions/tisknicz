---
id: T-0014
title: Serilog + OpenTelemetry + Azure Monitor wiring — AddMakablesObservability + RequestEnrichmentMiddleware
status: done
size: M
owner: dotnet-backend
created: 2026-05-23
updated: 2026-05-23
depends_on: [T-0009]
blocks: []
adrs: [0023]
phase: 1
---

# T-0014 — Observability

## Scope
- `backend/src/Directory.Packages.props` — adds Serilog (AspNetCore, Settings.Configuration, Sinks.Console), OpenTelemetry (Extensions.Hosting, Instrumentation.AspNetCore/Http/EFCore), Azure.Monitor.OpenTelemetry.AspNetCore. OTel pinned to 1.15.x to clear GHSA-g94r-2vxg-569j and satisfy the Azure.Monitor.OpenTelemetry.AspNetCore 1.3.0 floor.
- `backend/src/Makables.Config/Makables.Config.csproj` — references the new packages.
- `backend/src/Makables.Config/Extensions/AddMakablesObservability.cs` — `WebApplicationBuilder.AddMakablesObservability(serviceName)` wires Serilog (`UseSerilog` reading from configuration; JSON in non-Dev, plain console in Dev) and OpenTelemetry (Traces + Metrics with ASP.NET Core / HttpClient / EF Core instrumentation). Azure Monitor exporter is enabled when `AzureMonitor:ConnectionString` (or `APPLICATIONINSIGHTS_CONNECTION_STRING`) is set.
- `backend/src/Makables.Config/Middleware/RequestEnrichmentMiddleware.cs` — pushes `request_id`, `correlation_id` (from W3C `traceparent`), `user_id`, `country_code` onto Serilog `LogContext` so every log entry within the request carries them.
- `backend/src/Makables.Config/Extensions/UseMakablesPipeline.cs` — calls `UseSerilogRequestLogging()` and `UseMiddleware<RequestEnrichmentMiddleware>()` before the existing CORS/Auth/RateLimiter stack.
- `backend/src/Makables.Web.{Customer,Maker,Admin,Public}/Program.cs` — calls `builder.AddMakablesObservability("makables-<audience>-api")` before the rest of the registrations.
- `backend/src/Makables.IntegrationTests/HostStartup/WebHostStartupTests.cs` — adds `Host_Uses_Serilog_As_Logger_Provider` test across all four hosts.

## Out of scope
- Functions host observability (deferred — Functions has its own AI worker package; T-0030 covers it).
- Custom OTel exporters for metrics dashboards (App Insights bridge sufficient for MVP).
- Log sampling / scrubbing rules (deferred to ops hardening).

## Acceptance criteria
- **AC-1** Build clean; 137 tests pass (109 unit + 28 integration; +4 new Serilog tests, one per host).
- **AC-2** Each Web host registers `Serilog.ILogger` in the container after `AddMakablesObservability` runs.
- **AC-3** `UseSerilogRequestLogging` + `RequestEnrichmentMiddleware` are wired in `UseMakablesPipeline` ahead of CORS/Auth/RateLimiter.
- **AC-4** Azure Monitor exporter is gated on a non-empty connection string so tests and local runs skip it.

## Status log
- 2026-05-23 done. 137 tests pass.
