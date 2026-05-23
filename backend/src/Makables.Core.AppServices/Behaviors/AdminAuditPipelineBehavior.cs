using MediatR;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Common;

namespace Makables.Core.AppServices.Behaviors;

/// <summary>
/// MediatR pipeline behavior that, for any command implementing
/// <see cref="IAdminAuditableCommand"/>, captures a before/after JSONB
/// snapshot of the target and inserts an <see cref="AdminAuditLogEntry"/>
/// after the handler returns successfully. Per ADR 0014 / patterns §A.11.
///
/// The behavior depends on an <see cref="IAdminAuditLogWriter"/>
/// (implemented in <c>Infra.Database</c>) that knows how to persist log
/// entries and snapshot entities by id.
///
/// Wraps ALL request shapes derived from <see cref="BusinessResult"/>;
/// the inner guard ignores non-admin-auditable requests.
/// </summary>
public sealed class AdminAuditPipelineBehavior<TRequest, TResponse>(
    IAdminAuditLogWriter writer,
    IUserSessionProvider session,
    IClock clock,
    IIdGenerator idGenerator)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : BusinessResult
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IAdminAuditableCommand auditable)
        {
            return await next(cancellationToken);
        }

        // Capture before-state via the writer (it has DbContext access).
        var beforeJson = await writer.CaptureSnapshotAsync(
            auditable.TargetEntity,
            auditable.TargetId,
            cancellationToken);

        var response = await next(cancellationToken);

        if (!response.IsSuccess)
        {
            // Failed commands don't get an audit-log entry. UnitOfWork
            // won't commit anyway; we just skip writing the audit row.
            return response;
        }

        var afterJson = await writer.CaptureSnapshotAsync(
            auditable.TargetEntity,
            auditable.TargetId,
            cancellationToken);

        var adminUserId = session.GetUserId() ?? "system";

        var entry = AdminAuditLogEntry.Record(
            id: idGenerator.Next(),
            adminUserId: adminUserId,
            actionCode: auditable.ActionCode,
            targetEntity: auditable.TargetEntity,
            targetId: auditable.TargetId,
            beforeJson: beforeJson,
            afterJson: afterJson,
            now: clock.UtcNow,
            notes: auditable.Notes);

        await writer.AppendAsync(entry, cancellationToken);

        return response;
    }
}
