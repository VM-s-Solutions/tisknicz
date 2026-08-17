---
id: 0020
title: Background jobs — Azure Functions on Docker; timer + queue triggers; outbox as the message hub
status: accepted
date: 2026-05-21
deciders: [Architect]
---

# 0020 — Background jobs

## Context

Six classes of work happen off the request path:
1. **Outbox processing** — every side effect deferred by webhook handlers / commands.
2. **Scheduled domain transitions** — auto-deliver orders after 7 days.
3. **External state polling** — Packeta shipment status, ARES cache eviction.
4. **Payout batch generation** — weekly admin-triggerable but also auto-run.
5. **Retry sweeps** — outbox + per-table retry sweeps for failed integrations.
6. **GDPR / cleanup** — purging expired audit data, anonymizing user data on request.

We need a runtime that's reliable, observable, and operationally similar to Cleansia (Azure Functions on Docker).

## Decision

### Hosting: Azure Functions v4 on Docker, in `Makables.Functions`

One project, one Dockerfile, deployed as a Linux container to Azure Functions Premium plan (Cleansia parity). Premium plan because:
- Always-on (no cold start delays affecting auto-deliver punctuality).
- Predictable cost.
- VNet integration available if we later restrict Postgres to private network.

The project references `Makables.Config`, `Makables.Core.AppServices`, `Makables.Infra.*`. Functions are thin wrappers that dispatch to MediatR commands — same pattern as controllers:

```csharp
public class GenerateInvoiceFunction(ISender mediator, ILogger<GenerateInvoiceFunction> logger)
{
    [Function(nameof(GenerateInvoiceFunction))]
    public async Task Run(
        [QueueTrigger("generate-invoice")] GenerateInvoiceMessage message,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GenerateInvoice.Command(message.OrderId), ct);
        if (!result.IsSuccess)
        {
            logger.LogError("Generate invoice failed: {Code}", result.Error?.Code);
            throw new InvalidOperationException($"Failed: {result.Error?.Code}");   // triggers Azure retry
        }
    }
}
```

### Trigger types

| Trigger | Pattern | Use cases |
|---|---|---|
| `[TimerTrigger]` | CRON expression | Auto-deliver, shipment status sync, retry sweep, GDPR cleanup |
| `[QueueTrigger("<queue>")]` | Azure Storage Queue | Per-event work: generate invoice, generate label, send email (via outbox processor) |
| (`[HttpTrigger]`) | HTTP, secret-protected | Admin-triggered actions like "force run payout batch now" |

We deliberately do **not** use Service Bus or Event Grid at MVP. Storage Queues are sufficient and trivial to operate.

### The outbox is the message hub

ADR 0016 established the `outbox_event` table. The `ProcessOutboxFunction` is the most-important Function in the system. It runs on **two triggers**:

1. **Timer** (every 30 seconds): sweep `outbox_event` for rows where `processed_at IS NULL AND next_retry_at <= now()`. Process up to N rows per invocation (N = 50 at launch).
2. **HTTP** (admin-triggered, secret-protected): "process now" for ops investigations.

For each event, the function routes by `event_type` to a Mediator command:

| `event_type` | Mediator command |
|---|---|
| `email.send` | `SendEmail.Command(toAddress, templateCode, locale, data)` |
| `invoice.generate` | `GenerateInvoice.Command(orderId)` |
| `label.generate` | `GenerateLabel.Command(orderId)` |
| `notification.maker` | `NotifyMaker.Command(makerId, kind, data)` |
| `notification.admin` | `NotifyAdmin.Command(kind, data)` |

On success: `UPDATE outbox_event SET processed_at = now() WHERE id = ?`.
On failure: increment `retry_count`, set `next_retry_at` per the schedule in patterns §A.14, store `last_error_type` / `last_error_code`.
On `last_error_type = Permanent | Configuration`: stop retrying; admin sees stalled outbox in the admin dashboard.

### Specific Functions at launch

| Name | Trigger | Schedule / Queue | Purpose |
|---|---|---|---|
| `ProcessOutbox` | Timer + HTTP | every 30s | Drain outbox |
| `AutoDeliverOrders` | Timer | daily 08:00 UTC | Move `Shipped` orders to `Delivered` after 7 days |
| `SyncShipmentStatuses` | Timer | every 6h | Pull Packeta status for in-flight shipments |
| `EvictExpiredRegistryCache` | Timer | daily 02:00 UTC | Clean `company_registry_cache` |
| `RunWeeklyPayoutBatch` | Timer + HTTP | Monday 02:00 UTC + on-demand | Generate payout batches |
| `GenerateInvoice` | Queue | `generate-invoice` queue | Render PDF + write to blob + outbox-enqueue customer email with attachment |
| `GenerateLabel` | Queue | `generate-label` queue | Fetch label from Packeta + write to blob (proactively, before maker opens dashboard) |
| `SendEmail` | Queue | `send-email` queue | Render template + submit to Resend (called by ProcessOutbox for `email.send` events) |
| `DataRetentionCleanup` | Timer | weekly Sunday 03:00 UTC | Purge expired auth artifacts — refresh tokens (IP + user-agent), one-time tokens (IP), login-attempt buckets (keyed by email, incl. addresses that never registered). T-0114. Order / invoice / payout data has statutory retention and is out of scope; a subject's erasure request goes through `DeleteUserPermanently` (T-0110), not this job. |

### Why a hybrid (outbox + queues)

The outbox table is the **system of record** for "something needs to happen". The queues are the **transport** for fan-out and parallelism. `ProcessOutbox` reads the outbox and enqueues to the right queue; specialized Functions consume the queues. This means:
- Even if a queue is lost or misconfigured, the outbox still has the work.
- Adding a new event type doesn't require touching multiple Functions — just route in `ProcessOutbox`.

For "fast path" events that don't need rendering work (e.g. `notification.admin`), `ProcessOutbox` can execute the Mediator command directly without enqueueing.

### Concurrency and idempotency

- Functions run with `maxConcurrentCalls = 1` for `ProcessOutbox` to keep the sweep simple. Per-queue Functions (`GenerateInvoice`, `SendEmail`) can scale.
- Every command invoked from a Function is idempotent — same outbox row → same effect, regardless of how many times the trigger fires.
- `outbox_event` row is "claimed" via `UPDATE ... WHERE processed_at IS NULL AND id = ? RETURNING ...` so two concurrent sweeps can't both process the same row.

### Local development

Functions run locally via `func start` (Azure Functions Core Tools). Postgres comes from `docker compose`. Outbox triggers can be exercised by inserting rows manually or by triggering an HTTP endpoint that simulates a webhook.

### Observability

- Every Function logs through `ILogger<T>` with structured properties: `function_name`, `outbox_event_id`, `aggregate_id`, `event_type`.
- Failures emit `traceparent`-correlated logs so the full path of a failed email send (controller → outbox row → ProcessOutbox → SendEmail → Resend) is reconstructible in App Insights.
- Custom metrics: `outbox_lag_seconds` (now - oldest unprocessed row), `outbox_stalled_count` (rows with `Permanent`/`Configuration` errors).

### Admin surface

`/dashboard/admin/operations` shows:
- Outbox lag and stalled count
- Each Function's recent runs (last 24h success/failure)
- "Process outbox now" button (HTTP trigger)
- Stalled events with a "force retry" admin action (audited per ADR 0014)

## Alternatives considered

- **Hangfire inside an ASP.NET host** — viable; less operational separation. Rejected to keep "Web hosts handle requests, Functions handle off-request work" boundary clean. Also no Cleansia precedent.
- **Service Bus instead of Storage Queue** — overkill for MVP. Service Bus's FIFO, sessions, dead-letter, and pub-sub features aren't needed yet. Migrate if MVP outgrows Storage Queue (~100k messages/day).
- **No outbox, write directly to queues from handlers** — rejected. Queues aren't transactional with Postgres; we'd lose events on a Postgres-commit-but-queue-publish-failure window. Outbox-in-same-DB is the proven pattern.
- **CRON-only (no queues)** — rejected. CRON sweep latency makes user-facing waits noticeable (an order paid would take up to 30s to show an invoice). Queues give us sub-second fan-out.
- **Use Azure Functions Consumption plan** — rejected. Cold starts hurt punctuality for `AutoDeliverOrders` and `SyncShipmentStatuses`. Premium plan is the Cleansia call.

## Consequences

### Positive
- One place to look at "what's happening off the request path": the outbox table.
- Failure isolation: a Resend outage doesn't stop label generation; a Packeta outage doesn't stop invoice rendering.
- Adding a new background job = one Function class + one outbox `event_type` route.
- Observability is straightforward: every event has an outbox row with its full payload + error history.

### Negative
- Two queues plus an outbox feels heavy for a small launch. Justified by long-term: removing it later costs much more than carrying it from day one.
- Premium plan has a baseline cost (~$50–100/month) even at zero traffic. Acceptable for the punctuality + warmth.

## Compliance / verification

- Reviewer: Functions are thin wrappers; Mediator does the work. No business logic in `Makables.Functions/*.cs`.
- Reviewer: outbox-routing code in `ProcessOutbox` is the single switch; new event types must be added there.
- Reviewer: any new outbox `event_type` has a documented retry classification.
- SecOps: Function HTTP triggers require an `x-functions-key` or a custom auth header.
- Integration test: a failing SendEmail leaves `retry_count` incremented and `next_retry_at` set per schedule.
- Integration test: an outbox event with `last_error_type = Permanent` is not retried again.

## Related

- Patterns: §A.5 pipeline behaviors, §A.14 retry classification, §A.20 idempotent webhooks
- Roles: many — `docs/architecture/roles/outbox.md` (to be authored as a system role)
- ADR 0016 (payment webhook → outbox)
- ADR 0017 (shipment status sync, label generation)
- ADR 0019 (email send via outbox)
