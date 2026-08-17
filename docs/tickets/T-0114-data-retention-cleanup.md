---
id: T-0114
title: DataRetentionCleanup Function — purge expired auth artifacts
status: done
size: S
owner: dotnet-backend
created: 2026-06-12
updated: 2026-08-17
depends_on: [T-0011, T-0020, T-0022]
blocks: []
user_stories: []
adrs: [0012, 0020, 0023]
phase: 5
manual_steps: [deploy-trigger]
security_touching: true
layers: [dotnet-backend, dotnet-db, secops]
---

# T-0114 — DataRetentionCleanup Function

## Context

ADR 0020's launch job table has carried a `DataRetentionCleanup` row (weekly
Sunday 03:00 UTC) since the background-jobs decision, described only as
"anonymize / purge per GDPR retention policy". The Function was never built,
and the INDEX row read "placeholder; full GDPR retention policy in v1.1".

Three auth side-tables grow without bound and hold personal data that no
business process needs once the artifact has expired:

| Table | Personal data | Why it accumulates |
|---|---|---|
| `refresh_tokens` | `ip_address`, `user_agent` per issued session | one row per login, forever |
| `one_time_tokens` | `ip_address` per magic-link / confirmation / reset | one row per request, forever |
| `login_attempt_buckets` | **the email address itself** (it is the primary key) | ADR 0012 §Lockout deliberately consumes *ghost* slots for addresses that never registered, so this table accretes emails of non-users |

The third is the sharpest: an anti-enumeration measure quietly builds a
permanent list of every address anyone ever tried to log in with. Data
minimisation (GDPR Art. 5(1)(c)) is the argument for all three.

## Scope

- `IAuthRetentionStore` (`Core.Domain/Identity/`) + `AuthRetentionPurgeResult`
  (per-table counts, no PII, safe to log).
- `AuthRetentionStore` (`Infra.Database/Identity/`) — three raw set-based
  DELETEs on a `IDbContextFactory` context, the `ICompanyRegistryCacheStore`
  isolation precedent (T-0032 M-1 / T-0113): the caller is a timer with no
  request scope. Raw SQL rather than `ExecuteDelete` for the same reason
  T-0113 gives — the SQLite unit harness cannot translate a `DateTimeOffset`
  comparison server-side.
- `AuthRetentionOptions` (`Auth:Retention`, default 30 days) — a grace window
  *after* expiry, so a recent abuse investigation still has the trail.
- `DataRetentionCleanupFunction` — timer on `%DataRetentionCleanup:Schedule%`,
  clamps a misconfigured window to ≥ 1 day (T-0113 clamp precedent), logs the
  three counts.
- Bicep: `dataRetentionCleanupSchedule` param (`0 0 3 * * 0`) →
  `DataRetentionCleanup__Schedule` app setting.

## Out of scope

- **Order / invoice / payout data.** Statutory accounting retention applies;
  purging it on a timer would be a compliance defect, not a fix.
- **A subject's erasure request.** That is `DeleteUserPermanently` (T-0110) and
  the self-service account deletion — a targeted, audited, on-demand path. This
  job is the standing sweep that runs whether or not anyone asks; the two are
  complements, not substitutes.
- **Anonymisation.** Nothing here is anonymised-and-kept; expired artifacts are
  deleted outright. There is no analytics use for them.
- **Outbox / audit-log retention.** `admin_audit_log` is deliberately
  append-only with reject triggers (T-0105); any retention on it needs its own
  decision.

## Acceptance criteria

- **AC-1** Given refresh tokens and one-time tokens that expired before
  `now - retention`, when the Function runs, then they are deleted and the
  per-table counts are logged.
- **AC-2** Given an artifact that is still live, or expired but inside the
  retention window, when the Function runs, then it survives (strict
  less-than cutoff on every table).
- **AC-3** Given a refresh token that is revoked but not yet expired, when the
  Function runs, then it survives — ADR 0012 reuse detection needs the revoked
  row while the token could still be replayed.
- **AC-4** Given a login-attempt bucket whose lockout is still in force, when
  the Function runs, then it survives even if its last attempt predates the
  cutoff (defensive against a misconfigured short window).
- **AC-5** Given a configured retention of 0 or negative, when the Function
  runs, then the window is clamped to 1 day.
- **AC-6** Given a second run with nothing new expired, when it completes, then
  it deletes zero rows (idempotent).

## Test plan

- `AuthRetentionStoreTests` — 9 cases on the in-memory SQLite factory harness
  (AC-1–AC-4, AC-6, per-table counts, empty-table shape).
- `DataRetentionCleanupFunctionTests` — 6 cases with a substituted store
  (cutoff arithmetic, AC-5 clamp, long window, default, token pass-through).
- `AuthRetentionStorePostgresTests` — 2 cases on real Postgres 16. Not
  redundant with the SQLite unit tests: T-0160 shipped a bug precisely because
  SQLite degrades column types, so the `timestamptz` comparison and the
  nullable `locked_until` guard are re-proven as deployed.

## Status log

- 2026-06-12 opened as a placeholder row in INDEX (draft).
- 2026-08-17 `draft → done` by dotnet-backend. Scope resolved from "placeholder"
  to the three auth side-tables — a defensible MVP retention policy rather than
  an empty stub. Evidence: `Makables.Tests` 1999/1999 (1983 before this
  branch), `Makables.IntegrationTests` 274/274 against Postgres 16.
  **Operator step:** none — the schedule ships with a Bicep default; override
  `DataRetentionCleanup__Schedule` or `Auth__Retention__ExpiredArtifactRetentionDays`
  only to change the policy.
