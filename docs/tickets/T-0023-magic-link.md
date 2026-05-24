---
id: T-0023
title: Magic-link flow — RequestMagicLink + ConsumeMagicLink + shared OneTimeToken
status: done
size: M
owner: dotnet-backend
created: 2026-05-24
updated: 2026-05-24
depends_on: [T-0022]
blocks: [T-0024, T-0025]
adrs: [0012, 0019, 0020]
phase: 2
---

# T-0023 — Magic link

## Scope

### Shared one-time-token aggregate
ADR 0012 §Magic link / §Email confirmation / §Password reset all specify
the same shape (32-byte URL-safe-base64, SHA-256 hashed at rest, N-minute
TTL, single-use). Built once here so T-0024 and T-0025 only need to add a
use case each.

- `Core.Domain/Identity/OneTimeTokenPurpose.cs` — enum
  `MagicLink` / `EmailConfirmation` / `PasswordReset`. The discriminator
  prevents a token issued for one flow from being consumed by another.
- `Core.Domain/Identity/OneTimeToken.cs` — `BaseEntity` (not `Auditable`)
  with the SHA-256 hex as PK, `UserId`, `Purpose`, `ExpiresAt`,
  `ConsumedAt`, `CreatedAt`, `IpAddress`. Domain methods: `Issue`,
  `Consume` (throws on double-credit), `IsConsumed`, `IsExpired`,
  `IsRedeemable`.
- `Core.Domain/Identity/IOneTimeTokenRepository.cs` — `GetByHashAsync`
  (PK probe), `CountIssuedSinceAsync(userId, purpose, since)` for the
  rate-limit, `InvalidateRedeemableAsync(userId, purpose, now)` for the
  password-reset flow's "invalidate prior tokens", `Add`.
- `Core.Domain/Identity/OpaqueTokenFactory.cs` — `GenerateUrlSafe32()`
  and `Sha256Hex(raw)`. Replaces the per-flow `RefreshTokenHasher` so
  refresh tokens, magic-link tokens, confirmation tokens, and reset
  tokens all go through one helper (no encoding drift). `RefreshTokenHasher`
  removed; Login / Logout / Refresh handlers rewired to the factory.

### Infra.Database
- `Configurations/OneTimeTokenConfiguration.cs` — `one_time_tokens` table.
  PK = `token_hash`. Composite index on `(user_id, purpose, created_at)`
  for rate-limit + invalidate flows. Index on `expires_at` so the
  T-0114 cleanup job can purge without a table scan.
- `Repositories/OneTimeTokenRepository.cs` — three lookups + `Add`.
- `Migrations/20260524…_OneTimeTokens.cs` — generated via local Postgres.

### Core.AppServices/Features/Auth
- `RequestMagicLink.cs` — `Command(Email, IpAddress)` → `BusinessResult`.
  - Always returns `Success` (no enumeration leak).
  - Unknown email / soft-deleted user → silent no-op.
  - Rate limit: 3 requests per email per 10 minutes (constants
    `MaxRequestsPerWindow` + `RateLimitWindow`) — exceeded → silent
    no-op.
  - Success: mints `(raw, hash)` via `OpaqueTokenFactory`, persists the
    hash with `Purpose = MagicLink`, enqueues outbox event
    `auth.magicLink.send` with `OutboxPayload(UserId, Email, RawToken,
    ExpiresAt)`. The raw token never appears in logs; the T-0014
    `SensitivePropertyMasker` redacts any property name containing
    "token".
  - `IPersistOnFailureCommand` — defense-in-depth so any future
    per-user state survives a failure path.

- `ConsumeMagicLink.cs` — `Command(RawToken, Audience, UserAgent?, IpAddress?)`
  → `BusinessResult<SessionResult>`.
  - Single generic error code `AuthMagicLinkInvalid` for missing,
    wrong-purpose, expired, or already-consumed tokens — no
    discriminator the attacker can probe.
  - Soft-deleted user → invalid AND consume the token (a stolen valid
    link cannot be replayed after reactivation).
  - Audience mismatch for non-admin → forbidden AND consume the token.
  - Happy path: consume the token, mark the email confirmed
    (redemption proves inbox control), reset lockout counters via
    `User.RegisterSuccessfulLogin`, issue access JWT + 30-day refresh
    token via the same `OpaqueTokenFactory` + `RefreshToken.IssueNew`
    used by `Login`.
  - `IPersistOnFailureCommand` so the consume mutation survives
    failure-path returns (B-1 contract).

### Config
- `AddMakablesInfrastructure.cs` — registers `IOneTimeTokenRepository`.

### Common
- `BusinessErrorMessage.AuthMagicLinkInvalid = "auth.magicLinkInvalid"`.

### Tests (+25 facts; 265 total)
- `Tests/Domain/Identity/OneTimeTokenTests.cs` — 7 facts: factory + shape,
  blank-hash guard, past-expiry guard, `Consume` flips state, double-
  `Consume` throws, expiry boundary inclusive, ip truncated at 64.
- `Tests/Domain/Identity/OpaqueTokenFactoryTests.cs` — 4 facts:
  url-safe alphabet, per-call uniqueness, SHA-256 determinism, blank
  guard.
- `Tests/AppServices/Features/Auth/RequestMagicLinkHandlerTests.cs` —
  5 facts: unknown email no-op, soft-deleted no-op, rate-limit no-op,
  happy-path persistence + outbox enqueue, rate-limit window
  computation.
- `Tests/AppServices/Features/Auth/ConsumeMagicLinkHandlerTests.cs` —
  7 facts: unknown / wrong-purpose / consumed / expired / soft-deleted
  (burns token) / audience-mismatch (burns token) / happy path
  (consumes + confirms email + issues session).

## Out of scope
- Email send itself — T-0028 (`IEmailProvider` + Resend impl) and T-0029
  (`ProcessOutboxFunction` that drains `auth.magicLink.send` and calls
  the provider).
- HTTP endpoints — land with T-0035 (the auth pages ticket).
- IP-based per-IP rate limit beyond the per-email budget — the ASP.NET
  RateLimiter already covers global flood control; the per-email budget
  here is the ADR-mandated layer.

## Acceptance criteria
- **AC-1** Build clean; 265 tests pass (224 unit + 41 integration; +25
  since T-0022).
- **AC-2** `RequestMagicLink` returns `Success` for unknown-email /
  soft-deleted / rate-limited inputs — pinned by separate tests.
- **AC-3** Rate-limit window is `now - RateLimitWindow` inclusive —
  pinned by a `Received(...)` assertion on the repo call.
- **AC-4** `ConsumeMagicLink` collapses every invalid-token reason to
  one error code; tokens consumed on soft-deleted-user and audience-
  mismatch so they can't be replayed.
- **AC-5** Happy-path consume sets `EmailConfirmedAt` on the user
  (magic-link redemption proves inbox control per ADR 0012).
- **AC-6** Refresh tokens, magic-link tokens, confirmation tokens, and
  password-reset tokens all flow through one `OpaqueTokenFactory`;
  per-flow `RefreshTokenHasher` is removed (no encoding drift).

## Status log
- 2026-05-24 done. 265 tests pass.
