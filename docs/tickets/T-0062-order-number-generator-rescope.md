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
- IANA TZ IDs (`Europe/Prague`) work cross-platform on .NET 5+ via the default ICU globalization provider: Linux containers resolve via bundled ICU; Windows hosts resolve via ICU bundled with the .NET runtime (no `TryConvertIanaIdToWindowsId` call required). This assumes the default production configuration; deployments that opt into NLS via `System.Globalization.UseNls=true` (or `runtimeconfig` equivalents) would need explicit IANA → Windows mapping, but Makables does not enable that switch. Empirically verified by the year-contract integration test running on a Windows host process.

**Year-source contract** (documented in the role doc, see Docs section): the year is **country-local**, derived from `CountryConfiguration.TimeZoneId`. A 23:30 Prague order on 2026-12-31 gets a 2026-sequence number; a 02:00 Prague order on 2027-01-01 gets a 2027-sequence number. Matches what the customer sees on the invoice.

### Internal config-loader plumbing

`OrderNumberGenerator` already takes `(MakablesDbContext db, IClock clock)`. It now also takes `ICountryConfigurationRepository configs`. No new repository methods; uses the existing `GetByCodeAsync`. DI lifetime stays Scoped.

### `InvoiceNumberGenerator` + `PayoutBatchNumberGenerator` — out of scope

T-0062 changes the order generator only. Invoices (T-0068) and payout batches (T-0101) currently still take `int year`/`int weekNumber` parameters at their interfaces. They will be migrated to the same TZ-aware pattern in their respective tickets if/when needed — keeping T-0062 narrow per the S sizing. The internal `NumberingSequenceAllocator` does **not** change; only the order-shaped public seam moves.

### Postgres test harness (`Makables.IntegrationTests/Numbering/`)

`Testcontainers.PostgreSql` is already referenced in the integration-test csproj (`backend/src/Makables.IntegrationTests/Makables.IntegrationTests.csproj:16`) but never instantiated. T-0062 stands up the first harness:

- **`PostgresHarness`** at `backend/src/Makables.IntegrationTests/Common/PostgresHarness.cs` — `IAsyncLifetime` xunit fixture spinning up a `postgres:16-alpine` container (matching production), running `MakablesDbContext.Database.MigrateAsync()` (the real migration pipeline, closing the T-0123 gap for this surface). The CZ `countries` + `country_configuration` rows are seeded by the `InitialSchema` migration itself, so the harness does not add a separate seed step. Exposes `CreateDbContext()` + `ResetMutableTablesAsync()` for per-test isolation.
- Shared via xunit `ICollectionFixture<PostgresHarness>` so all tests in the same collection reuse one container; per-test isolation via `TRUNCATE … CASCADE` on mutable tables in `ResetMutableTablesAsync` (NOT outer rollback transactions — the generator under test uses `SELECT … FOR UPDATE` inside its own explicit transaction, and an outer rollback would mask the commit/rollback semantics the race + rollback tests verify). Seed tables (`countries`, `country_configuration`) are explicitly preserved across resets.
- This harness will be reused by every Phase-4 race-sensitive test that follows (T-0066 Comgate webhook race, T-0067 MarkPaid concurrency, T-0083 auto-cancel race).

T-0123 (already queued) is **reduced in scope** by this work — its remaining job is the dedicated migration-pipeline-only assertions; the harness itself ships in T-0062.

### Tests

- **`backend/src/Makables.IntegrationTests/Numbering/OrderNumberGeneratorRaceSafetyTests.cs`** (new, 3 tests, Postgres-only):
  - `Two_concurrent_NextAsync_calls_produce_consecutive_numbers` — pre-seeds the first allocation via `SeedFirstAllocationAsync()` (consuming `M-CZ-20260001`) so the assertion targets the **steady-state** `SELECT … FOR UPDATE` serialisation property in isolation from the first-allocation race (which has its own dedicated test below). Then `Task.WhenAll(genA.NextAsync("CZ", ct), genB.NextAsync("CZ", ct))` against independent `IServiceScope`s wrapping independent `MakablesDbContext` instances → results are exactly `{"M-CZ-20260002", "M-CZ-20260003"}` (order doesn't matter, just contiguous + duplicate-free). Closes ADR 0009:144 compliance item.
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

- **AC-1** (Race safety, ADR 0009:144) Given a DB with a CZ row in `country_configuration` and a pre-seeded first allocation that consumed `M-CZ-20260001` (so the test targets the steady-state FOR UPDATE serialisation property, not the first-allocation race), when two `OrderNumberGenerator.NextAsync("CZ", ct)` calls run on independent `IServiceScope`s with `Task.WhenAll`, then the two returned numbers are contiguous (`{"M-CZ-20260002", "M-CZ-20260003"}` modulo order) with zero duplicates. Verified by a Postgres Testcontainers integration test.
- **AC-2** (Rollback safety, ADR 0009:143) Given a transaction that calls `NextAsync` then throws, when the transaction rolls back, then the row in `numbering_sequence` for `(CZ, Order, 2026)` either does not exist (first-allocation rollback) or has `last_used_value` equal to its pre-call value. Verified by a Postgres Testcontainers integration test.
- **AC-3** (First-allocation race) Given an empty `numbering_sequence` table, when two concurrent allocators race to call `NextAsync("CZ", ct)` for the same year, then exactly one INSERT wins; the loser either retries cleanly (returns `0002`) or fails fast with a translated error. The behaviour is pinned by an integration test so a future allocator rewrite cannot regress silently.
- **AC-4** (TZ-aware year, user Q2) Given `IClock` returns `2026-12-31T22:30:00Z` (23:30 in `Europe/Prague`, still local 2026), when `NextAsync("CZ", ct)` runs, then the returned number contains `2026`. Given `IClock` returns `2026-12-31T23:30:00Z` (00:30 local 2027), then the returned number contains `2027`. Verified by a parameterised Postgres integration test.
- **AC-5** (Interface contract) Given the codebase, when it builds, then `IOrderNumberGenerator.NextAsync(string countryCode, CancellationToken)` is the only public method (the `int year` parameter is removed). `OrderNumberGenerator` reads `CountryConfiguration.TimeZoneId` internally and computes the country-local year; no caller can supply the wrong year.
- **AC-6** (Generator delegation) Given a unit test with NSubstitute mocks, when `OrderNumberGenerator.NextAsync` runs, then it forwards `NumberingScope.Order` (verified by the assertion on the mocked allocator/repository call). Acts as a fast regression net.
- **AC-7** (Test harness) Given the integration test suite runs, when complete, then `PostgresHarness` spins up `postgres:16-alpine` via Testcontainers and runs `MakablesDbContext.Database.MigrateAsync()` to apply every migration (closing the T-0123 migration-pipeline coverage gap for this surface). The CZ `countries` + `country_configuration` rows are provided by the `InitialSchema` migration seed (no separate seed step in the harness). The harness is shared via xunit `ICollectionFixture` so multiple tests reuse one container.
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

A caller passing `clock.UtcNow.Year` to the existing signature would compile, run, and ship the wrong year for the 1-hour window between 23:00 UTC Dec 31 and 00:00 UTC Jan 1 (midnight local Prague in winter, since CET = UTC+1). Removing the parameter forces every caller to opt into the TZ-aware contract. The internal `NumberingSequenceAllocator` keeps its `int year` parameter because it's the shared infrastructure that invoice + payout generators also use — they will adopt the TZ-aware pattern at their layer if/when needed (likely T-0068 for invoices, since invoices are legally regulated and the year mismatch would be a real compliance issue).

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
- 2026-06-05 Copilot review on PR — 5 findings (1 High compile claim + 4 Low factual-precision claims). 3-lens × 5-finding adversarial verify (15 verdicts + 1 synthesis); 3/3 unanimous on every finding.
  - **H-1 — DECLINED.** Copilot claimed `[CollectionDefinition(Name)]` won't compile because `Name` is a class-scoped const referenced from a class-level attribute. Per C# spec §17.2 (and verified by the build evidence + 956 tests passing), class-level attributes resolve unqualified names against the class's own member scope; a class's `const` IS accessible to its own attribute. The code is valid C# and the existing build proves it. No change.
  - **L-1, L-2, L-3, L-4 — FOLDED.** All four wrong-year-window doc claims said "30 minutes" but the actual window is **1 hour** (Prague is UTC+1 in winter / CET; 23:00 UTC Dec 31 = 00:00 local Jan 1; 00:00 UTC Jan 1 = 01:00 local Jan 1). Existing test code in `OrderNumberGeneratorYearContractTests.cs` already correctly exercises the 1-hour math (22:30Z → 23:30Z spans an hour); the docs had drifted. Folded in four files: `IOrderNumberGenerator.cs` XML doc, `order-numbering.md`, ADR 0009 amendment, and this ticket's Technical Notes. Build clean post-edit.
- 2026-06-05 second Copilot review on PR — 5 findings (1 High + 1 Medium + 3 Lows). 3-lens × 5-finding adversarial verify (15 verdicts + 1 synthesis).
  - **R2-H1 — DECLINED** (3/3 lenses agreed Copilot is wrong). Copilot claimed `TimeZoneInfo.FindSystemTimeZoneById` does not reliably resolve IANA IDs on Windows and suggested wrapping with `TryConvertIanaIdToWindowsId` + `OperatingSystem.IsWindows()`. The shipped code targets .NET 10, which uses ICU as the default globalization provider on Windows (not just Linux) — IANA IDs resolve natively. Copilot's concern applies to .NET Framework 4.x / .NET Core pre-5.0, not .NET 10. Empirical confirmation: the `OrderNumberGeneratorYearContractTests` integration test calls `TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague")` on the Windows host process (the test harness runs locally; only Postgres is in the Docker container) and passes — direct proof that IANA resolution works on this Windows .NET 10 environment. Per CLAUDE.md "no defensive code unless explicit", and no genuine multi-host risk (Linux WSL uses the same ICU path), the 3-line defensive wrapper would be backwards-compat scaffolding for a runtime we don't target. No code change.
  - **R2-M1 — FOLDED.** Ticket Technical Notes claim "On Windows hosts the framework auto-translates" is imprecise — it conflates ICU availability with automatic IANA resolution without scoping to .NET 5+/ICU-default deployments. Rewrote to explicitly call out the cross-platform ICU path + the documented escape hatch (`System.Globalization.UseNls=true`) for completeness. Makables doesn't enable NLS; the year-contract integration test on Windows empirically verifies the default path works.
  - **R2-L1 — FOLDED.** Ticket Scope section claimed the harness "plus seeds the CZ CountryConfiguration row" (separate step) and left per-test isolation strategy open ("decided in implementation"). Shipped reality: CZ + countries rows are seeded by the `InitialSchema` migration (so `MigrateAsync()` covers it), and per-test isolation is `TRUNCATE … CASCADE` via `ResetMutableTablesAsync` (deliberately not outer rollback, which would mask the commit semantics the rollback tests verify). Aligned both bullets and AC-7.
  - **R2-L2 — FOLDED.** Ticket narrative + AC-1 both claimed the race-safety test asserts `{M-CZ-20260001, M-CZ-20260002}` from a clean DB. Shipped test deliberately pre-seeds the first allocation via `SeedFirstAllocationAsync()` (consuming `0001`) so the assertion targets the **steady-state** FOR UPDATE serialisation in isolation from the first-allocation race (which has its own dedicated test). Real-world results are `{0002, 0003}`. The rationale was already in the status log (and reviewer-approved), but the ticket body itself needed to match shipped reality so future readers don't have to chase the status log. Two text edits — race-test narrative + AC-1.
  - **R2-L3 — DECLINED** (2/3 lenses; fold threshold not met). Copilot suggested tightening `OrderNumberGeneratorDelegationTests.cs` assertion from `.Contain("FOR")` to `.Contain("near \"FOR\"")`. Tightening would couple the test more tightly to SQLite's internal error-message format which varies across versions; the broader `"FOR"` substring is pragmatically safe (SQLite's parse error for unsupported `FOR UPDATE` reliably mentions the keyword) and the test's primary contracts are verified via other assertions. Reviewer's L-1 already flagged this as informational only. No change.
