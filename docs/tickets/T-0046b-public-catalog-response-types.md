---
id: T-0046b
title: Public CatalogController [ProducesResponseType] + canonical dev port 5104
status: done
size: S
owner: dotnet-backend
created: 2026-05-30
updated: 2026-05-30
depends_on: [T-0043, T-0044, T-0045]
blocks: [T-0047, T-0048]
user_stories: []
adrs: [0022]
phase: 3
---

# T-0046b — Public catalog response types + canonical dev port

## Context

T-0046 surfaced two paper cuts that we don't want to compound across T-0047 (`/katalog/[slug]`) and T-0048 (`/produkt/[id]`):

1. **The Public host's three catalog endpoints had no `[ProducesResponseType]` attributes.** NSwag emits `Promise<void>` for any `IActionResult`-returning action without a typed response attribute, so the regenerated `public-api.v1.ts` was useless as a typed client. T-0046 worked around it by hand-mirroring the DTOs in `lib/api-client-helpers/catalog.ts`. Doing that for every Phase-3 frontend ticket compounds drift risk: the DTO mirror would silently fall out of sync with `Core.Domain/Catalog/ICatalogQueries.cs` whenever a field is added.
2. **The Public host's dev port (5004) collides with another local project** on this dev machine. T-0046 had to override `ASPNETCORE_URLS` to 5104 just to regen the client. Future tickets touching the Public host would hit the same collision.

This is a small, mechanical pre-T-0047 cleanup so the next two catalog frontend tickets don't need workarounds.

## Scope

- Annotate every action on `Makables.Web.Public.Controllers.CatalogController` with `[ProducesResponseType(typeof(<dto>), 200)]` + the relevant error response (`Error` 400 for the paged list, `Error` 404 for the by-id/by-slug endpoints).
- Move the Public host's canonical dev port from 5004 → 5104 in `Properties/launchSettings.json` so the host comes up cleanly on the standard `dotnet run` invocation.
- Update `frontend/nswag/config.json` to point at `http://localhost:5104/openapi/v1.json` so `npm run generate:api -- --host public` works out of the box.
- Regenerate `frontend/src/lib/api-client/public-api.v1.ts` against the now-typed spec. The generated `makers(...)` returns `Promise<PagedDataOfMakerListItem>`; `makers2(slug)` returns `Promise<MakerProfile>`; `products(productId)` returns `Promise<ProductDetail>`.

## Out of scope

- **Switching `catalog.ts` to call the generated `PublicApi` class.** The hand-written `apiFetch` wrapper pattern (see `profile.ts`) is the project convention because it returns `Result<T, ApiError>` — the generated client throws `ApiException`, which doesn't fit. T-0046's helper continues to work; its only follow-up is to drop the "workaround" wording from the comment when T-0046 rebases on master after this lands. That comment edit lives in T-0046, not here.
- The other three Web hosts. They have a similar gap, but T-0046b is scoped to unblock Phase-3 frontend.

## Acceptance criteria

- **AC-1** Given the Public host is running, when the OpenAPI spec at `/openapi/v1.json` is fetched, then `/api/v1/catalog/makers` declares a `200` response with schema `PagedDataOfMakerListItem` and a `400` with schema `Error`. The two by-id/by-slug endpoints declare `200` + `404`.
- **AC-2** Given the regenerated `public-api.v1.ts`, when a TypeScript consumer calls `client.makers(...)`, then the return type is `Promise<PagedDataOfMakerListItem>` (not `Promise<void>`).
- **AC-3** Given a clean checkout, when a dev runs `dotnet run` in `Makables.Web.Public/`, then the host comes up on port 5104 without a port collision against unrelated local projects.
- **AC-4** Given the new port, when a dev runs `npm run generate:api -- --host public`, then the script reaches the spec and regenerates the client without manual config edits.
- **AC-5** Build clean, 832 tests pass (no behavior change — purely metadata).

## Technical notes

- `[ProducesResponseType]` lives on the controller action, not on the base `MakablesApiController`. Per-action because each action has its own success-shape; the error shape is uniform (`Error`) but the status code differs by error type.
- The base controller's `HandleResult<T>` still returns `IActionResult` — fine; the attribute is what NSwag reads.
- This is the first use of `[ProducesResponseType]` in the platform. If we adopt it broadly later, the patterns doc + a convention filter could centralise the error attribute. Not in scope here.
- No new namespaces required; the controller now imports `Makables.Core.Domain.Catalog` (for the DTOs) and `Microsoft.AspNetCore.Http` (for `StatusCodes`).

## Files touched

- `backend/src/Makables.Web.Public/Controllers/CatalogController.cs` — 6 attribute lines + 2 using directives.
- `backend/src/Makables.Web.Public/Properties/launchSettings.json` — 5004 → 5104 in both http and https profiles.
- `frontend/nswag/config.json` — Public host URL updated to 5104.
- `frontend/src/lib/api-client/public-api.v1.ts` — regenerated.
- `frontend/src/lib/api-client/.spec-hashes.json` — spec hash updated.

## Status log

- 2026-05-30 done. Build clean, 832 tests pass (750 unit + 82 integration), frontend `tsc --noEmit` + `lint` clean. Spec verified to declare `PagedDataOfMakerListItem` / `MakerProfile` / `ProductDetail` for the three endpoints; regenerated client's `makers(...)` now returns the typed `Promise<PagedDataOfMakerListItem>`. No dual-reviewer pass — change is mechanical (3 attribute lines + 1 port number + 1 regenerated file).
