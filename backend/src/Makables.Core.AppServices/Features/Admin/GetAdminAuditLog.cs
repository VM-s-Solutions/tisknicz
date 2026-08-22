using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Admin;
using Makables.Core.Domain.Common;
using MediatR;

namespace Makables.Core.AppServices.Features.Admin;

/// <summary>
/// Admin audit-log list (T-0111 / US-admin-0015). Read-only over the
/// append-only <c>AdminAuditLogEntry</c> set. Filters: adminUserId,
/// actionCode, targetEntity, date range (Q-E, no more). The list DTO omits
/// before/after JSONB (detail-page concern). No audit row for reads.
/// </summary>
public static class GetAdminAuditLog
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public sealed record Query(
        int Page,
        int PageSize,
        string? AdminUserId,
        string? ActionCode,
        string? TargetEntity,
        DateTimeOffset? DateFrom,
        DateTimeOffset? DateTo,
        /// <summary>Scopes the log to ONE entity (T-0177, audit ADM-H2).</summary>
        string? TargetId = null) : IQuery<GetAdminAuditLogResponse>;

    /// <summary>Globally-unique name (NSwag PR #38 convention).</summary>
    public sealed record GetAdminAuditLogResponse(PagedData<AdminAuditLogItemDto> Entries);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.Page)
                .Cascade(CascadeMode.Stop)
                .InclusiveBetween(1, int.MaxValue / MaxPageSize)
                .WithErrorCode(BusinessErrorMessage.MinValue);

            RuleFor(q => q.PageSize)
                .Cascade(CascadeMode.Stop)
                .InclusiveBetween(1, MaxPageSize)
                .WithErrorCode(BusinessErrorMessage.MinValue);

            RuleFor(q => q.TargetId)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength)
                .When(q => !string.IsNullOrEmpty(q.TargetId));

            When(q => q.DateFrom.HasValue && q.DateTo.HasValue, () =>
                RuleFor(q => q.DateFrom!.Value)
                    .LessThanOrEqualTo(q => q.DateTo!.Value)
                    .WithErrorCode(BusinessErrorMessage.MinValue));
        }
    }

    public sealed class Handler(IAdminQueries adminQueries)
        : IRequestHandler<Query, BusinessResult<GetAdminAuditLogResponse>>
    {
        public async Task<BusinessResult<GetAdminAuditLogResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var filter = new AdminAuditLogFilter(
                query.AdminUserId, query.ActionCode, query.TargetEntity,
                query.DateFrom, query.DateTo, query.TargetId);

            var page = await adminQueries.GetAdminAuditLogPagedAsync(
                filter, query.Page, query.PageSize, cancellationToken);

            return BusinessResult.Success(new GetAdminAuditLogResponse(page));
        }
    }
}
