---
role: Outbox
kind: domain-service
status: accepted
---

# Outbox

## Responsibility

Be the system-of-record for "something needs to happen off the request path." Events written to the outbox are guaranteed to be processed at-least-once, with retry classification and stalled-event surfacing.

## Collaborators

- **Order**, **Invoice**, **PayoutBatch**, **User**, etc. (any aggregate can produce outbox events)
- **`ProcessOutboxFunction`** (reads + writes: claims rows, marks `processed_at`)
- **EmailProvider**, **InvoiceService**, **ShippingCarrier**, etc. (the side-effect targets that the function dispatches to)

## Knows

- The event types it routes (see ADR 0020 table)
- The retry schedule (per ADR §A.14 + ADR 0014)
- Sensitive-payload redaction at write time

## Does NOT know

- Why the event was raised (the producing handler decides)
- How a specific event is processed (`ProcessOutboxFunction` routes to commands)

## Operations

```csharp
// Producers (inside handlers, before commit):
void Enqueue(string eventType, string aggregateId, object payload)

// Consumer (Function):
Task<int> DrainAsync(int maxRows, CancellationToken ct)
Task RetryStalledAsync(CancellationToken ct)
```

## Invariants

- Outbox writes happen inside the producing handler's transaction. They commit or roll back atomically.
- An unprocessed event has `processed_at IS NULL`; processing claims it via `UPDATE ... WHERE processed_at IS NULL RETURNING ...` to prevent double-processing.
- A successfully-processed event has `processed_at IS NOT NULL`.
- An event with `last_error_type = Permanent | Configuration` is never retried; admin must intervene.
- `retry_count` is monotonically non-decreasing.

## Read surfaces

- **Maker-scoped read (T-0112, US-maker-0017):** `IPayoutQueries.GetMakerOutboxEventsForOrderAsync(makerId, orderId, page, pageSize, ct)` — a maker reads the outbox events of their OWN order for a delivery-status audit trail. Projection-only: event **type** + derived `OutboxDeliveryStatus` + **timestamp**; NO payload internals are exposed (the producing handler's snapshot stays internal). **No maker retry** — retry is admin-only (AC-2); this is a read-only window, not a control surface. IDOR shield in the projection: a cross-maker / unknown order id returns an EMPTY page (not an oracle, not a 403). Maker resolved from session → `IMakerRepository.GetByUserIdAsync`, NEVER from a request param. Feature: `backend/src/Makables.Core.AppServices/Features/Payouts/GetMakerOutboxEventsForOrder.cs`; query impl `backend/src/Makables.Infra.Database/Payouts/PayoutQueries.cs:149`; DTO `MakerOutboxEventDto`.

## Admin control surface (T-0109 / US-admin-0014)

Two admin-only mutations on a STALLED event (`Web.Admin`, audited via `IAdminAuditableCommand`). Retry is **admin-only** — no maker/customer retry exists (the maker read window in "Read surfaces" is read-only).

- **`RequeueForRetry(now)` — one-shot, ladder-preserving force-retry (AC-1).** Sets `NextRetryAt = now` so the next `ProcessOutbox` sweep re-picks the row, and increments `RetryCount` so the attempt is counted. It deliberately does **NOT** reset the backoff ladder: on the next failure, `RecordFailure` + `OutboxRetryPolicy.NextAttempt` continue from the bumped `RetryCount` (re-entering the ladder at the current rung, or stalling immediately if `MaxTransientAttempts` is already exhausted). Per locked decision A.1. It does NOT touch `LastErrorKind` / `LastErrorCode` — the stall's diagnostic stays visible until the next attempt overwrites it (via `RecordFailure`) or clears it (via `MarkProcessed`). Refuses an already-processed row (the handler pre-guards with a clean `outbox.alreadyProcessed`; the throw is the belt-and-braces backstop). `checked(...)` overflow guard mirrors `RecordFailure`.
- **`Acknowledge(adminUserId, now)` — admin marks a stalled event as resolved (won't be retried).** Sets `AcknowledgedAt` / `AcknowledgedBy` and closes the row (`ProcessedAt = now`, `NextRetryAt = null`) so it leaves the unprocessed seek set. Requires a non-empty admin user id.

`AcknowledgedAt` / `AcknowledgedBy` are the audit columns backing this surface. Distinguish from `ParkPendingConsumer` (in-flight handoff, no `RetryCount` bump, refuses to park a stalled row).

## Implementation pointer

Table: `outbox_event` (defined in ADR 0016). Function: `backend/src/Makables.Functions/ProcessOutboxFunction.cs`. Producer helper: `backend/src/Makables.Core.AppServices/Outbox/IOutbox.cs` + `OutboxRepository`. Entity transforms (`RequeueForRetry` / `Acknowledge`): `backend/src/Makables.Core.Domain/Outbox/OutboxEvent.cs`.

## Related

- ADRs: 0014 (retry classification), 0016 (origin), 0019 (emails via outbox), 0020 (Functions pipeline)
- Stories: US-admin-0014 (admin retry/acknowledge), US-maker-0017 (maker delivery-status read window)
- Roles: `email-provider`, `invoice`, `shipping-carrier` (the consumers), `admin-audit-log-entry`
