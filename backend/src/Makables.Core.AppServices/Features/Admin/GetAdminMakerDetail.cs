using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Admin;
using Makables.Core.Domain.Common;
using MediatR;

namespace Makables.Core.AppServices.Features.Admin;

/// <summary>
/// Single privileged maker header for the admin detail page (T-0119b).
/// Ignores the soft-delete filter (a deactivated maker's detail stays
/// reachable — that's where the admin confirms the deactivation).
/// Unknown id → <c>maker.notFound</c>. The controller writes the
/// T-0137 PII read-audit row on the successful path (the DTO carries
/// the account email).
/// </summary>
public static class GetAdminMakerDetail
{
    public sealed record Query(string MakerId) : IQuery<GetAdminMakerDetailResponse>;

    /// <summary>Globally-unique name (NSwag convention).</summary>
    public sealed record GetAdminMakerDetailResponse(AdminMakerDetailDto Maker);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.MakerId)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(IAdminQueries adminQueries)
        : IRequestHandler<Query, BusinessResult<GetAdminMakerDetailResponse>>
    {
        public async Task<BusinessResult<GetAdminMakerDetailResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var maker = await adminQueries.GetMakerDetailAsync(query.MakerId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<GetAdminMakerDetailResponse>(
                    Error.NotFound("makerId", BusinessErrorMessage.MakerNotFound));
            }

            return BusinessResult.Success(new GetAdminMakerDetailResponse(maker));
        }
    }
}
