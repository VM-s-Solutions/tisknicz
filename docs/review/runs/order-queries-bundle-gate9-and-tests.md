# Order Queries Bundle — Gate 9 + Test Catchup Audit

Branch: `feat/T-0049ab-maker-backend-prep` (working tree clean at audit time)
Date: 2026-06-09

---

## Task 1 — Gate 9 Consistency

**Verdict: `GATE9_PASS`**

- `node scripts/check-consistency.mjs` → exit code 0 ("clean").
- Tracked count: **111** baseline (matches the 107 baseline + 4 new T1 false-positives for the 4 new one-file query feature files: `GetCustomerOrders.cs`, `GetCustomerOrderDetails.cs`, `GetMakerOrders.cs`, `GetMakerOrderDetails.cs`).
- No new violations beyond the predicted T1 wrappers.

---

## Task 2 — Test Catchup Audit

### Inventory (claimed vs verified)

| File | Claimed | Verified | Match |
|---|---|---|---|
| `Makables.Tests/.../GetCustomerOrdersHandlerTests.cs` | 11 | 11 | OK |
| `Makables.Tests/.../GetMakerOrdersHandlerTests.cs` | 10 | 10 | OK |
| `Makables.Tests/.../GetCustomerOrderDetailsHandlerTests.cs` | 7 | 7 | OK |
| `Makables.Tests/.../GetMakerOrderDetailsHandlerTests.cs` | 6 | 6 | OK |
| `Makables.IntegrationTests/.../GetCustomerOrdersIntegrationTests.cs` | 3 | 3 | OK |
| `Makables.IntegrationTests/.../GetMakerOrdersIntegrationTests.cs` | 3 | 3 | OK |
| `Makables.IntegrationTests/.../OrderDetailsIntegrationTests.cs` | 5 | 5 | OK |
| **Total** | **45** | **45** | OK |

### Coverage gap matrix (must-cover surfaces)

| Surface | Covered? | Test | Notes |
|---|---|---|---|
| Handler happy path (Customer list) | YES | `Happy_path_returns_paged_data_with_default_sort` | |
| Handler happy path (Maker list) | YES | `Happy_path_returns_PagedData_with_maker_scoped_orders` | |
| Handler happy path (Customer detail) | YES | `Happy_path_returns_dto_with_all_lifecycle_timestamps_preserved` | |
| Handler happy path (Maker detail) | YES | `Happy_path_returns_dto_with_payout_and_lifecycle_preserved` | |
| Validator failure — Page < 1 | YES | `Validator_rejects_Page_below_1` / `Validator_rejects_zero_page` | |
| Validator failure — PageSize > Max | YES | `Validator_rejects_PageSize_above_max` / `_rejects_oversized_PageSize` | |
| Validator failure — inverted date range | YES (Customer) | `Validator_rejects_inverted_date_range` | Maker handler lacks equivalent — see gap below |
| Validator happy path | YES | `Validator_accepts_happy_path` | |
| IDOR — wrong session (Customer list) | YES | `Unauthorized_when_session_has_no_user` + `Session_userId_passed_to_query_not_request_input` (implicit via cross-tenant integration) | |
| IDOR — wrong session (Maker list) | YES | `Unauthorized_when_session_has_no_user` | |
| IDOR — Customer detail wrong session | YES | `Customer_userId_mismatch_returns_NotFound` + `Unauthorized_when_session_has_no_user` | |
| IDOR — Maker detail wrong session | YES | `Order_ownership_mismatch_returns_OrderNotFound` + `Maker_not_found_for_user_returns_MakerNotFound` | |
| Cross-tenant isolation (integration) | YES | `Cross_tenant_isolation_returns_zero_results...`, `cross_maker_isolation`, `cross_tenant_returns_404`, `cross_maker_returns_404` | |
| Paging boundary (integration) | YES | `Pagination_returns_correct_window` (customer) | Maker integration lacks an explicit paging-window test — minor gap |
| Filter semantics — State | YES | `Filter_by_State_passes_through_to_queries`, `State_filter_is_forwarded_to_OrderFilter` | One representative state (`Paid`); acceptable |
| Filter semantics — DateRange | YES | `Filter_by_date_range_passes_through_to_queries`, `DateRange_filter_is_forwarded_to_OrderFilter` | |
| OrderSort.CreatedAtDesc (default) | YES | `Happy_path_returns_paged_data_with_default_sort` | |
| OrderSort.TotalAmountDesc | YES | `Sort_variant_TotalAmountDesc_passes_through` | |
| OrderSort.CreatedAtAsc | NO | — | Switch arm exists in `OrderQueries.cs:335` |
| OrderSort.TotalAmountAsc | NO | — | Switch arm exists in `OrderQueries.cs:339` |
| OrderSort.StateAsc | NO | — | Switch arm exists in `OrderQueries.cs:341` |
| Date range edge — start == end | NO | — | Validator semantics undocumented for same-day; could surface a `from > to` regression |
| Date range edge — start > end (Maker handler) | NO | — | Customer covered, Maker validator has no equivalent test |
| PageSize == 1 (lower boundary accept) | NO | — | Only "reject 0" and "reject Max+1" present |
| PageSize == Max (upper boundary accept) | NO | — | Happy path uses 20, not Max |
| ProductTitle null (custom order path) | NO | — | DTO field is nullable but no test seeds `ProductTitle: null` |
| InvoicePdfUrl null path (no invoice) | YES | `InvoicePdfUrl_nullable_when_invoice_not_yet_generated`, `InvoicePdfUrl_populated_when_invoice_exists` | |
| Attachments order preservation | YES | `Attachments_field_correctness_preserves_order_and_count` | |
| Attachments cap 10 — projection | DEFER | — | Relying on T-0064 gate via `AddOrderAttachmentHandlerTests` + `OrderAttachmentUploadTests.Upload_rejected_at_11th_attachment...`. Acceptable. |
| DTO pinning — Maker has no PII | YES | `MakerOrderListItemDto_carries_MakerPayoutAmountMinor_and_has_no_CustomerEmail_or_PlatformFee`, `MakerOrderDetailDto_carries_no_CustomerEmail_or_PlatformFee_field` | |
| UnreadMessageCount pinned null until T-0079 | YES | `UnreadMessageCount_is_nullable_int_until_T_0079`, `..._is_null_pin_until_T_0079` (integration) | |
| Empty result PagedData(TotalCount=0) | YES | `Empty_result_returns_empty_PagedData_with_TotalCount_zero` | |

### Recommended TEST_CATCHUP (lean — max 5)

Implementer already exceeded the baseline target (45 tests across 7 files vs. baseline minimum of ~28). Keep follow-up to high-value gaps:

1. **OrderSort coverage of remaining 3 arms.** Add a `[Theory]` driven by `[InlineData(OrderSort.CreatedAtAsc)] [InlineData(OrderSort.TotalAmountAsc)] [InlineData(OrderSort.StateAsc)]` in `GetCustomerOrdersHandlerTests` asserting forwarding to `OrderQueries`. The handler is a pure pass-through, so 3 small Theory rows close the projection-arm gap.
2. **Maker validator: inverted date range.** Customer has it; Maker doesn't. Single test in `GetMakerOrdersHandlerTests`.
3. **PageSize boundary accept tests.** Two tests (`PageSize=1` accepted, `PageSize=MaxPageSize` accepted) across both validators — guards against accidental "off-by-one" in `LessThanOrEqualTo`.
4. **ProductTitle null projection.** One test seeding an order with `ProductTitle=null` (custom order path) — confirms the nullable DTO field round-trips.
5. **Integration paging — Maker.** Mirror the customer `Pagination_returns_correct_window` for the maker list so per-audience paging shipped to NSwag is wire-verified, not just handler-mocked.

### Rule violations

None. All tests use `[Fact]`/`[Theory]`, no `[Ignore]`/`[Skip]`, no inline error strings in the new test files (Gate 9 confirms).

---

## Final Verdict

- Gate 9: PASS (111 tracked, exit 0).
- Test counts: 45 verified vs 45 claimed (100%).
- Coverage gaps: 7 minor surfaces; 5 are worth folding (above). Rest are intentional deferrals (attachment cap → T-0064, multi-state filter → representative-state policy).
- No HARD-FAIL on Gate 5: pure-logic tests (Validator paths) are present alongside the handlers, not after-the-fact.
