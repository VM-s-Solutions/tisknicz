# Order-queries bundle — Reviewer final verdict

> Final pass against the actual diff on `feat/order-queries-bundle`. Supersedes the preliminary draft at `docs/review/runs/order-queries-bundle-draft.md`.

## Bundle scope (T-0080 + T-0081 + T-0082)

Read-only bundle. Four new endpoints behind a fresh `IOrderQueries` read-side seam:

| Endpoint | Host | Ticket |
|---|---|---|
| `GET /api/v1/orders` (list) | Customer | T-0080 |
| `GET /api/v1/orders/{orderId}` (detail) | Customer | T-0082 |
| `GET /api/v1/orders` (list) | Maker | T-0081 |
| `GET /api/v1/orders/{orderId}` (detail) | Maker | T-0082 |

Zero migrations, zero outbox events, zero new `BusinessErrorMessage` codes, zero state-machine touches.

**Commit SHA range reviewed:** `7120881..59021c2` (7 commits inclusive: 5 production + 1 baseline + 1 NSwag regen).

## Verdict

**APPROVE** — every locked decision honoured, all 17 mandatory checks pass, build clean (0 warnings / 0 errors), 1326 unit tests + 170 integration tests green. Zero HIGH / MEDIUM / LOW findings net-new against the draft.

Rationale:
- IDOR shield correctly implemented as a WHERE-predicate baked into EF (lines 56, 120, 178, 243 of `OrderQueries.cs`). Cross-tenant probes return `TotalCount=0` (lists) or `null` → 404 (details). No `Unscoped()` short-circuit anywhere.
- GDPR data-minimization lock holds: zero `Email` references in any of the maker DTOs / projections. Only doc-comment mentions explaining the deliberate absence.
- Two separate detail queries / handlers / DTOs (compile-time IDOR shield) — confirmed at `GetCustomerOrderDetails.cs` + `GetMakerOrderDetails.cs`. No shared audience-flag handler.
- All 4 Response wrappers carry the globally-unique `Get*Response` prefix; no bare `record Response` in the 4 new feature files (pre-existing bare `Response` in `AddOrderAttachment.cs`, `CreateOrder.cs`, `CreatePaymentSession.cs`, `MarkOrderPaid.cs` are unrelated to this bundle).
- NSwag regen committed at `59021c2` (`customer-api.v1.ts` + `maker-api.v1.ts` + `.spec-hashes.json`); all 4 Response types resolve in the generated client.

## Confirmed / refuted findings

| # | Check | Status | Citation |
|---|---|---|---|
| 1 | A.4 one-file feature shape | CONFIRMED | All 4 files: single `public static class` with nested `Query`/`Validator`/`Handler` + top-level Response wrapper. `GetCustomerOrders.cs:32`, `GetMakerOrders.cs:35`, `GetCustomerOrderDetails.cs:29`, `GetMakerOrderDetails.cs:27`. |
| 2 | IDOR shield WHERE-predicate (lists + details) | CONFIRMED | `OrderQueries.cs:56` (customer list), `:120` (maker list), `:178` (customer detail), `:243` (maker detail). Zero `Unscoped()` / `IgnoreQueryFilters` calls. |
| 3 | Customer email NEVER on maker responses | CONFIRMED | `MakerOrderListItemDto.cs` + `MakerOrderDetailDto.cs`: zero `Email` properties. `OrderQueries.cs` lines 134 + 239 are doc comments only — projection expression trees never reference `o.ContactEmail`. AC-4 reflection pin asserted in `OrderDetailsIntegrationTests` per commit `ef6f5e6` message ("the maker happy-path test also reads the response as a raw string and pins NotContain `customerContactEmail` / `customerEmail`"). |
| 4 | Two separate detail queries | CONFIRMED | Separate files (`GetCustomerOrderDetails.cs`, `GetMakerOrderDetails.cs`), separate handlers, separate Query records, separate Response wrappers. No audience flag. |
| 5 | Globally-unique Response naming | CONFIRMED | `GetCustomerOrdersResponse` (`GetCustomerOrders.cs:51`), `GetMakerOrdersResponse` (`GetMakerOrders.cs:51`), `GetCustomerOrderDetailsResponse` (`GetCustomerOrderDetails.cs:36`), `GetMakerOrderDetailsResponse` (`GetMakerOrderDetails.cs:34`). Grep for bare `record Response` inside the 4 new feature files: 0 hits. |
| 6 | AsNoTracking + IgnoreAutoIncludes on every read | CONFIRMED | `OrderQueries.cs:54-55`, `:118-119`, `:176-177`, `:241-242` — every base query opens with `.AsNoTracking().IgnoreAutoIncludes()`. ADR 0023 NFR satisfied. |
| 7 | Page-size clamp via Validator | CONFIRMED | `GetCustomerOrders.cs:64-67` + `GetMakerOrders.cs:62-65` both use `InclusiveBetween(1, MaxPageSize)` where `MaxPageSize = 50`. Plus `Page` upper-bound `int.MaxValue / MaxPageSize` to prevent Skip-offset overflow (T-0043 precedent applied). |
| 8 | Sort enum default + 5-value switch | CONFIRMED | `OrderSort.cs` has 5 values; `ApplySort` at `OrderQueries.cs:330-345` has 4 named arms + default `_ => CreatedAtDesc` covering both `CreatedAtDesc` and any future unrecognised value. Default safe; tie-breaker `ThenByDescending(o => o.Id)` (or `ThenBy` for asc) applied on every arm. |
| 9 | Two-pass count + skip/take | CONFIRMED | `OrderQueries.cs:60` + `:75-95` (customer list), `:124` + `:136-156` (maker list). Two SQL statements per call per ADR 0023 / T-0080 §AC-12 lock. PagedData positional args `(items, page, pageSize, totalCount)` correct at `:97` + `:158` (matches the `PagedData<T>` ctor; the ticket sample's wrong-positional was avoided). |
| 10 | Date range validator (DateFrom > DateTo error) | CONFIRMED | `GetCustomerOrders.cs:86-91` + `GetMakerOrders.cs:78-83` use `When(DateFrom + DateTo set, () => RuleFor(DateFrom).LessThanOrEqualTo(DateTo))` with `BusinessErrorMessage.MinValue` code. No new code added. |
| 11 | InvoicePdfUrl backend-built + null when missing | CONFIRMED | `OrderQueries.cs:218-221` (customer) + `:282-285` (maker): `db.Set<Invoice>().Where(i => i.OrderId == o.Id && i.PdfBlobPath != null).Select(i => "/api/v1/orders/" + o.Id + "/invoice").FirstOrDefault()`. Backend-built relative URL; `null` when no invoice or no PdfBlobPath. Placeholder route documented in commit `1f690ef` message; T-0086 lands the real route (acceptable). |
| 12 | Attachments inline with backend-built DownloadUrl | CONFIRMED | `OrderQueries.cs:208-217` + `:272-281`: `db.Set<OrderAttachment>().Where(a => a.OrderId == o.Id).OrderBy(...).Select(a => new OrderAttachmentSummaryDto(... "/api/v1/orders/" + o.Id + "/attachments/" + a.Id))`. Backend-built relative URL; no raw blob path. `OrderAttachmentSummaryDto` carries Id, Filename, ContentType, SizeBytes, DownloadUrl. |
| 13 | DTO location at Core.Domain/Orders/Queries | CONFIRMED | All DTOs at `Core.Domain/Orders/Queries/` matching T-0049a precedent (`MakerProductListItem` in `Core.Domain`). Layer boundary correct — `Core.AppServices` cannot host DTOs that `Core.Domain` interfaces return. |
| 14 | Defensive empty-session-id returns | CONFIRMED | `OrderQueries.cs:45-48` (`PagedData.Empty` for blank customerId), `:110-113` (same for makerId), `:166-169` + `:232-235` (return `null` for blank orderId/userId). Defence-in-depth alongside `[Authorize]` middleware. |
| 15 | Build + tests green | CONFIRMED | `dotnet build`: **0 warnings, 0 errors** in 22s. `dotnet test Makables.Tests`: **1326 passed**. `dotnet test Makables.IntegrationTests`: **170 passed**. Exact match with the brief's expected baseline. |
| 16 | Forbidden patterns | CONFIRMED | Grep across the 4 new feature files + `OrderQueries.cs` for `SaveChangesAsync`, `Console.WriteLine`, `dynamic`, `countryCode ==`: 0 hits. Inline error strings: 0 hits (existing `BusinessErrorMessage.OrderNotFound` + `MakerNotFound` + `MinValue` + `InvalidEnumValue` + `Required` + `MaxLength` reused). |
| 17 | NSwag regen committed | CONFIRMED | Commit `59021c2` regenerates `customer-api.v1.ts` + `maker-api.v1.ts` + updates `.spec-hashes.json`. Grep for `Get(Customer|Maker)Orders(Response\|OrderDetailsResponse)` in the api-client: hits in both `customer-api.v1.ts` + `maker-api.v1.ts`. |

## New findings

**None.** No HIGH, no MEDIUM, no LOW net-new findings against the draft pre-flight.

The draft's HIGH-5 ticket-spec-vs-entity mismatches (Maker.DisplayName, Order.OrderItems, VatAmountMinor, ICustomerSessionContext, Order.Invoice navigation) were all correctly resolved by the implementer at code time:
- `Maker.CompanyName` used (`OrderQueries.cs:87`, `:198`).
- `Order.Product` accessed via explicit `db.Set<Product>()` LEFT JOIN — no navigation property assumed (`OrderQueries.cs:91-94`).
- `VatAmountMinor` computed inline via `ComputeVatAmount(gross, rateBp)` half-up integer rounding at `OrderQueries.cs:191`/`:256` + helper at `:301-311`.
- `IUserSessionProvider.GetUserId()` used uniformly — no `ICustomerSessionContext` invented (`GetCustomerOrders.cs:103`, `GetMakerOrders.cs:96`, etc.).
- `Invoice` LEFT JOIN explicit via `db.Set<Invoice>()` — no navigation property assumed (`OrderQueries.cs:218`, `:282`).

The draft's MEDIUM observations (tie-breaker sort, two-pass count, `DownloadUrlsOptions`) were folded into the implementation as expected:
- Tie-breaker `ThenByDescending(o => o.Id)` (or `ThenBy` ascending mirror) on every sort arm at `OrderQueries.cs:336, 338, 340, 342, 344`.
- Two-pass count + skip/take confirmed at all 4 list/detail call sites.
- `DownloadUrlsOptions` NOT introduced — implementer inlined the relative-path construction (`"/api/v1/orders/" + o.Id + "/attachments/" + a.Id`). Acceptable: the audience host's base URL is known at the FE; the relative path is stable across environments; the `DownloadUrlsOptions` indirection would have added a config knob with no current consumer.

## Bundle DoR compliance

| Check | Status |
|---|---|
| All 3 tickets at status ready (DoR satisfied individually) | ✓ |
| Bundle scope named in branch (`feat/order-queries-bundle`) | ✓ |
| Bundle dep chain documented (T-0080 → T-0081 → T-0082) | ✓ |
| No external blockers | ✓ |
| Single parallel-reviewer artifact (this file supersedes draft) | ✓ |
| L-split rule not triggered (all three are M) | ✓ |
| Bundle LOC budget ~3000 prod + 1500 tests | ✓ (~2100 prod + ~1880 tests inside Orders scope) |

## Quality gates summary

| Gate | Status | Notes |
|---|---|---|
| Gate 1 — Build green | PASS | 0 warnings / 0 errors |
| Gate 2 — Unit tests green | PASS | 1326 / 1326 |
| Gate 3 — Integration tests green | PASS | 170 / 170 |
| Gate 4 — Checklist walk | PASS | All A-J sections pass; see findings table above |
| Gate 5 — TDD (pure logic test-first) | PASS | Validator tests present (`GetCustomerOrdersHandlerTests.cs:187-238` + maker mirror); handler tests carve-out applies for orchestration code per `docs/process/tdd-policy.md` §"Carve-outs" — same-commit landing accepted |
| Gate 6 — NSwag parity | PASS | Regen commit `59021c2`; all 4 Response types resolve in `customer-api.v1.ts` + `maker-api.v1.ts`; `.spec-hashes.json` updated |
| Gate 7 — Docs touched where applicable | PASS | No new ADR / process / arch doc required; tickets carry the locked decisions |

Gate 8 (perf) + Gate 9 (consistency) are owned by their respective agents; this verdict does not block on them. Optimizer ping recommended at PR-open per draft §"Optimizer ping (Gate 8)" for composite-index verification on `(customer_user_id, created_at DESC)` + `(maker_id, created_at DESC)` — flagged for the optimizer agent, not a reviewer block.

## Harvest note

Zero recurring findings to log. The bundle is the cleanest read-only bundle to date — every pre-flight risk landed exactly as designed in the draft; the implementer did the entity-surface adaptation work the draft anticipated. No additions to `docs/review/recurring-findings.md`.
