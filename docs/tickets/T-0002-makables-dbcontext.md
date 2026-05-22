---
id: T-0002
title: MakablesDbContext + soft-delete query filter + AuditableSaveChangesInterceptor
status: done
size: M
owner: dotnet-backend
created: 2026-05-22
updated: 2026-05-22
depends_on: [T-0001, T-0004, T-0006]
blocks: [T-0007, T-0008, T-0010, T-0011]
user_stories: []
adrs: [0011, 0013, 0014]
phase: 1
---

# T-0002 — MakablesDbContext + soft-delete query filter + audit interceptor

## Scope

- `IUnitOfWork` interface in `Core.Domain/SeedWork/`
- `MakablesDbContext` in `Infra.Database/` — empty of entity sets (Phase 2+ adds them); applies `IEntityTypeConfiguration<T>` from the assembly; applies global query filter on `IsActive` for every `Auditable`-derived entity
- `AuditableSaveChangesInterceptor` in `Infra.Database/Interceptors/` — reads `IUserSessionProvider` + `IClock`, stamps `CreatedBy/At` on Added entities (only if not pre-set) and `UpdatedBy/At` on Modified entities; falls back to `"system"` for anonymous callers
- Test harness using SQLite in-memory + test-only `WidgetEntity : Auditable`
- 5 interceptor tests + 3 soft-delete filter tests
- Moved `IUserSessionProvider` from `Core.AppServices.Abstractions` to `Core.Domain.Common` (the interceptor in `Infra.Database` needs it; `Infra.*` does not reference `Core.AppServices` per ADR 0001)

## Side-deliverables: T-0006 reviewer follow-ups

- Money positional ctor now validates + normalizes (T-0006 MINOR #3)
- Two new tests: `Direct_Constructor_Normalizes_Currency`, `Direct_Constructor_Rejects_Wrong_Length`
- Renamed `MarkDeactivated_Is_Idempotent_Stamp_Wise` → `MarkDeactivated_Called_Twice_Last_Call_Wins` (T-0006 MINOR #4)

## Out of scope

- Concrete production entities (Phase 2+)
- Country query filter at the DB layer (application-layer scoping per ADR 0013)
- RLS (not used per ADR 0013)
- Migrations runner (T-0008 wires the migrate-on-startup)
- Reviewer T-0006 MAJOR #1 (patterns.md doc drift on `CreatedOn` vs `CreatedAt`) — separate docs ticket
- Reviewer T-0006 MAJOR #2 (role files for IClock, IIdGenerator, IUserSessionProvider) — separate docs ticket

## Acceptance criteria

- **AC-1** Build clean.
- **AC-2** 8 new tests pass (5 interceptor + 3 soft-delete filter).
- **AC-3** Interceptor stamps `CreatedBy/At` on Added; falls back to `"system"` when no user.
- **AC-4** Interceptor honours pre-set `CreatedBy` (seed/migration scenarios).
- **AC-5** Interceptor stamps `UpdatedBy/At` on Modified; `CreatedAt` unchanged.
- **AC-6** Global query filter excludes soft-deleted rows from default queries; `IgnoreQueryFilters()` returns them.
- **AC-7** No `Core.AppServices` reference from `Infra.Database` (verified via csproj).

## Status log

- 2026-05-22 done. 60 tests pass.
