---
id: 0013
title: Data scoping — EF Core global query filters for soft-delete; application-layer country and ownership scoping
status: accepted
date: 2026-05-21
deciders: [Architect, user]
---

# 0013 — Data scoping and soft delete

## Context

Supabase's RLS was a tempting "defense in depth" boundary that enforced ownership and tenancy at the DB layer. Post-pivot (ADR 0007), the .NET backend is the only writer; the DB is private. We need to replicate the *guarantees* RLS provided — without enabling Postgres RLS, which would add operational complexity for marginal benefit when only one app talks to the DB.

The two guarantees we need:
1. **Soft delete** is the default. A "deleted" row stays in the DB (audit + GDPR considerations) but is excluded from queries by default.
2. **Ownership and country scoping** — a customer query never returns another customer's data; a maker query never returns another maker's products; cross-country queries are explicit.

## Decision

### Soft delete via EF Core global query filter

Every `Auditable` entity gets an automatic global query filter on `IsActive`:

```csharp
// MakablesDbContext.OnModelCreating
foreach (var entityType in modelBuilder.Model.GetEntityTypes())
{
    if (typeof(Auditable).IsAssignableFrom(entityType.ClrType))
    {
        var param = Expression.Parameter(entityType.ClrType, "e");
        var isActiveProp = Expression.Property(param, nameof(Auditable.IsActive));
        var filter = Expression.Lambda(isActiveProp, param);
        modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
    }
}
```

A "deletion" calls the entity's `MarkDeactivated(by, at)` method (sets `IsActive=false`, populates `DeactivatedBy/At`). Hard delete is reserved for GDPR "right to erasure" requests, executed by a dedicated admin command that bypasses query filters explicitly.

### Country and ownership scoping at the application layer

We do **not** use a global query filter for country or ownership. Reasons:
- Country scoping is contextual: an admin querying all CZ orders is legitimate; a customer querying all CZ orders is not. A single global filter can't capture this.
- EF Core global filters that depend on a service (`IUserSessionProvider`) tightly couple the DbContext to request scope and produce surprising behavior in background jobs.

Instead:
- Repositories that return user-scoped data require the user/maker id as a parameter (or read it from `IUserSessionProvider` injected into the repository).
- Repositories that return country-scoped data accept a `countryCode` parameter or use the user's `CountryCodePrimary` claim.
- Specifications (`patterns.md §A.8`) carry the scoping in the predicate.
- Controllers must not call generic "give me everything" repository methods; they must call scoped methods.

Example:

```csharp
public interface IOrderRepository
{
    // Customer scope — caller supplies userId, often from IUserSessionProvider
    IQueryable<Order> ForCustomer(string customerUserId);

    // Maker scope
    IQueryable<Order> ForMaker(string makerId);

    // Admin only — no scoping. Method name signals the privilege.
    IQueryable<Order> Unscoped();
}
```

`Unscoped()` is callable only from `Web.Admin` controllers (enforced by Reviewer; we don't add a runtime guard because that would be an additional code surface to maintain).

### `IgnoreQueryFilters` for legitimate exceptions

Admin queries that need to see soft-deleted rows (audit trail, GDPR reconciliation) call `.IgnoreQueryFilters()` explicitly. Every such call site is documented with a comment explaining why.

### Hard delete (GDPR)

A single admin command: `DeleteUserPermanently.Command(userId, reason)` calls a dedicated `IUserDataDeletionService` that:
1. Anonymizes related orders/invoices (replace customer PII with placeholders; legal retention requires the order itself stays).
2. Hard-deletes the `User` row.
3. Hard-deletes related addresses if no other entity references them.
4. Hard-deletes refresh tokens.
5. Writes an `admin_audit_log` entry recording the deletion.

This service is the **only** place EF Core hard-delete (`Remove()` + commit) is called for user data. Reviewer enforces.

### Defense in depth

Even though we don't use Postgres RLS, we have three layers of authorization:
1. **JWT audience + role** validated by middleware before the request reaches a controller.
2. **`[Authorize]` attribute** on controllers/actions with policy specifying the required role.
3. **Repository scoping** — `ForCustomer(userId)` / `ForMaker(makerId)` / `Unscoped()` — surfaces in code review when someone tries to escape scope.

We considered Postgres RLS as a fourth layer; rejected because:
- Single-writer architecture means application-layer enforcement is sufficient.
- RLS policies are operationally awkward (they apply to migrations too; need to be disabled for seed data; service-role bypass adds another credential to manage).
- The Cleansia precedent is application-layer scoping; it has shipped successfully.

If we ever add a second writer (e.g. a data-science read replica that lets analysts query directly), we add RLS at that point in a superseding ADR.

## Alternatives considered

- **Enable Postgres RLS in addition to application-layer scoping** — rejected. Marginal benefit; significant complexity. Reconsider if a second writer appears.
- **Single global query filter for country (using `IUserSessionProvider`)** — rejected. Breaks in background jobs where there's no user context.
- **Soft-delete via a `deleted` discriminator column instead of `is_active`** — rejected. `Auditable.IsActive` is already present and serves the same purpose.
- **Hard delete by default; archive table for "deleted" rows** — rejected. Complicates queries (UNION across two tables) and audit trails.

## Consequences

### Positive
- Soft delete is automatic — no risk of forgetting the filter in a new query.
- Ownership and country scoping is **visible in code**: the method name (`ForCustomer`, `ForMaker`, `Unscoped`) reveals intent in every call site.
- GDPR hard delete is a single, audited code path.

### Negative
- No DB-layer guard against a bug in application scoping. Mitigated by Reviewer checklist + integration tests that verify cross-tenant reads fail.
- `IgnoreQueryFilters()` is a footgun. Mitigated by Reviewer checking every call site.
- New developers must internalize "soft-delete is the default" — a `DELETE` SQL statement bypasses the filter and is a code smell. Mitigated by EF Core's `Remove()` API being the only blessed path, and `Remove()` triggers `Deactivated()` for `Auditable` entities (via the `AuditableSaveChangesInterceptor`).

## Compliance / verification

- Reviewer checklist: every new query targeting `Auditable` entities relies on the soft-delete filter or uses `.IgnoreQueryFilters()` with a comment.
- Reviewer checklist: every controller endpoint calls a scoped repository method (`ForCustomer` / `ForMaker` / `Unscoped` from admin host only).
- Reviewer checklist: `Unscoped()` is only called from `Web.Admin` controllers.
- Integration test: customer A's order list does not include customer B's orders.
- Integration test: maker A cannot fetch maker B's products even by guessing product IDs.
- Integration test: hard delete is callable only via `DeleteUserPermanently` command, only by admin role.

## Related
- Patterns: §A.11 Auditable, §A.19 EF Core query filters
- ADR 0007 — Stack pivot (RLS dropped with Supabase)
- ADR 0014 (next) — Admin audit log
