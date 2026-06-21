namespace Makables.Core.Domain.Auditing;

/// <summary>
/// Read-side audit boundary (T-0137 / Q-0028). Records a privileged admin
/// READ of customer PII as an <see cref="AdminAuditLogEntry"/> row.
///
/// <para>
/// Distinct from <see cref="IAdminAuditLogWriter"/> (the command-side seam,
/// whose <c>AppendAsync</c> only <c>.Add()</c>s and relies on the
/// UnitOfWork pipeline behavior to commit). A read has no write transaction:
/// the implementation owns its OWN <c>DbContext</c> scope (via
/// <c>IDbContextFactory&lt;MakablesDbContext&gt;</c>, the T-0032 ARES-cache
/// precedent) and commits the single audit row in one self-contained
/// <c>SaveChangesAsync</c> — so a pure read never opens the request-scoped
/// UoW and never calls <c>SaveChangesAsync</c> inside a handler (the
/// no-SaveChanges-in-handlers rule stays intact).
/// </para>
///
/// <para>
/// Reads have no before/after state delta, so the entry carries
/// <c>beforeJson = afterJson = null</c>. Per the ADR 0014 read-side
/// carve-out amendment (T-0137 / Q-0028) this is scoped to the high-signal
/// single-record / file-download reads (invoice-PDF, payout CSV, order
/// detail) — NOT the high-volume paginated list reads, which stay un-audited
/// ("would flood the table").
/// </para>
/// </summary>
public interface IAdminReadAuditWriter
{
    /// <summary>
    /// Append an audit row for a successful privileged PII read and commit it
    /// on a dedicated DbContext. The actor is resolved from the current admin
    /// session; the caller supplies the request IP / user-agent (HTTP context
    /// is a web concern that does not belong in <c>Infra.Database</c>).
    /// </summary>
    /// <param name="actionCode">Dot-notation read action, e.g. <c>invoice.pdf.download</c>.</param>
    /// <param name="targetEntity">Lowercase entity name, e.g. <c>invoice</c>.</param>
    /// <param name="targetId">The id of the entity read.</param>
    /// <param name="ipAddress">Caller remote IP (nullable).</param>
    /// <param name="userAgent">Caller user-agent (nullable).</param>
    /// <param name="notes">Optional free-text note (nullable).</param>
    Task AuditReadAsync(
        string actionCode,
        string targetEntity,
        string targetId,
        string? ipAddress,
        string? userAgent,
        string? notes,
        CancellationToken cancellationToken);
}
