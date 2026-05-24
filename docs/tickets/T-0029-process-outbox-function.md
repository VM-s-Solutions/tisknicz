---
id: T-0029
title: ProcessOutboxFunction + SendEmailFunction — timer + HTTP sweep, send-email queue, retry policy
status: done
size: M
owner: dotnet-backend
created: 2026-05-25
updated: 2026-05-25
depends_on: [T-0011, T-0028]
blocks: []
adrs: [0020, 0019]
phase: 2
---

# T-0029 — ProcessOutboxFunction (+ SendEmailFunction)

## Scope

Two thin Azure Functions per ADR 0020 §"The outbox is the message hub" / §"Specific Functions at launch", plus the orchestration services they wrap (kept in `Core.AppServices` so they're testable without the Functions runtime).

User picked the **hybrid queue design** (vs inline dispatch) so the architecture matches ADR 0020 verbatim — the outbox is the system of record; Storage Queues are the transport.

### Domain (`Core.Domain/Outbox/`)
- `IOutboxConsumerRepository.cs` — consumer-side reads: `LoadDueAsync(int batchSize, DateTimeOffset now, ct)` + `GetByIdAsync(string id, ct)`. Producer-side `IOutbox` unchanged.
- `IOutboxQueuePublisher.cs` — fan-out abstraction. Single method `PublishSendEmailAsync(string outboxEventId, ct)` — only the id flows through the queue; payload always stays in Postgres.
- `OutboxEvent.cs` — new `ParkPendingConsumer(DateTimeOffset parkedUntil)` method. Advances `NextRetryAt` without bumping `RetryCount` / setting error fields / marking processed. Used by `OutboxDispatcher` after publishing the row id so a concurrent sweep doesn't re-publish before the consumer confirms; the park window also bounds redelivery if the queue silently drops a message. Refuses to park an already-processed row.

### Core.AppServices
- `Common/OutboxRetryPolicy.cs` — single source of truth for the backoff curve: **1m → 5m → 15m → 1h → 6h → 24h, then stall** (6 attempts). Non-transient kinds (Permanent / Configuration / Unknown) stall immediately. Pinned by `OutboxRetryPolicyTests` (12 facts).
- `Features/Outbox/IOutboxDispatcher.cs` (+ `OutboxDispatcher`, `DispatchSummary`) — sweep orchestrator. Loads up to `BatchSize = 50` rows ordered by `created_at ASC`; routes each by `event_type`:
  - `auth.magicLink.send` / `auth.emailConfirmation.send` / `auth.passwordReset.send` → publish row id to `send-email` queue → `evt.ParkPendingConsumer(now + 15m)`.
  - Unknown event_type → `evt.RecordFailure(Permanent, EmailEventTypeUnknown, nextRetryAt: null)` (stall).
  - Queue-publish exception → `evt.RecordFailure(Transient, OutboxQueuePublishFailed, nextRetry per policy)`.
  - Single `IUnitOfWork.SaveChangesAsync` per sweep (not per row).
- `Features/Outbox/ISendEmailHandler.cs` (+ `SendEmailHandler`, `HandleOutcome`) — per-row consumer. `HandleAsync(string outboxEventId, ct)`:
  - Loads the row by id. Missing → `RowMissing` no-op.
  - Already `ProcessedAt`-set → **idempotent no-op** (Sent) — handles Storage Queue at-least-once delivery without sending duplicate emails.
  - Calls `IEmailSendService.SendAsync(eventType, payloadJson, ct)`.
  - On success → `MarkProcessed(now)` + SaveChanges.
  - On failure → classify `ErrorType` → `OutboxErrorKind`, then `RecordFailure(kind, code, OutboxRetryPolicy.NextAttempt(...))` + SaveChanges.
  - **Never throws on classified failure** — outbox owns the retry budget; throwing would double-bill with Azure-queue retry.

### Infra.Database
- `Repositories/OutboxConsumerRepository.cs` — EF impl. Hot-path query uses the existing `ix_outbox_event_due` composite index (created in T-0011's `OutboxAndAuditLog` migration). Tracked entities so callers can mutate + SaveChanges.

### Infra.Common
- `Outbox/OutboxQueueOptions.cs` — `OutboxQueues:ConnectionString` (Key Vault ref in prod; `UseDevelopmentStorage=true` for local Azurite); `OutboxQueues:SendEmailQueueName` (default `"send-email"`).
- `Outbox/StorageQueueOutboxPublisher.cs` — `QueueClient` per queue with `QueueMessageEncoding.Base64`. Queue auto-created on first publish (`CreateIfNotExistsAsync` under a `SemaphoreSlim` so two concurrent publishers don't race the metadata call). Message body is the bare outbox event id.

### Functions (`Makables.Functions/Outbox/`)
- `ProcessOutboxFunction.cs`:
  - **Timer trigger** (`%ProcessOutbox:Schedule%`, default `*/30 * * * * *`). Calls `IOutboxDispatcher.DispatchDueAsync`; logs summary; does not throw (per-row failures are already recorded on the outbox itself).
  - **HTTP trigger** (`POST /api/outbox/process`, `AuthorizationLevel.Function` — `x-functions-key` per ADR 0020 SecOps note). Returns `OkObjectResult(summary)` for the admin ops dashboard.
- `SendEmailFunction.cs`:
  - **Queue trigger** on `%OutboxQueues:SendEmailQueueName%` against `AzureWebJobsStorage`. Calls `ISendEmailHandler.HandleAsync`; logs outcome; does not throw on classified failure.

### DI (`AddMakablesInfrastructure`)
- New: `IOutboxConsumerRepository → OutboxConsumerRepository` (scoped), `IOutboxQueuePublisher → StorageQueueOutboxPublisher` (singleton, `QueueClient` is thread-safe), `IOutboxDispatcher → OutboxDispatcher` (scoped), `ISendEmailHandler → SendEmailHandler` (scoped), `OutboxQueueOptions` bound from configuration.
- Registered in `Makables.Config` so every host (Web.* and Functions) gets the same shape — Web hosts don't currently enqueue but the symmetry keeps later cross-cutting work (e.g. admin "force retry" command) straightforward.

### `BusinessErrorMessage`
- New codes: `OutboxQueuePublishFailed`, `OutboxRowNotFound`.

### Configuration
- `host.json` — `extensions.queues` tuned for low-volume + responsiveness (`maxPollingInterval=2s`, `batchSize=16`, `maxDequeueCount=5`, `visibilityTimeout=1m`). Concurrency for `ProcessOutboxTimer` is naturally 1 because the timer fires once per schedule (Isolated Worker doesn't honour the legacy `maxConcurrentCalls` for timers).
- `local.settings.json` — wires `ProcessOutbox:Schedule`, `OutboxQueues:*`, plus the now-`ValidateOnStart` shared options (`SendGrid:*`, `PublicAppUrls:WebBaseUrl`, `Jwt:*`, `ConnectionStrings:Postgres`) so `func start` boots locally.

### Packages
- `Azure.Storage.Queues 12.22.0` (added to `Infra.Common`).

### Tests (+27 facts; 458 total = 376 unit + 82 integration)
- `OutboxRetryPolicyTests` — 12 facts pin the schedule + stall semantics.
- `OutboxEventTests` — 2 new facts pin `ParkPendingConsumer` (advances NextRetryAt without touching RetryCount/error fields; refuses to park already-processed).
- `OutboxDispatcherTests` — 6 facts (empty batch no-op; happy-path publishes + parks; unknown event_type stalls; queue publish failure records Transient w/ next retry per policy; mixed batch processes all then saves once; cancellation propagates).
- `SendEmailHandlerTests` — 6 facts (RowMissing on missing id; already-processed idempotent no-op; happy-path MarkProcessed + SaveChanges; Transient failure with next-retry-per-policy; Permanent failure stalls immediately; transient chain stalls at MaxTransientAttempts+1).

## Out of scope
- Real Functions-runtime integration tests — require a Functions host fixture + Azurite container. Coverage via the orchestration-service unit tests is sufficient for T-0029; runtime smoke happens manually during `func start`.
- Admin "force retry" / "acknowledge stalled" command — `OutboxEvent.Acknowledge(...)` already exists from T-0011; the admin UI lands in Phase 5+ when the operations dashboard is built.
- Additional outbox event types (invoice.generate, label.generate, notification.*) — Phase 4+ tickets. T-0029 only routes the three Phase-2 auth event types.
- SendGrid event-webhook (bounce / complaint) — deferred per T-0028 scope.

## Acceptance criteria
- **AC-1** Build clean; 458 tests pass (376 unit + 82 integration).
- **AC-2** `OutboxDispatcher` loads up to 50 due rows ordered by `created_at ASC`, routes the 3 auth event types to `send-email` queue, stalls unknown event_types as Permanent, records queue-publish failures as Transient with next retry per `OutboxRetryPolicy`.
- **AC-3** `SendEmailHandler` is idempotent on already-processed rows (queue at-least-once safety).
- **AC-4** `SendEmailHandler` classifies `IEmailSendService` failures against `OutboxRetryPolicy`; never throws on classified failure.
- **AC-5** `OutboxRetryPolicy` schedule is 1m → 5m → 15m → 1h → 6h → 24h (6 attempts); non-transient kinds stall immediately.
- **AC-6** `OutboxEvent.ParkPendingConsumer` advances `NextRetryAt` without touching `RetryCount` or error fields, and refuses to park a processed row.
- **AC-7** `ProcessOutboxFunction` has both timer (`*/30 * * * * *`) and HTTP (`POST /api/outbox/process`, `AuthorizationLevel.Function`) triggers per ADR 0020.
- **AC-8** `SendEmailFunction` is a queue-trigger thin wrapper that does no business logic per ADR 0020 §"Compliance".
- **AC-9** Queue message body carries only the outbox event id; the payload never leaves Postgres.
- **AC-10** CLAUDE.md hygiene: no business logic in `Makables.Functions/*.cs`; no `SaveChangesAsync` outside the dedicated orchestrators; `Core.Domain` has no third-party packages.

## Status log
- 2026-05-25 done. 458 tests pass. Awaiting dual reviewer (security + code-quality) per workflow.
