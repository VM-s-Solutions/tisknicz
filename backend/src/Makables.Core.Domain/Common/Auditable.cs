namespace Makables.Core.Domain.Common;

/// <summary>
/// Base for every transactional entity. Carries country scope and audit
/// columns (created/updated/deactivated by + when) per ADR 0011 / patterns
/// §A.11. Audit columns are populated automatically by the
/// <c>AuditableSaveChangesInterceptor</c> (added in T-0002) reading from
/// <c>IUserSessionProvider</c> and <c>IClock</c>.
/// </summary>
public abstract class Auditable : BaseEntity
{
    /// <summary>
    /// ISO 3166-1 alpha-2 code. Set at creation, never mutated.
    /// </summary>
    public string CountryCode { get; protected internal set; } = default!;

    public string CreatedBy { get; protected internal set; } = default!;

    public DateTimeOffset CreatedAt { get; protected internal set; } = DateTimeOffset.UtcNow;

    public string? UpdatedBy { get; protected internal set; }

    public DateTimeOffset? UpdatedAt { get; protected internal set; }

    public string? DeactivatedBy { get; protected internal set; }

    public DateTimeOffset? DeactivatedAt { get; protected internal set; }

    /// <summary>
    /// Stamp the audit creation fields. Called by the interceptor on insert;
    /// kept public so domain factories that need to set audit at construction
    /// (e.g. seed data) can do so explicitly.
    /// </summary>
    public Auditable MarkCreated(string createdBy, DateTimeOffset createdAt)
    {
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        return this;
    }

    public Auditable MarkUpdated(string updatedBy, DateTimeOffset updatedAt)
    {
        UpdatedBy = updatedBy;
        UpdatedAt = updatedAt;
        return this;
    }

    /// <summary>
    /// Soft delete. Flips <see cref="BaseEntity.IsActive"/> to false and
    /// stamps the deactivation audit fields. Domain code calls this
    /// (typically via a domain method on the concrete entity); the
    /// interceptor does NOT call it implicitly on EF Remove.
    /// </summary>
    public Auditable MarkDeactivated(string deactivatedBy, DateTimeOffset deactivatedAt)
    {
        DeactivatedBy = deactivatedBy;
        DeactivatedAt = deactivatedAt;
        IsActive = false;
        return this;
    }
}
