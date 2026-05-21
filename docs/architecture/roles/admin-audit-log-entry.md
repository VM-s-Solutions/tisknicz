---
role: AdminAuditLogEntry
kind: aggregate
status: accepted
---

# AdminAuditLogEntry

## Responsibility

Capture a single admin write action with a before/after snapshot of the affected entity, for forensic and coordination purposes.

## Collaborators

- **User** (the admin who acted; `admin_user_id`)
- (Any aggregate; the target entity)
- **`AdminAuditPipelineBehavior`** (creates rows automatically)

## Knows

- `AdminUserId`, `ActionCode` (dot-notation, e.g. `maker.verify`)
- `TargetEntity`, `TargetId`
- `BeforeJson`, `AfterJson` (with sensitive fields redacted)
- `Notes` (optional admin-typed string)
- `IpAddress`, `UserAgent`, `CreatedAt`

## Does NOT know

- How the action was triggered (UI vs API direct)
- The semantic significance of the action (it's just a record)

## Lifecycle

- **Created by:** `AdminAuditPipelineBehavior` automatically for every command implementing `IAdminAuditableCommand`
- **Modified by:** never (DB trigger rejects UPDATE)
- **Persisted by:** `IAdminAuditLogRepository`
- **Destroyed by:** never (DB trigger rejects DELETE)

## Invariants

- Append-only (DB-enforced).
- Sensitive fields redacted at write time via `AuditSerializer`.
- One row per admin write command.

## Implementation pointer

`backend/src/Makables.Core.Domain/Auditing/AdminAuditLogEntry.cs`. Pipeline: `backend/src/Makables.Core.AppServices/Behaviors/AdminAuditPipelineBehavior.cs`.

## Related

- ADRs: 0014 (this role's defining ADR)
- Roles: `user`
