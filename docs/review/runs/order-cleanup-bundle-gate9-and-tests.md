# Order Cleanup Bundle (T-0079, T-0083) — Gate 9 + Test-Catchup Audit

Date: 2026-06-10 · Branch: `feat/order-cleanup-bundle` · Auditor: Reviewer agent

---

## Gate 9 — Mechanical consistency

**Verdict: PASS.** `node scripts/check-consistency.mjs` → exit 0, **118 tracked** (baseline 111 + 7 new, exactly as expected).

The 7 new tracked rows are all T1 one-file-feature false-positives, all line 1, "feature file must declare a public static class wrapper":

1. `Features/OrderMessages/GetCustomerOrderMessages.cs`
2. `Features/OrderMessages/GetMakerOrderMessages.cs`
3. `Features/OrderMessages/MarkCustomerOrderMessagesAsRead.cs`
4. `Features/OrderMessages/MarkMakerOrderMessagesAsRead.cs`
5. `Features/OrderMessages/PostCustomerOrderMessage.cs`
6. `Features/OrderMessages/PostMakerOrderMessage.cs`
7. `Features/Orders/CancelExpiredOrder.cs`

No untracked violations. No count overrun.

---

## Test inventory — claimed vs verified

Counting method: `[Fact]`/`[Theory]` attributes per file; branch delta via `git diff master...HEAD`.

**Totals.** Claimed unit 1333 → 1379 (+46). Verified: diff adds exactly **+46** unit attributes; runtime case count = 956 `[Fact]` + 423 `[InlineData]` (0 `[MemberData]`) = **1379 exactly**. Integration: **+1** verified. Totals fully reconcile.

| File | Claimed | Verified | Match |
|---|---|---|---|
| `Domain/Orders/OrderUnreadCountTests.cs` | 6 | 6 | OK |
| `Domain/Orders/OrderNotificationDebounceTests.cs` | 10 | **9** | Off by 1 |
| `Domain/Orders/OrderCancellationSourceTests.cs` | 3 | 3 | OK |
| `Domain/Outbox/OutboxEventTypesTests.cs` | +8 new | **+9 new** (22 total, 0 removed) | Off by 1 |
| `OrderMessages/PostCustomerOrderMessageHandlerTests.cs` | 6 | 6 | OK |
| `OrderMessages/PostMakerOrderMessageHandlerTests.cs` | 3 | 3 | OK |
| `OrderMessages/MarkCustomerOrderMessagesAsReadHandlerTests.cs` | 3 | 3 | OK |
| `OrderMessages/GetCustomerOrderMessagesHandlerTests.cs` | 2 | 2 | OK |
| `Orders/CancelExpiredOrderHandlerTests.cs` | 5 | 5 | OK |
| `Orders/GetCustomerOrdersHandlerTests.cs` | fixed for DTO field | Modified (+4/−2): adds `UnreadMessageCount: 0` to DTO ctor, no new tests | OK |
| `Payments/CancelExpiredPendingPaymentOrdersIntegrationTests.cs` | +1 integration | 1 | OK |

The two off-by-ones cancel out (6+9+3+9+6+3+3+2+5 = 46): one test was misattributed between the debounce and outbox files in the claim. Benign bookkeeping error, no missing tests at the total level.

Note (not a bug): `MarkNotificationEmittedFor_Maker_sets_customer_pointer` is correct semantics — author=Maker → recipient=Customer → `CustomerPendingNotificationEmailAt` (verified at OrderNotificationDebounceTests.cs:126–135).

---

## Coverage gap matrix

| Must-cover surface | Covered? | Test | Notes |
|---|---|---|---|
| Unread counter domain ops (increment/clamp/reset, both roles) | YES | `OrderUnreadCountTests` (6) | Idempotent-at-zero included |
| Debounce window predicate (`ShouldEmit`, boundary at exactly 5 min) | YES | `OrderNotificationDebounceTests` (9) | `ClearPendingNotificationFor_Maker` untested (only Customer clear) — minor |
| `OrderCancellationSource` enum pinning | YES | `OrderCancellationSourceTests` (3) | |
| New outbox event types | YES | `OutboxEventTypesTests` (+9) | |
| `PostCustomerOrderMessage` handler | PARTIAL | 6 tests incl. debounce-suppress, after-window refresh, cross-tenant, no-session | New `Failure` refs `MakerNotFound` (:130) and `MakerUserMissing` (:140) have **no negative-path test in this diff** (must-cover §9; codes asserted only in pre-existing tests for *other* handlers) |
| `PostMakerOrderMessage` handler | PARTIAL | 3 tests (happy, cross-maker `OrderNotFound`, payload) | Missing vs customer twin: **debounce-suppress**, **after-window refresh**, **no-session Unauthorized**; new `Failure(OrderCustomerUserMissing)` (:119) untested in diff (§9) |
| `MarkCustomerOrderMessagesAsRead` handler | YES | 3 tests (reset+clear pointer, idempotent, cross-tenant) | |
| `MarkMakerOrderMessagesAsRead` handler | **NO — FILE MISSING** | — | Handler surfaces `OrderNotFound` ×2 (:53, :61); zero tests. Must-cover §9 hard fail |
| `GetMakerOrderMessages` handler | **NO — FILE MISSING** | — | Customer twin has 2 tests; zero on maker side |
| `Post*OrderMessage.Validator` body rules (§5) | **NO** | — | New codes `OrderMessageBodyEmpty` / `OrderMessageBodyTooLong` (new in `BusinessErrorMessage.cs` this branch): **0 references in any test file**. No `TestValidate` coverage for any OrderMessages validator. Must-cover §5 hard fail: "Adding a new `RuleFor` clause without a new test = hard fail" |
| `CancelExpiredOrder` handler | YES | 5 tests (happy AutoExpiry, already-cancelled, paid-mid-flight, not-found, payload) | |
| Cancel-expired DB-level flow | YES | `CancelExpiredPendingPaymentOrdersIntegrationTests` (1) | |
| 5-min debounce end-to-end (2 posts in window → exactly 1 outbox row) | **NO** | — | Headline locked decision of the bundle; only unit-level coverage exists. No "debounce" hit anywhere in `Makables.IntegrationTests` |
| Unread denormalization end-to-end (post increments counterparty; mark-read → 0) | **NO** | — | Only DB-adjacent hit is `GetMakerOrdersIntegrationTests.cs:276` `GET_orders_UnreadMessageCount_is_null_pin_until_T_0079` — a stale null-pin, untouched on this branch, whose name says it expires at T-0079 (this bundle). Customer list DTO got real values; maker list still pins null. Needs re-point or implementation decision at final review |

Gap count: **2 missing test files, 1 validator hard-fail (§5), 2 missing integration tests, 3 untested new `Failure` codes (§9), 1 stale pin test** — plus 2 minor asymmetries (maker debounce/no-session handler tests, maker pointer-clear domain test).

---

## Recommended fold (5 items)

1. **REQUIRED — create `MarkMakerOrderMessagesAsReadHandlerTests.cs`**, mirroring the customer twin: happy path resets `MakerUnreadMessageCount` + clears maker pending pointer; idempotent second call returns zero marked; cross-tenant returns `OrderNotFound`.
2. **REQUIRED — create `GetMakerOrderMessagesHandlerTests.cs`**, mirroring the customer twin: happy path forwards to query with maker scope; no-session returns Unauthorized.
3. **REQUIRED (must-cover §5) — validator tests** for `PostCustomerOrderMessage.Validator` + `PostMakerOrderMessage.Validator` via `TestValidate`, asserting `OrderMessageBodyEmpty` and `OrderMessageBodyTooLong` (new codes, currently zero test references repo-wide). Fold the §9 negative-path assertions (`OrderCustomerUserMissing`, `MakerNotFound`, `MakerUserMissing`) into the twin handler test files while there.
4. **Debounce end-to-end integration test**: two sequential posts within the 5-minute window against real Postgres → exactly one outbox event row; a third post after the window → second row.
5. **Unread denormalization integration test**: post increments counterparty's unread count at DB level; mark-read resets to 0; resolve the stale `GET_orders_UnreadMessageCount_is_null_pin_until_T_0079` pin in the same change.

Items 1–3 are blocking per `docs/process/must-cover-tests.md` (§5, §9: "New code in any category … without a test commit in the same PR is a HARD FAIL"). Items 4–5 are strongly recommended given the bundle's headline locked decision is the debounce.
