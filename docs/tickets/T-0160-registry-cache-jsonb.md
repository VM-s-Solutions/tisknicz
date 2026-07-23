---
id: T-0160
title: Registry cache upsert 42804 (jsonb vs text) — first live ARES lookup 500'd; walk-surfaced
status: in_review
size: S
owner: dotnet-backend
created: 2026-07-23
updated: 2026-07-23
depends_on: [T-0032, T-0159]
blocks: []
user_stories: [US-maker-0001]
adrs: [0018]
phase: 7
manual_steps: []
security_touching: false
layers: [dotnet-backend, dotnet-db]
---

# T-0160 — Registry cache jsonb upsert fix

## Context

The first-ever live ARES lookup on dev (T-0159 registry-preview, exercised
by the T-0153 walk) returned a bodiless 500. Diagnosed via the new
ops-diagnostics workflow (App Insights was empty — ingestion gap; the App
Service filesystem docker log had the truth):
`PostgresException 42804: column "payload" is of type jsonb but expression
is of type text`. The T-0032 cache store's raw `ON CONFLICT` upsert passes
the payload as a text parameter; Postgres does not implicitly cast text
parameters to `jsonb` in VALUES. **Every test ran on the SQLite harness,
where `jsonb` degrades to TEXT** — the config comment even documents the
degradation — so the mismatch could only ever fire on real Postgres. The
same latent bug breaks real maker registration (RegisterMaker persists
through the identical path), meaning maker onboarding on dev was broken
since T-0032 shipped and nobody could know until live traffic hit it.

## Scope

- **Store fix**: provider-aware upsert — Postgres gets `{payload}::jsonb`,
  SQLite keeps the plain parameter (no cast-to-jsonb syntax there).
- **Boundary hardening** (`AresCompanyRegistry`): cache READ failure → warn
  + treat as miss (ARES stays the source of truth); cache WRITE failure →
  warn + serve the fetched record uncached. The adapter's documented "no
  exceptions cross the boundary" contract now actually holds — a cache
  infrastructure failure degrades availability of the cache, never of the
  lookup.
- **Real-Postgres coverage**: new `CompanyRegistryCacheStorePostgresTests`
  (Testcontainers harness) pins insert + conflict-update round-trips
  against the actual `jsonb` column — the test that would have caught this
  at T-0032 time.

## Acceptance criteria

- **AC-1** Given dev's real Postgres, when `registry-preview` runs for a
  valid IČO, then it returns the ARES display slice (no 500) and the row
  lands in `company_registry_cache`.
- **AC-2** Given a cache-store outage, when a lookup runs, then the request
  still succeeds from ARES with a logged warning (both read and write
  paths pinned by unit tests).
- **AC-3** Given the Postgres integration harness, when the upsert runs
  twice for one IČO, then both insert and EXCLUDED-update round-trip.

## Test plan reference

2 new adapter unit tests (degrade on read/write failure) — suite
1901/1901; 1 new Postgres integration test (CI Docker). Live re-verify on
dev after deploy = walk evidence.

## Status log

- 2026-07-23 `draft → in_progress → in_review` — root-caused with the new
  ops-diagnostics workflow the same hour it shipped.
