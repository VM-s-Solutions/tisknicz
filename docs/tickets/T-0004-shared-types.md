---
id: T-0004
title: Shared types — BusinessResult, Error, ErrorType, BusinessErrorMessage, ICommand / IQuery, MakablesApiController
status: done
size: S
owner: dotnet-backend
created: 2026-05-22
updated: 2026-05-22
depends_on: [T-0001]
blocks: [T-0002, T-0003, T-0005, T-0006, T-0007, T-0010, T-0011]
user_stories: []
adrs: [0002]
phase: 1
---

# T-0004 — Shared types

## Context

Reordered ahead of T-0002 because these types are pure C# in `Core.Domain` + `Core.AppServices` and depend on nothing in the Infra layer. Every subsequent ticket consumes them.

## Scope

Following [ADR 0002](../adr/0002-command-query-and-result.md) and `docs/architecture/patterns.md` §A.3, §A.4, §A.6.

In `Makables.Core.Domain/Common/`:
- `BusinessResult` (non-generic) + `BusinessResult<T>` (generic) with `Success` / `Failure` factories and `ValidationFailure(IEnumerable<ValidationFailure>)` helper
- `Error` sealed record: `(string Field, string Code, ErrorType Type, object? Details)` with static factories `Validation`, `NotFound`, `Conflict`, `Forbidden`, `Unauthorized`, `Transient`, `Permanent`, `Configuration`, `Unknown`
- `ErrorType` enum: `Validation, Unauthorized, Forbidden, NotFound, Conflict, Transient, Permanent, Configuration, Unknown`

In `Makables.Core.AppServices/Abstractions/`:
- `ICommand` (returns `BusinessResult`) and `ICommand<TResponse>` (returns `BusinessResult<TResponse>`)
- `IQuery<TResponse>` (returns `BusinessResult<TResponse>`)
- `ICommandHandler<TCommand>`, `ICommandHandler<TCommand, TResponse>`, `IQueryHandler<TQuery, TResponse>` — derive from `IRequestHandler<TRequest, TResponse>`

In `Makables.Core.AppServices/Common/`:
- `BusinessErrorMessage` static class with dot-notation error code constants (`auth.required`, `auth.forbidden`, `order.notFound`, `validation.required`, `validation.minLength`, etc.). Stub the categories now; expand as features ship.

In `Makables.Config/Controllers/`:
- `MakablesApiController` abstract base inheriting `ControllerBase`. Provides `Mediator` (resolved lazily from request services) and `HandleResult(BusinessResult)` + `HandleResult<T>(BusinessResult<T>)` that map `ErrorType` → HTTP status.

Unit tests in `Makables.Tests/`:
- Construction round-trips: `Success` is success; `Failure(Error)` is not.
- Error factory shape: `Error.NotFound("order")` produces `("order", "order.notFound", ErrorType.NotFound)`.
- `BusinessResult.ValidationFailure(...)` aggregates FluentValidation `ValidationFailure` items into a single Error.

## Out of scope

- Pipeline behaviors (T-0003)
- Money value object (T-0005)
- `Auditable` base entity (T-0006)
- Any actual command, query, controller — only the abstractions and base classes

## Acceptance criteria

- **AC-1** `dotnet build` clean (no new warnings/errors).
- **AC-2** `dotnet test` passes. At least 6 unit tests covering BusinessResult / Error / BusinessErrorMessage.
- **AC-3** `BusinessResult.Success()` / `.Failure(error)` / `.Success<T>(value)` / `.Failure<T>(error)` / `.ValidationFailure(failures)` all compile and behave correctly.
- **AC-4** Every `ErrorType` enum value maps to a sensible HTTP status code in `MakablesApiController.HandleResult` (per patterns §A.6 table).
- **AC-5** `BusinessErrorMessage` constants are dot-notation strings; verified by inspection.
- **AC-6** `ICommand` / `ICommand<TResponse>` / `IQuery<TResponse>` derive from MediatR's `IRequest<TResponse>` chain.

## Files touched (expected)

- `backend/src/Makables.Core.Domain/Common/BusinessResult.cs`
- `backend/src/Makables.Core.Domain/Common/Error.cs`
- `backend/src/Makables.Core.Domain/Common/ErrorType.cs`
- `backend/src/Makables.Core.AppServices/Abstractions/ICommand.cs`
- `backend/src/Makables.Core.AppServices/Abstractions/IQuery.cs`
- `backend/src/Makables.Core.AppServices/Abstractions/ICommandHandler.cs`
- `backend/src/Makables.Core.AppServices/Abstractions/IQueryHandler.cs`
- `backend/src/Makables.Core.AppServices/Common/BusinessErrorMessage.cs`
- `backend/src/Makables.Config/Controllers/MakablesApiController.cs`
- `backend/src/Makables.Tests/Common/BusinessResultTests.cs`
- `backend/src/Makables.Tests/Common/ErrorTests.cs`

## Status log

- 2026-05-22 `draft → ready → in_progress` by PM. Reordered ahead of T-0002 because T-0002's interceptor depends on `Auditable` (T-0006), which in turn benefits from these types being in place.
- 2026-05-22 `in_progress → done`. All ACs satisfied:
  - **AC-1** Build clean: 0 warnings, 0 errors
  - **AC-2** 15 unit tests pass (8 in BusinessResultTests, 7 in ErrorTests)
  - **AC-3** BusinessResult Success/Failure/ValidationFailure all behave correctly (generic + non-generic)
  - **AC-4** All 9 ErrorType values map to HTTP codes (Validation 400, Unauthorized 401, Forbidden 403, NotFound 404, Conflict 409, Transient 503, Permanent 422, Configuration 500, Unknown 500)
  - **AC-5** BusinessErrorMessage holds ~45 dot-notation codes across auth/validation/order/product/maker/registry/country/payment/shipping/review/file/payout/user
  - **AC-6** ICommand / ICommand&lt;T&gt; / IQuery&lt;T&gt; derive from `IRequest<...>`

  Adaptation: `BusinessResult.ValidationFailure` originally typed against FluentValidation per ADR 0002, but that would have forced `Core.Domain` to take a FluentValidation reference (violates ADR 0001). Introduced `ValidationDetail(Field, Code, Message)` record in `Core.Domain.Common`; T-0003's validation behavior will map FluentValidation `ValidationFailure` → `ValidationDetail` at the boundary.
