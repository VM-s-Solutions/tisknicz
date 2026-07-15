using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Admin;
using Makables.Core.Domain.Common;
using MediatR;

namespace Makables.Core.AppServices.Features.Admin;

/// <summary>
/// Admin cross-tenant maker list (T-0119b / US-admin-0003..0005).
/// Read-only, ignores the soft-delete filter (deactivated makers stay
/// browsable). Privileged row carries the account email (T-0111
/// precedent — no GDPR redaction for admin). Filters: one search term
/// (company partial / exact IČO) + verification flag, no more (Q-E
/// minimalism). No audit row (ADR 0014 audits writes; the LIST is
/// low-forensic-value per the T-0137 scope).
/// </summary>
public static class GetAdminMakers
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public sealed record Query(
        int Page,
        int PageSize,
        string? Search,
        bool? IsVerified) : IQuery<GetAdminMakersResponse>;

    /// <summary>Globally-unique name (NSwag convention).</summary>
    public sealed record GetAdminMakersResponse(PagedData<AdminMakerListItemDto> Makers);

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

            When(q => !string.IsNullOrEmpty(q.Search), () =>
                RuleFor(q => q.Search!)
                    .MaximumLength(200)
                    .WithErrorCode(BusinessErrorMessage.MaxLength));
        }
    }

    public sealed class Handler(IAdminQueries adminQueries)
        : IRequestHandler<Query, BusinessResult<GetAdminMakersResponse>>
    {
        public async Task<BusinessResult<GetAdminMakersResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var page = await adminQueries.GetAllMakersPagedAsync(
                new AdminMakerFilter(query.Search, query.IsVerified),
                query.Page, query.PageSize, cancellationToken);

            return BusinessResult.Success(new GetAdminMakersResponse(page));
        }
    }
}
