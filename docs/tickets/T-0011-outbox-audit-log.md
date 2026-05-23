---
id: T-0011
title: Outbox + AdminAuditLog + AdminAuditPipelineBehavior
status: done
size: M
owner: dotnet-backend
created: 2026-05-23
updated: 2026-05-23
depends_on: [T-0002, T-0003, T-0006, T-0008, T-0010]
blocks: [T-0014]
adrs: [0014, 0020]
phase: 1
---

# T-0011 — Outbox + Admin Audit Log

## Scope

Core.Domain.Outbox:
- `OutboxEvent` entity (not Auditable — bookkeeping). Fields: Id, AggregateId, EventType, PayloadJson, CreatedAt, ProcessedAt, RetryCount, NextRetryAt, LastErrorKind, LastErrorCode, AcknowledgedAt, AcknowledgedBy. Domain methods: Enqueue, MarkProcessed, RecordFailure, Acknowledge. Per ADR 0014 / 0020 / patterns §A.20.
- `OutboxErrorKind` enum (None/Transient/Permanent/Configuration/Unknown).
- `IOutbox` producer interface.

Core.Domain.Auditing:
- `AdminAuditLogEntry` entity (not Auditable — IS the audit log). Fields: Id, AdminUserId, ActionCode, TargetEntity, TargetId, BeforeJson, AfterJson, Notes, IpAddress, UserAgent, CreatedAt. Single static `Record` factory. Per ADR 0014.
- `IAdminAuditLogWriter` — snapshot + persistence boundary; lives in Core.Domain (not Core.AppServices) so Infra.Database can implement it without violating ADR 0001 (same rationale as IUserSessionProvider).

Core.AppServices.Abstractions:
- `IAdminAuditableCommand` — marker for admin write commands; carries ActionCode/TargetEntity/TargetId/Notes metadata.

Core.AppServices.Behaviors:
- `AdminAuditPipelineBehavior<TRequest, TResponse>` — for any `IAdminAuditableCommand`, captures before/after snapshots via `IAdminAuditLogWriter.CaptureSnapshotAsync`, appends entry on handler success. Wrapped inside the surrounding command's transaction (Audit row commits atomically with the state change via UnitOfWorkPipelineBehavior).

Infra.Database:
- `OutboxWriter` — IOutbox impl that Add()s an OutboxEvent inside the surrounding transaction.
- `AdminAuditLogWriter` — IAdminAuditLogWriter impl. Uses reflection on DbContext.Set<T>() + FindAsync to snapshot any registered entity by id; serializes via System.Text.Json with a redaction list (PasswordHash, TokenHash, ApiKey, Secret, SigningKey).
- `OutboxEventConfiguration` (snake_case columns, JSONB payload, partial index `ix_outbox_event_due` filtered on `processed_at IS NULL`).
- `AdminAuditLogEntryConfiguration` (JSONB before/after, three composite indexes for typical query patterns).

Migration `20260523110529_OutboxAndAuditLog`:
- Creates `outbox_event` and `admin_audit_log` tables with indexes.
- Adds Postgres trigger `admin_audit_log_reject_modification` that raises an exception on UPDATE/DELETE — application-layer convention reinforced at the DB layer per ADR 0014.

Pipeline ordering (Makables.Config/AddMakablesMediator):
- Validation → AdminAudit → UnitOfWork. AdminAudit wraps handler + writes audit row; UnitOfWork commits both atomically.

DI wiring (AddMakablesInfrastructure):
- `IOutbox` → `OutboxWriter` (Scoped)
- `IAdminAuditLogWriter` → `AdminAuditLogWriter` (Scoped)

## Out of scope

- Concurrent-safety integration test for outbox processing against Testcontainers Postgres (deferred to ProcessOutboxFunction in T-0020+).
- The DB trigger isn't exercised in tests (SQLite doesn't have plpgsql); will be verified in the Testcontainers Postgres test in T-0020+.
- Concrete admin commands implementing IAdminAuditableCommand — those land in Phase 5 admin tickets.

## Acceptance criteria

- **AC-1** Build clean (0 warnings, 0 errors).
- **AC-2** ≥10 new tests pass.
- **AC-3** OutboxEvent state machine correct: Enqueue → Optional RecordFailure(retry) → MarkProcessed; OR Enqueue → RecordFailure(Permanent, nextRetryAt=null) → Acknowledge.
- **AC-4** AdminAuditLogEntry rejects missing required fields; allows null BeforeJson (create commands) and AfterJson.
- **AC-5** Migration creates both tables with the append-only trigger on admin_audit_log.
- **AC-6** AdminAuditLogWriter redacts PasswordHash / TokenHash / ApiKey / Secret / SigningKey.

## Status log

- 2026-05-23 done. 129 tests passing (109 unit + 20 integration; +15 in T-0011: 8 OutboxEvent + 7 AdminAuditLogEntry).
