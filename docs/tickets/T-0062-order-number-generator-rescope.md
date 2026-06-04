# T-0062 — OrderNumberGenerator: TZ-aware year + race-safety + rollback test coverage

**Phase:** 4 (Orders)
**Size:** S (rescoped from original M-equivalent; one S interface change + Postgres test harness + 4 integration tests + 1 unit test)
**State:** `ready`
**Depends on:** T-0007 (`IOrderNumberGenerator` + `NumberingSequenceAllocator`), T-0060 (`Order.OrderNumber`), T-0010 (`CountryConfiguration` with `TimeZoneId`)
**Owner:** `dotnet-backend`
**ADRs:** 0009 (Numbering sequences), 0004 (CountryConfiguration)
**Role doc:** [docs/architecture/roles/order-numbering.md](../architecture/roles/order-numbering.md)

## Why now

T-0007 shipped the numbering infrastructure end-to-end (interface, `SELECT…FOR UPDATE` allocator, DI registration, format `M-{CC}-{YYYY}{NNNN}`) but left two **ADR 0009 compliance items open at lines 143–144**: (a) a failing command must leave `last_used_value` unchanged, (b) two concurrent commands must serialize and produce consecutive numbers. Both require a real Postgres (SQLite does not honour `FOR UPDATE` row locks the same way), and at T-0007's merge time no Postgres integration test harness existed. T-0061 confirmed `CountryConfiguration.TimeZoneId` exists (`CountryConfiguration.cs:24`), making it cheap to fix the second hidden bug surfaced during T-0062 research: the current `NextAsync(countryCode, int year, ct)` signature lets a caller pass `clock.UtcNow.Year`, which silently buckets a 23:30 Prague order on Dec 31 into the **previous year's** sequence — wrong for the customer-facing invoice.

T-0063 (`CreateOrder.Handler`) opens next and will wire the call site. T-0062 fixes the year contract and closes the two race-safety gaps **before** T-0063 hard-codes the wrong year source.

## Rescope note

The original INDEX framing "OrderNumber + IOrderNumberGenerator integration into CreateOrder" assumed `CreateOrder.Handler` exists. It does not (that is T-0063's scope). T-0062 is rescoped per user decision Q1 to the work that genuinely belongs **between T-0007 and T-0063**: a tighter generator contract + the compliance tests that should have shipped with T-0007 but were deferred for harness reasons. The "wiring into `CreateOrder`" lives in T-0063 verbatim per `patterns.md:413-454` — T-0062 just makes sure the call site can't get the year wrong and pins the race contract before that wiring lands.

## Scope

### Interface change (`IOrderNumberGenerator`)

Per user decision Q2, the generator owns the timezone-to-year conversion so callers cannot pass the wrong year by mistake. The current `Task<string> NextAsync(string countryCode, int year, CancellationToken)` becomes:

```csharp
public interface IOrderNumberGenerator
{
    Task<string> NextAsync(string countryCode, CancellationToken cancellationToken);
}
```

`OrderNumberGenerator` (the Postgres-backed impl) internally:
1. Loads `CountryConfiguration` by `countryCode` (via the existing `ICountryConfigurationRepository`).
2. Computes `var nowInCountryTz = TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow.UtcDateTime, TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId));`
3. Passes `nowInCountryTz.Year` to `NumberingSequenceAllocator.AllocateAsync(..., NumberingScope.Order, year, ct)`.
4. Returns the formatted string verbatim.

**Why this shape:**
- A caller passing `clock.UtcNow.Year` is now impossible — the parameter no longer exists at the public seam.
- The TZ lookup happens once per call (the existing FOR UPDATE row-lock already dominates per-call cost; this lookup is microseconds vs. milliseconds).
- IANA TZ IDs (`Europe/Prague`) work on Linux containers per [TimeZoneInfo.TryConvertIanaIdToWindowsId](https://learn.microsoft.com/en-us/dotnet/api/system.timezoneinfo) on .NET 8+ (.NET 10 inherits). On Windows hosts the framework auto-translates.

**Year-source contract** (documented in the role doc, see Docs section): the year is **country-local**, derived from `CountryConfiguration.TimeZoneId`. A 23:30 Prague order on 2026-12-31 gets a 2026-sequence number; a 02:00 Prague order on 2027-01-01 gets a 2027-sequence number. Matches what the customer sees on the invoice.

### Internal config-loader plumbing

`OrderNumberGenerator` already takes `(MakablesDbContext db, IClock clock)`. It now also takes `ICountryConfigurationRepository configs`. No new repository methods; uses the existing `GetByCodeAsync`. DI lifetime stays Scoped.

### `InvoiceNumberGenerator` + `PayoutBatchNumberGenerator` — out of scope

T-0062 changes the order generator only. Invoices (T-0068) and payout batches (T-0101) currently still take `int year`/`int weekNumber` parameters at their interfaces. They will be migrated to the same TZ-aware pattern in their respective tickets if/when needed — keeping T-0062 narrow per the S sizing. The internal `NumberingSequenceAllocator` does **not** change; only the order-shaped public seam moves.

### Postgres test harness (`Makables.IntegrationTests/Numbering/`)

`Testcontainers.PostgreSql` is already referenced in the integration-test csproj (`backend/src/Makables.IntegrationTests/Makables.IntegrationTests.csproj:16`) but never instantiated. T-0062 stands up the first harness:

- **`PostgresHarness`** at `backend/src/Makables.IntegrationTests/Common/PostgresHarness.cs` — `IAsyncLifetime` xunit fixture spinning up a `postgres:16-alpine` container (matching production), running `MakablesDbContext.Database.MigrateAsync()` (the real migration pipeline, closing the T-0123 gap for this surface) plus seeding the CZ `CountryConfiguration` row, and exposing a per-test `MakablesDbContext` scope.
- Shared via xunit `ICollectionFixture<PostgresHarness>` so all tests in the same collection reuse one container; per-test isolation via DB transaction rollback or per-test schema (decided in implementation — both are standard).
- This harness will be reused by every Phase-4 race-sensitive test that follows (T-0066 Comgate webhook race, T-0067 MarkPaid concurrency, T-0083 auto-cancel race).

T-0123 (already queued) is **reduced in scope** by this work — its remaining job is the dedicated migration-pipeline-only assertions; the harness itself ships in T-0062.

### Tests

- **`backend/src/Makables.IntegrationTests/Numbering/OrderNumberGeneratorRaceSafetyTests.cs`** (new, 3 tests, Postgres-only):
  - `Two_concurrent_NextAsync_calls_produce_consecutive_numbers` — `Task.WhenAll(genA.NextAsync("CZ", ct), genB.NextAsync("CZ", ct))` against independent `IServiceScope`s wrapping independent `MakablesDbContext` instances → results are exactly `{"M-CZ-20260001", "M-CZ-20260002"}` (order doesn't matter, just contiguous + duplicate-free). Closes ADR 0009:144 compliance item.
  - `Failed_command_leaves_last_used_value_unchanged` — start transaction, call `NextAsync`, throw before save, await rollback. Query `numbering_sequence` directly: row absent if first allocation, or `last_used_value` equals pre-call value. Closes ADR 0009:143 compliance item.
  - `First_allocation_race_creates_row_exactly_once` — empty `numbering_sequence`, two concurrent allocators for `(CZ, Order, 2026)`. Exactly one INSERT wins; the loser either retries cleanly (gets `0002`) or the test asserts whatever the actual behaviour is and pins it. Validates the CREATE-then-INCREMENT branch in `NumberingSequenceAllocator.cs:52-56`.

- **`backend/src/Makables.IntegrationTests/Numbering/OrderNumberGeneratorYearContractTests.cs`** (new, 2 tests, Postgres-only):
  - `NextAsync_buckets_orders_by_country_local_year_not_UTC` — seed `IClock` to `2026-12-31T22:30:00Z` (23:30 in Prague, still 2026-12-31 local) → number includes `2026`. Seed `IClock` to `2026-12-31T23:30:00Z` (00:30 in Prague on 2027-01-01) → number includes `2027`. **This is the test that proves Q2 was the right call.**
  - `NextAsync_throws_clean_error_when_country_configuration_missing` — call `NextAsync("XX", ct)` for an unseeded country → either `BusinessResult.Failure` or `InvalidOperationException` per implementation choice. Document the contract.

- **`backend/src/Makables.Tests/Domain/Numbering/OrderNumberGeneratorDelegationTests.cs`** (new, 1 unit test, NSubstitute):
  - `NextAsync_forwards_NumberingScope_Order_and_country_local_year_to_allocator` — verifies the generator passes `NumberingScope.Order` (not Invoice / PayoutBatch) and the country-local year through to whichever internal collaborator the impl chose. Acts as a regression net if `NumberingScope.Order` is ever swapped or the TZ conversion is bypassed.

- **Update existing tests** to the new signature:
  - `backend/src/Makables.Tests/Domain/Numbering/NumberingSequenceTests.cs` — no change (this tests the entity, not the generator).
  - `backend/src/Makables.IntegrationTests/HostStartup/WebHostStartupTests.cs` — DI smoke test stays valid; the interface is still resolvable.
  - No callers of `IOrderNumberGenerator.NextAsync` exist yet (T-0063 will add the first), so no consumer-side updates needed.

### Docs

- **`docs/architecture/roles/order-numbering.md`** — update the contract line to say "year is country-local, derived from `CountryConfiguration.TimeZoneId`." Cross-ref ADR 0009.
- **`docs/adr/0009-numbering.md`** — add a one-paragraph amendment under "Decision" capturing the TZ-aware-year rule and the rationale (customer-facing year on invoice should match the customer's local calendar). Note that invoice + payout-batch generators retain the explicit-year parameter for now and migrate when their tickets land.
- **`backend/src/Makables.Infra.Database/UniqueConstraintTranslator.cs`** — no change needed; the existing "intentionally unmapped: ix_orders_order_number" rationale (T-0060 M-1) holds — the new TZ-aware path doesn't change the monotonic invariant.

## Acceptance criteria

- **AC-1** (Race safety, ADR 0009:144) Given a clean DB with a CZ row in `country_configuration`, when two `OrderNumberGenerator.NextAsync("CZ", ct)` calls run on independent `IServiceScope`s with `Task.WhenAll`, then the two returned numbers are contiguous (`{"M-CZ-20260001", "M-CZ-20260002"}` modulo order) with zero duplicates. Verified by a Postgres Testcontainers integration test.
- **AC-2** (Rollback safety, ADR 0009:143) Given a transaction that calls `NextAsync` then throws, when the transaction rolls back, then the row in `numbering_sequence` for `(CZ, Order, 2026)` either does not exist (first-allocation rollback) or has `last_used_value` equal to its pre-call value. Verified by a Postgres Testcontainers integration test.
- **AC-3** (First-allocation race) Given an empty `numbering_sequence` table, when two concurrent allocators race to call `NextAsync("CZ", ct)` for the same year, then exactly one INSERT wins; the loser either retries cleanly (returns `0002`) or fails fast with a translated error. The behaviour is pinned by an integration test so a future allocator rewrite cannot regress silently.
- **AC-4** (TZ-aware year, user Q2) Given `IClock` returns `2026-12-31T22:30:00Z` (23:30 in `Europe/Prague`, still local 2026), when `NextAsync("CZ", ct)` runs, then the returned number contains `2026`. Given `IClock` returns `2026-12-31T23:30:00Z` (00:30 local 2027), then the returned number contains `2027`. Verified by a parameterised Postgres integration test.
- **AC-5** (Interface contract) Given the codebase, when it builds, then `IOrderNumberGenerator.NextAsync(string countryCode, CancellationToken)` is the only public method (the `int year` parameter is removed). `OrderNumberGenerator` reads `CountryConfiguration.TimeZoneId` internally and computes the country-local year; no caller can supply the wrong year.
- **AC-6** (Generator delegation) Given a unit test with NSubstitute mocks, when `OrderNumberGenerator.NextAsync` runs, then it forwards `NumberingScope.Order` (verified by the assertion on the mocked allocator/repository call). Acts as a fast regression net.
- **AC-7** (Test harness) Given the integration test suite runs, when complete, then `PostgresHarness` spins up `postgres:16-alpine` via Testcontainers, runs `MakablesDbContext.Database.MigrateAsync()` to apply every migration (closing the T-0123 migration-pipeline coverage gap for this surface), seeds the CZ `CountryConfiguration` row, and is shared via xunit `ICollectionFixture` so multiple tests reuse one container.
- **AC-8** (Test suite) Build clean. Unit tests count: 866 (baseline 865 + 1 new). Integration tests count: 89+ (baseline 84 + 5 new Postgres-only tests + 0 changes to existing). Total: 955+.
- **AC-9** (Docs) `docs/architecture/roles/order-numbering.md` documents the country-local-year contract. `docs/adr/0009-numbering.md` amends the Decision section with the TZ-aware-year rule and explains why invoice + payout generators retain the explicit-year parameter for now.
- **AC-10** (T-0123 scope reduction) Update `docs/tickets/INDEX.md` row for T-0123 to note that the Postgres test harness now ships in T-0062; T-0123's remaining scope is the dedicated migration-pipeline-only assertions.

## Out of scope

- Invoice (`IInvoiceNumberGenerator`) and payout batch (`IPayoutBatchNumberGenerator`) interface migrations to the TZ-aware pattern (T-0068 and T-0101 respectively, if/when needed).
- `CreateOrder.Command/Validator/Handler/Response` — T-0063 owns the entire feature.
- Idempotency / deduplication of duplicate `CreateOrder` submissions (T-0063 territory).
- The Czech i18n catalogue keys for any new `BusinessErrorMessage` codes — none added here (the generator throws or returns a typed result; the user-facing error surfaces in T-0063 once a handler exists).

## Technical notes

### Why this isn't just a T-0123 sub-task

T-0123 ships the Postgres-based migration-validation harness. T-0062 ships the Postgres-based race-safety harness. They are the same physical infrastructure (`Testcontainers.PostgreSql` + `Database.MigrateAsync()`), but the work is gated by different needs: T-0062 cannot land before T-0063 wires `IOrderNumberGenerator`, and T-0063 cannot land before the race-safety tests are pinned. Doing T-0062 first means the harness exists when T-0066/T-0067/T-0083 need it, and T-0123 reduces to dedicated migration-only assertions. Net effort is the same; the harness lands ~1 sprint earlier.

### Why the interface changes the year parameter, not just the impl

A caller passing `clock.UtcNow.Year` to the existing signature would compile, run, and ship the wrong year for 30 minutes per year. Removing the parameter forces every caller to opt into the TZ-aware contract. The internal `NumberingSequenceAllocator` keeps its `int year` parameter because it's the shared infrastructure that invoice + payout generators also use — they will adopt the TZ-aware pattern at their layer if/when needed (likely T-0068 for invoices, since invoices are legally regulated and the year mismatch would be a real compliance issue).

### Why Postgres 16 specifically

Production uses Postgres 16 per the deploy Bicep (`docs/adr/0023-*.md`); matching versions in tests means we catch version-specific lock behaviour. `postgres:16-alpine` keeps the container small (~80 MB) — startup is ~3 s on first run, sub-second on warm caches.

### Year-determination edge case: DST transitions

The `2026-12-31` boundary case in AC-4 doesn't hit a DST boundary (CET stays winter time through Dec/Jan). The DST-aware behaviour is implicit in `TimeZoneInfo.ConvertTimeFromUtc`; we don't write a dedicated DST test because the .NET BCL covers it.

### What happens if `country_configuration` is missing a TimeZoneId

T-0010 made `TimeZoneId` `IsRequired()` in the EF mapping (verified at `CountryConfiguration.cs:24` default value pattern), so a NULL would already be a seed-data integrity issue surfacing at `Order.Create`-time. The `OrderNumberGenerator` does not need a redundant null-check; if it ever sees NULL the existing `TimeZoneInfo.FindSystemTimeZoneById(null)` would throw `ArgumentNullException` and surface to the logger immediately (programmer-error path, same as T-0061 currency-mismatch).

## Test plan

Inline above (see Scope > Tests). No separate `docs/test-plans/` file.

## Status log

- 2026-06-04 `draft → ready` by PM. Expanded from INDEX row after T-0061 merged. Three user decisions captured upfront via a 4-reader research workflow + synthesis judge:
  - **Q1 — Rescoped** to "race-safety + rollback test coverage" because the original "integration into CreateOrder" title assumed a handler that's T-0063's scope. The honest S work between T-0007 and T-0063 is closing the two ADR 0009 compliance items (lines 143-144) and tightening the year contract.
  - **Q2 — TZ-aware year, correct from day 1** via `CountryConfiguration.TimeZoneId`. The interface drops the `int year` parameter; impl reads the country's TZ and computes country-local year. The 23:30 Prague edge case is real (currently buckets the order into the previous year's sequence — wrong for the customer-facing invoice number).
  - **Q3 — Pull Testcontainers into T-0062** to ship the Postgres harness that every Phase-4 race-sensitive test (T-0066, T-0067, T-0083) needs. T-0123 reduces to migration-pipeline-only scope as a result.

  Verified upfront: `CountryConfiguration.TimeZoneId` exists at `CountryConfiguration.cs:24`. `Testcontainers.PostgreSql` is already referenced in `Makables.IntegrationTests.csproj:16`. No new package additions needed.
- 2026-06-04 done. `dotnet-backend` agent implemented per ticket; reviewer pass **APPROVE** with no Blockers, no Mediums, three Lows (informational only). Build clean; 956 tests pass (866 unit + 90 integration; baseline 921 = 865 unit + 84 integration after T-0061 merged; +1 unit + +6 integration new — the year-contract `[Theory]` counts as 3 test cases in the runner). Docker daemon up; the 5 Postgres tests executed end-to-end against `postgres:16-alpine`.
  - **Five agent deviations** all confirmed sound by reviewer:
    1. `Two_concurrent_NextAsync_calls_produce_consecutive_numbers` pre-seeds the first row so the steady-state FOR UPDATE serialisation property is what's asserted (ADR 0009:144 "two concurrent commands serialize"). The first-allocation race is the explicit subject of the separate `First_allocation_race_creates_row_exactly_once` test.
    2. `First_allocation_race_creates_row_exactly_once` is a two-path assertion (path 1: loser surfaces `UniqueConstraintViolationException` — the allocator does not retry; path 2: scheduling lets the second caller see the committed row). Both leave the table with exactly one row; the post-race invariant `rowCount == 1` is the flake-resistant assertion.
    3. `OrderNumberGeneratorDelegationTests` is interaction-only — the static `NumberingSequenceAllocator` can't be mocked, so SQLite's `SqliteException` on `FOR UPDATE` proves execution reached the allocator while NSubstitute confirms the year-derivation wiring. Round-trip year + scope live in the Postgres `OrderNumberGeneratorYearContractTests`.
    4. `PostgresHarness` skips an explicit CZ-seed step because `InitialSchema` migration already inserts CZ via raw SQL (lines 113-135). `MigrateAsync()` covers it. `ResetMutableTablesAsync` explicitly excludes `countries` / `country_configuration`.
    5. `PostgresHarness` uses `TRUNCATE … CASCADE` + per-test ctor reset, not transaction rollback. Wrapping each test in an outer rollback would mask exactly the commit semantics under test. Sub-millisecond on near-empty tables; CASCADE handles future FK-bearing tables (T-0066 `order_payment_attempts`).
  - **Three informational Lows** noted, none requested as merge-blockers:
    - **L-1** — delegation test asserts on SQLite's parse-error string ("FOR" substring). Stable across recent SQLite releases but coupled to text; a future upgrade could regress this assertion without breaking the delegation contract. Acceptable for now.
    - **L-2** — `ResetMutableTablesAsync` hardcodes `numbering_sequence, orders`. CASCADE handles FK-children, but non-FK-linked mutable tables added later need explicit appends. The method name telegraphs the extension expectation.
    - **L-3** — `PostgresHarness.InitializeAsync`'s throwaway `MakablesDbContext` uses `await using` block scope, while race tests use explicit finally-block `DisposeAsync()` for symmetry. No leak; nit only.
