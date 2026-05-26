---
id: T-0034
title: UpdateMakerProfile + admin VerifyMaker / DeactivateMaker / RefreshMakerFromAres
status: done
size: M
owner: dotnet-backend
created: 2026-05-26
updated: 2026-05-26
depends_on: [T-0033]
blocks: [T-0036, T-0119]
adrs: [0014, 0018]
phase: 2
---

# T-0034 — Maker self-service + admin commands

## Scope

The four mutators that activate the Maker entity after T-0033 ships its read-only baseline.

### Domain (`Core.Domain/Makers/`)
- `Maker.cs` — extended with the four maker-editable fields:
  - `Bio` (≤500 chars), `BankAccount`, `PersonalPickupEnabled`, `PickupNote`.
  - `UpdateProfile(bio?, bankAccount?, personalPickupEnabled?, pickupNote?)` — null = "leave alone", empty string = "clear". Snapshot fields are NEVER touched (US-maker-0003 AC-2).
- `Validators/CzechBankAccountValidator.cs` — ČNB mod-11 weighted checksum + structural rules (`[prefix-]number/bankCode`).
- `IMakerRepository.GetByIdAsync(string id)` — load by primary key for the admin commands (tracked read because admin commands mutate).

### Domain (`Core.Domain/Addresses/`)
- `Address.Update(street, houseNumber, city, zip, countryCodeIso, state?)` — in-place mutator used by `RefreshMakerFromAres` to refresh the legal-seat row when ARES reports a moved company. Clears `Latitude`/`Longitude` (re-geocode on the next sweep). The audit `CountryCode` (owner tenancy) is NEVER changed.

### Core.AppServices (`Features/Maker/`)
- `UpdateMakerProfile.cs` — maker self-service patch. Command has NO UserId/MakerId field by design; the target Maker is resolved from `IUserSessionProvider.GetUserId()` (IDOR shield, pinned by a reflection-based test).
- `VerifyMaker.cs` — admin flips `IsVerified` to true. `AlreadyVerified` short-circuits with `BusinessErrorMessage.MakerAlreadyVerified`. Implements `IAdminAuditableCommand` so the `AdminAuditPipelineBehavior` captures before/after JSONB per ADR 0014.
- `DeactivateMaker.cs` — admin soft-deletes via `Auditable.MarkDeactivated(adminUserId, clock.UtcNow)`. Re-deactivation short-circuits with `BusinessErrorMessage.MakerNotActive`. Audited.
- `RefreshMakerFromAres.cs` — admin re-fetches ARES, updates the snapshot via `Maker.UpdateSnapshot(...)` (preserves `IsVerified` per ADR 0018), and refreshes the linked Address row in-place. Audited. Transient ARES failures pass through with `ErrorType.Transient` so the admin can retry. A missing Address row logs a warning but does not fail the command — the snapshot still updates.

### Infra.Database
- `MakerConfiguration.cs` — adds `bio` / `bank_account` / `personal_pickup_enabled` / `pickup_note` columns.
- `Migrations/20260526091906_MakerProfileFields.cs` — additive migration; defaults `personal_pickup_enabled = false`.

### Tests (+46 facts; 669 total = 587 unit + 82 integration)
- `Domain/Makers/MakerTests.cs` — 5 new facts (default profile-field values, patch semantics, empty-string-clears, 500-char rejection, profile patch leaves snapshot intact).
- `Domain/Makers/CzechBankAccountValidatorTests.cs` — 18 facts (3 canonical valids + 15 invalid + 2 bad-checksum theories).
- `Domain/Addresses/AddressTests.cs` — 3 new facts (`Update` mutator: clears coordinates; preserves audit `CountryCode`; rejects blank required).
- `AppServices/Features/Maker/UpdateMakerProfileHandlerTests.cs` — 4 facts (Unauthorized, NotFound, patches+leaves snapshot, IDOR-shield reflection check).
- `AppServices/Features/Maker/VerifyMakerHandlerTests.cs` — 4 facts (NotFound, happy path, double-verify rejection, audit metadata).
- `AppServices/Features/Maker/DeactivateMakerHandlerTests.cs` — 4 facts (NotFound, soft-delete + audit stamping, re-deactivation rejection, audit metadata).
- `AppServices/Features/Maker/RefreshMakerFromAresHandlerTests.cs` — 6 facts (NotFound, transient passthrough, snapshot+address refresh + IsVerified preserved, stale flag propagation, missing-address resilience, audit metadata).

### Out of scope
- **Categories.** US-maker-0003 mentions categories; the Category entity + m:n table are T-0040 territory. Deliberately deferred.
- **Pickup-address management.** The `personal_pickup_enabled` toggle + `pickup_note` text ship now; setting an actual pickup `Address` row (separate from the legal seat) is deferred to the address-graph work.
- **User.Phone.** Lives on the User entity and is patched via the existing `User.UpdateProfile`. The T-0036 frontend can issue both commands when the form covers Maker + User fields together.
- **HTTP endpoints.** T-0119 admin frontend + T-0036 maker dashboard wire the controllers.

## Acceptance criteria
- **AC-1** Maker self-service: `UpdateMakerProfile` patches `Bio` / `BankAccount` / `PersonalPickupEnabled` / `PickupNote`. Null = "leave alone"; empty string clears.
- **AC-2** ARES-snapshot fields (`CompanyName`, `IČO`, `RegisteredAddress`, `LegalForm`, `DIČ`) are NOT in the `UpdateMakerProfile.Command` shape (US-maker-0003 AC-2 — only an admin can change them via `RefreshMakerFromAres`).
- **AC-3** Bank account format is enforced by `CzechBankAccountValidator` (ČNB mod-11 + structural rules); failure returns `validation.bankAccountFormat`.
- **AC-4** `UpdateMakerProfile` resolves the target Maker from `IUserSessionProvider.GetUserId()` — there is no `UserId` / `MakerId` field on the Command (IDOR shield).
- **AC-5** `VerifyMaker` flips `IsVerified`; double-verify returns `maker.alreadyVerified` (Conflict).
- **AC-6** `DeactivateMaker` soft-deletes via `Auditable.MarkDeactivated`; re-deactivation returns `maker.notActive` (Conflict).
- **AC-7** `RefreshMakerFromAres` updates the snapshot via `Maker.UpdateSnapshot` (preserves `IsVerified` per ADR 0018) and refreshes the linked Address in-place. Transient ARES failures pass through as `ErrorType.Transient`.
- **AC-8** All three admin commands implement `IAdminAuditableCommand` with `ActionCode` ∈ {`maker.verify`, `maker.deactivate`, `maker.refreshFromAres`}, so the pipeline writes the audit row per ADR 0014.
- **AC-9** 669 tests pass (587 unit + 82 integration).
- **AC-10** CLAUDE.md hygiene: no `SaveChangesAsync` in handlers; all error codes from `BusinessErrorMessage`; `Core.Domain` no third-party packages; handlers are thin orchestrators.

## Status log
- 2026-05-26 done. 669 tests pass. Awaiting dual reviewer (security + code-quality) per workflow.
