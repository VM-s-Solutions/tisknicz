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
- Custom OTel exporters for metrics dashboards beyond App Insights (MVP relies on the AzMonitor exporter).
- Concrete instrument creation for the six required custom signals — `MakablesMeters.All` registers the names so future tickets can `new Meter(...)` against them and have them exported automatically.

## Reviewer findings (commit 8a9875e) and resolutions

Reviewer returned **BLOCKER × 3** + **MAJOR × 4** + **MINOR × 5**. All BLOCKERs and the actionable MAJORs are addressed in the follow-up commit on master:

- **BLOCKER B-1 (sampling)** — added `MakablesTraceSampler` implementing ADR 0023 §4 (100% webhooks, 10% other successes; trace-ratio fallback). Wired via `SetSampler(...)` on the tracer provider. Error-path coverage is preserved by `RecordException = true` on ASP.NET / HttpClient instrumentation.
- **BLOCKER B-2 (custom metrics)** — added `MakablesMeters.All` (`Outbox`, `Payments`, `Webhooks`, `Orders`, `Payouts`) and registered each via `AddMeter(...)` so instruments created in their owning modules will automatically export. The six required signals from ADR 0023 §4 are documented in code; their concrete instrument creation lands in their owning tickets (Outbox: T-0011 follow-up; Payments: T-0065; Webhooks: T-0066; Orders/Auto-deliver: T-0077; Payouts: T-0102).
- **BLOCKER B-3 (redaction)** — added `SensitivePropertyMasker` Serilog enricher; redacts property names containing `password`, `secret`, `apikey`, `tokenhash`, `refreshtoken`, `signingkey`, `comgatepayload`, `authorization` (case-insensitive). Replaces value with `"***"` so downstream consumers see the property shape but never the secret.
- **MAJOR M-1 (middleware integration test)** — added two tests per host (`Host_Echoes_CorrelationId_When_Traceparent_Header_Is_Supplied`, `Host_Generates_CorrelationId_When_No_Traceparent_Header_Is_Supplied`) that exercise `RequestEnrichmentMiddleware` end-to-end. The middleware now writes `x-correlation-id` on the response so the test can assert wiring without a custom log sink.
- **MAJOR M-2 (EF Core OTel beta)** — kept the beta pin; added inline rationale in `Directory.Packages.props` so the next reviewer doesn't re-flag it.
- **MAJOR M-3 (Program.cs identical-line confirmation)** — confirmed by diff: same one-line `builder.AddMakablesObservability(...)` in all four hosts.
- **MAJOR M-4 (middleware ordering — `user_id` always null)** — corrected `UseMakablesPipeline` to: `UseCors → UseAuthentication → RequestEnrichmentMiddleware → UseSerilogRequestLogging → UseAuthorization → UseRateLimiter`. Enrichment now sees populated `HttpContext.User`.
- **MINOR N-1 (string-literal claim name)** — added `Makables.Core.Domain.Common.MakablesClaimTypes.CountryCode`; both the auth provider and the enrichment middleware route through it.
- **MINOR N-2 (lax traceparent parser)** — replaced split-based extractor with `[GeneratedRegex(@"^00-([0-9a-f]{32})-[0-9a-f]{16}-[0-9a-f]{2}$")]`.
- **MINOR N-3 (doc-drift on "+4 tests")** — the +4 tests claim was a per-host count for one new `[Fact]` inherited via the test base class. The follow-up adds two more facts × four hosts = +8 additional tests. AC-1 below restated.

## Acceptance criteria
- **AC-1** Build clean; 145 tests pass (109 unit + 36 integration; reviewer-fix commit adds +8 enrichment tests on top of the +4 Serilog logger-provider tests already shipped in 8a9875e).
- **AC-2** Each Web host registers `Serilog.ILogger` in the container after `AddMakablesObservability` runs.
- **AC-3** `RequestEnrichmentMiddleware` runs AFTER `UseAuthentication` so `user_id` / `country_code` claims are visible; integration tests assert the `x-correlation-id` response header proves wiring.
- **AC-4** Azure Monitor exporter is gated on a non-empty connection string so tests and local runs skip it.
- **AC-5** Trace sampling follows ADR 0023 §4 (`MakablesTraceSampler`); custom-meter names registered via `MakablesMeters.All`; sensitive properties redacted via `SensitivePropertyMasker`.

## Status log
- 2026-05-23 done. Initial commit 8a9875e. 137 tests.
- 2026-05-23 reviewer fix folded in. 145 tests. BLOCKERs B-1/B-2/B-3 closed; MAJORs M-1/M-3/M-4 closed; M-2 documented; MINORs N-1/N-2/N-3 closed.
