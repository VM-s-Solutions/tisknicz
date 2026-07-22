---
id: T-0113
title: EvictExpiredRegistryCache Function (timer) — prune stale ARES cache rows
status: done
size: S
owner: dotnet-backend
created: 2026-07-22
updated: 2026-07-22
depends_on: [T-0032, T-0029]
blocks: []
user_stories: []
adrs: [0018, 0020]
phase: 5
manual_steps: []
security_touching: false
layers: [dotnet-backend, dotnet-db]
---

# T-0113 — EvictExpiredRegistryCache Function (timer)

## Context

The ARES company-registry DB cache (`company_registry_cache`, T-0032) has no
reaper. Per ADR 0018 §"Caching policy" a row is a usable stale-fallback only
while `FetchedAt > now - StaleFallbackDays` (7 days); past that it can never be
served yet still occupies a row. `AresCompanyRegistry` upserts on every
cache-miss lookup, so without eviction the table grows unbounded — the exact
kind of manual-maintenance debt a self-running marketplace must avoid
(CLAUDE.md). The `CompanyRegistryCacheEntry` XML-doc already names this
Function (`EvictExpiredRegistryCache`) as the deferred owner of the cleanup.

## Scope

- **`ICompanyRegistryCacheStore.EvictFetchedBeforeAsync(fetchedBefore, ct)`** —
  new store method returning the affected-row count. Raw set-based
  `DELETE FROM company_registry_cache WHERE fetched_at < {cutoff}` in the
  store's own `IDbContextFactory` scope, mirroring the existing raw-SQL
  `UpsertAsync` in the same file (the EF Core SQLite test provider cannot
  translate a `DateTimeOffset` comparison, and `fetched_at` is always written
  UTC so the parameter comparison is chronologically correct on both
  providers).
- **`EvictExpiredRegistryCacheFunction`** (`Makables.Functions/Registry/`) —
  thin timer Function. Computes `cutoff = clock.UtcNow - StaleFallbackDays`
  (clamped `Max(1, …)` exactly like the read path so the two windows can never
  drift), calls the store, logs the removed count. No business logic in the
  Function.
- **`functions.bicep`** — new `evictExpiredRegistryCacheSchedule` param
  (default `0 30 2 * * *`, daily 02:30 UTC — offset from T-0083's 02:00 so the
  nightly cleanup jobs don't fire together) + `EvictExpiredRegistryCache__Schedule`
  app setting.
- Tests: store-level (deletes only rows before the cutoff; strict `<` boundary;
  empty table → 0) + Function-level (cutoff = now − StaleFallbackDays; clamps
  non-positive config to 1 day).

## Alternatives Considered

- **LINQ `ExecuteDeleteAsync` with a `DateTimeOffset` predicate** — *rejected:
  the EF Core SQLite provider (integration tests) cannot translate a
  `DateTimeOffset` server-side comparison; raw SQL matches the file's existing
  `UpsertAsync` pattern and stays provider-agnostic.*
- **Materialize rows then `RemoveRange`** — *rejected: loads the whole expired
  set and still needs the untranslatable filter; raw SQL is set-based and
  cheaper.*
- **A MediatR command dispatched by the Function (T-0083 shape)** — *rejected:
  eviction is pure infrastructure bookkeeping — no domain transition, no
  outbox, no `BusinessResult`; the store already owns the isolated-DbContext
  seam, so a command would be ceremony.*

## Out of scope

- Changing the 24h TTL / 7-day stale-fallback windows (ADR 0018 policy).
- The in-memory hot cache (its own `IMemoryCache` TTL, no persistence to prune).
- GDPR data-retention cleanup (separate T-0114).

## Acceptance criteria

- **AC-1** Given cache rows fetched more than `StaleFallbackDays` ago, when the
  Function runs, then exactly those rows are deleted and rows within the window
  are kept.
- **AC-2** Given a row fetched exactly at the cutoff, when eviction runs, then
  it is NOT deleted (strict `<`, so the stale-fallback window is never trimmed
  by a boundary error).
- **AC-3** Given a misconfigured `StaleFallbackDays` ≤ 0, when the Function
  runs, then the cutoff is clamped to 1 day (matches the read path) so
  still-usable rows are never evicted.
- **AC-4** Given an empty (or all-fresh) table, when the Function runs, then it
  deletes 0 rows and logs a clean zero count.

## Technical notes

- Eviction cutoff derives from `IOptions<AresOptions>.StaleFallbackDays`, the
  same value `AresCompanyRegistry` uses to accept a stale row — single source
  of truth, no drift.
- The store's own `IDbContextFactory` scope keeps the delete isolated from any
  request UoW (same reason the rest of `CompanyRegistryCacheStore` does).

## Files touched

- `Makables.Core.Domain/Registry/ICompanyRegistryCacheStore.cs`
- `Makables.Infra.Database/Registry/CompanyRegistryCacheStore.cs`
- `Makables.Functions/Registry/EvictExpiredRegistryCacheFunction.cs` (new)
- `infra/bicep/modules/functions.bicep`
- `Makables.Tests/Infra/Database/Registry/CompanyRegistryCacheStoreTests.cs` (new)
- `Makables.Tests/Functions/Registry/EvictExpiredRegistryCacheFunctionTests.cs` (new)

## Test plan reference

Covered by the two new xUnit classes (6 cases). Full unit suite green
(1865/1865); `az bicep build` clean. Integration tests need Docker
(Testcontainers) — unaffected by this change.

## Status log

- 2026-07-22 `draft → in_progress → done` — built while the dev environment
  (T-0153 walk) was blocked on an operator Azure restart. Picked as the
  highest-value unblocked ticket (self-running-marketplace hygiene, fully
  offline-verifiable). One PR, `feat/T-0113-evict-registry-cache`.
