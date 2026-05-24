---
id: T-0025
title: Password reset — RequestPasswordReset + ConfirmPasswordReset; revoke all refresh tokens on success
status: done
size: M
owner: dotnet-backend
created: 2026-05-24
updated: 2026-05-24
depends_on: [T-0022, T-0023, T-0024]
blocks: []
adrs: [0012, 0019, 0020]
phase: 2
---

# T-0025 — Password reset

## Scope

Closes the Sprint-2 identity foundation. Reuses everything from T-0023/T-0024 (shared `OneTimeToken` + `OpaqueTokenFactory` + atomic-claim repo + `IPersistOnFailureCommand` + timing-equalization pattern).

### Core.AppServices/Features/Auth
- `RequestPasswordReset.cs` — `Command(Email, IpAddress)` → `BusinessResult`. Always Success; silent no-op for unknown / soft-deleted / rate-limited (3 per 10 min). On happy path **invalidates any still-redeemable prior reset tokens** via `IOneTimeTokenRepository.InvalidateRedeemableAsync` so the previously-emailed link can't compete with a newer one (per ADR 0012 §Password reset). TTL **1 hour** per ADR. Outbox event `auth.passwordReset.send`. Timing-equalization invariant inherited from T-0023.
- `ConfirmPasswordReset.cs` — `Command(RawToken, NewPassword)` → `BusinessResult`. Atomic-claim via `TryConsumeAsync`. Single error code `AuthPasswordResetInvalid`. On happy path: hash the new password, set it on the user (which also resets lockout state via `User.SetPasswordHash`), **revoke every active refresh token** for the user (ADR 0012 §Password reset: "force re-login everywhere"). Does NOT mint a session — user logs in with the new password.

### Common
- `BusinessErrorMessage.AuthPasswordResetInvalid = "auth.passwordResetInvalid"`.

### Tests (+10 facts; 311 total)
- 4 RequestPasswordReset facts (incl. B-1 timing pin + the "no Invalidate on rate-limited" pin).
- 6 ConfirmPasswordReset facts (incl. lost-race, cross-purpose-no-burn, soft-deleted-after-claim, happy-path revokes two refresh tokens, password policy).

## Out of scope
- Email send — T-0028/T-0029.
- HTTP endpoints — T-0035.
- T-0024 reviewer follow-ups (security M-2 Register skip rate-limit, code-quality MA-1 template extraction, MA-2 OutboxEventTypes registry, FakeClock dedupe) — folded together with T-0025's own reviewer findings in the next commit.

## Acceptance criteria
- **AC-1** Build clean; 311 tests pass.
- **AC-2** `RequestPasswordReset` returns Success for every input shape AND invalidates prior reset tokens ONLY on the happy path.
- **AC-3** B-1 timing pin holds.
- **AC-4** `ConfirmPasswordReset` uses atomic `TryConsumeAsync`; lost race returns Invalid without setting the password.
- **AC-5** Happy path rotates the password hash AND revokes every active refresh token (pinned by an assertion on two pre-existing refresh tokens).
- **AC-6** Cross-purpose tokens rejected without burn.
- **AC-7** Password-policy validation (min 10 chars) applies to `NewPassword`.

## Status log
- 2026-05-24 done. 311 tests pass. Reviewer follow-ups pending — to be folded with T-0024's deferred items in the consolidation commit.
