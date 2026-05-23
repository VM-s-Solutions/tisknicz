---
id: T-0013
title: NSwag pipeline — config, generate script, CI parity check, pre-commit hook
status: done
size: M
owner: dotnet-backend + frontend
created: 2026-05-23
updated: 2026-05-23
depends_on: [T-0009, T-0012]
blocks: [T-0015]
adrs: [0022]
phase: 1
---

# T-0013 — NSwag pipeline

## Scope
- `frontend/nswag/config.json` — points at all four hosts' `/openapi/v1.json`; defines per-host output paths under `src/lib/api-client/<host>-api.v1.ts`.
- `frontend/scripts/generate-api.mjs` — orchestrates NSwag. Probes each host's OpenAPI doc, skips unreachable hosts with a warning, runs NSwag, recomputes the SHA-256 hash, updates `.spec-hashes.json`.
- `frontend/scripts/check-api-parity.mjs` — CI parity check. Compares committed `.spec-hashes.json` against the live spec; exits non-zero on drift.
- `frontend/scripts/check-api-client-manual-edits.mjs` — pre-commit hook. Rejects staged commits that touch `*-api.v1.ts` without also touching `.spec-hashes.json`.
- `frontend/src/lib/api-client/README.md` — explains the contract.
- `frontend/src/lib/api-client/.spec-hashes.json` — initial empty placeholder.
- `frontend/package.json` — `generate:api`, `check:api`, `check:api-client` scripts; `nswag` 14.5 in devDependencies.

## Side-deliverable: T-0011 reviewer BLOCKER fix
Reviewer of `03f5991` returned BLOCKER: pipeline registration was `Validation → AdminAudit → UnitOfWork`. MediatR runs first-registered as outermost, so the audit row added after `next` returned was never persisted (SaveChanges had already happened). Swapped to `Validation → UnitOfWork → AdminAudit` so UoW wraps Audit, and a single SaveChanges flushes handler state + audit row atomically.

Other T-0011 reviewer findings (redaction list, pipeline e2e test, composite-PK assumption, catch-all suppression, entity-name ambiguity, marker scope) folded into the sprint-1 follow-up list.

## Out of scope
- Husky pre-commit wiring (T-0016).
- First real client generation (requires Phase 2 endpoint with DTOs).

## Acceptance criteria
- **AC-1** Build clean; 133 tests pass.
- **AC-2** All four NSwag config entries point at the right host URL.
- **AC-3** generate-api.mjs is idempotent.
- **AC-4** check-api-parity.mjs exits non-zero on drift.
- **AC-5** check-api-client-manual-edits.mjs rejects manual edits.
- **AC-6** T-0011 BLOCKER closed (pipeline order corrected).

## Status log
- 2026-05-23 done. 133 tests still pass. T-0011 BLOCKER closed.
