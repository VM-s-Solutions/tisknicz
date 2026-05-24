---
id: T-0022
title: IAuthService — Register / Login / Refresh / Logout + LoginAttemptBucket (ghost lockout slots)
status: done
size: L
owner: dotnet-backend
created: 2026-05-24
updated: 2026-05-24
depends_on: [T-0020, T-0021]
blocks: [T-0023, T-0027]
adrs: [0012, 0013]
phase: 2
---

# T-0022 — AuthService

## Scope

### Q-0004 outcome
Per the answer to Q-0004, ghost lockout state lives in a new
`login_attempt_buckets` table keyed by `email_normalized` — persistent and
scale-out safe, in lock-step with the per-User counter.

### Core.Domain (`Identity/`)
- `LoginAttemptBucket.cs` — per-email failed-attempt counter + lockout
  state. Inherits `BaseEntity` (not `Auditable`) because buckets are
  anti-abuse infrastructure, not transactional data — no `CountryCode`,
  no soft delete, no `created_by`. Domain methods: `Create`,
  `RegisterFailedAttempt` (no-op while already locked, mirrors
  `User.RegisterFailedLogin`'s lock-extension guard from T-0020),
  `Reset`, `IsLocked`.
- `ILoginAttemptBucketRepository.cs` — `GetAsync` + `Add`.
- `LockoutOptions.cs` — moved here from Infra.Common.Auth (lives with
  the domain rule, consumed via `IOptions<LockoutOptions>`). Defaults:
  threshold 5, window 15 min per ADR 0012 §Lockout.
- `RefreshTokenHasher.cs` — moved here from Infra.Common.Auth. SHA-256
  of the raw refresh token → lowercase hex. Static helper; pure
  function, no third-party deps so it stays inside Core.Domain.

### Infra.Database
- `Configurations/LoginAttemptBucketConfiguration.cs` — table `login_attempt_buckets`, PK = `email_normalized`, no audit columns.
- `Repositories/LoginAttemptBucketRepository.cs` — thin EF wrapper. No `IgnoreQueryFilters` because the soft-delete filter only attaches to `Auditable` types.
- `Migrations/20260524094130_LoginAttemptBuckets.cs` + designer + snapshot — generated via `dotnet ef migrations add` against the local Postgres instance.

### Core.AppServices (`Features/Auth/`)
Four use cases, each in its own file per CLAUDE.md / patterns §A.3:

- `SessionResult.cs` — shared DTO for `{UserId, AccessToken, AccessTokenExpiresAt, RefreshToken, RefreshTokenExpiresAt}`. Returned by Login + Refresh.

- `Register.cs` — `Register.Command(Email, Password, FullName, CountryCodePrimary, Role)` → `Response(UserId)`. Validates email/password shape, rejects `Admin` role on the public path, blocks re-registration via `EmailExistsAsync` (which respects soft-deleted rows per T-0020 fix). Account is created with `EmailConfirmedAt = null`; the user must confirm before login succeeds (T-0024 ships the email flow).

- `Login.cs` — `Login.Command(Email, Password, Audience, UserAgent?, IpAddress?)` → `SessionResult`. Flow:
  1. Resolve or create the per-email bucket FIRST so unknown emails consume ghost slots identically to known ones.
  2. Bucket-locked → `AuthLocked`.
  3. User lookup respects soft-deleted rows; missing/deleted → burn a bucket slot, return `AuthInvalidCredentials` (same code as wrong password — no enumeration leak).
  4. User-row lock → `AuthLocked`.
  5. Wrong password → register against both User and bucket counters, return `AuthInvalidCredentials`.
  6. Email unconfirmed → `AuthEmailNotConfirmed` (intentional specific code; UI prompts to re-send confirmation).
  7. Audience mismatch for non-admin → `AuthForbidden`. Admins can log in to any audience.
  8. Success → reset both counters, transparently re-hash if `NeedsRehash`, mint access JWT + 30-day refresh token (raw via CSPRNG → URL-safe base64; hash stored).

- `Refresh.cs` — `Refresh.Command(RawRefreshToken, Audience, UserAgent?, IpAddress?)` → `SessionResult`. Rotates the token, marks the old one revoked, links the new one in the same `FamilyId`. Reuse detection: a token whose `RevokedAt is not null` triggers revocation of every active sibling in the family (catches stolen-token replay per ADR 0012 §Refresh token). Returns `AuthRequired` for all reuse / unknown / expired cases so the attacker can't tell which path fired.

- `Logout.cs` — `Logout.Command(RawRefreshToken)` → no value. Idempotent: unknown / already-revoked tokens succeed silently so the UI doesn't flap.

### Config
- `AddMakablesInfrastructure.cs` — binds `LockoutOptions` from `Auth:Lockout` config section; registers `ILoginAttemptBucketRepository`.

### Tests
- `Makables.Tests/Domain/Identity/LoginAttemptBucketTests.cs` — 6 facts: factory + email/id, blank guard, lock-at-threshold, no-op while locked (matches User.RegisterFailedLogin contract), Reset, IsLocked after window.
- `Makables.Tests/AppServices/Features/Auth/RegisterHandlerTests.cs` — 3 facts: conflict on existing email, admin-role rejection, happy path (NSubstitute repo + verified `Add(User)` call).
- `Makables.Tests/AppServices/Features/Auth/LoginHandlerTests.cs` — 9 facts: ghost slot for unknown email, wrong password increments both counters, bucket-lock short-circuit, user-row lock, unconfirmed-email gate, non-admin audience mismatch, admin cross-audience, happy path resets both counters and issues both tokens, transparent rehash.
- `Makables.Tests/AppServices/Features/Auth/RefreshHandlerTests.cs` — 7 facts: unknown token, already-revoked triggers family revocation, expired, soft-deleted user revokes-and-fails, audience mismatch, audience match (happy path), per-rotation family preservation.
- `Makables.Tests/AppServices/Features/Auth/LogoutHandlerTests.cs` — 2 facts: revoke live token; idempotent for unknown.

## Out of scope
- Magic-link flow — T-0023.
- Email confirmation flow — T-0024.
- Password reset flow — T-0025.
- Google OAuth — T-0026.
- Authentication middleware on the four Web hosts — T-0027.
- Controllers / HTTP endpoints for these use cases — land with T-0035 (the auth pages ticket) so cross-stack wiring stays atomic.
- Breached-password blocklist (`AuthPasswordTooCommon`) — deferred; ticket-level follow-up because the static list is a small operational concern, not a domain logic gap.

## Acceptance criteria
- **AC-1** Build clean; 238 tests pass (197 unit + 41 integration; +27 new auth facts).
- **AC-2** `Login` consumes a ghost-bucket slot for unknown emails identically to known ones — verified by `Unknown_email_consumes_a_ghost_bucket_slot_and_returns_invalid_credentials` asserting `Add(bucket with attempt=1)`.
- **AC-3** Reuse-detection burns the whole token family — verified by `Already_revoked_token_triggers_family_wide_revocation_and_returns_unauthorized`.
- **AC-4** Audience binding is enforced on Login and Refresh — non-admin users cannot cross audiences; admins can.
- **AC-5** Lockout window is NOT extended by attempts inside it (both User and bucket counters share the same guard).
- **AC-6** Transparent re-hash runs on a successful login when `NeedsRehash` returns true.

## Reviewer findings (commit 009e451) and resolutions

Reviewer returned **BLOCKER × 2** + **MAJOR × 4** + **MINOR × 3**. Resolutions in a follow-up commit on master:

- **BLOCKER B-1 (UoW silently drops failure-path mutations)** — added `IPersistOnFailureCommand` marker in `Core.AppServices/Abstractions/`; the `UnitOfWorkPipelineBehavior` now commits when `IsSuccess || request is IPersistOnFailureCommand`. Marked `Login.Command` and `Refresh.Command`. Without this fix, the lockout counters on the wrong-password path, the family-wide revocation on reuse detection, and the explicit revoke on a soft-deleted user were ALL silently discarded.
- **BLOCKER B-2 (timing-attack vector)** — added `IPasswordHasher.DummyHashForTimingEqualization` (lazy, cached, current-policy parameters). `Login.Handler` runs a dummy `Verify` on the unknown / soft-deleted / no-password-hash branch so total latency matches the known-email path within one Argon2id evaluation.
- **MAJOR M-1 (atomicity)** — addressed implicitly by B-1: `Refresh.Command` is now `IPersistOnFailureCommand`, so partial state survives unexpected failure mid-rotation rather than vanishing.
- **MAJOR M-2 (`GenerateRefreshToken` duplicated)** — promoted to `RefreshTokenHasher.GenerateNewPair()`. Login and Refresh both call it.
- **MAJOR M-3 (no SaveChanges-not-called assertion)** — added `UoW_Commits_On_Failure_When_Command_Implements_IPersistOnFailureCommand` to `PipelineBehaviorTests`; the existing negative test pins the other half.
- **MAJOR M-4 (claim about integration tests)** — kept the doc honest; no integration tests for the four use cases are added in this commit. They land with T-0027 / T-0035 when the HTTP edges exist.
- **MINOR Mi-1 (no-op bucket row on successful first login)** — bucket is now created lazily via `EnsureBucket(ref bucket, …)` only when a failed attempt is about to be recorded. New test pins it.
- **MINOR (audience comparison drift)** — added `User.MatchesAudience(audience)`; both handlers route through it.

## Acceptance criteria (revised)
- **AC-1** Build clean; 240 tests pass (199 unit + 41 integration; +2 since 009e451).
- **AC-2** Unknown emails consume a ghost slot AND run a dummy Argon2id verify so latency doesn't leak existence — pinned by a new `Verify(...)` assertion.
- **AC-3** Reuse-detection burns the whole token family AND persists the revocation — pinned by behavior test + the new persistence-contract test.
- **AC-4** Audience binding via `User.MatchesAudience`; non-admins can't cross, admins can.
- **AC-5** Lockout window not extended by attempts inside it; failure-path mutations persist via `IPersistOnFailureCommand`.
- **AC-6** Transparent re-hash on successful login when `NeedsRehash` returns true.
- **AC-7** Successful first login for an account with no prior bucket does NOT create a no-op bucket row.

## Status log
- 2026-05-24 done. 238 tests. Q-0004 resolved in code.
- 2026-05-24 T-0022 reviewer fix folded in. 240 tests. BLOCKERs B-1/B-2 closed; MAJORs M-1/M-2/M-3 closed; M-4 acknowledged; MINOR Mi-1 closed; audience helper extracted.
