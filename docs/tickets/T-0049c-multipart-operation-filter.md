---
id: T-0049c
title: Multipart operation/schema transformer — rewrite IFormFile request bodies to canonical OpenAPI shape
status: in_progress
size: S
owner: dotnet-backend
created: 2026-06-02
updated: 2026-06-02
depends_on: [T-0049b]
blocks: [T-0064]
user_stories: [US-maker-0004]
adrs: [0022]
phase: 3
---

# T-0049c — Multipart operation/schema transformer for IFormFile request bodies

## Context

T-0049b shipped `[ProducesResponseType]` annotations on the Maker host's
`ProductController` and regenerated `maker-api.v1.ts`. The Maker host's
`POST /api/v1/products/{productId}/images` is the **only multipart endpoint in
the platform today**. NSwag emits its signature as:

```ts
imagesPOST(productId: string, file: FileParameter | undefined): Promise<UploadProductImageResponse>
```

The `| undefined` union is wrong: the backend requires the file (the defensive
empty-file check in `UploadImage` returns 400). The action lives behind a
controller comment that calls the gap acknowledged-but-not-fixed and points
the reader at this ticket.

T-0049b also proved the two obvious fixes do not work:

- `[FromForm, Required] IFormFile file` on the action — `Microsoft.AspNetCore.OpenApi`
  inlines the full `IFormFile` interface (`contentDisposition`, `headers`, `length`,
  `name`, …) into the request body schema, producing a synthetic `Body` class far
  worse than the optional `FileParameter`.
- A wrapper DTO with `[Required] IFormFile File` — same inlining behaviour, same
  synthetic `Body` shape.

The correct fix is a schema transformer that targets `multipart/form-data`
request bodies and rewrites them to the canonical OpenAPI 3.0 shape:

```json
{ "type": "object", "properties": { "file": { "type": "string", "format": "binary" } }, "required": ["file"] }
```

This lands now (Sprint 7 prep) so T-0064 (order attachments — Phase 4) inherits
the fix on day one instead of replicating T-0049's `FileParameter | undefined`
workaround.

## Scope

- Add a multipart schema transformer (an `IOpenApiSchemaTransformer` /
  `IOpenApiOperationTransformer` — implementation choice left to the agent,
  whichever produces the cleanest rewrite for the Microsoft.AspNetCore.OpenApi 10
  API) that:
  - Detects request bodies with content type `multipart/form-data`.
  - For every property whose source CLR type is `IFormFile`, rewrites the
    property schema to `{ "type": "string", "format": "binary" }`.
  - Preserves non-`IFormFile` properties on the wrapper-DTO case (T-0064 will
    likely add `Caption: string` alongside `File: IFormFile`; only the file
    property's schema gets rewritten — string fields stay typed as
    `{ "type": "string" }`).
  - Sets `required` on the request body schema to include every property the
    DTO declares as required (`[Required]`, non-nullable reference type, or
    value type — match how Microsoft.AspNetCore.OpenApi already infers
    required-ness for non-multipart bodies).
- Register the transformer inside `MakablesOpenApiExtensions.AddMakablesOpenApi`
  in `backend/src/Makables.Config/Extensions/AddMakablesOpenApi.cs` so all four
  hosts inherit it via their existing `AddMakablesOpenApi("v1")` call.
- Remove (or rewrite to point at this ticket as the fix) the T-0049b
  "acknowledged gap" comment block at the top of `UploadImage` in
  `backend/src/Makables.Web.Maker/Controllers/ProductController.cs`. The
  defensive empty-file check stays — it remains the runtime contract
  enforcement and the spec-level required does not eliminate the need for
  it.
- Regenerate `frontend/src/lib/api-client/maker-api.v1.ts`. New signature:
  ```ts
  imagesPOST(productId: string, file: FileParameter): Promise<UploadProductImageResponse>
  ```
- Bump `frontend/src/lib/api-client/.spec-hashes.json` for `maker-api.v1`.
- Add an integration test in `backend/src/Makables.IntegrationTests/` that
  boots the Maker host via `WebApplicationFactory`, fetches
  `/openapi/v1.json`, and pins the upload endpoint's request-body schema
  shape:
  - `requestBody.content['multipart/form-data'].schema.type === 'object'`
  - `requestBody.content['multipart/form-data'].schema.properties.file` equals
    `{ "type": "string", "format": "binary" }`
  - `requestBody.content['multipart/form-data'].schema.required` includes
    `"file"`

## Out of scope

- New multipart endpoints. T-0064 (order attachments) will land in Phase 4 and
  will exercise the wrapper-DTO branch; this ticket only proves the design
  handles both the bare-`IFormFile` parameter case (today) and the wrapper-DTO
  case (T-0064 forward).
- Switching `UploadImage` to a wrapper DTO. The bare `IFormFile file` parameter
  signature stays — the transformer rewrites whatever shape lands in the spec
  so the action signature itself does not need to change.
- Customer / Admin / Public host multipart endpoints. None exist today; the
  transformer is registered on every host uniformly so the moment one is added
  it inherits the fix (same pattern as the T-0049b enum schema transformer).
- The defensive empty-file check at the top of `UploadImage`. It stays as the
  runtime contract enforcement. Spec-level `required: ['file']` informs the
  client; it does not let the server skip the check.
- Removing the `FileParameter` appendix in `frontend/scripts/generate-api.mjs`.
  That appendix only adds the interface declaration NSwag emits but does not
  declare; it is orthogonal to the union-fix this ticket lands.
- Any backend behavior change. This ticket is metadata + a transformer; no
  command, handler, validator, or controller logic changes.

## Acceptance criteria

- **AC-1** Given the Maker host is running, when `/openapi/v1.json` is fetched,
  then `paths['/api/v1/products/{productId}/images'].post.requestBody.content['multipart/form-data'].schema`
  equals `{ "type": "object", "properties": { "file": { "type": "string", "format": "binary" } }, "required": ["file"] }`
  (property order may vary; structural equality is what's pinned).
- **AC-2** Given the regenerated `maker-api.v1.ts`, when a TypeScript consumer
  calls `client.imagesPOST(productId, file)`, then the second parameter type is
  `FileParameter` (no `| undefined` union). `frontend/src/lib/api-client-helpers/maker-products.ts`
  no longer needs to type-narrow around the union — confirm by reading the
  helper after regen and noting that nothing breaks.
- **AC-3** Given any of the four Web hosts (`Customer`, `Maker`, `Admin`,
  `Public`) is running, when its container is inspected, then the multipart
  schema transformer is registered on its OpenAPI document (proven by all four
  hosts calling `AddMakablesOpenApi("v1")` and the transformer being added
  inside that extension — same wiring as the T-0049b enum transformer). No
  per-host registration drift.
- **AC-4** Given the multipart transformer encounters a non-multipart request
  body (e.g. `application/json` on `Create` / `Update`), then the request body
  schema is unchanged — only `multipart/form-data` bodies are rewritten.
  Verified by an assertion in the integration test that the JSON body schema
  on `POST /api/v1/products` still resolves to `CreateProductRequest` shape.
- **AC-5** Given a future wrapper DTO with `File: IFormFile` + `Caption: string`
  (T-0064 will land such a DTO), when the spec is generated, then only the
  `File` property's schema is rewritten to `{ "type": "string", "format": "binary" }`;
  `Caption` stays as `{ "type": "string" }`; `required` lists every property
  the DTO declares as required. The transformer must be designed for this
  shape now even though no production endpoint exercises it yet — the design
  decision is documented in the Technical notes below.
- **AC-6** Backend build clean. 773 unit + 82 integration tests (T-0049b
  baseline) pass; plus 1 new integration test from this ticket → 856 tests
  total. The new test boots the Maker host (cheaper than all four; the
  transformer's per-host parity is covered by AC-3 via shared registration).
  Frontend `npx tsc --noEmit` and `npm run lint` clean. The T-0049b
  "acknowledged gap" comment block at `UploadImage` is removed (or rewritten
  to point at this ticket as the closing fix).

## Technical notes

### Canonical OpenAPI 3.0 multipart shape

OpenAPI 3.0 § "Considerations for File Uploads" (and the OpenAPI 3.1 binary
data section) define the canonical multipart-file shape:

```yaml
requestBody:
  required: true
  content:
    multipart/form-data:
      schema:
        type: object
        properties:
          file:
            type: string
            format: binary
        required:
          - file
```

`format: binary` is the marker NSwag's Fetch template uses to emit
`FileParameter` for the parameter (rather than `string`). The
`required` array on the schema is what eliminates the `| undefined` union in
the generated method signature.

### Transformer scope: properties, not parameters

`Microsoft.AspNetCore.OpenApi` represents multipart bodies as a request-body
schema with one property per form field. The transformer therefore rewrites
**schema properties**, not parameter objects. For the bare
`IFormFile file` parameter case (today's `UploadImage`), the emitter already
produces a single-property schema named `file` — the transformer just rewrites
that property's schema. For the wrapper-DTO case (T-0064 forward), the
emitter produces one property per DTO field — the transformer iterates them
and rewrites only the ones whose CLR property type is `IFormFile`.

### How to detect a property's CLR type from inside the transformer

`IOpenApiSchemaTransformer.TransformAsync` receives an
`OpenApiSchemaTransformerContext` with `JsonTypeInfo` / `JsonPropertyInfo`
attached — the property's source CLR type is on `JsonPropertyInfo.PropertyType`.
Check `typeof(IFormFile).IsAssignableFrom(propertyType)`. This avoids
string-matching property names and survives DTO renames.

If the operation-level transformer (`IOpenApiOperationTransformer`) is more
ergonomic than the schema-level one for "detect multipart, find IFormFile
properties, rewrite schemas" — pick whichever lands cleaner. Both APIs are
supported in Microsoft.AspNetCore.OpenApi 10. Document the choice inline in
the transformer's XML doc so the next reader understands the trade-off.

### Where to follow the existing pattern

`backend/src/Makables.Config/Extensions/AddMakablesOpenApi.cs` already hosts
the enum schema transformer T-0049b shipped. The new multipart transformer
goes in the same file (or a sibling file in the same folder, called from
inside `AddMakablesOpenApi`) so all four hosts pick it up via the existing
`AddMakablesOpenApi("v1")` call in their `Program.cs`. Mirror the enum
transformer's XML doc style: explain the wire-vs-spec mismatch this fixes
and why the rule is global.

### `[FromForm, Required]` and wrapper DTOs explicitly NOT used

T-0049b's Copilot M3 documented the failure mode: both attempts blow up the
schema by inlining the `IFormFile` interface. The action signature stays as
plain `IFormFile file` (bare parameter) — the transformer rewrites the spec
without requiring any action-side annotation. This is the design.

### Required-property inference for the wrapper-DTO case

When T-0064 lands a wrapper DTO with `File: IFormFile` + `Caption: string`:
- The transformer must include every required property in `schema.required`,
  not just `file`. "Required" means: `[Required]` attribute present, OR
  non-nullable reference type, OR value type (these are the same heuristics
  `Microsoft.AspNetCore.OpenApi` uses elsewhere; let it infer required-ness
  first, then only ensure the file property is added if it was inferred as
  optional).
- Non-`IFormFile` property schemas are left untouched. A `Caption: string`
  property stays as `{ "type": "string" }`.
- The design decision: rewrite-then-merge, not rewrite-from-scratch. The
  transformer overlays its rewrite on top of the emitter's output rather than
  replacing the full schema; that keeps non-file fields honest and means the
  same transformer handles the bare-parameter case (one property) and the
  wrapper-DTO case (many properties) with identical code.

### Integration test pattern: boot one host, read the spec via HTTP

The test harness in `backend/src/Makables.IntegrationTests/HostStartup/WebHostStartupTests.cs`
already proves `WebApplicationFactory<TProgram>` boots each host and serves
`/openapi/v1.json` (see `Host_OpenApi_Document_Is_Served`). The new test
follows the same pattern: boot `Makables.Web.Maker.Program`, GET
`/openapi/v1.json`, parse the JSON, navigate to the upload endpoint's
request body, assert the shape. No need to boot all four hosts — AC-3 is
covered by the shared `AddMakablesOpenApi` registration.

### What the spec hash change means

Every regen reshuffles the contents of `maker-api.v1.ts` and therefore the
hash in `.spec-hashes.json`. The pre-commit hook will catch any uncommitted
delta. T-0049b set the precedent: do not quote the hash in the status log —
the file is the source of truth.

## Files touched (expected)

- `backend/src/Makables.Config/Extensions/AddMakablesOpenApi.cs` — extend
  `MakablesOpenApiExtensions` with the multipart transformer registration
  (keep the existing enum schema transformer in place).
- Optionally one new file in the same folder for the transformer class — the
  agent's call whether it stays inline as a lambda (like the enum transformer)
  or factors to a sibling class. Either way, registration goes through
  `AddMakablesOpenApi`.
- `backend/src/Makables.Web.Maker/Controllers/ProductController.cs` — remove
  or update the T-0049b "Out of scope" comment block at `UploadImage` so it
  no longer says the gap is acknowledged-but-not-fixed. Defensive empty-file
  check stays.
- `backend/src/Makables.IntegrationTests/HostStartup/MultipartSchemaTests.cs`
  (or similar) — one new test pinning the spec shape. Uses the existing
  `WebApplicationFactory<Makables.Web.Maker.Program>` harness.
- `frontend/src/lib/api-client/maker-api.v1.ts` — regenerated (no manual
  edits; the pre-commit hook enforces this).
- `frontend/src/lib/api-client/.spec-hashes.json` — `maker-api.v1` hash
  updated.

## Test plan reference

Inline. AC-1 / AC-4 / AC-5 are exercised by the new integration test. AC-2
is exercised by `tsc --noEmit` against the regenerated client + a manual
read of the regenerated signature line. AC-3 is exercised by the existing
host-startup tests (all four hosts boot through `AddMakablesOpenApi`; if
the transformer registration fails any host, those tests fail). AC-6 is
the build + full test suite.

## Status log

- 2026-06-02 `draft → ready` by PM. Backlog row in `INDEX.md` carried the
  full intent since T-0049b shipped; expanded to a full ticket file ahead
  of Sprint 7 kickoff per `docs/status/sprint-7.md` carry-overs table.
  Owner `dotnet-backend`.
- 2026-06-02 `ready → in_progress` by PM. First Sprint 7 carry-over picked
  off the backlog; `dotnet-backend` agent invoked with handoff brief.
