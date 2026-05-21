---
id: 0022
title: NSwag pipeline — backend emits OpenAPI; frontend regenerates on every contract change; CI enforces parity
status: accepted
date: 2026-05-21
deciders: [Architect]
---

# 0022 — NSwag pipeline

## Context

ADR 0007 mandates that the frontend reaches the backend only through an NSwag-generated TypeScript client. ADR 0021 introduced URL-path versioning per audience. We need concrete mechanics: who runs NSwag, when, where the output lives, how breaking changes are detected, how local development works.

## Decision

### Backend emits OpenAPI specs

Each `Web.*` host adds `Swashbuckle.AspNetCore` and exposes:

```
GET /openapi/v1.json     -- machine-readable spec
GET /swagger             -- human-readable UI (dev/staging only; gated in production)
```

Specs are version-and-audience scoped: `Web.Customer/openapi/v1.json` covers customer-API v1 only.

### Frontend client layout

```
frontend/src/lib/api-client/
├── README.md                       # explains regeneration, blocks manual edits
├── customer-api.v1.ts              # generated
├── customer-api.v1.client.ts       # generated client class
├── maker-api.v1.ts
├── maker-api.v1.client.ts
├── admin-api.v1.ts
├── admin-api.v1.client.ts
├── public-api.v1.ts
└── public-api.v1.client.ts
```

One generated file pair per host per version. Imports use `import { CustomerApi } from '@/lib/api-client/customer-api.v1.client';`.

### Regeneration: explicit, not magic

NSwag is invoked **manually** by the developer changing the contract:

```bash
# from frontend/
npm run generate:api          # regenerates all four clients
npm run generate:api -- --host customer    # regenerate one
```

Under the hood, `npm run generate:api`:
1. Reads `frontend/nswag/config.json` listing the four host URLs and target output paths.
2. Hits the dev backend at `http://localhost:5001/openapi/v1.json` etc. (developer must have the backend running).
3. Runs NSwag CLI to generate the TypeScript files.
4. Runs `prettier` over the output for consistent diffs.

The developer commits the regenerated files in the same PR as the backend change.

### CI enforces parity

CI runs a parity check on every PR:

1. Build the backend solution.
2. Start each `Web.*` host in test mode.
3. Fetch `/openapi/v1.json` from each.
4. Compute a stable hash of each spec (normalized: keys sorted, whitespace stripped).
5. Compare against the committed hash in `frontend/src/lib/api-client/.spec-hashes.json`.

If hashes don't match: **CI fails** with a clear message telling the developer to regenerate. This catches the "I changed the controller but forgot to regenerate the client" class of mistake.

The `.spec-hashes.json` file is committed alongside the generated client. It's the audit trail of which spec version produced which client.

### Blocking manual edits

A pre-commit hook (Husky) and a CI check enforce that files in `frontend/src/lib/api-client/` are not edited by hand unless the corresponding `.spec-hashes.json` entry has also changed. The README at the top of `api-client/` says so explicitly with a one-paragraph warning.

If a developer needs to add helper methods (e.g. a wrapper around the generated client), they go in `frontend/src/lib/api-client-helpers/` — separate folder, separate from generated output.

### Apifetch wrapper

The generated NSwag clients are *low-level*: they make raw HTTP calls and throw on non-2xx. The frontend's `lib/runtime/api-fetch.ts` wraps every call:

```ts
export async function apiFetch<T>(
  call: () => Promise<T>
): Promise<Result<T, ApiError>> {
  try {
    const value = await call();
    return Ok(value);
  } catch (e) {
    if (e instanceof ApiException) {
      const error = parseBackendError(e);
      if (error.kind === 'Unauthorized') {
        const refreshed = await tryRefresh();
        if (refreshed) {
          const retried = await call();
          return Ok(retried);
        }
      }
      return Err(error);
    }
    return Err(networkError());
  }
}
```

This turns the generated client's exceptions into the `Result<T, ApiError>` discriminated union that mirrors the backend's `BusinessResult<T>`. All frontend code calls APIs through this wrapper.

### Local development workflow

1. Backend dev changes a controller signature.
2. Backend dev runs the backend locally (`dotnet run --project Makables.Web.Customer`).
3. Frontend dev (could be the same person) runs `npm run generate:api -- --host customer`.
4. Generated diff appears in the working tree; frontend dev commits it with their consuming change.
5. PR opens; CI verifies parity.

For the autonomous build phase: the `dotnet-backend` agent regenerates the client in the same ticket where it adds or modifies a controller. The `frontend` agent consumes the regenerated types in the same ticket if cross-stack, or in a follow-up ticket if the contract is read-only and stable.

### Version coexistence

When `v2` arrives:
- Backend exposes both `/openapi/v1.json` and `/openapi/v2.json`.
- `generate:api` produces both `customer-api.v1.client.ts` and `customer-api.v2.client.ts`.
- Frontend code gradually migrates imports from `v1` to `v2`.
- After the deprecation window, the `v1` client file is deleted; backend removes the v1 endpoints.

### NSwag generator config

`frontend/nswag/config.json`:

```json
{
  "runtime": "Net80",
  "defaultVariables": null,
  "documentGenerator": {
    "fromDocuments": [
      { "url": "http://localhost:5001/openapi/v1.json", "output": "../src/lib/api-client/customer-api.v1.ts", "className": "CustomerApi" },
      { "url": "http://localhost:5002/openapi/v1.json", "output": "../src/lib/api-client/maker-api.v1.ts", "className": "MakerApi" },
      { "url": "http://localhost:5003/openapi/v1.json", "output": "../src/lib/api-client/admin-api.v1.ts", "className": "AdminApi" },
      { "url": "http://localhost:5004/openapi/v1.json", "output": "../src/lib/api-client/public-api.v1.ts", "className": "PublicApi" }
    ]
  },
  "codeGenerators": {
    "openApiToTypeScriptClient": {
      "template": "Fetch",
      "promiseType": "Promise",
      "operationGenerationMode": "MultipleClientsFromOperationId",
      "typeScriptVersion": 5.0,
      "exceptionClass": "ApiException",
      "withCredentials": true
    }
  }
}
```

Production / staging URLs are not used by the generator — generator always reads from local. CI starts the backend locally to generate the parity check spec.

## Alternatives considered

- **Hand-written TypeScript clients** — rejected. Drift between backend and frontend is inevitable; hand-writing wastes time and produces bugs.
- **OpenAPI generator (`openapi-typescript-codegen`) instead of NSwag** — viable but less integrated with the .NET tooling. NSwag is the Cleansia precedent and handles edge cases of Swashbuckle's spec well.
- **Publish the OpenAPI spec as a versioned npm package** — overkill at MVP. Reconsider when we have a third-party consumer.
- **Auto-run NSwag in a Git hook** — rejected. Hooks tend to fail silently or with confusing errors. CI parity check + explicit `npm run generate:api` is more robust.
- **Generate clients at build time inside the frontend's `next build`** — rejected. Slows local dev; couples build to backend availability.

## Consequences

### Positive
- Type safety end to end. Renaming a field on the backend causes the frontend to fail to compile.
- Generated diff is reviewable in PRs.
- Parity check in CI catches the "forgot to regenerate" mistake every time.
- Deprecation and version migration have a clear mechanic.

### Negative
- Requires the backend to be running locally for generation. Mitigated: documented in `frontend/README.md`; Docker compose can bring up backend for frontend devs who don't want to run .NET.
- Generated files are large and appear in PR diffs. Mitigated: prettier normalizes formatting; reviewers focus on `.spec-hashes.json` for the change signature.

## Compliance / verification

- Reviewer: any PR changing a controller signature also commits regenerated `*.v1.ts` files AND an updated `.spec-hashes.json`.
- Reviewer: no manual edits to `api-client/*.ts` files (other than the helper folder).
- CI: spec-hash parity check passes.
- CI: `npm run generate:api` produces an empty diff against committed clients (idempotent).

## Related

- Patterns: §A.21 NSwag client generation, §B.4 calling the API via api-fetch
- ADR 0007 (NSwag is the contract)
- ADR 0021 (versioning surfaces in the URL and the generated client filename)
