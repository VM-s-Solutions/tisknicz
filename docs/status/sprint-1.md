# Sprint 1 — Foundation scaffold

**Started:** 2026-05-22
**Owner:** orchestrator (driving directly; `dotnet-backend` charter)
**Reviewer:** `general-purpose` sub-agent acting per `reviewer` charter

## Tickets completed (this session)

| Ticket | Commit | Tests | Reviewer verdict |
|---|---|---|---|
| T-0001 — Scaffold .NET solution (15 projects) | `9c57c87` | n/a | not reviewed (pre-process) |
| T-0004 — Shared types (BusinessResult, Error, ICommand/IQuery, MakablesApiController) | `860c4fe` | 15 | not reviewed (drove ahead) |
| T-0005 — Money + MoneyFormatter | `eb6e593` | +20 = 35 | APPROVED w/ 3 MAJOR test-gap findings (backfilled in T-0006 commit) |
| T-0006 — Auditable + IClock + IIdGenerator + IUserSessionProvider | `6104c1b` | +10 = 50 (+5 T-0005 backfill) | APPROVED w/ 2 MAJOR doc-drift findings (closed by `7cd000b`) |
| T-0002 — MakablesDbContext + soft-delete filter + audit interceptor | `9b81a80` | +8 = 60 (+2 T-0006 Money ctor backfill) | APPROVED w/ 3 MINOR / 3 NIT (none code-blocking) |
| docs follow-up | `7cd000b` | n/a | n/a — closes T-0006's two MAJOR doc findings: align `CreatedAt` naming across patterns.md + ADRs; add role files for IClock / IIdGenerator / IUserSessionProvider |
| T-0003 — MediatR pipeline behaviors (Validation + UnitOfWork) | `86bfc21` | +8 = 68 | reviewer not yet dispatched |

**Master HEAD:** `86bfc21`. 7 commits ahead of `origin/master`. No pushes.

## Test counts over time

- After T-0001: 0 (project scaffolds; no production code)
- After T-0004: 15
- After T-0005: 35
- After T-0006 (+ T-0005 backfill): 50
- After T-0002 (+ T-0006 Money backfill): 60
- After T-0003: **68**

All passing. Zero warnings. `TreatWarningsAsErrors=true` globally.

## Pending follow-ups (none blocking)

- **T-0002 reviewer MINOR #1**: `ADR 0013:117` has a stale claim that `EF Remove` triggers `Deactivated()` via the interceptor. The implementation correctly does not do this. Fold into a future docs ticket.
- **T-0002 reviewer MINOR #2**: `TestDbHarness.CastOptions` uses `IDbContextOptionsBuilderInfrastructure` which is brittle. Workaround for the typed-options mismatch; safe to leave until a second test DbContext appears.
- **T-0002 reviewer MINOR #3**: tighter assertion on `MarkDeactivated` test's `UpdatedBy`. ~1-line fix when convenient.

## Adjustments made vs. the original backlog

- **Ticket ordering**: T-0004 → T-0005 → T-0006 → T-0002 → T-0003 (instead of T-0002 → T-0003 → T-0004...). T-0002's audit interceptor depends on `Auditable` from T-0006 and the `BusinessResult` types from T-0004; pulling them forward removed a circular dependency.
- **PR-per-ticket gate relaxed by user choice**: discovery + each ticket is a separate commit on `master` (no per-ticket PR) since the user opted for fast local merges. Reviewer still runs in background after each merge; findings are folded into the next ticket's commit (or a dedicated docs ticket).
- **ICommandMarker introduced** (T-0003): a non-generic marker so the UoW behavior can constrain `where TRequest : ICommandMarker` and cover both `ICommand` and `ICommand<TResponse>`. ADR 0002 / patterns §A.3 still describe the public surface correctly; the marker is an internal mechanism.

## Pending tickets in Sprint 1

| Ticket | Title | Size | Depends on |
|---|---|---|---|
| T-0007 | NumberingSequence + IOrderNumberGenerator / IInvoiceNumberGenerator / IPayoutBatchNumberGenerator (FOR UPDATE lock) | M | T-0002 |
| T-0008 | DI wiring: AddMakablesInfrastructure / Auth / Cors / Mediator / Clients / RateLimiting | M | T-0001, T-0003 |
| T-0009 | Four Web hosts sharing Config; per-host Program.cs; per-host CORS + rate limit | M | T-0008 |
| T-0010 | Country + CountryConfiguration entity + seed migration (CZ row) | M | T-0002, T-0006 |
| T-0011 | Outbox + AdminAuditLogEntry + AdminAuditPipelineBehavior | M | T-0002, T-0006 |
| T-0012 | API versioning (Asp.Versioning.Mvc) | S | T-0009 |
| T-0013 | NSwag pipeline + CI spec-hash parity check | M | T-0009, T-0012 |
| T-0014 | Structured logging + OpenTelemetry + App Insights | M | T-0009 |
| T-0015 | Frontend scaffold (route groups, api-client folder, apiFetch, Result, i18n) | M | T-0013 |
| T-0016 | Bicep + deploy pipelines | L | T-0014 |

T-0014, T-0015, T-0016 can fan out in parallel at the end of Sprint 1 once T-0013 lands.

## Sprint 1 progress: 5 / 16 tickets done (T-0001, T-0002, T-0003, T-0004, T-0005, T-0006 + docs follow-up = 7 commits)

Wait — that's 6 feature tickets + 1 docs = 7 commits. Correcting: T-0001/0002/0003/0004/0005/0006 = **6 of 16 tickets done**.

## Next session start point

The next ticket is **T-0007 — NumberingSequence + generators**. This requires:
- `NumberingSequence` entity (inherits `Auditable`)
- `IEntityTypeConfiguration<NumberingSequence>` in `Infra.Database/Configurations/`
- `IOrderNumberGenerator` / `IInvoiceNumberGenerator` / `IPayoutBatchNumberGenerator` interfaces in `Core.Domain/Numbering/`
- Concrete generators in `Infra.Database/Numbering/` using `SELECT ... FOR UPDATE` (raw SQL, Postgres-specific — tests use SQLite which doesn't support `FOR UPDATE`, so the generator's locking behavior gets an integration test against Testcontainers Postgres rather than the SQLite harness)
- Format: `M-CZ-YYYYNNNN`, `FV-CZ-YYYYNNNN`, `VYP-CZ-YYYY-Www`

After T-0007, T-0008 wires everything via `AddMakablesXxx` extension methods in `Makables.Config`. From T-0009 onward the four hosts get real `Program.cs` with the full pipeline.
