---
id: T-0012
title: API versioning + per-host /openapi/v1.json
status: done
size: S
owner: dotnet-backend
created: 2026-05-23
updated: 2026-05-23
depends_on: [T-0009]
blocks: [T-0013]
adrs: [0021]
phase: 1
---

# T-0012 — API versioning + OpenAPI per host

## Scope
- `AddMakablesApiVersioning` extension in `Makables.Config` — `Asp.Versioning.Mvc` URL-segment versioning per ADR 0021; default v1.0; `AddApiExplorer` with `'v'VVV` group-name format; `SubstituteApiVersionInUrl=true`.
- Each Web host's `Program.cs` now calls `AddMakablesApiVersioning()` and `AddOpenApi("v1")` + `app.MapOpenApi()`.
- One new integration test per host (`Host_OpenApi_Document_Is_Served`) hits `/openapi/v1.json` and asserts the response contains `"openapi"`.

## Acceptance criteria
- **AC-1** Build clean (0 warnings, 0 errors).
- **AC-2** 4 new integration tests pass; total 133.
- **AC-3** GET /openapi/v1.json returns 200 with a valid OpenAPI document on each host.
- **AC-4** URL-segment versioning is wired (controllers in later tickets decorate with `[ApiVersion("1.0")]` + `[Route("api/v{version:apiVersion}/...")]`).

## Status log
- 2026-05-23 done. 133 tests passing.
