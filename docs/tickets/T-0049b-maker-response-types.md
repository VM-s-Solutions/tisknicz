---
id: T-0049b
title: Maker ProductController [ProducesResponseType] + NSwag client regen
status: done
size: S
owner: dotnet-backend
created: 2026-06-01
updated: 2026-06-01
depends_on: [T-0049a]
blocks: [T-0049]
user_stories: [US-maker-0004]
adrs: [0022]
phase: 3
---

# T-0049b — Maker host product mutations response types

## Context

T-0049a added the five mutation endpoints (`Create`, `Update`, `Delete`, `UploadImage`, `RemoveImage`) on `Makables.Web.Maker.Controllers.ProductController` and the two new read queries (`List`, `GetById`). The two read endpoints carry `[ProducesResponseType]`; the five mutations do NOT. Without those attributes the OpenAPI document records the actions' return type as the un-introspectable `IActionResult`, and NSwag emits each as `Promise<void>`. T-0049 (frontend dashboard CRUD) needs the typed response shapes (`{id}` after Create, `{imageId}` after UploadImage) to wire the success callbacks without hand-mirroring DTOs the way T-0046 had to for the Public catalog.

Same paper-cut, same fix as T-0046b for the Public host.

## Scope

- Annotate every mutation on `Makables.Web.Maker.Controllers.ProductController` with `[ProducesResponseType]`:
  - `Create` → `200 CreateProductResponse`, `401 Error`, `404 Error`.
  - `Update` → `200` (no body), `401 Error`, `404 Error`.
  - `Delete` → `200` (no body), `401 Error`, `404 Error`.
  - `UploadImage` → `200 UploadProductImageResponse`, `401 Error`, `404 Error`, `409 Error`.
  - `RemoveImage` → `200` (no body), `401 Error`, `404 Error`.
- Introduce two controller-level response records (`CreateProductResponse`, `UploadProductImageResponse`) to give the OpenAPI schema dictionary distinct top-level names — `CreateProduct.Response` and `AddProductImage.Response` are both nested records named `Response` and `Microsoft.AspNetCore.OpenApi` names schemas by unqualified type-name, so without the wrap they collide on a single `Response` schema and NSwag emits the wrong shape for one of them. Same nested-record pattern stays in `Core.AppServices`; the controller just projects via `BusinessResult.Success(new ...)`.
- Regenerate `frontend/src/lib/api-client/maker-api.v1.ts` against the now-typed spec. `productsPOST` returns `Promise<CreateProductResponse>`; `imagesPOST` returns `Promise<UploadProductImageResponse>`; the void-payload mutations stay `Promise<void>` honestly.
- Patch `frontend/scripts/generate-api.mjs` to append the canonical NSwag `FileParameter` interface to the generated file when the identifier is referenced but undeclared — this is the first multipart endpoint anywhere in the platform's API surface and NSwag's Fetch template emits the reference without the declaration, breaking `tsc --noEmit`.

## Out of scope

- The other three Web hosts (Customer, Admin still have `[ProducesResponseType]` gaps on their mutation endpoints).
- Switching the frontend dashboard to consume the regenerated `MakerApi` class — the `apiFetch` `Result<T, ApiError>` pattern continues to be the project convention (see T-0046b §Out-of-scope). T-0049 wires the dashboard against `apiFetch`-style helpers; the typed client is for the helpers to use as the canonical response-shape source.
- The Maker host's canonical dev port. 5002 is free on this dev machine — no port move needed (Public moved 5004 → 5104 in T-0046b only because 5004 collided locally).
- Any new `BusinessErrorMessage` codes. Endpoints reuse `MakerNotFound`, `ProductNotFound`, `ProductImageLimitReached`, `Unauthorized`.

## Acceptance criteria

- **AC-1** Given the Maker host is running, when the OpenAPI spec at `/openapi/v1.json` is fetched, then every mutation endpoint declares a `200` response with the appropriate schema (or no body for `Update`/`Delete`/`RemoveImage`), plus `401 Error` and `404 Error`; `UploadImage` additionally declares `409 Error` (image cap).
- **AC-2** Given the spec, the `200` schema for `CreateProduct` is `CreateProductResponse` (not the collided `Response` shape); the `200` schema for `UploadImage` is `UploadProductImageResponse` (distinct top-level name).
- **AC-3** Given the regenerated `maker-api.v1.ts`, when a TypeScript consumer calls `client.productsPOST(...)`, then the return type is `Promise<CreateProductResponse>` (not `Promise<void>`); `client.imagesPOST(...)` returns `Promise<UploadProductImageResponse>`.
- **AC-4** No `400 Error` declared on the `[FromBody]` mutations or the multipart upload. The framework emits `ValidationProblemDetails` (RFC 7807) for malformed JSON / multipart parse failures before `HandleResult` runs — declaring `400 → Error` would mislead generated clients about a 400 response shape they may not see (same lesson as T-0046b on `GetMakers`). The handlers' own FluentValidation 400s ARE `Error`-shaped, but they share the status code with the model-binding 400s; one schema per status code, and we don't claim the wrong one.
- **AC-5** Build clean, 773 unit + 82 integration tests still pass (no behavior change — purely metadata + a controller-level response projection that preserves status and payload).
- **AC-6** `npx tsc --noEmit` and `npm run lint` clean from `/frontend/`.

## Technical notes

### Schema collision and the wrap

`Microsoft.AspNetCore.OpenApi` names schemas by `JsonTypeInfo.Type.Name`, ignoring the outer scope. `CreateProduct.Response` and `AddProductImage.Response` both come out as `Response`, with the second registration overwriting the first in the schema dictionary — leading to NSwag emitting `Promise<Response>` for both `Create` and `UploadImage` with whichever shape won the race. The first-pass commit on this ticket hit exactly that: the spec had a single `Response` schema with `{imageId}`, and `Create` would have been wired against the wrong type.

The fix is the controller-level projection: `CreateProductResponse { id }` and `UploadProductImageResponse { imageId }` are top-level types, get unique schema names, and the controller wraps the handler's `Success(new CreateProduct.Response(...))` into `Success(new CreateProductResponse(...))` before `HandleResult`. No change to the CQRS nesting convention in `Core.AppServices`. A schema-name resolver on the OpenAPI side (e.g. fully-qualified name) would solve it generically but is out of scope here.

### `FileParameter` appendix in `generate-api.mjs`

The Maker host's multipart `UploadImage` endpoint is the first `IFormFile` parameter anywhere in the platform's API surface. NSwag's Fetch template emits the type reference `FileParameter` for multipart parameters but doesn't include the interface declaration in the generated file. The script now appends the canonical NSwag shape (`{ data: any; fileName: string }`) when the identifier is referenced but undeclared — the shape isn't ad-hoc; the same generated file uses it inline (`content_.append("file", file.data, file.fileName ? file.fileName : "file")`). Future multipart endpoints inherit the same fix automatically.

### `400 Error` honesty (same as T-0046b)

The mutating endpoints take `[FromBody]` JSON (`Create`, `Update`) or multipart (`UploadImage`) — both produce `ValidationProblemDetails` (RFC 7807) on malformed input before `HandleResult` runs. The handlers' own FluentValidation 400s ARE the domain `Error` shape. Status code 400 carries either body shape depending on which failed first, so we don't declare either — declaring one would lie about the other. The four non-validation domain failures we DO declare (`401`/`404`/`409`) only ever come through `HandleResult` and are uniformly `Error`-shaped.

### Maker host dev port: 5002 is free on this dev machine

T-0046b had to move Public from 5004 → 5104 due to a local collision. Port 5002 is free here — no `launchSettings.json` / `nswag/config.json` / `api-fetch.ts` / `README.md` / `ci.yml` change needed. The four sources still agree on 5002.

## Files touched

- `backend/src/Makables.Web.Maker/Controllers/ProductController.cs` — two new controller-level response records + 5 sets of `[ProducesResponseType]` attribute lines + two `HandleResult` projection edits (Create and UploadImage).
- `frontend/scripts/generate-api.mjs` — append `FileParameter` interface to the generated file when referenced but undeclared.
- `frontend/src/lib/api-client/maker-api.v1.ts` — regenerated (first commit of this file).
- `frontend/src/lib/api-client/.spec-hashes.json` — `maker-api.v1` hash updated.
- `docs/tickets/T-0049b-maker-response-types.md` — this file.

## Status log

- 2026-06-01 done. Build clean, 773 unit + 82 integration tests pass (matches T-0049a baseline). Spec verified: `productsPOST` → `CreateProductResponse`, `imagesPOST` → `UploadProductImageResponse`, `productsPUT`/`productsDELETE`/`imagesDELETE` typed as `Promise<void>`. Spec hash: `03b84406f79b1c29fe8a36f410460da1e0b8c5e40d88249d58811cf60240b021`. Frontend `tsc --noEmit` + `lint` clean. No dual-reviewer pass — change is mechanical (attributes + controller-level response wrap + a small one-off script appendix).
- 2026-06-01 Copilot review folded — six findings.
  - **M1 + M2 — 401 missing on the two read endpoints.** Both `GET /products` and `GET /products/{productId}` are `[Authorize]`d but their OpenAPI metadata declared only 200 + 404. Added `[ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]` so the regenerated client treats 401 as a typed `Error` instead of an "unexpected" exception. Hash bumped to `3d2476be0edc0554c856c94f539577e4dfa9087dfc8f5a62bf50ecd6ebd20bd4`.
  - **M3 — `IFormFile file` marked optional in the generated client (acknowledged, not fixed).** The runtime is fine (the defensive `file is null || file.Length == 0` check returns 400). The ergonomics gap is real, but the obvious fixes — `[FromForm, Required] IFormFile` and a wrapper DTO with `[Required] IFormFile File` — both **blow up the spec**: the emitter inlines the entire `IFormFile` interface (`contentDisposition`, `headers`, `length`, `name`, ...) as the request body schema, which makes the generated client far worse (a synthetic `Body` class with all those fields exposed). Reverted both attempts. Documented the gap in a code comment at the upload-image action and queued a real follow-up: T-0049c — `IOperationFilter` / explicit `MultipartFormDataContent` schema override for multipart endpoints. Not in scope for T-0049b.
  - **L1 + L2 — `FileParameter.fileName` should be optional.** The NSwag-generated multipart code falls back to the literal `"file"` when `fileName` is falsy (`content_.append("file", file.data, file.fileName ? file.fileName : "file")`); the type was declaring it required. Updated the `scripts/generate-api.mjs` appendix to `fileName?: string` and regenerated. Also tightened the appendix comment to call out the fallback so the next reader doesn't think it's wrong.
  - **L3 — `IMakerProductQueries` XML doc misleading.** The summary said "The handler is the IDOR shield" — but the projection ALSO enforces `p.MakerId == makerId` (belt-and-braces, the tests pin both layers). Rewrote that bullet to say: caller supplies `makerId`, implementations MUST enforce maker scoping, cross-maker probes surface as `NotFound`. Captures the actual contract obligation.
  - **L4 — `MakerProductQueriesTests` summary wrong sort field.** Said `IsActive desc, CreatedAt desc`; the projection actually orders `IsActive desc, Id desc` (ULID as the time-proxy because SQLite can't `ORDER BY` `DateTimeOffset` — same workaround `CatalogQueries` uses). Updated the summary to call out `Id desc` and the SQLite reason so debugging future sort changes doesn't start from a wrong premise.
