---
id: T-0024
title: Email confirmation — SendEmailConfirmation + ConfirmEmail; Register auto-enqueues first token
status: done
size: M
owner: dotnet-backend
created: 2026-05-24
updated: 2026-05-24
depends_on: [T-0022, T-0023]
blocks: [T-0025]
adrs: [0012, 0019, 0020]
phase: 2
---

# T-0024 — Email confirmation

## Scope

Builds on T-0023's shared `OneTimeToken` + `OpaqueTokenFactory` + atomic-claim repository, and on T-0022's `IPersistOnFailureCommand` + `User.MatchesAudience` discipline. The security lessons folded into T-0023 (timing equalization, atomic claim, no audience-burn) ship in T-0024 by construction.

### Core.AppServices/Features/Auth
- `SendEmailConfirmation.cs` — user-driven "resend" flow. Always returns Success; silent no-op for unknown / soft-deleted / already-confirmed / rate-limited. TTL **24 hours** per ADR 0012 §Email confirmation. Same 3-per-10-min budget as magic link. Outbox event type `auth.emailConfirmation.send`. Timing equalization: ALWAYS runs `CountIssuedSince` + `OpaqueTokenFactory.GenerateUrlSafe32()` + JSON serialize via sentinel user id so unknown-email latency matches happy path.
- `ConfirmEmail.cs` — atomic-claim redemption. Single error code `AuthEmailConfirmationInvalid` for every reject reason (missing / wrong purpose / expired / consumed / lost race / soft-deleted user). Does NOT mint a session — the user logs in normally afterward. Uses `TryConsumeAsync` for the M-1 race fix; idempotent if the email was confirmed externally before redemption.

### Register handler extended (atomic registration + email send)
`Register.Handler` now mints the first confirmation `OneTimeToken` and enqueues the `auth.emailConfirmation.send` outbox event in the same UoW commit as the user insert. T-0029's `ProcessOutboxFunction` will deliver the email out-of-band.

### Common
- `BusinessErrorMessage.AuthEmailConfirmationInvalid = "auth.emailConfirmationInvalid"`.

### Tests (+12 facts; 301 total)
- `Tests/AppServices/Features/Auth/SendEmailConfirmationHandlerTests.cs` — 5 facts: unknown / already-confirmed / rate-limited no-ops, happy path, B-1 timing-equalization assertion.
- `Tests/AppServices/Features/Auth/ConfirmEmailHandlerTests.cs` — 7 facts: unknown / wrong-purpose / consumed / lost-race / soft-deleted-after-claim / happy path / idempotent already-confirmed.
- `Tests/AppServices/Features/Auth/RegisterHandlerTests.cs` — extended to assert the auto-enqueue of the confirmation token + outbox event.

## Out of scope
- Email send itself — T-0028 (Resend impl) + T-0029 (outbox processor) own the actual delivery.
- HTTP endpoints — T-0035 (the auth pages ticket).
- Gating order placement on `EmailConfirmedAt is not null` — owned by T-0063 (CreateOrder).

## Acceptance criteria
- **AC-1** Build clean; 301 tests pass (238 unit + 63 integration; +12 since T-0023 fix).
- **AC-2** `SendEmailConfirmation` returns `Success` for every input shape — pinned by separate tests for unknown / already-confirmed / rate-limited / happy path.
- **AC-3** `SendEmailConfirmation` no-op paths pay the same DB + crypto cost as the happy path so latency doesn't leak existence — pinned by `Unknown_email_path_also_runs_CountIssuedSince_to_equalize_latency`.
- **AC-4** `ConfirmEmail` collapses every invalid-token reason to one error code AND uses atomic `TryConsumeAsync` for the redemption (M-1 race fix carried forward from T-0023).
- **AC-5** `Register.Handler` enqueues the first email-confirmation token + outbox event in the same UoW commit as the user insert — pinned by extended `RegisterHandlerTests`.
- **AC-6** `ConfirmEmail.Handler` correctly rejects cross-purpose tokens (a `MagicLink` token cannot be redeemed here, and vice-versa) without burning them — pinned per-purpose.

## Status log
- 2026-05-24 done. 301 tests pass. Built on the security baseline from T-0023's reviewer-fix commit.
