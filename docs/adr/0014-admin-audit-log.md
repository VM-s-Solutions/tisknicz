---
id: 0014
title: Admin audit log — append-only record of every admin write
status: accepted
date: 2026-05-21
deciders: [Architect, user]
---

# 0014 — Admin audit log

## Context

Two admins (you + one assistant per ADR 0001 personas) share the same `admin` role at launch. They need to see each other's actions to coordinate; the platform needs an immutable trail for dispute resolution, fraud forensics, and (eventually) compliance audits.

`Auditable.UpdatedBy/On` records *who last touched* a row but loses the history before that. We need the full trail.

## Decision

### Append-only `admin_audit_log` table

```sql
CREATE TABLE admin_audit_log (
  id TEXT PRIMARY KEY,                       -- ULID
  admin_user_id TEXT NOT NULL,
  action_code TEXT NOT NULL,                 -- e.g. "maker.verify", "order.refund", "country.updateVat"
  target_entity TEXT NOT NULL,               -- e.g. "maker", "order", "country_configuration"
  target_id TEXT NOT NULL,
  before_json JSONB,                         -- pre-change snapshot (null for create actions)
  after_json JSONB,                          -- post-change snapshot (null for delete actions)
  notes TEXT,                                -- optional free-form note the admin types
  ip_address INET,
  user_agent TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_admin_audit_log_admin_user_id ON admin_audit_log(admin_user_id, created_at DESC);
CREATE INDEX idx_admin_audit_log_target ON admin_audit_log(target_entity, target_id, created_at DESC);
CREATE INDEX idx_admin_audit_log_action ON admin_audit_log(action_code, created_at DESC);
```

**Append-only:** no `UPDATE` or `DELETE` SQL from application code. Enforced by a Postgres trigger that raises an exception on UPDATE/DELETE attempts (operationally bulletproof, not just a code convention).

### Every admin write goes through the audit pipeline behavior

A new MediatR pipeline behavior `AdminAuditPipelineBehavior` runs for any command marked with the `IAdminAuditableCommand` interface. It:
1. Captures the `before_json` snapshot of the target entity by re-running a fetch query.
2. Executes the handler.
3. If successful: captures `after_json`, writes the `admin_audit_log` row, commits everything atomically in the surrounding `UnitOfWorkPipelineBehavior` transaction.

```csharp
public interface IAdminAuditableCommand : ICommand
{
    string ActionCode { get; }            // e.g. "maker.verify"
    string TargetEntity { get; }
    string TargetId { get; }
    string? Notes { get; }
}

// Or for commands with a response:
public interface IAdminAuditableCommand<TResponse> : ICommand<TResponse>
{
    string ActionCode { get; }
    string TargetEntity { get; }
    string TargetId { get; }
    string? Notes { get; }
}
```

The interface is declarative — the command type's properties supply the metadata. Example:

```csharp
public record Command(string MakerId, string? Notes) : IAdminAuditableCommand<Response>
{
    public string ActionCode    => "maker.verify";
    public string TargetEntity  => "maker";
    public string TargetId      => MakerId;
}
```

### Which commands are audited

Every command running against `Web.Admin` that **writes** anything. Concretely (Batch 4+ commands will conform):
- `VerifyMaker.Command`
- `DeactivateMaker.Command`
- `UpdateCountryConfiguration.Command`
- `CreatePayoutBatch.Command`
- `RefundOrder.Command`
- `ChangeOrderStateManually.Command`
- `DeleteUserPermanently.Command` (GDPR)
- `UpdateCategory.Command`
- `ApproveDispute.Command` / `RejectDispute.Command`

**Not audited:**
- Admin reads (list endpoints, queries) — not writes; would flood the table.
- System-initiated changes (cron auto-deliver, webhook state transitions) — these are tracked by `Auditable.UpdatedBy = "system"`. Cron actions get a separate `system_event_log` if/when needed (post-MVP).

### `before_json` / `after_json` shape

JSONB snapshots are the **serialized entity** at the moment of capture. Not a diff (computed on-the-fly when displayed in admin UI). Reasons:
- Schemas evolve; storing the full snapshot at the time of the action means we don't need to back-fill or interpret old columns later.
- JSONB compresses well in Postgres; storage cost is manageable.

Sensitive fields (`PasswordHash`, `RefreshToken.TokenHash`) are redacted before serialization by an `AuditSerializer` that knows the entity schema. Reviewer audits the serializer's redact list against new sensitive fields.

### Admin UI surface

Admin dashboard exposes:
- **Recent activity feed** — last 50 entries across all admins, sorted by `created_at DESC`.
- **Per-target history** — on a maker / order / country detail page, a "History" tab shows every audit log entry for that target.
- **Per-admin activity** — view what a specific admin did in a date range.

The UI never offers an "edit" or "delete" button on audit log entries.

### Multi-country

Audit log entries are not country-scoped at the table level (the `admin_audit_log` table is global). The target entity carries its own country code; admin filtering by country queries `JOIN target_entity ON target_id`. Acceptable for a single-tenant admin team.

If a future per-country admin team appears, we'd scope `admin_audit_log` by adding `country_code` to the entry — backward-compatible migration.

## Alternatives considered

- **Postgres `CREATE TRIGGER` on every audited entity to write audit rows** — rejected. Triggers are hard to test, hard to discover, and tightly couple the schema to audit. Application-layer is more visible.
- **Event-sourcing all admin actions (full event stream)** — overkill for MVP. The audit log is a side record, not the system of record.
- **Log to a SIEM (Splunk, Datadog Logs) instead of a table** — rejected. SIEM is for ops events; admin audit needs to be queryable in the admin UI and survive log retention rotation.
- **Skip the JSONB snapshot; record only `action_code` + `target_id`** — rejected. The whole point is forensics; "I verified this maker" without seeing the maker's state at the time is much weaker evidence.
- **Audit only "high-stakes" actions (payouts, refunds, config)** — rejected by user. Auditing everything is the safe default; the table size is not a concern for MVP scale.

## Consequences

### Positive
- Forensics: every admin action is reconstructible.
- Coordination: admins see each other's recent activity in the dashboard.
- Compliance-ready for future audits (GDPR data-controller obligations, accountant queries).
- The trigger-enforced append-only constraint is bulletproof — even if application code is buggy, the DB rejects UPDATE/DELETE.

### Negative
- Storage cost: JSONB snapshots accumulate. At MVP scale (~10 admin writes/day) this is < 100 MB/year. Bounded.
- The `AdminAuditPipelineBehavior` adds latency to admin writes (an extra query to fetch the before-state). Acceptable: admin writes are rare and latency-tolerant.
- Developers must remember to mark commands `IAdminAuditableCommand`. Mitigated by Reviewer: any command in `Features/` invoked from `Web.Admin` controller must implement the interface.

## Compliance / verification

- DB constraint: trigger rejects UPDATE/DELETE on `admin_audit_log`.
- Reviewer checklist: every admin-host write command implements `IAdminAuditableCommand`.
- Reviewer checklist: `AuditSerializer` redact list updated when new sensitive fields are added.
- Integration test: an admin command produces a log row with correct `before_json`/`after_json`.
- Integration test: attempting UPDATE/DELETE on `admin_audit_log` from the application raises an exception.

## Related
- Patterns: §A.5 MediatR pipeline behaviors, §A.11 Auditable
- ADR 0007 — Stack pivot
- ADR 0013 — Data scoping and soft delete (hard-delete `DeleteUserPermanently` is audited)
