---
id: 0002
title: Command/Query split, Result type, AppError, pipeline middleware
status: accepted
date: 2026-05-21
deciders: [Architect, user]
---

# 0002 — Command/Query split, Result type, AppError, pipeline middleware

## Context
We need a consistent way to express use cases, handle expected failures, validate input, and bound transactions. The user has a strong baseline from CQRS + MediatR in .NET. We want the same shape in TypeScript without a heavyweight library.

## Decision
Adopt `patterns.md` §3, §4, §5, §6 as a bundle.

1. **Use cases are explicit:** every server-side action is a **command** (mutates) or a **query** (reads). Marker types `Command<TIn,TOut>` and `Query<TIn,TOut>` in `domain/shared/command-query.ts`.
2. **All handlers return `Result<T, AppError>`** — discriminated union with `ok: true | false`. `Ok(value)` and `Err(error)` constructors. No `throw` for expected failures.
3. **`AppError` is uniform**: `{ kind: ErrorKind; code: string; message: string; details?: unknown }`. `ErrorKind` includes request-level (`Validation`, `NotFound`, `Conflict`, `Forbidden`, `Unauthorized`) and integration-level (`Transient`, `Permanent`, `Configuration`, `Unknown`). Codes are dot-notation strings from a centralized `ErrorCodes` object.
4. **Pipeline middleware** wraps every handler: `withRequestContext → withAuth → withValidation → withLogging → withTransaction → handler`. Composed via `compose(...)` per route. Handlers contain happy-path logic only.
5. **`handleResult(Result<T>)` maps to `NextResponse`** with HTTP status codes per `ErrorKind` (400/401/403/404/409/422/500/503).
6. **`withTransaction` only wraps commands.** Queries skip it.
7. **Feature file structure**: one file per use case under `src/lib/features/<entity>/<use-case>.ts`. Exports the Zod input schema, the deps interface, and the curried handler.

## Alternatives considered

- **Throw exceptions for failures** — rejected. Loses type information about what can fail, conflates expected with unexpected errors, makes the API contract implicit.
- **Use a library like `neverthrow` or `fp-ts` for Result** — rejected. Both are great; both add a dependency and a learning curve. Our `Result` type is ~20 lines; we don't need monad transformers for an MVP.
- **MediatR-equivalent library (e.g., `tsyringe-mediator`)** — rejected. The composition is simple enough that a `compose(...mws)(handler)` helper does the job. No magic.
- **Inline business logic in Route Handlers** — rejected. Couples HTTP concerns to business logic; impossible to test without an HTTP layer.

## Consequences

- **Positive:** types tell the full story of every handler. Reviewer can see at a glance what can go wrong.
- **Positive:** retry logic for webhooks/jobs can branch on `error.kind` cleanly (see ADR for error classification, Batch 4).
- **Positive:** every command handler is automatically validated and transactional. No "I forgot to commit" bugs.
- **Negative:** more boilerplate per route than `if (!user) return 401`. Mitigation: a small `pipeline-builder` helper that defaults the common middleware so a typical Route Handler is 4 lines.
- **Negative:** developers used to throwing must internalize the `Result` discipline. Reviewer enforces.

## Compliance / verification

- Reviewer checklist: every handler return type is `Promise<Result<T>>`. Zero `throw` for expected failures in `features/`.
- Reviewer checklist: every command goes through `withTransaction`; every query does not.
- Reviewer checklist: every `AppError.code` is defined in `ErrorCodes` (no inline string literals).
- Reviewer checklist: every Route Handler ends with `return handleResult(await pipeline(...))`.
- Test convention: handler unit tests assert `result.ok === true/false` and `result.error?.code` for failure paths.

## Related
- Patterns: §3 Command vs Query, §4 Result type, §5 Pipeline middleware, §6 Feature file structure
- Depends on: ADR 0001 (layering)
