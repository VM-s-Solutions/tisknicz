---
id: T-0123
title: Migration-pipeline assertions — prove the schema, not just that migrations run
status: done
size: S
owner: dotnet-db
created: 2026-06-20
updated: 2026-08-17
depends_on: [T-0062]
blocks: []
user_stories: []
adrs: [0003, 0009, 0020]
phase: 5
manual_steps: []
security_touching: false
layers: [dotnet-db]
---

# T-0123 — Migration-pipeline assertions

## Context

T-0062 shipped `PostgresHarness`, which calls `MigrateAsync` at fixture start.
That proves the migrations **run**. It does not prove they produce the schema
the code assumes — and the gap is not theoretical:

- **T-0160** shipped a live 500 on the first real ARES lookup because the raw
  upsert passed a text parameter to a `jsonb` column. Every unit test passed:
  SQLite degrades `jsonb` to TEXT.
- Nothing anywhere asserted that a money column is `bigint`, that a timestamp
  is `timestamptz`, or that a partial index kept its filter. A dropped filter
  does not error — the plan just changes.

## Scope

`Makables.IntegrationTests/Database/MigrationPipelineTests.cs`, on the shared
Postgres collection. Four groups:

1. **The journal** — every migration in the assembly is applied, none pending,
   and the committed model snapshot matches the current model (catches an
   entity change committed without `dotnet ef migrations add`, whose first
   symptom is otherwise a runtime column-not-found in the most expensive
   environment).
2. **Money (ADR 0003)** — every `*_minor` column is `bigint`; every table with
   minor units also carries a currency column; every currency column is
   `char(3)`.
3. **Time** — no `timestamp without time zone` anywhere. A naive column drops
   the offset, which is *correct on a UTC dev box* and wrong in production.
4. **Audit + indexes** — the ten `Auditable` tables carry the full audit column
   set with a NOT NULL `is_active` (a nullable flag would let the global query
   filter skip rows); ten named indexes behind hot read paths exist; three
   partial-index filters that carry correctness weight are pinned; the registry
   cache payload is `jsonb`.

## Design note — invariants, not a snapshot

The money / timestamp / audit checks enumerate the live catalog rather than
listing tables by name. A schema-snapshot test goes stale, gets noisy, and
ends up muted; an invariant test gets *more* valuable as the schema grows,
because a new table that violates the rule is exactly the finding. The
timestamp test additionally asserts that timestamptz columns *do* exist — an
all-empty result would otherwise come just as easily from a mistyped catalog
query, and the assertion would pass forever while proving nothing.

## Out of scope

- Down-migration / rollback testing. Nothing in the deploy path runs `Down`,
  and asserting it would pin behaviour we deliberately do not rely on.
- Migration *performance* (lock duration on large tables) — real, but it needs
  production-sized data, not a fresh container.

## Acceptance criteria

- **AC-1** Given the migrated database, when the journal is inspected, then
  every assembly migration is applied and none are pending.
- **AC-2** Given an entity change with no corresponding migration, when the
  suite runs, then the model-drift test fails.
- **AC-3** Given the schema, when money columns are enumerated, then every
  `*_minor` is `bigint`, every such table has a currency column, and every
  currency column is 3 characters.
- **AC-4** Given the schema, when timestamp columns are enumerated, then none
  is `timestamp without time zone`.
- **AC-5** Given the ten `Auditable` tables, when their columns are inspected,
  then each carries `country_code`, `is_active`, `created_by`, `created_at`,
  `updated_by`, `updated_at`, with `is_active` NOT NULL.
- **AC-6** Given the named hot-path indexes and the three correctness-bearing
  partial filters, when the catalog is inspected, then each exists with its
  filter intact.

## Status log

- 2026-06-20 opened (draft) — gap surfaced while reviewing T-0062's harness.
- 2026-08-17 `draft → done` by dotnet-db. 34 assertions, all green on the
  first run: the schema already satisfied every invariant, which is the point
  — they are now *pinned* rather than incidentally true.
  `Makables.IntegrationTests` 306/306 (272 before) against Postgres 16.
