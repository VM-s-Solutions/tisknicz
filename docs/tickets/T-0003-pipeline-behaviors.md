---
id: T-0003
title: MediatR pipeline behaviors — Validation + UnitOfWork
status: done
size: S
owner: dotnet-backend
created: 2026-05-22
updated: 2026-05-22
depends_on: [T-0001, T-0004, T-0002]
blocks: [T-0007, T-0008, T-0011]
user_stories: []
adrs: [0002]
phase: 1
---

# T-0003 — MediatR pipeline behaviors

Per ADR 0002 / patterns §A.5. Two behaviors run on every MediatR request:

1. **`ValidationPipelineBehavior<TRequest, TResponse>`** — fans out to all registered `IValidator<TRequest>`, runs them in parallel, on failure short-circuits the handler and returns `BusinessResult.ValidationFailure(...)`. Maps FluentValidation's `ValidationFailure` to our `ValidationDetail` at the boundary (keeps `Core.Domain` free of the FluentValidation dependency).

2. **`UnitOfWorkPipelineBehavior<TRequest, TResponse>`** — wraps only commands (`TRequest : ICommandMarker`), calls `IUnitOfWork.SaveChangesAsync` after handler success.

## Scope

- `Core.AppServices/Behaviors/ValidationPipelineBehavior.cs`
- `Core.AppServices/Behaviors/UnitOfWorkPipelineBehavior.cs`
- `Core.AppServices/Abstractions/ICommand.cs` — introduces `ICommandMarker` interface so both `ICommand` and `ICommand<TResponse>` can be selected by `where TRequest : ICommandMarker` in the UoW behavior

## Acceptance criteria

- **AC-1** Build clean.
- **AC-2** 8 pipeline tests pass.
- **AC-3** Validation failure on a non-typed command returns `BusinessResult.ValidationFailure` and does NOT commit UoW.
- **AC-4** Validation failure on a typed command returns `BusinessResult<T>.ValidationFailure` (correct generic type) and does NOT commit UoW.
- **AC-5** Successful command commits UoW exactly once.
- **AC-6** Handler-returned `Failure` does NOT commit UoW.
- **AC-7** Query (not `ICommandMarker`) is NOT touched by `UnitOfWorkPipelineBehavior`.
- **AC-8** Pipeline works with zero registered validators (no-op short-circuit at top of `ValidationPipelineBehavior.Handle`).

## Status log

- 2026-05-22 done. 68 tests pass (was 60; +8 pipeline tests).
- Build failure during dev: MediatR 13 requires `ILoggerFactory` in DI; fixed by adding `services.AddLogging()` to the test container.
- Design choice: `ICommandMarker` introduced as a non-generic marker because `ICommand<TResponse>` does NOT derive from `ICommand` (their `IRequest<...>` parents conflict). Both inherit from `ICommandMarker` instead.
