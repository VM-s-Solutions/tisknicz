# Gate 3 (Security) — T-0127 admin-read-gaps

**Branch:** `feat/admin-read-gaps` (8 commits) · **Scope:** 4 admin Unscoped reads + delete-user proactive pre-disable
**Verdict: GATE3_PASS**

---

## 1. The 4 Unscoped reads — audience is the shield

| Read | Controller | `[Authorize]` | Host audience |
|---|---|---|---|
| GetCountryConfiguration `GET /country-configurations/{cc}` | CountryConfigurationsController | yes (class) | Web.Admin |
| GetAdminOrderDetail `GET /admin-orders/{id}` | AdminQueriesController | yes (class) | Web.Admin |
| GetStalledOutboxEvents `GET /outbox-events/stalled` | OutboxEventsController | yes (class) | Web.Admin |
| GetPayoutBatches `GET /payout-batches` | PayoutBatchesController | yes (class) | Web.Admin |

All four are on the admin host behind `[Authorize]`; ADR 0013 enforces the admin JWT audience per host. No new unauthed path; no new auth surface — the FE pages sit under the existing `(admin)` gated subtree.

**Cross-audience 401 proof (genuine, not bare-unauth):** `AdminReadGapsIntegrationTests.Customer_JWT_is_rejected_on_the_new_admin_reads` issues a **real signed token** via the production `JwtIssuer` (same `SigningKeyBase64`), role `Customer`, audience `MakablesAudiences.Customer`, then replays it against the admin host. All four reads return `401 Unauthorized`. This is the true cross-audience replay (valid signature, wrong `aud`), not an anonymous probe. The admin happy-path tests use a separately-issued `MakablesAudiences.Admin` token, confirming the gate discriminates on audience, not merely on token presence.

**Order-detail PII parity:** `AdminOrderDetailDto` carries `CustomerEmail` + contact snapshot (`ContactName`, `ContactPhone`, `CustomerNotes`). The admin list (`AdminOrderListItemDto`) already exposes `CustomerEmail`. The detail adds name/phone/notes — this is the intended operator surface (Q-0024 option a, explicitly "admin is privileged, NO GDPR redaction") and is bounded to the order header; no line items / message thread / payment secrets. Privileged-but-intentional. OK.

## 2. Delete-user pre-disable cannot weaken the backend

- The probe `userHasInFlightOrders(customerUserId)` is **read-only** — it calls `getAdminOrders` (a paged GET, `pageSize:1`) across the in-flight states. No mutation.
- The pre-disable predicate is **purely presentational**: `canSubmit = emailMatches && reasonValid && !preBlocked` with the in-code note "the server re-checks every gate (T-0110)". A `'unknown'` probe (transient failure) does **not** pre-disable — it defers to the backend.
- `eraseUser` sends only `{ confirmedEmail, reason }` to `POST /users/{id}/erase` — **no "skip-checks" / bypass signal**.
- The **backend gate stays authoritative**: `DeleteUserPermanently.Handler` independently re-runs (3) the retype gate (`UserDeleteConfirmationMismatch`) and (4) the in-flight interlock (`HasInFlightOrderForUserAsync` over `[PendingPayment, Paid, Accepted, Shipped, Disputed]` → `UserCannotDeleteWithInFlightOrders`), and the deletion seam re-guards in-flight defensively. A client that bypasses the pre-disable still hits this gate. OK.

## 3. No new auth surface
The 4 reads extend existing admin-host controllers under the existing audience gate. No new unauthed route. FE pages under `(admin)` gated subtree. OK.

## 4. PII in the new reads
- **Stalled-outbox:** `StalledOutboxEventDto = (Id, EventType, AggregateId, LastErrorCode, RetryCount, CreatedAt)`. Projection selects exactly these — **`PayloadJson` is NOT projected** (the entity column exists, seeded `"{}"`, but never reaches the DTO). No payload internals / PII leak. **PASS.**
- **Payout-batch list:** `AdminPayoutBatchListItemDto` projection selects `Id, BatchNumber, State, TotalAmountMinor, Currency, OrderCount, MakerCount, CreatedAt, CompletedAt` — **NO `BankReference`, NO bank account, NO `CsvBlobPath`**. Bank-account data stays CSV-only (controller-direct stream, `private, no-store`). **PASS.**
- **Order-detail:** privileged email/contact — see §1, intentional.

## 5. Country-config GET
`GetCountryConfigurationResponse` returns VAT rates (bp), platform-fee bp, shipping price (minor), invoicing mode, and provider **codes** (`comgate`, `packeta`, `ares`, `resend`) — these are selection keys, **not secrets**. The Comgate secret / API keys live in Configuration and never enter this DTO. Admin-gated. OK.

## 6. Reason/notes/filters — injection
Read-only. `customerUserId`/`makerId`/`country` filters bind via parameterized EF `.Where(==)`; `customerEmail` uses parameterized `EF.Functions.ILike` (bound parameter, not concatenated); `state` binds to the `OrderState` enum (type-safe). No injection surface. OK.

---

## Folds
None. No BLOCK, no fold required.

**GATE3_PASS** — all six checks clear; cross-audience replay pinned with real signed non-admin tokens; stalled DTO carries no payload; payout list carries no bank account; backend erase gate remains authoritative behind the proactive UX pre-disable.
