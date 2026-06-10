# Gate 3 (Security) — order-cleanup bundle (T-0079 + T-0083)

**Branch:** `feat/order-cleanup-bundle` (5 commits, `18f8401..ea3271f`)
**Reviewer:** Security & DevOps agent
**Date:** 2026-06-10

## Verdict: GATE3_FOLD_RECOMMENDED

One HIGH finding (check 7 — observability of the pay-after-auto-cancel race), foldable in-bundle with a
small change to `ComgateWebhookController`. One out-of-bundle Q-item (check 13 — rate limiting). All
IDOR / PII / enumeration checks pass.

---

## Check 1 — [Authorize] + JWT audience per host: PASS

- Controller-level `[Authorize]` on both controllers:
  - `backend/src/Makables.Web.Customer/Controllers/OrderMessagesController.cs:28`
  - `backend/src/Makables.Web.Maker/Controllers/OrderMessagesController.cs:19`
- Audience isolation enforced: `backend/src/Makables.Config/Extensions/AddMakablesAuth.cs:103-104`
  (`ValidateAudience = true`, `ValidAudiences = acceptedAudiences`) with the policy map at lines
  125–132: Customer host accepts `[Customer, Admin]`, Maker host `[Maker, Admin]`. A customer JWT
  replayed against the maker API fails audience validation → 401.

## Check 2 — IDOR shield, two layers: PASS

**Compile-time:** 6 separate per-audience features. All three maker features resolve makerId via
`IMakerRepository.GetByUserIdAsync` — never raw session id as makerId:
- `GetMakerOrderMessages.cs:60`, `PostMakerOrderMessage.cs:78`, `MarkMakerOrderMessagesAsRead.cs:49`

**Runtime (WHERE baked into SQL):**
- `GetByOrderForCustomerAsync`: `backend/src/Makables.Infra.Database/OrderMessages/OrderMessageQueries.cs:54-56`
  — `m.OrderId == orderId && EXISTS(Order WHERE Id == orderId && CustomerUserId == customerUserId)`.
- `GetByOrderForMakerAsync`: `OrderMessageQueries.cs:108-110` — same shape with `MakerId == makerId`.
- `MarkAsReadForCustomerAsync` / `MarkAsReadForMakerAsync` bulk `ExecuteUpdateAsync` carries the same
  ownership EXISTS subquery: `backend/src/Makables.Infra.Database/OrderMessages/OrderMessageRepository.cs:48-56`
  and `:71-79`. Never unscoped on orderId alone.
- Defence-in-depth: post/mark-read handlers additionally pre-load the order via the scoped
  `GetByIdForCustomerAsync` / `GetByIdForMakerAsync` (`backend/src/Makables.Infra.Database/Orders/OrderRepository.cs:49-75`,
  predicate `o.Id == orderId && o.CustomerUserId == ...` / `o.MakerId == ...`).

## Check 3 — Cross-tenant 404 / no enumeration oracle: PASS

- Post + MarkRead return generic `BusinessErrorMessage.OrderNotFound` for both "no such order" and
  "wrong owner": `PostCustomerOrderMessage.cs:97-101`, `PostMakerOrderMessage.cs:90-94`,
  `MarkCustomerOrderMessagesAsRead.cs:67-71`, `MarkMakerOrderMessagesAsRead.cs:58-62`.
- Maker-audience JWT without a maker row also surfaces `OrderNotFound` (`PostMakerOrderMessage.cs:79-85`)
  or an empty page (`GetMakerOrderMessages.cs:61-66`) — leaks nothing.
- List endpoints return an empty page (not 404) for cross-tenant probes per the documented T-0080
  list-empty contract — identical response for "nonexistent" and "other tenant's" order, so no oracle.

## Check 4 — PII logging hygiene: PASS

Grep across all new T-0079/T-0083 files for `LogInformation|LogDebug|LogTrace|LogWarning` referencing
`Body` / `messageBody`: zero hits. New handlers log only ids and state names. (Pre-existing hits in
`Infra.Clients` are HTTP response bodies, unrelated to message content and not in this diff.)

## Check 5 — Email enumeration (message payloads): PASS

- Customer payload (`PostMakerOrderMessage.cs:123-131`): `Email = order.ContactEmail` (customer's own
  snapshot), `SenderName = maker.CompanyName` (display name). No maker email.
- Maker payload (`PostCustomerOrderMessage.cs:144-151`): `MakerEmail = makerUser.Email` (recipient's
  own), `SenderName = order.ContactName` (snapshot display name). No customer account email —
  matches the T-0081 §A.2 data-minimization lock documented on the payload record
  (`backend/src/Makables.Core.Domain/Outbox/OrderMessagePostedMakerEmailPayload.cs:9-13`).

## Check 6 — OrderCancelledCustomerEmailPayload: PASS

`CancelExpiredOrder.cs:124-131` — `Email = order.ContactEmail`, `ContactName`, no maker email field
in the record (`backend/src/Makables.Core.Domain/Outbox/OrderCancelledCustomerEmailPayload.cs:26-33`).

## Check 7 — Comgate pay-after-auto-cancel race: PASS on the state guard; HIGH finding on observability

**State guard holds.** `Order.MarkAsPaid` (`backend/src/Makables.Core.Domain/Orders/Order.cs:545-546`)
refuses any `State != PendingPayment`, so Cancelled → Paid is impossible. The webhook controller's
`IsAlreadyInTargetState` (`ComgateWebhookController.cs:234-245`) does NOT treat Cancelled as a target
state for PAID, so the command dispatches and the handler correctly returns `OrderInvalidTransition`.

**HIGH — money captured against a Cancelled order produces only a misleading Info log.**
`backend/src/Makables.Web.Public/Controllers/Webhooks/ComgateWebhookController.cs:182-188`: every
`OrderInvalidTransition` is logged as
`LogInformation("... lost the race ... (already transitioned). Idempotent 200.")` and returns 200,
which terminates Comgate retries. In the T-0083 race (customer completes payment after the 02:00 UTC
sweep cancelled the order), Comgate HAS captured the customer's money, the order stays Cancelled,
there is no refund flow until T-0105/T-0106, and the only signal is an Info-level log whose wording
("lost the race / already transitioned") actively hides the refund liability. With the platform's
zero-manual-intervention-between-weekly-checkpoints posture, this is funds held with no ops alert.

**Recommended fold (small, in this PR — T-0083 creates this race):** in the
`OrderInvalidTransition` branch, when `order.State is OrderState.Cancelled or OrderState.Refunded`,
log `Warning` (or `Error`) with explicit wording, e.g. "Comgate PAID webhook for cancelled order
{OrderId} (CancellationSource={Source}) — payment captured, manual refund required (T-0105 pending)."
Keep the 200 (idempotency contract). The ticket bar was "error/log Warning acceptable for MVP" — the
handler errors, but the surfaced log level is Information, below the bar.

## Check 8 — Function-level auth: PASS

`backend/src/Makables.Functions/Payments/CancelExpiredPendingPaymentOrdersFunction.cs:48-50` —
`[TimerTrigger("%CancelExpiredPendingPaymentOrders:Schedule%")]` only. No HttpTrigger, no HTTP surface.

## Check 9 — No new webhooks: CONFIRMED

Diff adds only the two OrderMessages controllers and the TimerTrigger Function. No new webhook
endpoints; `ComgateWebhookController` untouched by this branch.

## Check 10 — No new file upload paths: CONFIRMED

No upload code in the diff. Message body is a length-capped text field (1–2000 chars, validated in
both Post validators against `OrderMessage.MaxBodyLength`).

## Check 11 — Body not in outbox payloads: PASS (stronger than required)

Neither `OrderMessagePostedCustomerEmailPayload` nor `OrderMessagePostedMakerEmailPayload` has a Body
field. The email branches (`backend/src/Makables.Core.AppServices/Features/Email/IEmailSendService.cs:100-159`)
substitute only `sender_name` / `unread_count` / `action_url` / order identifiers — the digest email
never contains message content at all; the body is read only in-app behind the IDOR-scoped queries.

## Check 12 — MarkAsRead DoS / write-IDOR surface: PASS

Cross-tenant callers get 404 at the handler's scoped order load before the bulk update ever runs;
even a hypothetical direct repository call affects zero rows due to the EXISTS ownership predicate
(`OrderMessageRepository.cs:48-56`, `:71-79`). 200 `{MarkedCount: 0}` on zero rows is only reachable
for the caller's own order — nothing leaks.

## Check 13 — Rate limiting on PostMessage: ABSENT — Q-item recommendation (out-of-bundle)

Both hosts register `AddMakablesRateLimiting(Audience)` (`Web.Customer/Program.cs:20`,
`Web.Maker/Program.cs:19`) and the pipeline calls `UseRateLimiter()`
(`backend/src/Makables.Config/Extensions/UseMakablesPipeline.cs:27`), **but** the per-host "default"
fixed-window policy (`AddMakablesRateLimiting.cs:57-63`) is a *named* policy with no
`GlobalLimiter` and no `[EnableRateLimiting("default")]` mounted on any controller. Only
`addresses-autocomplete` and `shipping-widget-config` endpoints are actually limited. PostMessage
(2000-char bodies, authenticated) is therefore effectively unlimited → DB-bloat spam surface.
Mitigations already present: email spam is capped by the 5-min debounce (1 email / 5 min / order /
direction), and the surface requires a valid JWT. Pre-existing gap, not introduced by this bundle.

**Q-item:** mount the "default" policy globally (`RateLimiterOptions.GlobalLimiter`) or via
`[EnableRateLimiting("default")]` on `MakablesApiController`, and consider a per-user partition for
message posting (mirroring the autocomplete policy shape).

---

## Summary

| # | Check | Result |
|---|---|---|
| 1 | [Authorize] + audience per host | PASS |
| 2 | IDOR two-layer | PASS |
| 3 | Cross-tenant 404 / no oracle | PASS |
| 4 | PII logging hygiene | PASS |
| 5 | Message email payload enumeration | PASS |
| 6 | Cancelled email payload | PASS |
| 7 | Comgate pay-after-cancel race | PASS guard / **HIGH** observability — fold recommended |
| 8 | Function TimerTrigger only | PASS |
| 9 | No new webhooks | CONFIRMED |
| 10 | No new file uploads | CONFIRMED |
| 11 | Body not in outbox | PASS |
| 12 | MarkAsRead write-IDOR/DoS | PASS |
| 13 | Rate limiting on PostMessage | ABSENT — Q-item (out-of-bundle) |

**HIGH findings: 1.** **Verdict: GATE3_FOLD_RECOMMENDED.**
