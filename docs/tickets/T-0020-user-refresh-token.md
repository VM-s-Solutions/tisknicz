---
id: T-0020
title: User + RefreshToken entities + EF migration + repositories
status: done
size: M
owner: dotnet-backend + dotnet-db
created: 2026-05-23
updated: 2026-05-23
depends_on: [Phase 1 done]
blocks: [T-0021]
adrs: [0012, 0013]
phase: 2
---

# T-0020 — User + RefreshToken entities

## Scope

### Core.Domain (`Makables.Core.Domain/Identity/`)
- `UserRole.cs` — enum `Customer` / `Maker` / `Admin`.
- `User.cs` — aggregate with email/normalized email, password hash, Google sub, lockout state. Domain methods: `Create`, `NormalizeEmail`, `SetPasswordHash`, `ConfirmEmail`, `LinkGoogleSub`, `UpdateProfile`, `RegisterFailedLogin`, `RegisterSuccessfulLogin`, `IsLocked`. All mutations go through methods; properties are private-set.
- `RefreshToken.cs` — token record with hash, family id, expiry, revocation. Methods: `IssueNew`, `IssueRotation`, `MarkRotated`, `Revoke`, `IsActiveAt`. Reuse detection lives at the service layer (T-0022) — the entity provides the building blocks.
- `IUserRepository.cs` — `GetByIdAsync`, `GetByEmailNormalizedAsync`, `GetByGoogleSubAsync`, `EmailExistsAsync` (uses `IgnoreQueryFilters` so soft-deleted emails still block re-registration), `Add`.
- `IRefreshTokenRepository.cs` — `GetByTokenHashAsync` (uses `IgnoreQueryFilters` so revoked tokens are reachable for reuse detection), `GetActiveByFamilyAsync`, `GetActiveByUserAsync`, `Add`.

### Infra.Database
- `Configurations/UserConfiguration.cs` — EF mapping. `users` table with unique index on `email_normalized` and a filtered unique index on `google_sub` (Postgres partial index `WHERE google_sub IS NOT NULL`). Role enum stored as string.
- `Configurations/RefreshTokenConfiguration.cs` — `refresh_tokens` table. Unique index on `token_hash`; non-unique indexes on `user_id` and `family_id`. `token_hash` capacity 128 chars (room for a future migration to longer digests).
- `Repositories/UserRepository.cs` and `RefreshTokenRepository.cs` — thin EF wrappers; tracked reads where mutation follows; `IgnoreQueryFilters` documented inline per ADR 0013.
- `Migrations/20260523211105_Identity.cs` — adds both tables.

### Config
- `AddMakablesInfrastructure.cs` — registers `IUserRepository` and `IRefreshTokenRepository` as scoped.

### Tests
- `Makables.Tests/Domain/Identity/UserTests.cs` — 13 facts covering normalization (NFC + lowercase), country uppercase, factory guards, password-set lockout reset, idempotent `ConfirmEmail`, Google-sub re-link rejection, lockout threshold + reset, profile trim.
- `Makables.Tests/Domain/Identity/RefreshTokenTests.cs` — 8 facts covering country normalization, user-agent truncation, family id preservation across rotation, revoke-on-rotate, reuse detection (second `MarkRotated` throws), idempotent `Revoke`, `IsActiveAt` after revoke / expiry.

## Out of scope
- Argon2id hashing (T-0021).
- JWT signing + verification (T-0021).
- `AuthService` (Register / Login / Refresh / Logout) — T-0022.
- Magic link / email confirmation / password reset entities — T-0023 / T-0024 / T-0025 (the same migration could carry them but the per-ticket trace is cleaner with separate migrations).

## Acceptance criteria
- **AC-1** Build clean; 170 tests pass (134 unit + 36 integration; +25 new Identity facts).
- **AC-2** `users` and `refresh_tokens` tables exist with the columns and indexes from ADR 0012.
- **AC-3** Email normalization is NFC + lowercase; comparison is normalized on both sides via `User.NormalizeEmail`.
- **AC-4** Re-registration is blocked even for soft-deleted accounts (`EmailExistsAsync` ignores the soft-delete filter).
- **AC-5** Refresh-token reuse detection has a clear domain trigger: calling `MarkRotated` twice throws — the service layer (T-0022) catches this and revokes the family.

## Status log
- 2026-05-23 done. 170 tests pass.
