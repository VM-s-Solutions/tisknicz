---
id: T-0109
title: Force-retry / acknowledge stalled outbox events (admin)
status: ready
size: S
owner: dotnet-backend
created: 2026-06-14
updated: 2026-06-14
depends_on: [T-0029, T-0103, T-0105]
blocks: [T-0108]
user_stories: [US-admin-0014]
adrs: [0013, 0014, 0020]
phase: 4
manual_steps: []
security_touching: true
layers: [domain, appservices, web-admin]
---

# T-0109 — Force-retry / acknowledge stalled outbox events (admin)

## Context

T-0109 is the **second ticket in the admin-cleanup bundle** (risk-ascending: T-0111 read-only audit/order/invoice list queries → **T-0109 smallest mutation** → T-0108 country-config provider change → T-0110 irreversible GDPR hard-delete). All four ship under one PR. T-0109 is the smallest mutation of the four: two one-file admin commands that nudge a single `OutboxEvent` row's retry state. It directly satisfies **US-admin-0014 — Force-retry / acknowledge stalled outbox events** (all three AC).

The outbox state machine and its admin escape hatches are already half-built. `OutboxEvent` (T-0029, `Core.Domain/Outbox/OutboxEvent.cs`) already exposes `Acknowledge(adminUserId, now)` (line 107 — sets `AcknowledgedAt`/`AcknowledgedBy` + `ProcessedAt`, clears `NextRetryAt`), `RecordFailure`, `MarkProcessed`, `ParkPendingConsumer`, and the `RetryCount`/`NextRetryAt`/`LastErrorKind`/`LastErrorCode` fields. `OutboxRetryPolicy` (T-0029, `Core.AppServices/Common/OutboxRetryPolicy.cs`) owns the transient backoff ladder (`1m, 5m, 15m, 1h, 6h, 24h`; stall after 6 attempts). `IOutboxConsumerRepository.GetByIdAsync` (T-0029) already loads a single row by id and returns it tracked. The `ProcessOutboxFunction` sweep (T-0029) re-picks any row where `processed_at IS NULL AND (next_retry_at IS NULL OR next_retry_at <= now)`.

What's **missing** is (1) one new domain method — `OutboxEvent.RequeueForRetry(now)` — that flips a *stalled* row back into the due set without resetting the backoff ladder, and (2) the two admin commands + endpoints that drive `RequeueForRetry` and the existing `Acknowledge`. T-0109 adds exactly that. No migration (every field `RequeueForRetry` and `Acknowledge` touch already exists on the `outbox_events` table), no new entity, no outbox emission (admin nudges the outbox; it does not enqueue *into* it), no email.

`RetryOutboxEvent` and `AcknowledgeOutboxEvent` are `IAdminAuditableCommand` — the `AdminAuditPipelineBehavior` (T-0103) captures the before/after JSONB snapshot + reason atomically with the mutation (ADR 0014). Both run under the `Web.Admin` host audience (ADR 0013 — a customer/maker JWT cannot replay here). Per Q-0021 (ruled this engagement), a no-op re-call (retrying an already-processed row that Silent-Succeeds, or re-acknowledging an already-acknowledged row) still writes a benign "admin attempted X" audit row — that is itself audit-worthy. There is no "no second audit row" AC anywhere in this ticket.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked the retry/acknowledge semantics at the 2026-06-14 bundle deliberation (Q-D). PM-absorbed decisions follow from the T-0103 audit-pipeline + T-0105 `IAdminAuditableCommand` + T-0029 outbox precedents.

### A. User-locked at the 2026-06-14 deliberation (Q-D) — non-negotiable

1. **Retry = one-shot "try now", backoff-ladder PRESERVED.** `RequeueForRetry(now)` sets `NextRetryAt = now`, increments `RetryCount`, and clears the stall (`NextRetryAt` becomes non-null again so the sweep re-picks it). It does **NOT** reset `RetryCount` to 0 — on the *next* failure, `RecordFailure` + `OutboxRetryPolicy.NextAttempt` continue from the bumped `RetryCount`, re-entering the backoff ladder at the current rung (or stalling immediately if `RetryCount` already exhausted `MaxTransientAttempts`). This is the load-bearing pure-logic surface tested red-first. **Rejected:** reset the ladder to 0 (turns "try once more" into "restart the whole 31-hour retry budget" — an admin clicking Retry on a genuinely-broken row would re-flood the queue with six fresh attempts; the admin wants *one* nudge, then back to the operator's judgement).

2. **Acknowledge = terminal, no re-attempt.** `Acknowledge(adminUserId, now)` (EXISTS) sets `ProcessedAt = now` + `AcknowledgedAt`/`AcknowledgedBy`, clears `NextRetryAt`. The row leaves the stalled set permanently and is never retried. The `Reason` (capped 2000 chars) rides the audit log. **Rejected:** acknowledge-then-also-retry (contradictory — acknowledge is the "give up, stop bothering me" action; a row the admin wants retried uses Retry).

3. **Retry on an already-processed row → guard, not crash.** A row with `ProcessedAt != null` has nothing to retry. The handler returns a clean `outbox.alreadyProcessed` 409 rather than mutating (the domain method also refuses, belt-and-braces). This is a *distinct* outcome from acknowledge's idempotency (see A.4) — a processed row is a state error for *retry* (the admin clicked the wrong button on a row that already drained), whereas re-acknowledging is a benign no-op. **Rejected:** Silent-Success on already-processed retry (hides operator error — "I clicked Retry, it said OK, why didn't it re-send?"; the 409 tells the admin the row already drained).

4. **Acknowledge on an already-acknowledged row → Silent-Success.** Re-acknowledging an already-acknowledged/processed row is a benign no-op: 200, no mutation, the existing `AcknowledgedBy`/`AcknowledgedAt` untouched. Per Q-0021 the pipeline still writes a benign "admin attempted acknowledge" audit row — accepted. **Rejected:** 409 on re-acknowledge (re-clicking Acknowledge on a row that's already gone from the stalled list is harmless; a hard error here just annoys the operator).

### B. ADR-locked (no relitigation)

- **ADR 0013 (per-audience JWT + unscoped admin reads).** Both commands run under the `Web.Admin` host audience; a customer/maker JWT cannot replay. The row load uses `IOutboxConsumerRepository.GetByIdAsync` (already unscoped — `OutboxEvent` is bookkeeping infrastructure, not `Auditable`, so there is no query filter to bypass).
- **ADR 0014 (UoW pipeline + admin audit).** `ValidationPipelineBehavior` runs on every request; `UnitOfWorkPipelineBehavior` commits the mutation; `AdminAuditPipelineBehavior` (T-0103) writes the before/after JSONB + reason in the same transaction. The handler does **NOT** call `SaveChangesAsync()`.
- **ADR 0020 (outbox / async side effects).** T-0109 only manipulates outbox *bookkeeping* state; it does not enqueue an event, send a queue message, or emit an email. The `ProcessOutboxFunction` sweep is the only thing that re-publishes a requeued row.
- **One-file feature shape.** `Features/Outbox/RetryOutboxEvent.cs` and `Features/Outbox/AcknowledgeOutboxEvent.cs`, each with nested `Command`, `Validator`, `Handler`, globally-unique `Response`.
- **`BusinessResult<T>` for expected failures.** Not-found and already-processed surface as `BusinessResult.Failure`; the validator clamps surface as 400.

### C. PM-absorbed (no user input needed; follow bundle defaults)

- **Both commands `IAdminAuditableCommand`** with fail-closed session check (no "system" attribution — a privileged delivery-infra mutation must name the admin). `ActionCode = "outbox.retry"` / `"outbox.acknowledge"`; `TargetEntity = "outbox"`; `TargetId = OutboxEventId`; `Notes = Reason` (Retry has no reason — `Notes` null; Acknowledge carries the reason).
- **Reason cap = 2000 chars** (RefundOrder / VerifyMaker m-3 precedent — matches the audit-log notes column width; oversize dies as a clean 400 not a 500). Retry has no reason field (one-shot nudge); Acknowledge requires a non-empty ≤2000-char reason.
- **Reuse `BusinessErrorMessage.OutboxRowNotFound` (`outbox.rowNotFound`, EXISTS line 619)** for the not-found case rather than minting `outbox.eventNotFound` (the existing code is the exact semantic; minting a duplicate would split the surface). **Add** one new code: `OutboxAlreadyProcessed = "outbox.alreadyProcessed"` for the retry-on-processed guard. Both get cs-CZ i18n parity keys.
- **`IOutboxAdminRepository`** is NOT introduced — `IOutboxConsumerRepository.GetByIdAsync` already loads a single tracked row by id. Reusing it keeps the outbox repository surface to one read+update interface. (The semantic — "load one row, mutate, let UoW commit" — is identical to the consumer's per-event update path.)
- **Page-based pagination, AsNoTracking, `.Unscoped()`** — N/A to T-0109 (no list query; that's T-0111). Listed here only to confirm the bundle defaults don't apply to this ticket.
- **NSwag regen — admin host only.** Two new `POST` endpoints under `/api/v1/outbox-events/{id}/...` are a contract change.
- **DI:** no new registration (`IOutboxConsumerRepository` already registered by T-0029; both handlers resolve it + `IClock` + `IUserSessionProvider`).

## Scope

Two one-file features on the **Web.Admin** host.

### Domain layer

- **`Core.Domain/Outbox/OutboxEvent.cs`** — add ONE method (the only domain change in this ticket):
  ```csharp
  /// <summary>
  /// Admin force-retry of a STALLED event (T-0109 / US-admin-0014 AC-1).
  /// One-shot "try now": sets <see cref="NextRetryAt"/> to <paramref name="now"/>
  /// so the next ProcessOutbox sweep re-picks the row, and increments
  /// <see cref="RetryCount"/> so the attempt is counted. Does NOT reset
  /// the backoff ladder — on the next failure, RecordFailure +
  /// OutboxRetryPolicy.NextAttempt continue from the bumped RetryCount
  /// (re-entering the ladder at the current rung, or stalling immediately
  /// if MaxTransientAttempts is already exhausted). Per locked decision A.1.
  ///
  /// Refuses an already-processed row — there is nothing to retry once
  /// ProcessedAt is set (locked decision A.3; the handler also pre-guards
  /// with a clean outbox.alreadyProcessed before reaching this method).
  /// </summary>
  public void RequeueForRetry(DateTimeOffset now)
  {
      if (ProcessedAt is not null)
          throw new InvalidOperationException("Cannot retry an already-processed event.");
      RetryCount = checked(RetryCount + 1);
      NextRetryAt = now;
  }
  ```
  Note: `RequeueForRetry` deliberately does **NOT** touch `LastErrorKind`/`LastErrorCode` — the stall's diagnostic remains visible until the next attempt overwrites it via `RecordFailure` or clears it via `MarkProcessed`. It does NOT reset `RetryCount`. The `checked(...)` mirrors `RecordFailure`'s overflow guard.

### AppServices layer

- **`Core.AppServices/Features/Outbox/RetryOutboxEvent.cs`** — NEW one-file feature:
  - `Command(string OutboxEventId) : ICommand<RetryOutboxEventResponse>, IAdminAuditableCommand`. `ActionCode => "outbox.retry"`; `TargetEntity => "outbox"`; `TargetId => OutboxEventId`; `Notes => null` (retry is a one-shot nudge, no operator reason).
  - `RetryOutboxEventResponse(string OutboxEventId, int RetryCount, DateTimeOffset NextRetryAt)` — globally-unique name (post-PR-#38 NSwag convention). `NextRetryAt` is non-nullable in the response because a successful requeue always sets it.
  - `Validator : AbstractValidator<Command>` — `OutboxEventId` `NotEmpty` (`BusinessErrorMessage.Required`) + `MaximumLength(40)` (`BusinessErrorMessage.MaxLength`), `Cascade(CascadeMode.Stop)`.
  - `Handler(IOutboxConsumerRepository outbox, IClock clock, IUserSessionProvider session, ILogger<Handler> logger)`:
    1. **Fail-closed session** — `if (string.IsNullOrEmpty(session.GetUserId())) return Failure(Error.Unauthorized());` (RefundOrder precedent — never attribute delivery-infra mutation to "system").
    2. **Load** — `var ev = await outbox.GetByIdAsync(command.OutboxEventId, ct);` → `if (ev is null) return Failure(Error.NotFound("outboxEventId", BusinessErrorMessage.OutboxRowNotFound));`.
    3. **Guard already-processed** — `if (ev.ProcessedAt is not null) return Failure(Error.Conflict("outboxEventId", BusinessErrorMessage.OutboxAlreadyProcessed));` (locked A.3 — clean 409, not a Silent-Success; the domain method's throw is the belt-and-braces backstop).
    4. **Mutate** — `ev.RequeueForRetry(clock.UtcNow);` (no `SaveChangesAsync` — UoW + audit pipeline commit).
    5. **Return** — `BusinessResult.Success(new RetryOutboxEventResponse(ev.Id, ev.RetryCount, ev.NextRetryAt!.Value));`.

- **`Core.AppServices/Features/Outbox/AcknowledgeOutboxEvent.cs`** — NEW one-file feature:
  - `Command(string OutboxEventId, string Reason) : ICommand<AcknowledgeOutboxEventResponse>, IAdminAuditableCommand`. `ActionCode => "outbox.acknowledge"`; `TargetEntity => "outbox"`; `TargetId => OutboxEventId`; `Notes => Reason`.
  - `AcknowledgeOutboxEventResponse(string OutboxEventId, DateTimeOffset AcknowledgedAt, string AcknowledgedBy)` — globally-unique name.
  - `Validator` — `OutboxEventId` as above; `Reason` `Cascade(Stop)` + `NotEmpty` (`Required`) + `MaximumLength(2000)` (`MaxLength`).
  - `Handler(IOutboxConsumerRepository outbox, IClock clock, IUserSessionProvider session, ILogger<Handler> logger)`:
    1. **Fail-closed session** — `var adminUserId = session.GetUserId();` → `if (string.IsNullOrEmpty(adminUserId)) return Failure(Error.Unauthorized());`.
    2. **Load** — as above; null → `OutboxRowNotFound`.
    3. **Silent-Success on already-acknowledged** — `if (ev.ProcessedAt is not null) { logger.LogInformation(...); return Success(BuildResponse(ev)); }` (locked A.4 — 200, no mutation; existing `AcknowledgedBy`/`AcknowledgedAt` preserved; Q-0021 benign audit row accepted). Note: an *acknowledged* row always has `ProcessedAt` set (Acknowledge sets both), so `ProcessedAt is not null` is the correct idempotency probe; if a row was processed by the *sweep* (not acknowledged) and the admin then acknowledges, the same probe Silent-Succeeds — acknowledging an already-drained row is equally a no-op. `BuildResponse` falls back to `AcknowledgedBy ?? adminUserId` / `AcknowledgedAt ?? ProcessedAt!.Value` so a swept-but-unacknowledged row still returns a coherent response.
    4. **Mutate** — `ev.Acknowledge(adminUserId, clock.UtcNow);` (EXISTS).
    5. **Return** — `BusinessResult.Success(BuildResponse(ev));`.

### Domain error codes

- **`Core.Domain/Common/BusinessErrorMessage.cs`** — add ONE code in the existing `=== Outbox processor (T-0029) ===` block:
  ```csharp
  public const string OutboxAlreadyProcessed = "outbox.alreadyProcessed";
  ```
  `OutboxRowNotFound` (line 619) is REUSED for not-found — no new code minted for it.

### i18n parity

- **`frontend/src/lib/i18n/cs-CZ.ts`** — add parity keys (admin / ops surface only; the customer never sees these):
  ```ts
  // T-0109 admin outbox retry/acknowledge codes (parity with
  // BusinessErrorMessage). Admin-surface only (T-0118 outbox UI).
  'outbox.rowNotFound': 'Tato fronta událostí už neexistuje.',
  'outbox.alreadyProcessed':
    'Tato událost už byla zpracována — není co opakovat.',
  ```
  `outbox.rowNotFound` had no cs-CZ key before (it shipped in `BusinessErrorMessage` at T-0029 but was never surfaced to a UI); adding it closes the parity gap the consistency check flags.

### Web.Admin host

- **`Web.Admin/Controllers/OutboxEventsController.cs`** — NEW controller, mirroring `OrdersController` (T-0105) one-liner convention:
  - `[ApiController]`, `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/outbox-events")]`, `[Authorize]` (admin audience per ADR 0013).
  - `[HttpPost("{id}/retry")]` → `Retry(string id, CancellationToken ct) => HandleResult(await Mediator.Send(new RetryOutboxEvent.Command(id), ct));`. `[ProducesResponseType(typeof(RetryOutboxEvent.RetryOutboxEventResponse), 200)]` + 400/401/404/409.
  - `[HttpPost("{id}/acknowledge")]` with `AcknowledgeOutboxEventRequest(string Reason)` body → `Acknowledge(string id, [FromBody] AcknowledgeOutboxEventRequest request, CancellationToken ct) => HandleResult(await Mediator.Send(new AcknowledgeOutboxEvent.Command(id, request.Reason), ct));`. `[ProducesResponseType(typeof(AcknowledgeOutboxEvent.AcknowledgeOutboxEventResponse), 200)]` + 400/401/404.
  - Routes resolve to `POST /api/v1/outbox-events/{id}/retry` and `POST /api/v1/outbox-events/{id}/acknowledge`.

### NSwag regen

The two new `POST` endpoints are a contract change → **NSwag regen REQUIRED in the same PR** (admin host client only). `npm run generate:api` produces the diff; `frontend/src/lib/api-client/` cannot be edited manually (pre-commit hook, T-0013). The new `RetryOutboxEventResponse` + `AcknowledgeOutboxEventResponse` appear in the generated admin client. T-0111 / T-0108 / T-0110 regen separately within the same PR.

## Tests

### OutboxEventTests (EXTEND, ~5 new domain unit tests — the red-first surface)

`backend/src/Makables.Tests/Domain/Outbox/OutboxEventTests.cs` — `RequeueForRetry` is the load-bearing pure-logic method; write these RED before the domain method exists.

1. **RequeueForRetry_sets_next_retry_to_now_and_bumps_retry_count** — seed a stalled event (`RecordFailure(Permanent, "x.permanent", null)` → `RetryCount == 1`, `NextRetryAt == null`). Call `RequeueForRetry(Now.AddHours(2))`. Assert `NextRetryAt == Now.AddHours(2)`, `RetryCount == 2`, `ProcessedAt == null`.
2. **RequeueForRetry_does_NOT_reset_the_backoff_ladder** — seed an event failed through `RetryCount == 4` (four `RecordFailure(Transient, ...)` calls). Call `RequeueForRetry(now)` → `RetryCount == 5`. Then `RecordFailure(Transient, "x", OutboxRetryPolicy.NextAttempt(Transient, 5, now))`. Assert `RetryCount == 6` and the next-attempt delay is `TransientBackoffs[5]` (24h) — proving the ladder resumed from the bumped count, NOT from 0. **This is the locked A.1 assertion.**
3. **RequeueForRetry_preserves_last_error_diagnostics** — after a `RecordFailure(Permanent, "email.invalidAddress", null)`, call `RequeueForRetry(now)`. Assert `LastErrorKind == Permanent` and `LastErrorCode == "email.invalidAddress"` still set (the stall diagnostic survives until the next attempt overwrites it).
4. **RequeueForRetry_refuses_already_processed_row** — `MarkProcessed(Now)`, then `RequeueForRetry(...)` → `Throw<InvalidOperationException>`.
5. **RequeueForRetry_on_a_transient_due_row_still_advances** — an event with `RetryCount == 2` and a future `NextRetryAt` (not yet stalled): `RequeueForRetry(now)` pulls `NextRetryAt` back to `now` and bumps to `RetryCount == 3` (admin can force-retry a not-yet-due row too — "try now" overrides the backoff wait).

### RetryOutboxEventHandlerTests (NEW, ~3 unit tests)

`backend/src/Makables.Tests/AppServices/Features/Outbox/RetryOutboxEventHandlerTests.cs` — NSubstitute mocks (`IOutboxConsumerRepository`, `IClock`, `IUserSessionProvider`).

1. **Stalled_event_is_requeued_for_retry** — session returns an admin id; repo returns a stalled event. Assert response carries the bumped `RetryCount` + `NextRetryAt == clock.UtcNow`; `RequeueForRetry` ran (the returned event's `NextRetryAt` equals the clock).
2. **Missing_event_returns_outbox_rowNotFound** — repo returns `null`. Assert `Failure` with `Error.Code == BusinessErrorMessage.OutboxRowNotFound`.
3. **Already_processed_event_returns_outbox_alreadyProcessed_409** — repo returns an event with `ProcessedAt` set. Assert `Failure` with `Error.Code == BusinessErrorMessage.OutboxAlreadyProcessed`; the event was NOT mutated (`RequeueForRetry` not reached). Plus a fail-closed-session test: empty session id → `Error.Unauthorized()`.

### AcknowledgeOutboxEventHandlerTests (NEW, ~3 unit tests)

`backend/src/Makables.Tests/AppServices/Features/Outbox/AcknowledgeOutboxEventHandlerTests.cs`.

1. **Stalled_event_is_acknowledged_with_admin_identity** — session returns `admin-1`; repo returns a stalled event. Assert response carries `AcknowledgedBy == "admin-1"`, `AcknowledgedAt == clock.UtcNow`; the event's `ProcessedAt` is set.
2. **Already_acknowledged_event_is_silent_success** — repo returns an already-acknowledged event (`AcknowledgedBy == "admin-2"`, `ProcessedAt` set earlier). Assert `Success`, response echoes the EXISTING `AcknowledgedBy == "admin-2"` (not the current caller), no re-mutation. (Q-0021 benign audit row is the pipeline's concern, not asserted here.)
3. **Reason_required_and_capped** — Validator rejects empty `Reason` (`Required`) and a 2001-char `Reason` (`MaxLength`). Plus fail-closed-session: empty session id → `Error.Unauthorized()`.

### OutboxEventAdminIntegrationTests (NEW, ~2 integration tests)

`backend/src/Makables.IntegrationTests/Outbox/OutboxEventAdminIntegrationTests.cs` — Testcontainers Postgres + admin-audience `WebApplicationFactory`.

1. **Retry_makes_a_stalled_row_due_again_for_the_sweep** — seed a stalled `outbox_events` row (`processed_at = null`, `next_retry_at = null`, `retry_count = 6`, `last_error_kind = Permanent`). `POST /api/v1/outbox-events/{id}/retry` as admin. Assert 200; re-read the row: `next_retry_at` is set to ~now and `retry_count == 7`; the row now matches the `LoadDueAsync` predicate (`processed_at IS NULL AND next_retry_at <= now`) so the next sweep picks it. Assert an `admin_audit_log` row with `action_code = "outbox.retry"` was written.
2. **Acknowledge_removes_a_row_from_the_stalled_set** — seed a stalled row. `POST /api/v1/outbox-events/{id}/acknowledge` with a reason as admin. Assert 200; re-read: `processed_at` + `acknowledged_at` + `acknowledged_by` set; the row no longer matches `LoadDueAsync` (it's processed). Assert an `admin_audit_log` row with `action_code = "outbox.acknowledge"` + the reason in `notes`.

## Alternatives Considered

- **Option A — Reset the backoff ladder on retry (`RetryCount = 0`).** *Rejected per A.1* — turns the admin's "try this once more" into "restart the entire 31-hour, six-attempt retry budget". An admin clicking Retry on a genuinely-broken row (bad email address that will never deliver) would re-flood the queue with six fresh attempts spaced across 31 hours, each failing again. The admin wants ONE nudge, then the row stalls again for human judgement. Preserving `RetryCount` is the whole point of the locked decision — and is exactly what test #2 pins.
- **Option B — Silent-Success on retrying an already-processed row.** *Rejected per A.3* — hides operator error. If the admin clicks Retry on a row that already drained and gets a 200, they'll wonder why the side effect didn't re-fire. The 409 `outbox.alreadyProcessed` tells them plainly: "this already ran, there's nothing to retry". (Acknowledge's idempotency is different — re-acknowledging a gone row is genuinely harmless.)
- **Option C — 409 on re-acknowledging an already-acknowledged row.** *Rejected per A.4* — re-clicking Acknowledge on a row that's already off the stalled list is harmless; a hard error just annoys the operator who double-clicked. Silent-Success (200, no re-mutation, preserve the original acknowledger) is the right idempotency posture, mirroring RefundOrder's already-Refunded Silent-Success.
- **Option D — One combined command with a `RetryOrAcknowledge` mode enum.** *Rejected* — violates "one capability = one story / one command". Retry and Acknowledge have different request shapes (Acknowledge needs a reason; Retry doesn't), different responses, different error semantics (409 vs Silent-Success on the already-processed path), and different audit `ActionCode`s. Folding them into one command with a discriminator enum muddies the validator and the audit trail. Two one-file features is the locked one-file-feature shape.
- **Option E — Mint `outbox.eventNotFound` as a new code.** *Rejected per C* — `BusinessErrorMessage.OutboxRowNotFound` (`outbox.rowNotFound`, line 619) already exists and is the exact semantic ("the outbox row you named doesn't exist"). Minting a parallel `eventNotFound` would split one concept across two codes and two i18n keys for no gain. Reuse + add the missing cs-CZ parity key.
- **Option F — New `IOutboxAdminRepository` for the admin load path.** *Rejected per C* — `IOutboxConsumerRepository.GetByIdAsync` already loads a single tracked row by id (the consumer's per-event re-read path). The admin's "load one, mutate, let UoW commit" is the identical access shape. A new interface would duplicate the method for no behavioral difference; the outbox repository surface stays at one read+update interface.
- **Option G — Have the admin command directly publish to the queue (skip the sweep).** *Rejected per ADR 0020 + B* — the `ProcessOutboxFunction` sweep is the single re-publish path; it owns the queue-publish + park-pending-consumer handoff (T-0029). The admin command's job is to flip the row back into the *due* set (`next_retry_at <= now`); the sweep does the rest on its next pass. Bypassing the sweep would duplicate the publish/park logic and risk a double-publish race the sweep was built to prevent.
- **Option H — Emit a `notification.admin` outbox event when a retry/ack happens.** *Rejected per PM defaults* — T-0109 explicitly emits no outbox/email (PM-absorbed). The action is already fully recorded in `admin_audit_log` (ADR 0014), which the dashboard reads (US-admin-0015). A self-referential outbox event about an outbox mutation adds noise with no consumer.

## Out of scope

- **Listing / surfacing stalled outbox events** — the stalled-count badge + the outbox triage list live on the admin dashboard (US-admin-0002 AC-1/AC-2) and are read models delivered by the frontend admin UI (T-0118) reading existing data. T-0109 ships only the two mutation endpoints. (The list query, if it needs a dedicated backend endpoint, is a separate ticket — not bundled here.)
- **Bulk retry / bulk acknowledge** — one event id per call. Bulk operations across the stalled set are post-MVP (mirrors US-admin-0010's "no bulk state changes" out-of-scope).
- **Resetting the backoff ladder** — explicitly rejected per A.1. There is no "reset retry count" action at MVP; an admin who wants a row to stop retrying uses Acknowledge.
- **Changing `OutboxRetryPolicy` (the backoff curve)** — the `1m/5m/15m/1h/6h/24h` ladder is owned by T-0029 and unchanged here. T-0109 *preserves* the ladder; it does not tune it.
- **Editing the event payload before retry** — the admin cannot mutate `PayloadJson`. A malformed payload is an Acknowledge candidate (give up), not a retry-after-edit candidate (post-MVP if ever).
- **`outbox.queuePublishFailed` handling** — that code is the sweep's own publish-failure path (T-0029), unrelated to the admin retry/ack surface. No change.
- **Migration** — none. Every field `RequeueForRetry` and `Acknowledge` touch (`NextRetryAt`, `RetryCount`, `ProcessedAt`, `AcknowledgedAt`, `AcknowledgedBy`) already exists on `outbox_events` (T-0029).
- **The other three bundle tickets** — T-0111 (read-only list queries), T-0108 (country-config provider change), T-0110 (GDPR hard-delete) are separate tickets in the same PR.

## Security notes

- **security_touching: YES** — both endpoints mutate delivery infrastructure (the outbox is the platform's side-effect backbone: emails, invoices, labels). A force-retry re-fires a side effect; an acknowledge silences one permanently. Both are admin-JWT-gated (`[Authorize]`, admin audience per ADR 0013) and fail-closed on session (no "system" attribution).
- **Q-0011 (TOUCHED, not closed).** These admin endpoints are admin-JWT-gated (2 trusted users) — materially lower spam/abuse risk than the customer surface Q-0011 was raised against. Q-0011 stays open as a standalone secops follow-up; **secops Gate 3 should re-confirm** the rate-limiting posture on the admin mutation surface as part of the bundle review. T-0109 does NOT expand scope to address Q-0011.
- **Audit completeness** — every successful retry/acknowledge writes a before/after JSONB audit row (ADR 0014, T-0103 pipeline). Per Q-0021, a no-op re-call (already-acknowledged Silent-Success) also writes a benign "admin attempted acknowledge" row — accepted as audit-worthy, not a defect.

## Acceptance criteria

- **AC-1** Given a stalled outbox event (`processed_at = null`, `next_retry_at = null`, non-`None` `last_error_kind`), when an admin `POST`s `/api/v1/outbox-events/{id}/retry`, then the response is `200 OK`, `next_retry_at` is set to ~now, `retry_count` is incremented by 1, and the row now matches the `ProcessOutbox` due-predicate so the next sweep re-picks it. (US-admin-0014 AC-1.)
- **AC-2** Given a row failed through `retry_count = N`, when the admin force-retries, then `retry_count = N+1` and the backoff ladder is **NOT** reset — a subsequent failure computes `NextAttempt(Transient, N+1, now)` from the bumped count (re-entering the ladder at rung `N`, or stalling if `N+1 > MaxTransientAttempts`). (Locked A.1; the load-bearing domain assertion.)
- **AC-3** Given a stalled (or any unprocessed) outbox event, when an admin `POST`s `/api/v1/outbox-events/{id}/acknowledge` with a non-empty reason, then the response is `200 OK`, `processed_at` + `acknowledged_at` + `acknowledged_by` are set, `next_retry_at` is cleared, and the row leaves the stalled set permanently (never retried). (US-admin-0014 AC-2.)
- **AC-4** Given a retry or acknowledge succeeds, when the action completes, then an `admin_audit_log` row is written with the matching `action_code` (`outbox.retry` / `outbox.acknowledge`), the admin's id, and (for acknowledge) the reason in `notes`. (US-admin-0014 AC-3; ADR 0014.)
- **AC-5** Given an `id` that matches no outbox row, when either endpoint is called, then the response is `404` with `outbox.rowNotFound`.
- **AC-6** Given an **already-processed** row, when `/retry` is called, then the response is `409` with `outbox.alreadyProcessed` and the row is unchanged. (Locked A.3 — retry on a drained row is operator error, surfaced loudly.)
- **AC-7** Given an **already-acknowledged** (or already-processed) row, when `/acknowledge` is called, then the response is `200` (Silent-Success), the row is unchanged, and the response echoes the **existing** `acknowledged_by` (not the current caller). A benign "admin attempted acknowledge" audit row may be written (Q-0021). (Locked A.4.)
- **AC-8** Given an anonymous request (no admin session), when either endpoint is called, then the response is `401 auth.required` and no mutation occurs (fail-closed — the command never attributes to "system"). Acknowledge additionally `400`s on empty/`>2000`-char reason.
- **AC-9** Build clean. Unit tests: baseline + ~5 domain (`OutboxEventTests` `RequeueForRetry`) + ~3 retry-handler + ~3 acknowledge-handler. Integration: baseline + ~2 (`OutboxEventAdminIntegrationTests`). `node scripts/check-consistency.mjs` exit 0 (no new T1–T7 violations; the new `OutboxAlreadyProcessed` code + reused `OutboxRowNotFound` both have cs-CZ parity keys). NSwag regen committed in the same PR (admin host); no manual edits to `frontend/src/lib/api-client/`.

## Technical notes

### Why preserve the backoff ladder on retry (not reset)

`RequeueForRetry` increments `RetryCount` rather than zeroing it because the admin's "Retry now" is a *single* nudge, not a budget reset. Consider a row that exhausted all six transient attempts and stalled (or stalled immediately on a Permanent error). If Retry reset `RetryCount = 0`, the row would re-enter the full `1m → 24h` ladder — six more failed attempts over 31 hours for a side effect that's genuinely broken. By bumping `RetryCount` and leaving the policy intact, the row gets exactly one more attempt; if it fails again, `OutboxRetryPolicy.NextAttempt(kind, RetryCount, now)` either schedules the next ladder rung (if there's budget left) or returns `null` (stall) — and the admin is back in the loop. This is the difference between "try once more" and "I take responsibility for restarting the whole retry process", and the user locked the former.

### Why reuse `IOutboxConsumerRepository.GetByIdAsync` (not a new admin repo)

The consumer's per-event re-read path (`SendEmailFunction` receives an id off the queue and re-loads the authoritative row) is the identical access shape the admin commands need: load one tracked row by id, mutate it, let the UoW pipeline commit. Introducing `IOutboxAdminRepository.GetByIdAsync` would duplicate the method verbatim for zero behavioral difference. The outbox repository surface stays minimal: `IOutbox` (producer enqueue) + `IOutboxConsumerRepository` (load + update). `OutboxEvent` is not `Auditable`, so `GetByIdAsync` has no query filter to bypass — the load is already "unscoped" in the ADR-0013 sense.

### Why retry-on-processed is a 409 but acknowledge-on-acknowledged is Silent-Success

They are different operator intents on a drained row. Retry means "make this side effect happen" — on an already-processed row that's impossible, and Silent-Success would hide the fact that nothing re-fired (the admin would expect the email/invoice/label to re-send). A 409 says "this already ran". Acknowledge means "stop bothering me about this" — on an already-acknowledged row, the operator's intent is already satisfied; re-clicking is harmless, so a 200 (no re-mutation, original acknowledger preserved) is the least-surprising response. The asymmetry is deliberate and locked (A.3 vs A.4).

### Why no `SaveChangesAsync` in the handlers

Both handlers mutate the tracked `OutboxEvent` and return. The `UnitOfWorkPipelineBehavior` (commands only, ADR 0014) commits the change; the `AdminAuditPipelineBehavior` (T-0103) writes the before/after JSONB audit row in the **same transaction**. A handler that called `SaveChangesAsync` itself would split the mutation from the audit row across two transactions, breaking the ADR-0014 atomicity guarantee.

## Files touched (expected)

### New
- `backend/src/Makables.Core.AppServices/Features/Outbox/RetryOutboxEvent.cs`
- `backend/src/Makables.Core.AppServices/Features/Outbox/AcknowledgeOutboxEvent.cs`
- `backend/src/Makables.Web.Admin/Controllers/OutboxEventsController.cs`
- `backend/src/Makables.Tests/AppServices/Features/Outbox/RetryOutboxEventHandlerTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/Outbox/AcknowledgeOutboxEventHandlerTests.cs`
- `backend/src/Makables.IntegrationTests/Outbox/OutboxEventAdminIntegrationTests.cs`

### Modified
- `backend/src/Makables.Core.Domain/Outbox/OutboxEvent.cs` — add `RequeueForRetry(DateTimeOffset now)`.
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` — add `OutboxAlreadyProcessed = "outbox.alreadyProcessed"` in the existing outbox block.
- `backend/src/Makables.Tests/Domain/Outbox/OutboxEventTests.cs` — add ~5 `RequeueForRetry` tests (red-first).
- `frontend/src/lib/i18n/cs-CZ.ts` — add `outbox.rowNotFound` + `outbox.alreadyProcessed` parity keys.
- `frontend/src/lib/api-client/*` — NSwag-regenerated (admin host); committed in the same PR.
- `docs/architecture/roles/outbox.md` — note the admin retry/acknowledge surface + `RequeueForRetry` ladder-preservation semantics.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0109.md`.

## Status log

- 2026-06-14 `draft` by PM. Created as the second ticket (smallest mutation) in the admin-cleanup bundle (risk-ascending: T-0111 → **T-0109** → T-0108 → T-0110, one PR). Reference precedents on master: T-0029 outbox state machine (`OutboxEvent.Acknowledge`/`RecordFailure`/`MarkProcessed`/`ParkPendingConsumer`, `OutboxRetryPolicy` ladder, `IOutboxConsumerRepository.GetByIdAsync`), T-0103 `AdminAuditPipelineBehavior`, T-0105 `RefundOrder` (`IAdminAuditableCommand` + fail-closed session + Silent-Success precedent). Slice scope: one new domain method (`RequeueForRetry`) + two one-file admin features + one admin controller + one new error code (`OutboxAlreadyProcessed`) + reuse `OutboxRowNotFound` + 2 cs-CZ parity keys + ~11 unit + ~2 integration tests. No migration, no outbox emission, no email.
- 2026-06-14 `draft → ready` by PM. User locked retry/acknowledge semantics at the bundle deliberation (Q-D): **A.1** retry = one-shot "try now", backoff ladder PRESERVED (`RetryCount++`, `NextRetryAt = now`, no reset) — rejected resetting the ladder; **A.2** acknowledge = terminal, no re-attempt — rejected acknowledge-then-retry; **A.3** retry-on-processed = clean `outbox.alreadyProcessed` 409 — rejected Silent-Success (hides operator error); **A.4** acknowledge-on-acknowledged = Silent-Success preserving the original acknowledger — rejected 409. PM-absorbed: both `IAdminAuditableCommand` + fail-closed session, reason cap 2000, reuse `OutboxRowNotFound` (no `eventNotFound` mint), reuse `IOutboxConsumerRepository` (no new admin repo), no `SaveChangesAsync`, no outbox/email, NSwag regen admin host. Q-0021 disposition: benign no-op audit rows accepted; no "no second audit row" AC anywhere. Q-0011 TOUCHED not closed — admin-JWT-gated, lower risk; flagged for secops Gate 3 re-confirmation; scope NOT expanded. **Ready for dotnet-backend.** Implemented in bundle order T-0111 → T-0109 → T-0108 → T-0110, same branch, one PR; TDD red-first on `OutboxEvent.RequeueForRetry`.
