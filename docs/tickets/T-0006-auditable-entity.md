---
id: T-0006
title: Auditable base entity + IClock + IIdGenerator + IUserSessionProvider
status: done
size: S
owner: dotnet-backend
created: 2026-05-22
updated: 2026-05-22
depends_on: [T-0001, T-0004]
blocks: [T-0002, T-0007, T-0010, T-0011]
user_stories: []
adrs: [0011, 0013, 0014]
phase: 1
---

# T-0006 — Auditable base + Clock + Id generator + UserSessionProvider

Per [ADR 0014 / patterns §A.11](../adr/0014-admin-audit-log.md) and role files. Delivers the entity bases used by every aggregate; the `AuditableSaveChangesInterceptor` itself lands in T-0002 (it requires `MakablesDbContext`).

## Scope

- `IEntity` / `IEntity<TKey>` in `Core.Domain/Common/`
- `BaseEntity : IEntity<string>` with mutable `Id` (set by concrete factories via `IIdGenerator`) and `IsActive`
- `Auditable : BaseEntity` with `CountryCode` + `CreatedBy/At` + `UpdatedBy/At` + `DeactivatedBy/At` + `MarkCreated/MarkUpdated/MarkDeactivated` methods
- `IClock` in `Core.Domain/Common/`; `SystemClock` impl in `Infra.Common/Time/`
- `IIdGenerator` in `Core.Domain/Common/`; `UlidIdGenerator` impl in `Infra.Common/Identifiers/` (keeps the Ulid package dep out of Core.Domain per ADR 0001)
- `IUserSessionProvider` in `Core.AppServices/Abstractions/` with `GetUserId/GetUserEmail/GetUserCountryCode` (no impl — supplied by Web hosts in T-0009)

## Out of scope

- `AuditableSaveChangesInterceptor` (T-0002 — needs `MakablesDbContext`)
- `HttpContextUserSessionProvider` / `SystemUserSessionProvider` impls (T-0009)
- Any concrete entity (Order, Maker, etc.) inheriting `Auditable` — those are Phase 2+

## Acceptance criteria

- **AC-1** Build clean.
- **AC-2** 10 tests pass: 7 for `Auditable` (initial state, MarkCreated, MarkUpdated, MarkDeactivated, idempotency, country preserved, Id roundtrip) + 3 for `UlidIdGenerator` (non-empty, unique-100, lexicographic monotonicity).
- **AC-3** ADR 0001 layering preserved: `Core.Domain` csproj still has zero `PackageReference` (verified — only the `Ulid` package is in `Infra.Common`).

## Side-deliverable: T-0005 reviewer follow-up

Reviewer of T-0005 (commit `eb6e593`) returned APPROVED with 3 MAJOR test-coverage gaps. Backfilled in this commit:

- `Subtract_Different_Currency_Throws` — currency mismatch on Subtract
- `Subtract_Underflow_Throws` — overflow protection on Subtract's negative path
- `PercentOfBp_Overflow_Throws` — overflow protection on PercentOfBp's decimal→long cast
- `PercentOfBp_On_Negative_Rounds_Away_From_Zero` — locks half-up semantics on negatives (MINOR #5)
- `Zero_Equals_CZK_Zero` — `Money.Zero(...)` factory test (MINOR #6)

Total Money tests now: 25 (was 20).

## Status log

- 2026-05-22 done. 50 tests pass (was 35; +7 Auditable + 3 UlidIdGenerator + 5 Money follow-ups).
