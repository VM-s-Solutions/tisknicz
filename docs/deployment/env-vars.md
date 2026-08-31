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
| `DisputeAutoEscalation:Schedule` | `0 0 9 * * *` | Daily 09:00 UTC — escalate customer-sourced disputes past the 7-day maker-response window with no maker reply (`EscalateDispute`, notification-only). T-0145. |
| `EvictExpiredRegistryCache:Schedule` | `0 30 2 * * *` | Daily 02:30 UTC — evict expired ARES registry-cache rows. Offset from the 02:00 sweep per the load-spreading convention. T-0136. |
| `DataRetentionCleanup:Schedule` | `0 0 3 * * 0` | Weekly Sunday 03:00 UTC — auth/identity data-retention cleanup. T-0140. |

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

## Operator-set: frontend (`NEXT_PUBLIC_*` — build/runtime, non-secret)

Public env vars baked into the Next.js bundle. `NEXT_PUBLIC_*` is the ONLY frontend
env prefix permitted (CLAUDE.md §Security — no secrets in the client bundle).

| Key | Notes |
|---|---|
| `NEXT_PUBLIC_SITE_URL` | The public site origin (e.g. `https://makables.cz`), no trailing slash. Drives `metadataBase` + absolute canonical/OG URLs in `sitemap.ts` / per-page `generateMetadata` (T-0131). Distinct from the API host — must be the browser-facing site origin, not `NEXT_PUBLIC_API_PUBLIC_BASE_URL`. A missing value falls back to a relative-URL build (canonical/OG degrade but the site renders). |
| `NEXT_PUBLIC_API_PUBLIC_BASE_URL` | The Public API host base URL the SSR/browser calls for catalog reads. (Pre-existing — documented here for completeness alongside the SEO addition.) |

## Web-host + Functions boot settings injected by Bicep (T-0138)

As of T-0138 the Bicep templates inject every `ValidateOnStart` app setting the
.NET hosts need to boot — so they are NOT a manual operator step on the App
Service config. The **non-secret** ones (`Jwt:Issuer`, `Comgate:MerchantId`,
`PublicAppUrls:WebBaseUrl`, the `Cors:AllowedOrigins:<audience>` array, the
Functions `*:Schedule` + `OutboxQueues:*`) are set from per-env `.bicepparam`
values or computed outputs. Blob access is injected as
`AzureBlobStorage:ConnectionString` (the blob account key) — **NOT**
`AzureBlobStorage:ServiceUri`: App Service/Functions rejects any app-setting
name ending in the reserved `__ServiceUri` suffix (error 04072). The host code
still supports both modes; the deploy just uses the connection string. The **secret**
ones (`Jwt:SigningKeyBase64`, `SendGrid:ApiKey`, `Comgate:Secret`,
`Packeta:ApiKey`, `Packeta:PublicWidgetKey`, `Mapbox:AccessToken`) flow as
`@secure()` params from **GitHub Actions secrets** at deploy time — never
committed. The full secret list + the operator setup is in
`docs/deployment/deploy-runbook.md`. (KV-reference relocation is a later
hardening step — launch-checklist.)

### Comgate deploy-time switches (non-secret, forwarded to Bicep)

Two **non-secret** Comgate values are read by the `.bicepparam` files via
`readEnvironmentVariable()` and must therefore be forwarded by the `Deploy
Bicep` step's `env:` block in *both* deploy workflows. They are GitHub
environment variables/secrets, not Key Vault entries.

| Name | App setting | Effect when unset |
|---|---|---|
| `COMGATE_WEBHOOK_ALLOWED_IPS` | `Comgate__WebhookAllowedIps__N` (comma-separated input, expanded to indexed keys) | Setting omitted → allowlist empty → **every payment callback is rejected with 401**. Fail-closed and silent; nothing else surfaces it. |
| `COMGATE_BASE_URL` | `Comgate__BaseUrl` | Setting omitted → the code default applies, which is the **live** gateway. Must be an absolute `https` URI; an empty value would fail `ValidateOnStart` and the host would not boot, which is why the setting is omitted rather than emitted empty. |

Neither value is hardcoded in the repo: Comgate's published source ranges are
operator-supplied, and a guessed range silently breaks the only route an order
has to `Paid`. `scripts/check-consistency.mjs` rule **T10** asserts that every
`readEnvironmentVariable()` name in `infra/bicep/envs/*.bicepparam` is actually
forwarded by both workflows, so a parameter can no longer be dead on arrival.

## Maintenance rule

Any PR that adds a `%key%` binding expression, a `ValidateOnStart` options class, a new
configuration-bound schedule, or a `NEXT_PUBLIC_*` frontend var MUST add the key here in
the same PR (Gate 7 docs parity) **and** inject it in the Bicep (`app-service.bicep` /
`functions.bicep`) so the deployed host can boot — see T-0138.
