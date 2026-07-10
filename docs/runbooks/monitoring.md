# Runbook — Monitoring & first response

> **Scope:** the alert signals the platform emits, the ADR 0023 §4 thresholds, what each signal
> means, the App Insights / Log Analytics query to confirm it, the likely cause, and the
> first-response action. Grounded in the T-0014 OpenTelemetry/Serilog stack
> (`AddMakablesObservability`, `MakablesMeters`) and the shipped infra
> (`infra/bicep/modules/app-insights.bicep`).
>
> **Owner:** SecOps. **Recipients:** admin email at launch (Sev 1 also future-SMS); two admins on
> rotation (ADR 0023 §4). **Telemetry sink:** `makables-prod-ai` (App Insights, workspace-backed by
> `makables-prod-law`, 30-day retention per `app-insights.bicep`).

## 0. Orientation

Every log line carries `correlation_id` (from `traceparent`), `user_id`, `country_code`,
`request_id` (ADR 0023 §4). **Start every investigation from the `correlation_id`** — it stitches the
request, handler, and outbox/webhook trace together. Secrets are redacted at the logger layer
(`SensitivePropertyMasker`). Custom metrics (the meter NAMES are registered in `MakablesMeters`,
ADR 0023 §4): `outbox_lag_seconds`, `outbox_stalled_count`, `payment_create_failures_total`,
`webhook_received_total`, `auto_deliver_count`, `payout_batch_total_minor`.

> ⚠ **EMISSION GAP — read before relying on the metric-based alerts below.** As of MVP, only the
> `makables.payouts.*` instruments actually emit values; the outbox/payment/webhook/auto-deliver
> metrics above are **registered meter names, not yet instrumented** (no code records them). The
> ADR 0023 §4 alert table is therefore the *target* state — SecOps wires the rules, but the
> outbox/payment/webhook gauges will read empty until the emission is added (logged as a follow-up).
> **The signal that works TODAY** for the highest-value alert (outbox stall) is DB-backed, not a
> metric: `GET /api/v1/outbox-events/stalled/count` on the admin host (the count the admin dashboard
> surfaces, T-0126) + the stalled-event list + the admin retry/ack UI (T-0118c). The outbox sections
> below lead with that endpoint; treat the `outbox_lag_seconds`/`outbox_stalled_count` charts as
> pending the emission follow-up.

## A. Alert table (thresholds verbatim from ADR 0023 §4)

| Signal | Threshold | Severity | Meter / source |
|---|---|---|---|
| Customer API 5xx rate | > 1% over 5 min | **Sev 2** (page-able) | ASP.NET request metrics |
| Webhook handler 5xx rate | > 5% over 5 min | **Sev 1** (page-able, immediate) | `Makables.Webhooks` |
| Outbox lag (`outbox_lag_seconds`) | > 5 min | **Sev 2** | `Makables.Outbox` |
| Outbox stalled count (`outbox_stalled_count`) | > 10 | **Sev 3** (next business day) | `Makables.Outbox` |
| Database CPU | > 80% over 10 min | **Sev 2** | Azure Monitor (Postgres) |
| Failed login rate | > 50/min from same IP | **Sev 3** (potential attack) | auth logs |
| Auto-deliver crashed | any failure | **Sev 2** | `Makables.Orders` / Function |

> These thresholds are the source of truth for the Azure Monitor alert rules. SecOps configures
> every rule above before launch (ADR 0023 §4 "Compliance"). ⚠ **confirm against the live
> environment:** the Azure Monitor *alert-rule resources* are not in the shipped Bicep
> (`infra/bicep/` defines App Insights + Log Analytics but no `Microsoft.Insights/metricAlerts`
> resources yet). Wiring the alert rules is a pre-launch operator task; this runbook defines what
> each rule must check.

---

## 1. Customer API 5xx rate — > 1% / 5 min — Sev 2

**Means:** the Customer host (`app-makables-customer-weu-prod`) is throwing unhandled errors on > 1% of
requests. This eats into the 99.5% customer-facing availability budget (ADR 0023 §3).

**Confirm (KQL, App Insights):**
```kql
requests
| where timestamp > ago(15m) and cloud_RoleName == "app-makables-customer-weu-prod"
| summarize total=count(), errors=countif(resultCode startswith "5") by bin(timestamp, 5m)
| extend errorRate = todouble(errors) / total
```
Then pivot to the failing operation and pull the exception:
```kql
exceptions
| where timestamp > ago(15m) and cloud_RoleName == "app-makables-customer-weu-prod"
| project timestamp, operation_Name, type, outerMessage, operation_Id
```

**Likely cause:** a bad deploy (check the deploy timeline — was there a slot swap?), a downstream
dependency down (Postgres, Comgate), or an uncaught edge case in a new handler. Expected failures
flow through `BusinessResult` (4xx), so a 5xx spike means something **unexpected** broke.

**First response:**
1. If it correlates with a recent deploy → **slot-swap rollback** (ADR 0023 §7, "easy rollback").
2. If Postgres-related → jump to §5 (DB CPU) and check the conn-string secret didn't just rotate.
3. Grab a `operation_Id` (= `correlation_id`) from a failing request and trace it end-to-end.

## 2. Webhook handler 5xx rate — > 5% / 5 min — Sev 1 (immediate)

**Means:** the Public host's Comgate/Packeta webhook endpoints are failing. **This is Sev 1 because
webhooks must not lose data** — Comgate retries on non-2xx, but sustained failure means payments
succeed while order state lags and customers are stuck (ADR 0023 §3).

**Confirm:**
```kql
requests
| where timestamp > ago(15m) and cloud_RoleName == "app-makables-public-weu-prod"
| where url has "/webhooks/"
| summarize total=count(), errors=countif(resultCode startswith "5") by bin(timestamp, 5m)
```
Cross-check the `webhook_received_total` counter (`Makables.Webhooks` meter, labeled by
provider + outcome) to see which provider and whether it's a verification reject vs. a 5xx.

**Likely cause:**
- IP-allowlist mismatch — Comgate changed its egress IPs and `Comgate:WebhookAllowedIps`
  (`ComgateOptions`) is stale → requests rejected. (Per `docs/security/webhook-verification.md`,
  Comgate origin IP + status re-fetch is mandatory.)
- The Comgate secret was just rotated and a host didn't restart (signature mismatch) → see
  `secret-rotation.md` §2.
- A downstream throw in the order state transition.

**First response:**
1. Confirm provider origin + IP allowlist against the provider portal. Update
   `Comgate:WebhookAllowedIps` and restart Public if Comgate's IPs changed.
2. Webhooks are **idempotent + re-fetch status** (`webhook-verification.md`; CLAUDE.md backend rule
   12) — once the host is healthy, Comgate's retry re-delivers and the order self-heals. **Do not**
   manually mutate order state to "catch up"; let the retried webhook do it.
3. If a payment is confirmed at Comgate but the order is still `PendingPayment` after recovery,
   re-fetch status manually and, if needed, use the admin order tooling — never trust the client
   redirect params alone (CLAUDE.md §Security).

## 3. Outbox lag — `outbox_lag_seconds` > 5 min — Sev 2

**Means:** the oldest unprocessed outbox row is > 5 minutes old (`outbox_lag_seconds` = now −
oldest unprocessed `created_at`). Background eventual-consistency is slipping (ADR 0023 §3 names
this exact alert).

**Confirm (works today):** check the `ProcessOutboxTimer` is firing — this is the live signal:
```kql
traces
| where timestamp > ago(30m) and message startswith "ProcessOutboxTimer tick"
| project timestamp, message
```
(The tick logs `loaded= routed= stalled= failedToPublish=` — see `ProcessOutboxFunction`.) If the
tick stopped, the host/schedule is the cause (below). ⚠ The `outbox_lag_seconds` gauge is **not
emitted yet** (see the EMISSION GAP note in §0) — chart it only once the emission follow-up ships;
until then the tick log + the stalled-count endpoint (§4) are the authoritative outbox signals.

**Likely cause:** the Functions host is down / not scaled, the `ProcessOutbox:Schedule`
(`*/30 * * * * *`) stopped firing, or the storage/queue conn string broke (no publish target).

**First response:**
1. Confirm the Functions host (`func-makables-weu-prod`) is running and `alwaysOn` is true
   (`functions.bicep` sets it).
2. **Force a drain** via the escape-hatch (T-0029): `POST /api/outbox/process` with the
   `x-functions-key` header (`ProcessOutboxFunction.RunHttp`). This runs `DispatchDueAsync`
   immediately. The response body returns the `Loaded/Routed/Stalled/FailedToPublish` summary.
3. If lag persists after a manual drain, the rows are likely **stalled** (not just late) → §4.

## 4. Outbox stalled count — `outbox_stalled_count` > 10 — Sev 3

**Means:** more than 10 rows are stuck with `Permanent`/`Configuration` errors — the exact set the
admin dashboard surfaces (T-0109/T-0126), predicate
`ProcessedAt == null && NextRetryAt == null && LastErrorKind != None`. These will **never** drain on
their own; they need operator action.

**Confirm (works today):** the authoritative live signal is `GET /api/v1/outbox-events/stalled/count`
on the admin host (the count the admin dashboard surfaces, T-0126) + the stalled-event list
(`GET /api/v1/outbox-events/stalled`) browsable in the admin outbox UI (T-0118c). ⚠ The
`outbox_stalled_count` metric is **not emitted yet** (§0 EMISSION GAP) — use the endpoint/UI, not the
chart, until the emission follow-up ships. Direct DB peek (matches the endpoint's predicate exactly):
```sql
SELECT id, event_type, last_error_kind, last_error_code, retry_count
FROM outbox_event
WHERE processed_at IS NULL AND next_retry_at IS NULL AND last_error_kind <> 'None'
ORDER BY created_at;
```

**Likely cause:** a `Configuration`-class failure (e.g. `ADMIN_NOTIFICATION_EMAIL` unset — see
`docs/deployment/env-vars.md`; the row parks `Configuration` and waits for the setting), or a
`Permanent` failure (malformed payload, a recipient SendGrid rejects).

**First response:**
1. Read `last_error_code` to classify. If it's a **missing config** (`Configuration`), fix the App
   Setting first (e.g. set `ADMIN_NOTIFICATION_EMAIL`), restart the Functions host.
2. **Admin retry UI (T-0118c / T-0109):** use the admin "retry" action — backed by
   `RetryOutboxEvent` (`outbox.retry`), which calls `OutboxEvent.RequeueForRetry` (sets
   `NextRetryAt = now`, bumps `RetryCount`, keeps the backoff ladder). Retry on an already-processed
   row returns a clean 409 (`outbox.alreadyProcessed`) — not a silent success. The action rides the
   admin audit pipeline (ADR 0014).
3. After requeue, the next `ProcessOutbox` pass (or a manual `POST /api/outbox/process`) re-publishes.
4. For a genuinely unrecoverable row, use the admin **Acknowledge** action so it leaves the stalled
   set (see `function-key-rotation.md` poison-queue note) — this is the only thing that breaks a
   re-poison loop.

## 5. Database CPU — > 80% / 10 min — Sev 2

**Means:** `pg-makables-weu-prod` (Postgres Flexible Server) is CPU-saturated. On a **Burstable** SKU
(staging `Standard_B1ms`; ⚠ confirm prod SKU — ADR 0023 §7 names `D2s_v3` General Purpose for prod,
but `main.bicep`'s default is `Standard_B2s`) this also **burns CPU credits**, after which the
server throttles hard.

**Confirm:** Azure Monitor → Postgres → CPU percent + (Burstable) credit balance. Find the
offending query:
```kql
dependencies
| where timestamp > ago(30m) and type == "postgresql"
| summarize p95=percentile(duration, 95), count() by name
| order by p95 desc
```

**Likely cause:** a missing index on a hot query (CLAUDE.md perf rule: every WHERE/ORDER BY/JOIN
column indexed), an unbounded list endpoint (every list must be paginated via `DataRangeRequest`),
or a traffic spike beyond the MVP scale assumptions (ADR 0023 §2: 50 catalog RPS).

**First response:**
1. Identify the slow query from the trace above; check it hits an index. A new perf-todo ticket if
   it's a missing index (ADR 0023 §1).
2. If it's a Burstable credit exhaustion and load is legit, **scale the SKU up** (B2s → larger / to
   General Purpose) — this is the "size up before 5,000 orders/day" lever (ADR 0023 §2, §8).
3. Check whether a secret rotation just restarted all hosts simultaneously (cold-cache stampede) —
   if so it's transient.

## 6. Failed login rate — > 50/min from same IP — Sev 3

**Means:** likely credential-stuffing / brute-force from one IP. **Confirm:** group `traces` with
`message has "login failed"` by `client_IP` per 1-min bin. **Cause:** automated attack — the platform
already has per-account lockout (`LockoutOptions`) + rate-limiting (`AddMakablesRateLimiting`).
**First response:** confirm lockout is engaging (the account locks before damage); if one IP is
hammering, block it at the ingress / WAF layer; audit whether any account actually authenticated from
that IP.

## 7. Auto-deliver crashed — any failure — Sev 2

**Means:** `AutoDeliverOrdersFunction` (daily 08:00 UTC, `AutoDeliverOrders:Schedule`) threw; orders
that should flip `Shipped → Delivered` after 7 days are stuck. **Confirm:** `exceptions` where
`operation_Name has "AutoDeliver"`; cross-check the `auto_deliver_count` gauge (a `0` on a day with
expected deliveries is the tell). **Cause:** a DB error or a throw in the delivery transition.
**First response:** trace by `operation_Id`; the job is idempotent and re-runs next day, or re-invoke
sooner; confirm no `Shipped` orders older than 7 days are wrongly stuck.

---

## Verification (manual — staging dry-run) — **manual_step**

> Flagged as the ticket's `manual_step` (SecOps, pre-launch). The first-response procedures are
> written now; firing a synthetic alert and walking the response is the launch gate. **Staging-only.**

1. **Force an outbox stall on staging:** insert / mutate an outbox row into the stalled predicate
   (`processed_at IS NULL AND next_retry_at IS NULL AND last_error_kind <> 'None'`), or trigger a
   `Configuration` failure by unsetting `ADMIN_NOTIFICATION_EMAIL` and firing an
   `order.disputed.adminEmail` path.
2. Confirm `outbox_stalled_count` rises in App Insights and the staging alert fires to the test
   recipient.
3. Walk §4 first-response: open the admin retry UI (T-0118c), retry the row, confirm it requeues
   (`RetryOutboxEvent` → `NextRetryAt = now`), then drain via `POST /api/outbox/process` with the
   staging function key and confirm `outbox_stalled_count` returns to 0.
4. **Bonus:** confirm at least one KQL query in this runbook returns rows on staging telemetry (so
   the queries aren't stale against the live schema).
5. Log the dry-run outcome. A failure blocks launch and re-opens this runbook; no production impact.
