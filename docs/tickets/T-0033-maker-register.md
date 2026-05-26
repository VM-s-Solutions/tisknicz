---
id: T-0033
title: Maker entity + IMakerRepository + RegisterMaker command (ARES → snapshot → User+Maker atomic)
status: done
size: L
owner: dotnet-backend
created: 2026-05-26
updated: 2026-05-26
depends_on: [T-0020, T-0030, T-0032]
blocks: [T-0034, T-0035]
adrs: [0010, 0012, 0018]
phase: 2
---

# T-0033 — Maker entity + RegisterMaker

## Scope

The first multi-aggregate atomic command in the codebase (User + Maker + Address, single `IUnitOfWork` commit). Per ADR 0018 §"Persist CompanyRecord directly" the Maker entity stores a SNAPSHOT of the ARES fields at registration time — invoices and the public catalog read from the snapshot, not from a live ARES re-fetch.

### Domain (`Core.Domain/Makers/`)
- `Maker.cs` (`Auditable`) — snapshot fields (`RegistrationNumber`, `VatId`, `CompanyName`, `LegalForm`, `IncorporatedOn`, `IsActiveInRegistry`, `SourceRegistry`, `SnapshotFetchedAt`, `SnapshotIsStale`) + FKs (`UserId`, `RegisteredAddressId`) + state flags (`IsVerified` admin gate, `IsActiveInRegistry` ARES snapshot, `IsActive` soft-delete inherited).
  - `MarkVerified()` — admin toggle. Refuses double-verify (`MakerAlreadyVerified` error code maps here).
  - `UpdateSnapshot(...)` — T-0034 admin refresh. Does NOT touch `IsVerified` (admin verification survives a registry refresh by design).
- `IMakerRepository.cs` — minimal surface for T-0033: `Add`, `IcoExistsAsync`, `GetByUserIdAsync`.

### Core.Domain.Common
- `BusinessErrorMessage.MakerCompanyDissolved` ("maker.companyDissolved") — distinct semantic from the existing `MakerIcoAlreadyRegistered` ("already on platform"). T-0033 reviewer (re-confirmed by user) pushed back on re-purposing the existing code.

### Infra.Database
- `Configurations/MakerConfiguration.cs` — `makers` table. Partial unique indexes on `user_id` and `registration_number` (`WHERE is_active`) so soft-delete + admin GDPR purge allow re-registration.
- `Makers/MakerRepository.cs` — tracked reads (admin commands mutate).
- `Migrations/20260526083814_Makers.cs` — creates the table.

### Core.AppServices (`Features/Maker/`)
- `RegisterMaker.cs` — `Command(Email, Password, FullName, CountryCodePrimary, RegistrationNumber)` → `Response(UserId, MakerId, SnapshotIsStale)`. 6-step orchestration per the design Q&A:
  1. **IČO format gate** via `CzechIcoValidator` (mod-11). Invalid format → `Validation/IcoFormatInvalid` without consuming the ARES rate-limit budget.
  2. **ARES lookup** via `ICompanyRegistry`. Failure passes through (NotFound / Transient / Permanent).
  3. **Dissolved-entity gate** — `!IsActiveInRegistry` → `Permanent/MakerCompanyDissolved`.
  4. **Conflict pre-checks** — email taken → `Conflict/AuthEmailAlreadyExists`; IČO on platform → `Conflict/MakerIcoAlreadyRegistered`. Both BEFORE any `.Add()` so a failure doesn't pollute the change tracker.
  5. **Build + add** `User` + `Address` (cloned from the ARES legal seat, re-keyed with a server-issued id + caller's tenant `CountryCode`) + `Maker`. `UnitOfWorkPipelineBehavior` commits everything atomically on success.
  6. **Email-confirmation token** via the shared `IOneTimeTokenIssuer` (same pipeline as customer `Register` — keeps the per-user rate-limit budget unified per T-0024 sec M-2).
  - **Stale ARES snapshot** (`IsStale = true`) does NOT block; the flag rides on `Response.SnapshotIsStale` so T-0035 can render a "registry data may be outdated, admin will refresh" notice.
  - **Geocoding** deliberately OUT of the handler (ADR 0010 §"Geocoding policy" is non-blocking). The address sits with `Latitude`/`Longitude` null; the `ix_addresses_pending_geocode` partial index from T-0030 supports a future retry sweep.

### DI
- `AddMakablesInfrastructure` registers `IMakerRepository → MakerRepository` (scoped).
- One side fix: `AddDbContextFactory<MakablesDbContext>(...)` lifetime explicitly set to `ServiceLifetime.Scoped` (T-0032 left it as the default singleton, which collided with `AddDbContext`'s scoped `DbContextOptions<T>` registration and tripped `BuildServiceProvider` validation when MediatR was wired into the host).

### Out of scope
- HTTP endpoint (T-0035 frontend wires it through a `RegisterMakerController` on the public host).
- Admin actions (`VerifyMaker`, `DeactivateMaker`, `RefreshMakerFromAres`) — T-0034.
- `maker.registered.notify-admin` outbox event + email template — out of scope; admin tracking will use a query in T-0034.
- Geocode-retry sweep — future ticket.

### Tests (+24 facts; 620 total = 538 unit + 82 integration)
- `Domain/Makers/MakerTests.cs` — 11 facts (factory normalisation; required-field rejection matrix; blank-optional-as-null; default `IsVerified = false`; `MarkVerified` toggles; double-verify throws; `UpdateSnapshot` refreshes fields but preserves `IsVerified`; blank-name rejection on update).
- `AppServices/Features/Maker/RegisterMakerHandlerTests.cs` — 13 facts:
  - Format gate (2 theory rows; verifies registry is NOT called).
  - Registry failure passthrough — NotFound (no aggregates added) + Transient.
  - Dissolved gate — `MakerCompanyDissolved` + no aggregates + no email issued.
  - Conflict pre-checks — email-taken short-circuits (ICO uniqueness NOT queried); ICO-taken rejects.
  - Happy path — User + Address + Maker added with correct shapes; email-confirmation issuer called once.
  - Stale ARES snapshot proceeds with `Response.SnapshotIsStale = true`.

## Acceptance criteria
- **AC-1** Build clean; 620 tests pass (538 unit + 82 integration).
- **AC-2** `Maker.Create` rejects every empty required field; normalises strings + uppercases country; blank optionals → null.
- **AC-3** `Maker.MarkVerified` is idempotent-on-state — refuses double-verify.
- **AC-4** `Maker.UpdateSnapshot` refreshes registry-snapshot fields but PRESERVES `IsVerified`.
- **AC-5** `RegisterMaker` 6-step flow runs in the documented order; no aggregate is added before all gates have passed; geocoding is not invoked from the handler.
- **AC-6** Stale ARES snapshot does NOT block registration; flag propagates to `Response.SnapshotIsStale` AND `Maker.SnapshotIsStale`.
- **AC-7** Dissolved ARES entity (`IsActiveInRegistry = false`) is rejected as `Permanent/MakerCompanyDissolved` — distinct from the platform-side "already registered" code.
- **AC-8** `RegisterMaker` shares the email-confirmation issuer with customer `Register` (per-user rate-limit budget unified, T-0024 sec M-2).
- **AC-9** Partial unique indexes (`ix_makers_user_id`, `ix_makers_registration_number`) gate active-row uniqueness; soft-deleted rows free the slot for re-registration.
- **AC-10** CLAUDE.md hygiene: no `SaveChangesAsync` in the handler; all error codes from `BusinessErrorMessage`; `Core.Domain` no third-party packages; the handler is a thin orchestrator (no business logic in controllers — no controllers in T-0033).

## Status log
- 2026-05-26 done. 620 tests pass. Awaiting dual reviewer (security + code-quality) per workflow.
- 2026-05-26 reviewer fixes folded into one commit. 623 tests pass (541 unit + 82 integration; +3 new pipeline-race tests).
  - **Security M-1 (race-condition unique-violation surfaced as 500)** — closed. Added `UniqueConstraintViolationException` (`Core.Domain/SeedWork/`), `IUniqueConstraintTranslator` (mapping table in `Infra.Database/UniqueConstraintTranslator.cs`), an override of `MakablesDbContext.SaveChangesAsync` that catches `DbUpdateException`/`PostgresException` SQLSTATE 23505 and rethrows the domain exception, and a translation step in `UnitOfWorkPipelineBehavior` that produces a typed `BusinessResult` failure via the same reflection shape as `ValidationPipelineBehavior`. Unknown constraint names rethrow (a brand-new index nobody mapped is a bug worth surfacing).
  - **Security M-2 (IDOR doc on `GetByUserIdAsync`)** — closed with the same "MUST resolve userId from the authenticated principal" warning T-0030 added to `IAddressRepository.GetByIdAsync`.
  - **Security m-1 (caller-supplied `CountryCodePrimary` not cross-checked against ARES)** — deferred to the architect; tracked for the SK-tenant onboarding work. Today there's only one tenant (CZ) so the gap is theoretical and consistent with customer `Register`.
  - **Code-quality M-1 (soft-delete filter on `MakerRepository`)** — clarified with an XML doc on the class explaining that `MakablesDbContext.ApplySoftDeleteQueryFilters` attaches a global `IsActive` filter to every `Auditable` entity (Maker inherited). The reviewer's concern was correct in spirit (the filter is invisible at the call site) but the global filter is already wired — no behavioural change needed.
  - **Code-quality m-* / NITs** — left as-is for T-0034 (parameter-count refactor of `Maker.Create`, `Address.CloneForPersistence` extension, `MakerEntityConfiguration` rename, source-registry allow-list).
