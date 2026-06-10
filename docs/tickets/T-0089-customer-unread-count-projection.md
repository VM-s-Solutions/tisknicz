---
id: T-0089
title: Customer unread-count projection — verification gate (core gap already shipped)
status: ready
size: S
owner: dotnet-backend
created: 2026-06-09
updated: 2026-06-09
depends_on: [T-0079, T-0080]
blocks: [T-0086a]
user_stories: [US-customer-0016]
adrs: [0022, 0023]
phase: 4
manual_steps: []
security_touching: false
layers: [web-customer, frontend-api-client]
---

# T-0089 — Customer unread-count projection — verification gate (core gap already shipped)

## Context

T-0089 is the **second ticket in the order-dashboards bundle** (`feat/order-dashboards-bundle`: T-0088 → T-0089 → T-0086a → T-0086b → T-0087a → T-0087b). The bundle plan reserved this slot for "add `UnreadMessageCount` to `CustomerOrderListItemDto` + the `GetCustomerOrdersPagedAsync` projection + NSwag regen" so the T-0086a customer order list can render unread-message badges.

**Reality check (2026-06-10, against the `feat/order-cleanup-bundle` working tree): the core gap is ALREADY SHIPPED.** The order-cleanup bundle's "Phase E" fold delivered it:

- `Core.Domain/Orders/Queries/CustomerOrderListItemDto.cs` carries `int UnreadMessageCount` (non-nullable, with XML doc referencing T-0079).
- `Infra.Database/Orders/OrderQueries.cs` `GetCustomerOrdersPagedAsync` projects the denormalized `o.CustomerUnreadMessageCount` column (line 96) — O(1) per row, no per-row JOIN on the messages table. The maker-side mirror (`o.MakerUnreadMessageCount`, line 158) shipped in the same fold.
- Handler unit tests (`GetCustomerOrdersHandlerTests.cs:42,181`) already pin the field's passthrough.
- The maker-side **integration** pin exists (`GetMakerOrdersIntegrationTests.GET_orders_UnreadMessageCount_returns_denormalized_value` — asserts a bumped counter flows to the wire and no-message orders surface `0`, not null).
- The NSwag clients type `unreadMessageCount` on both hosts (`customer-api.v1.ts:2022` as `number`; `maker-api.v1.ts:2725` as `number | undefined` — the maker contract shipped nullable at T-0081 and stays so per the DTO's documented decision).

**T-0089 therefore rescopes to a thin verification gate** with exactly one residue confirmed after PM re-verification:

1. **The customer-side integration pin is missing.** `GetCustomerOrdersIntegrationTests.cs` does not assert `UnreadMessageCount` anywhere — the maker file has the wire-level pin, the customer file (the one T-0086a actually builds on) does not. One mirrored test closes it.

> PM re-verification 2026-06-09: the NSwag regen initially flagged as a second residue (uncommitted working-tree diff) was a stale-snapshot artifact — the regen landed as commit `ea3271f` and merged to master via PR #44. `git show origin/master:frontend/src/lib/api-client/customer-api.v1.ts` confirms `unreadMessageCount` is committed. The regen gate is CLOSED; only the test residue remains.

**PM note — drop/absorb option:** this ticket is legitimately droppable if PM prefers; the only code change is one integration test. It is written as a thin S to keep the bundle's "NSwag regen gate before frontend slices" position explicit and owned, rather than absorbed silently into a frontend ticket (the test is backend-owned; T-0086a is frontend-owned; one-PR-per-ticket discipline argues against cross-owner absorption). Flagged in the bundle grooming report.

## Locked design decisions

Captured per `docs/process/deliberation.md`. Nothing here required user input — the original user-locked dimension (denormalized counter column read verbatim, no per-row subquery) was locked and implemented under T-0079; T-0089 only verifies it.

### A. User-locked (inherited from bundle plan, already satisfied)

1. **Customer order list rows carry `UnreadMessageCount` read from the denormalized `orders.customer_unread_message_count` column.** Shipped by the order-cleanup bundle fold. T-0089's job is proof, not implementation.

### B. ADR-locked (no relitigation)

- **ADR 0023 (read-side projections).** The shipped projection is `AsNoTracking` + `IgnoreAutoIncludes` + column-read — verified, no changes.
- **ADR 0022 (NSwag is the contract).** The regen diff for the new field MUST be committed and CI parity must pass before any frontend slice consumes `unreadMessageCount`. This is the gate T-0089 owns.
- **No manual edits to `frontend/src/lib/api-client/`** (pre-commit hook).

### C. PM-absorbed (no user input needed)

- **Rescope to verification-only.** No DTO change, no projection change, no migration, no error codes, no i18n keys.
- **One new integration test** in `GetCustomerOrdersIntegrationTests.cs` mirroring the maker-side `GET_orders_UnreadMessageCount_returns_denormalized_value` pin (same seed shape, customer host, customer JWT).
- **`CustomerOrderListItemDto.UnreadMessageCount` stays non-nullable `int`; `MakerOrderListItemDto.UnreadMessageCount` stays `int?`.** The asymmetry is documented in the maker DTO's XML doc ("the T-0081 wire contract shipped nullable; tightening it is NSwag churn for zero gain"). Do not harmonize.
- **Regen already on master.** PM re-verification confirmed the regen merged via PR #44 (commit `ea3271f`); this ticket's regen work is zero. AC-3 reduces to citing the master-side typing as proof.

## Scope

### Verification checklist (no code change — proofs recorded in the PR description)

1. `CustomerOrderListItemDto` on master carries `int UnreadMessageCount`.
2. `OrderQueries.GetCustomerOrdersPagedAsync` projects `o.CustomerUnreadMessageCount`.
3. `GetCustomerOrdersHandlerTests` pins the field (already present — lines 42, 181 at reality-check time).
4. `customer-api.v1.ts` on master types `unreadMessageCount: number` on the customer order list item (verified: merged via PR #44, commit `ea3271f`); `.spec-hashes.json` consistent; CI contract-parity green.

### Tests

- **`backend/src/Makables.IntegrationTests/Orders/GetCustomerOrdersIntegrationTests.cs`** — add one test, `GET_orders_UnreadMessageCount_returns_denormalized_value`:
  - Seed two orders for the requesting customer; bump `customer_unread_message_count` to 2 on one (post a maker message via the T-0079 seam or set the column through the seeded aggregate — match whichever mechanism the maker-side twin uses).
  - `GET /api/v1/orders` as the owning customer → 200; the bumped order's row carries `UnreadMessageCount == 2`; the untouched order carries `0` (not null).
  - Mirrors `GetMakerOrdersIntegrationTests.GET_orders_UnreadMessageCount_returns_denormalized_value` line-for-line where possible.

### NSwag regen

- **Conditional** per §C: verify committed, regen only if the order-cleanup PR did not carry the diff. No spec change originates in this ticket (the field is already in the OpenAPI document via the shipped DTO).

### Docs

- **`docs/tickets/INDEX.md`** — PM flips T-0089 to `**done**` post-merge, with a note "rescoped to verification gate — core gap shipped by order-cleanup bundle".

## Alternatives Considered

- **Option A — Implement the original scope (DTO field + projection + regen + 2 tests).** *Rejected by reality check* — the field, projection, handler pins, maker-side integration pin, and generated-client types all exist on the `feat/order-cleanup-bundle` tree (Phase E fold). Re-implementing is a no-op at best and a merge conflict at worst.
- **Option B — Drop the ticket entirely.** *Rejected (narrowly)* — two residues are real: the customer-side wire-level integration pin is missing (the maker side has one; asymmetric coverage on exactly the surface T-0086a consumes), and the NSwag regen diff was uncommitted at reality-check time. A thin S keeps the bundle's regen gate owned and auditable. PM MAY still drop this in favour of absorbing the test into another backend PR — the Context flags it.
- **Option C — Absorb into T-0086a.** *Rejected* — T-0086a is frontend-owned; the integration test is backend-owned (Testcontainers + seeded aggregates). Cross-owner tickets break the one-PR-per-ticket discipline and would put a backend test review inside a frontend PR.
- **Option D — Also tighten `MakerOrderListItemDto.UnreadMessageCount` from `int?` to `int` for symmetry.** *Rejected per the DTO's own documented decision* — the T-0081 wire contract shipped nullable; tightening is NSwag churn + a breaking client-type change for zero behavioural gain.
- **Option E — Add a dedicated unread-count endpoint (`GET /orders/unread-summary`).** *Rejected* — the denormalized per-row field on the existing list response is the locked T-0079 design; a separate endpoint adds a roundtrip and a second source of truth.

## Out of scope

- **Maker-side anything** — field, projection, and integration pin already shipped and tested.
- **Unread badge UI** — T-0086a (list) + T-0086b (detail) own rendering; the messages read-marking flow is T-0079 (done).
- **Counter write-side correctness** (increment/reset semantics) — owned and tested by T-0079 (`OrderUnreadCountTests`, message-feature handler tests).
- **DTO nullability harmonization** — explicitly rejected per Option D.
- **Any new endpoint, migration, error code, or i18n key** — none.

## Acceptance criteria

- **AC-1** Given the bundle branch head, when `CustomerOrderListItemDto`, `OrderQueries.GetCustomerOrdersPagedAsync`, and `GetCustomerOrdersHandlerTests` are inspected, then the field, the `o.CustomerUnreadMessageCount` projection, and the handler pins are present on master-merged code (verification proofs linked in the PR description).
- **AC-2** Given a customer with one order carrying 2 unread messages and one with none, when `GET /api/v1/orders` is called on the Customer host with the owner's JWT, then the response rows carry `unreadMessageCount: 2` and `unreadMessageCount: 0` respectively (the new integration test — `0`, never null, on the customer contract).
- **AC-3** Given master, when `frontend/src/lib/api-client/customer-api.v1.ts` is inspected, then `unreadMessageCount: number` typing is present (verified merged via PR #44 commit `ea3271f`; cite in the PR description) and the CI contract-parity check passes.
- **AC-4** Build clean; integration tests baseline + 1 new; `node scripts/check-consistency.mjs` exit 0; zero production-code diff in this ticket (test-only).

## Risk / mitigation

- **Risk: verification tickets rot into rubber stamps.** Mitigation: AC-2 is a real, currently-missing test — the ticket has at least one falsifiable deliverable.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0089.md`.

## Files touched (expected)

### Modified
- `backend/src/Makables.IntegrationTests/Orders/GetCustomerOrdersIntegrationTests.cs` — one new test.

### NOT modified (explicit non-changes — guards against scope creep)
- `backend/src/Makables.Core.Domain/Orders/Queries/CustomerOrderListItemDto.cs` — field already present.
- `backend/src/Makables.Core.Domain/Orders/Queries/MakerOrderListItemDto.cs` — nullability stays per Option D.
- `backend/src/Makables.Infra.Database/Orders/OrderQueries.cs` — projection already reads the column.
- `backend/src/Makables.Infra.Database/Migrations/` — no migration.
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` — no codes.
- `frontend/src/lib/i18n/cs-CZ.ts` — no keys.

## Commits hint

1. `test(T-0089): customer-side integration pin for UnreadMessageCount wire passthrough`

## Status log

- 2026-06-09 `draft` by PM. Created as the bundle's backend gap slice per the dashboards grooming session: "DTO field + projection + NSwag regen + 2 tests, OR rescope to verification if the order-cleanup fold already shipped it".
- 2026-06-09 `draft → ready` by BA/PM after source-level reality check: **core gap already shipped** — DTO field present, projection reads `o.CustomerUnreadMessageCount`, handler tests pin it, maker-side integration pin exists, NSwag clients type the field on both hosts (regen merged via PR #44 commit `ea3271f`, confirmed by PM re-verification against origin/master). Rescoped to a thin verification gate: 1 missing customer-side integration test. **Ready for dotnet-backend** (kept as the bundle's explicit regen-gate slot per Context rationale).

## Definition of Ready

- [x] Reality check performed against actual source (DTO, projection, tests, generated clients) — findings recorded in Context.
- [x] Residual scope is concrete and falsifiable (1 named test + 1 named verification).
- [x] No open design questions — all decisions inherited from T-0079/T-0080/T-0081 locks.
- [x] Drop/absorb recommendation explicitly surfaced for PM.
- [x] AC are observable proofs (wire values, committed-diff check, CI parity).
