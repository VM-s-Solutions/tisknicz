using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Admin;
using Makables.Core.Domain.Common;
using MediatR;

namespace Makables.Core.AppServices.Features.Admin;

/// <summary>
/// Paged cross-maker payout-batch list (T-0127 / Q-0029 / US-admin-0007).
/// <c>GET /api/v1/payout-batches</c> → a <c>PagedData</c> of
/// <see cref="AdminPayoutBatchListItemDto"/> (batch id, number, state,
/// total, order/maker counts, created/completed timestamps), AsNoTracking +
/// cross-maker (the existing <c>POST /payout-batches</c> is
/// <c>CreatePayoutBatch</c> — the GET is the list; the verb disambiguates).
/// The payout page browses Processing/Completed batches and
/// completes / downloads CSV by VISIBLE id, replacing the T-0118c count +
/// by-id surface. Read-only; no audit row. <c>[Authorize]</c> admin
/// audience (ADR 0013).
/// </summary>
public static class GetPayoutBatches
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public sealed record Query(int Page, int PageSize) : IQuery<GetPayoutBatchesResponse>;

    /// <summary>Globally-unique name (NSwag PR #38 convention).</summary>
    public sealed record GetPayoutBatchesResponse(PagedData<AdminPayoutBatchListItemDto> Batches);

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
        }
    }

    public sealed class Handler(IAdminQueries adminQueries)
        : IRequestHandler<Query, BusinessResult<GetPayoutBatchesResponse>>
    {
        public async Task<BusinessResult<GetPayoutBatchesResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var page = await adminQueries.GetPayoutBatchesPagedAsync(
                query.Page, query.PageSize, cancellationToken);

            return BusinessResult.Success(new GetPayoutBatchesResponse(page));
        }
    }
}
