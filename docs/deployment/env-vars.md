# Deployment env-vars — Functions host app settings

> Created as the order-cleanup-bundle review LOW-5 follow-up: the timer schedule keys
> (`AutoDeliverOrders:Schedule`, `CancelExpiredPendingPaymentOrders:Schedule`) existed only in
> `backend/src/Makables.Functions/local.settings.json` with no committed deployment list. This file is
> now the canonical list of app settings the operator must configure on the **Functions** app per
> environment. Per-audience Web hosts get their settings from `infra/bicep/modules/app-service.bicep`
> + Key Vault references (ADR 0023 §Secrets); they are not duplicated here.

Dev-parity values for everything below live in `backend/src/Makables.Functions/local.settings.json`.

## Injected by Bicep (`infra/bicep/modules/functions.bicep`) — no operator action

| Key | Source |
|---|---|
| `AzureWebJobsStorage` | Dedicated storage account connection string (TODO(T-0134): identity-based) |
| `FUNCTIONS_EXTENSION_VERSION` | `~4` |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | App Insights module output |
| `ConnectionStrings__Postgres` | Postgres module output (TODO(T-0134): Key Vault reference) |

## Operator-set: timer schedules (NCRONTAB, 6-field)

These are `%key%` binding expressions on `[TimerTrigger]` — there is **no in-code fallback**; a missing
key fails function indexing at host startup. The defaults below are the canonical per-ticket values.

| Key | Default | Purpose |
|---|---|---|
| `ProcessOutbox:Schedule` | `*/30 * * * * *` | Outbox drain sweep (every 30 s). T-0029. |
| `AutoDeliverOrders:Schedule` | `0 0 8 * * *` | Daily 08:00 UTC — `Shipped` → `Delivered` after 7 days. T-0077. |
| `SyncShipmentStatuses:Schedule` | `0 0 0,6,12,18 * * *` | Every 6 h — Packeta status pull for in-flight shipments. T-0078. |
| `CancelExpiredPendingPaymentOrders:Schedule` | `0 0 2 * * *` | Daily 02:00 UTC — cancel `PendingPayment` orders older than 24 h (`CancellationSource = AutoExpiry`). T-0083. |
| `RunWeeklyPayoutBatch:Schedule` | `0 0 2 * * 1` | Monday 02:00 UTC — weekly maker payout batch (`CreatePayoutBatch`). Also reachable via `POST /api/payouts/run-batch` (function key). T-0104. |

## Operator-set: outbox queues + dispatcher

| Key | Notes |
|---|---|
| `OutboxQueues:ConnectionString` | Storage account used for the outbox handoff queues. |
| `OutboxQueues:SendEmailQueueName` | `%...%` binding expression on `SendEmailFunction` — required. |
| `OutboxQueues:GenerateInvoiceQueueName` | `%...%` binding expression on `GenerateInvoiceFunction` — required. |
| `OutboxQueues:GenerateLabelQueueName` | `%...%` binding expression on `GenerateLabelFunction` — required. |
| `OutboxDispatcher:HandoffParkMinutes` | Park window for queue handoff (dev default `1`). |

## Operator-set: shared options (`ValidateOnStart` — host refuses to boot when missing)

Secrets come from Key Vault references (`@Microsoft.KeyVault(SecretUri=...)`), never plain app settings.

| Key | Secret? |
|---|---|
| `Jwt:Issuer` | no |
| `Jwt:SigningKeyBase64` | **yes** |
| `SendGrid:ApiKey` | **yes** |
| `SendGrid:DefaultFromAddress` | no |
| `PublicAppUrls:WebBaseUrl` | no |

## Operator-set: notification recipients (soft — NOT `ValidateOnStart`)

| Key | Notes |
|---|---|
| `ADMIN_NOTIFICATION_EMAIL` | Recipient of `order.disputed.adminEmail` (T-0106). Raw env-var override of `Email:AdminNotificationAddress` (`EmailOptions`); set per environment. Consumed at email **send** time, not at boot — a missing value does not stop the host; the outbox row parks `Configuration`-class (visible in admin outbox tooling, retried after the setting is fixed). |

## Maintenance rule

Any PR that adds a `%key%` binding expression, a `ValidateOnStart` options class, or a new
configuration-bound schedule MUST add the key here in the same PR (Gate 7 docs parity).
