using System.Text.Json;
using Makables.Core.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Makables.Infra.Database.Auditing;

/// <summary>
/// Persists <see cref="AdminAuditLogEntry"/> rows and captures
/// before/after JSONB snapshots from the EF Core change tracker.
/// Per ADR 0014.
///
/// Snapshot strategy: rely on EF Core's <see cref="ChangeTracker"/> to
/// return the entity by id from any registered DbSet, serialize it via
/// <c>System.Text.Json</c>, and pass through a redaction list. The
/// redaction list lives next to this class; new sensitive fields must be
/// added to <see cref="RedactedProperties"/>.
/// </summary>
public sealed class AdminAuditLogWriter(MakablesDbContext db)
    : Makables.Core.Domain.Auditing.IAdminAuditLogWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Property names that must NEVER appear in audit log snapshots.
    /// Comparison is case-insensitive. Add new entries when introducing
    /// new sensitive fields.
    /// </summary>
    private static readonly HashSet<string> RedactedProperties =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "PasswordHash",
            "TokenHash",
            "ApiKey",
            "Secret",
            "SigningKey",
        };

    public async Task<string?> CaptureSnapshotAsync(
        string targetEntity,
        string targetId,
        CancellationToken cancellationToken)
    {
        var entityType = db.Model
            .GetEntityTypes()
            .FirstOrDefault(t =>
                string.Equals(t.ShortName(), targetEntity, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.GetTableName(), targetEntity, StringComparison.OrdinalIgnoreCase));

        if (entityType is null)
        {
            return null;
        }

        var clrType = entityType.ClrType;

        // Use Set<TEntity>() dynamically via reflection so we can target
        // any entity type without compile-time generics.
        var setMethod = typeof(MakablesDbContext)
            .GetMethods()
            .First(m => m.Name == "Set" && m.IsGenericMethod && m.GetParameters().Length == 0)
            .MakeGenericMethod(clrType);

        var dbSet = setMethod.Invoke(db, null);
        if (dbSet is null) return null;

        var findAsyncMethod = dbSet.GetType()
            .GetMethod("FindAsync", new[] { typeof(object[]), typeof(CancellationToken) });
        if (findAsyncMethod is null) return null;

        var task = findAsyncMethod.Invoke(dbSet, new object?[] { new object[] { targetId }, cancellationToken });
        if (task is null) return null;

        // task is ValueTask<TEntity?>; await dynamically.
        await ((dynamic)task!).ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result");
        var entity = resultProperty?.GetValue(task);

        if (entity is null)
        {
            return null;
        }

        return SerializeWithRedaction(entity);
    }

    public Task AppendAsync(AdminAuditLogEntry entry, CancellationToken cancellationToken)
    {
        db.Set<AdminAuditLogEntry>().Add(entry);
        return Task.CompletedTask;
    }

    private static string SerializeWithRedaction(object entity)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in entity.GetType().GetProperties())
        {
            if (RedactedProperties.Contains(prop.Name))
            {
                dict[prop.Name] = "***REDACTED***";
                continue;
            }
            try
            {
                dict[prop.Name] = prop.GetValue(entity);
            }
            catch
            {
                // Navigation properties may throw on lazy load. Skip.
            }
        }
        return JsonSerializer.Serialize(dict, JsonOptions);
    }
}
