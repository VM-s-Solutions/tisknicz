---
id: T-0125
title: Debt-codification bundle — codify findings #2/#3 + Q-0013/Q-0019 ride-alongs
status: ready
size: M
owner: dotnet-backend
created: 2026-06-15
updated: 2026-06-15
depends_on: []
blocks: []
user_stories: []
adrs: [0009, 0013, 0022, 0023]
phase: 4
manual_steps: []
security_touching: false
layers: [backend, db, frontend, config]
---

# T-0125 — Debt-codification bundle

## Context

A chore bundle that turns three recurring reviewer findings + two open questions into mechanical, build-breaking guarantees. Scope is **fully specced and user-locked (2026-06-15)** — no `/feature` deliberation, no AskUserQuestion. Branch `chore/debt-codification-bundle`, one PR, implemented in the §C order.

Two new consistency checks (T8 i18n-parity, T9 unique-index→translator) are added to `scripts/check-consistency.mjs` to codify recurring-findings **#2** and **#3**. Both are **`hard:true` — NOT baselined**. These are real defects we never want to grandfather: a future violation must break the build, not silently inherit a baseline row. After this bundle lands, `node scripts/check-consistency.mjs` must STILL exit 0 on master (T8 allowlist seeded + T9 markers added + the latent dispute bug mapped = zero violations today).

The bundle also rides along two open questions whose fixes are cheap and topically adjacent: **Q-0013** (`/auth/login` → `/login` frontend sweep — 8 live 404 refs) and **Q-0019** (`ix_orders_payout_unclaimed` partial index — pre-empts the year-1 payout-scan cliff). **Q-0017** is recorded as already-resolved (verify-only). The latent **`ux_disputes_order_open`** translator bug surfaced during T9 design is fixed here (user ruled: MAP it) so the T9 check passes for that index without a marker.

`security_touching: NO` — the dispute-translator fix touches a Conflict path but adds **no new auth surface** (it converts an existing race-loser 500 into the same typed 409 the winner's pre-check returns). No new endpoint, no new permission, no new data exposure.

## Locked decisions (§A — user-locked 2026-06-15, non-negotiable)

- **A.1 — T8 is allowlist-driven, not key-everything.** A `BusinessErrorMessage` code is *satisfied* iff its dotted VALUE is a `cs-CZ.ts` key **OR** it is in a new `T8_NO_KEY_REQUIRED` allowlist (codes that intentionally use the `errors.ts` `resolveErrorMessage` type-fallback to generic `error.<type>` copy — confirmed behaviour). Parse codes via `public const string (\w+) = "([^"]+)";` → **group 2** (the dotted value). Parse keys via `^\s*'([^']+)'\s*:`. Flag any code NEITHER keyed NOR allowlisted. **Seed `T8_NO_KEY_REQUIRED` with the 70 currently-unkeyed codes** (enumerated by running the parse on master — architect estimated ~60; actual = 70). Message names the code + "add a cs-CZ key or allowlist it". A code comment points at `frontend/src/lib/runtime/errors.ts`.
- **A.2 — T9 scope = NAMED unique indexes only.** Walk `Infra.Database/Configurations/*.cs` for `.IsUnique()` chained with `.HasDatabaseName("...")`; parse `UniqueConstraintTranslator.cs` dict keys via `\[\s*"([^"]+)"\s*\]\s*=`. A named unique index is satisfied iff its name ∈ translator keys **OR** a `// no-translator: <reason>` marker is present on the index statement. **EF-auto-named indexes (no `HasDatabaseName`) are OUT of scope** (config/seed/auth-infra). Add `// no-translator:` markers to the **5 documented exclusions** (`ix_orders_order_number`, `ix_orders_payment_provider_ref`, `ix_invoices_invoice_number`, `ix_invoices_order_id`, `ix_makers_user_id`), reasons lifted from the translator's existing "Intentionally unmapped" prose.
- **A.3 — `ux_disputes_order_open`: MAP it.** Register it in the translator. No existing `DisputeAlreadyOpen`/`OrderAlreadyDisputed` code exists (verified) — map the race-loser to the SAME Conflict the handler's own already-Disputed invariant branch uses: `Error.Conflict("orderId", BusinessErrorMessage.OrderInvalidTransition)`. (`OpenDispute.Handler` line ~100 already uses `OrderInvalidTransition` for the Disputed-state conflict; the race-loser gets the identical typed 409 instead of a raw 500.) No new error code minted.
- **A.4 — Q-0013 sweep = exactly the 8 live source refs.** Leave all `/api/v1/auth/login` API-client refs and prose doc-comments UNTOUCHED.
- **A.5 — Q-0019 index needs NO translator entry** (it is not unique) — T9 N/A for it.
- **A.6 — both checks `hard:true`, NOT baselined.** `node scripts/check-consistency.mjs` must exit 0 after the bundle.

## Scope (6 items — checklist)

- [ ] **1. T8 i18n-parity check** in `scripts/check-consistency.mjs` (codifies finding #2). Allowlist-driven per A.1. `hard:true`, not baselined. Seed `T8_NO_KEY_REQUIRED` with the 70 unkeyed codes. Comment → `errors.ts`.
- [ ] **2. T9 unique-index→translator check** (codifies finding #3). Per A.2. `hard:true`, not baselined. Add `// no-translator:` markers to the 5 documented exclusions in `OrderConfiguration.cs` / `InvoiceConfiguration.cs` / `MakerConfiguration.cs`.
- [ ] **3. `ux_disputes_order_open` translator fix** (the latent bug). Per A.3: register the index → `Error.Conflict("orderId", BusinessErrorMessage.OrderInvalidTransition)`. Add a concurrent-double-`OpenDispute` integration test (insert an active dispute row directly, then dispatch `OpenDispute` → assert the loser gets the conflict code, NOT a 500). T9 then passes for this index WITHOUT a marker.
- [ ] **4. Q-0013 — `/auth/login` → `/login` sweep (frontend).** Replace the 8 live source refs: `middleware.ts:24`, `profile-client.tsx:53`, `pro-makery/page.tsx:159`, `verify-client.tsx:63`, `reset-client.tsx:56`+`116`, `register-form.tsx:52`+`99`, `register-maker-form.tsx:62`. Leave api-client `/api/v1/auth/login` refs untouched. Verify `next build` + the route resolves.
- [ ] **5. Q-0019 — `ix_orders_payout_unclaimed` partial index (DB).** Add to `OrderConfiguration.cs`: `HasIndex` on `orders`, `.HasDatabaseName("ix_orders_payout_unclaimed")`, partial `WHERE state='Delivered' AND payout_batch_id IS NULL AND is_active` (match the `HasFilter` convention of the existing `ix_orders_state` / `ix_orders_payout_batch_id` partials). Auto-generate the migration. NO translator entry (not unique) — T9 N/A.
- [ ] **6. Q-0017 — VERIFY-ONLY.** Already shipped at `20260613060609_FixEmailSubjectPlaceholders`. Record as verified-resolved in the bundle ledger. NO work.

### Closing-out tasks
- [ ] Flip recurring-findings.md rows **#2** and **#3** to `codified-in-script` (T8 / T9).
- [ ] Add **T8** and **T9** rows to `docs/review/checklist.md` §J (mechanical checks).
- [ ] On merge: flip **Q-0013** + **Q-0019** to `answered` in `docs/questions/open.md`.

## Acceptance criteria

- **AC-1 (T8)** Given a `BusinessErrorMessage` code that is neither a `cs-CZ.ts` key nor in `T8_NO_KEY_REQUIRED`, when `node scripts/check-consistency.mjs` runs, then it reports a T8 finding naming the code + "add a cs-CZ key or allowlist it" and exits 1. Given master as-is (70 codes seeded), the check produces zero T8 findings.
- **AC-2 (T8 hard)** Given the T8 check, when it fires, then the finding is NOT suppressible via the baseline file (`hard:true`) — a future unkeyed-and-unallowlisted code breaks the build.
- **AC-3 (T9)** Given a NAMED `.IsUnique().HasDatabaseName("x")` index with neither a translator key `x` nor a `// no-translator:` marker, when the check runs, then it reports a T9 finding and exits 1. EF-auto-named unique indexes (no `HasDatabaseName`) produce no T9 finding.
- **AC-4 (T9 markers)** Given the 5 documented exclusions, when the check runs, then each is satisfied by its `// no-translator:` marker and produces no T9 finding.
- **AC-5 (dispute map)** Given an order with an existing active dispute row inserted directly, when `OpenDispute` is dispatched concurrently (race-loser hits the `ux_disputes_order_open` 23505), then the loser receives `Error.Conflict("orderId", OrderInvalidTransition)` (HTTP 409), NOT a 500. The integration test asserts the 409 + conflict code. T9 passes for `ux_disputes_order_open` without a marker.
- **AC-6 (Q-0013)** Given the 8 live source refs, when swept to `/login`, then `next build` succeeds and the login route resolves at `/login`. Given the api-client `/api/v1/auth/login` refs, when inspected, then they are unchanged.
- **AC-7 (Q-0019)** Given the new migration applied, when the schema is inspected, then `ix_orders_payout_unclaimed` exists on `orders` with the partial filter `state = 'Delivered' AND payout_batch_id IS NULL AND is_active`. No translator entry exists for it (not unique).
- **AC-8 (Q-0017)** Given the bundle ledger, when read, then Q-0017 is recorded verified-resolved at `20260613060609_FixEmailSubjectPlaceholders` with no code diff.
- **AC-9 (exit 0)** Given the full bundle landed on master, when `node scripts/check-consistency.mjs` runs, then it exits 0 (zero NEW T1–T9 findings; T8 + T9 included).
- **AC-10 (docs)** recurring-findings rows #2/#3 read `codified-in-script`; checklist.md §J lists T8 + T9; on merge Q-0013 + Q-0019 read `answered`.
- **AC-11 (build/tests)** Backend build clean; the new concurrent-dispute integration test passes (Testcontainers Postgres); `next build` clean; NSwag parity unaffected (no contract change in this bundle).

## Test plan stub

- **T8 / T9 self-checks:** add a negative-case smoke (an unkeyed-unallowlisted fixture code → 1 finding; a named unique index without marker/key → 1 finding) OR assert against the real tree exiting 0. Minimal — the script is the test surface.
- **Dispute race (integration, Testcontainers Postgres):** seed Order in a disputable state; insert an active `Dispute` row directly (bypassing the handler); dispatch `OpenDispute`; assert `Error.Conflict` with code `order.invalidTransition`, NOT an unhandled 500. Mirrors the `ux_reviews_order_active` / `ux_payout_batches_open_per_country` concurrent-loser tests.
- **Q-0013:** `next build` + a route-resolution assertion on `/login`.
- **Q-0019:** the T-0123 migration-journal assertion layer (if landed) or a schema-introspection check that the partial index + filter exist.

## Files (expected)

### Modified
- `scripts/check-consistency.mjs` — add `ruleT8` (i18n parity, allowlist-driven, hard) + `ruleT9` (unique-index→translator, hard) + `T8_NO_KEY_REQUIRED` (70 seeds) to `RULES`.
- `backend/src/Makables.Infra.Database/UniqueConstraintTranslator.cs` — add `ux_disputes_order_open` mapping (§A.3).
- `backend/src/Makables.Infra.Database/Configurations/OrderConfiguration.cs` — `// no-translator:` markers on `ix_orders_order_number` + `ix_orders_payment_provider_ref`; NEW `ix_orders_payout_unclaimed` partial index (Q-0019).
- `backend/src/Makables.Infra.Database/Configurations/InvoiceConfiguration.cs` — `// no-translator:` markers on `ix_invoices_invoice_number` + `ix_invoices_order_id`.
- `backend/src/Makables.Infra.Database/Configurations/MakerConfiguration.cs` — `// no-translator:` marker on `ix_makers_user_id`.
- `frontend/src/middleware.ts`, `.../profile/profile-client.tsx`, `.../pro-makery/page.tsx`, `.../(auth)/verify/verify-client.tsx`, `.../(auth)/reset/reset-client.tsx`, `.../(auth)/register/register-form.tsx`, `.../(auth)/register/maker/register-maker-form.tsx` — `/auth/login` → `/login` (8 refs, Q-0013).
- `docs/review/recurring-findings.md` — flip #2/#3 → `codified-in-script`.
- `docs/review/checklist.md` — add T8 + T9 to §J.
- `docs/questions/open.md` — Q-0013 + Q-0019 → `answered` (on merge).

### New
- `backend/src/Makables.Infra.Database/Migrations/<ts>_AddOrdersPayoutUnclaimedIndex.cs` (+ `.Designer.cs`) — auto-generated (Q-0019).
- `backend/src/Makables.IntegrationTests/Orders/OpenDisputeConcurrencyTests.cs` — concurrent-double-OpenDispute race test (§A.3).

## Commits hint

This grooming commit is doc-only: `docs(debt-codification-bundle): groom T-0125 …`. Implementation follows in §C order — T8 (+seed) → T9 (+markers) → dispute map (+test) → Q-0019 (+migration) → Q-0013 sweep → ledger/docs flips — then one regen-free PR (no contract change).

## DoR
1. **not-duplicate** — codifies findings #2/#3 (logged pending) + closes Q-0013/Q-0019; no overlap with T-0123/T-0124 (those are harness + provider-factory chores).
2. **observable G/W/T AC** — AC-1…AC-11 above, each with a measurable proof (script exit code, schema introspection, HTTP 409, `next build`).
3. **sized M** — two script rules + 8 frontend edits + 1 migration + 1 translator entry + 1 integration test + doc flips. Under 16h.
4. **depends_on done** — none (all foundations on master: dispute entity T-0106, translator T-0033, check-consistency T1–T7, errors.ts fallback).
5. **manual_steps** — none.
6. **security_touching** — NO (dispute-translator fix adds no auth surface; noted in Context).
7. **layers** — backend, db, frontend, config.

## Status log

- 2026-06-15 `draft → ready` by PM. User-locked the full scope (§A.1–A.6) 2026-06-15; chore bundle, no deliberation. Six scope items: T8 i18n-parity check (finding #2, allowlist-driven, 70 seeds, hard), T9 unique-index→translator check (finding #3, 5 markers, hard), `ux_disputes_order_open` latent-bug map (§A.3 — mapped to the existing `OrderInvalidTransition` code since no `DisputeAlreadyOpen` code exists; verified) + concurrent race test, Q-0013 8-ref `/auth/login`→`/login` sweep, Q-0019 `ix_orders_payout_unclaimed` partial index + migration, Q-0017 verify-only (already at `20260613060609`). Both checks NOT baselined (real defects, never grandfathered); `check-consistency.mjs` exits 0 on master after the bundle. Closes Q-0013 + Q-0019 on merge; flips recurring-findings #2/#3 to codified-in-script; adds T8/T9 to checklist §J. **Ready for dotnet-backend** (frontend sweep folds into the same PR). depends_on: none.
