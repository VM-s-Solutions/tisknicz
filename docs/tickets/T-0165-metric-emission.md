---
id: T-0165
title: Emit the ADR 0023 §4 custom metrics (closes Q-0033)
status: done
size: M
owner: dotnet-backend
created: 2026-08-18
updated: 2026-08-18
depends_on: [T-0014, T-0102a]
blocks: []
user_stories: []
adrs: [0016, 0020, 0023]
phase: 6
manual_steps: [deploy-trigger]
security_touching: false
layers: [dotnet-backend, secops]
---

# T-0165 — Emit the ADR 0023 §4 custom metrics

## Context

T-0014 registered the meter **names** (`MakablesMeters`) and T-0102a added the
first real instruments on `Makables.Payouts`. The other four meters — Outbox,
Payments, Webhooks, Orders — had names and nothing recording onto them, so
every ADR 0023 §4 alert rule built on them would have read empty. That gap was
Q-0033, sitting `open` on the launch-blocking index since 2026-06-21 as a
*decision* for the user: wire the emission, or accept the documented DB/log
alternatives for MVP.

The decision only mattered while the work was hypothetical. The instrumentation
turned out to be small — the `IPayoutMetrics` seam already showed the shape —
so this closes the question by building it rather than by writing down which
alerts do not work.

## Scope

Four interfaces in `Core.Domain/Observability/` (keeping `Core.AppServices`
free of `System.Diagnostics.Metrics`, exactly as `IPayoutMetrics` does), four
singleton implementations in `Config/Observability/`, five call sites:

| Instrument | Recorded by | Tags |
|---|---|---|
| `makables.outbox.lag_seconds` | `OutboxDispatcher`, **every** sweep | — |
| `makables.outbox.stalled` | `OutboxDispatcher`, every sweep | — |
| `makables.outbox.dispatched` | `OutboxDispatcher` | `outcome` = routed / stalled / publish_failed |
| `makables.payments.sessions_created` | `CreatePaymentSession` | `provider`, `outcome` = created / transient / permanent |
| `makables.webhooks.received` | `ComgateWebhookController`, **every** exit path | `provider`, `outcome` = accepted / duplicate / rejected / malformed / error |
| `makables.orders.auto_delivered` | `AutoDeliverOrdersFunction` | — |
| `makables.orders.auto_cancelled` | `CancelExpiredPendingPaymentOrdersFunction` | — |

## Three decisions worth the words

- **Zero is recorded, not skipped.** An empty outbox sweep records
  `lag_seconds = 0`; a quiet night records `auto_delivered = 0`. A series that
  only writes when there is work is indistinguishable from a job that stopped
  firing — which is the outage the instrument exists to catch. Absence now
  means "the Function is not running", which is a real, alertable statement.
- **Every webhook exit path is counted.** The controller returns `200` for
  unknown-ref and for idempotent re-delivery by design (a 4xx would invite a
  retry storm), so HTTP status alone cannot tell an operator what happened.
  Routing every `return` through a `Counted(...)` local function makes an
  under-counted path a compile-time-visible omission rather than a silent one.
- **Telemetry may not break dispatch.** The stalled gauge costs one indexed
  `COUNT` per 30-second sweep; if that query fails, it is logged and swallowed
  so a telemetry hiccup cannot turn a working dispatch into a failed tick.
  Cancellation still propagates — swallowing that would turn host shutdown into
  a silently half-finished sweep.

## Out of scope

- **Creating the Azure Monitor alert rules.** Still an ops task
  (`docs/runbooks/monitoring.md` §A) and still absent from the Bicep — but it
  is now a task with signal behind it rather than a decision.
- **Tag cardinality beyond the outcome split.** Payment failures fold
  Configuration/Unknown into `permanent`: only the retry-worthy split is
  actionable, and the exact error code is already in the logs.
- Dashboards, and metrics for surfaces with no alert in ADR 0023 §4.

## Acceptance criteria

- **AC-1** Given an outbox sweep that finds nothing, when it completes, then
  `lag_seconds` (0) and the stalled gauge are still recorded.
- **AC-2** Given a sweep with work, when it completes, then `lag_seconds` is
  the age of the oldest event in the batch and each outcome is counted under
  routed / stalled / publish_failed.
- **AC-3** Given the stalled-count query throws, when the sweep runs, then the
  sweep still succeeds and still publishes; a cancellation still propagates.
- **AC-4** Given a payment session attempt, when the provider succeeds or
  fails, then exactly one `sessions_created` is recorded with the right
  outcome; a cached session records nothing (no provider call happened).
- **AC-5** Given a webhook delivery, when it is accepted / re-delivered /
  refused, then exactly one `received` is recorded per delivery with outcome
  accepted / duplicate / rejected respectively.
- **AC-6** Given a timer Function run with zero eligible orders, when it
  completes, then the counter is still recorded.

## Test plan

- `OutboxDispatcherMetricsTests` — 8 cases (AC-1–AC-3, incl. the swallow and
  the cancellation carve-out).
- `CreatePaymentSessionHandlerTests` — 6 new cases (AC-4, incl. the
  Permanent/Configuration/Unknown fold).
- `ComgateWebhookTests` — 3 new cases against real Postgres with a recording
  `IWebhookMetrics` (AC-5), asserting exactly-one-record-per-delivery.

## Status log

- 2026-08-18 `draft → done` by dotnet-backend. Evidence: `Makables.Tests`
  2033/2033 (2019 before), `Makables.IntegrationTests` 311/311 (308 before)
  against Postgres 16. Q-0033 closed; the launch-checklist line flipped from a
  pre-launch decision to a done item with the Azure-Monitor rule creation
  called out as the remaining ops task.
