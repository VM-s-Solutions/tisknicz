using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Catalog;
using Makables.Core.Domain.Common;
using MediatR;

namespace Makables.Core.AppServices.Features.Catalog;

/// <summary>
/// Public maker-profile page query (US-customer-0008). Anonymous.
/// Returns the maker header + active products (+ empty reviews until
/// T-0050). A slug that doesn't resolve to a publicly-listable maker
/// is a <see cref="BusinessErrorMessage.MakerNotFound"/> — the same
/// shape an inactive / unconfirmed maker produces, so the existence of
/// a hidden maker isn't probeable by slug.
/// </summary>
public static class GetMakerBySlug
{
    public sealed record Query(string Slug) : IQuery<MakerProfile>;

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.Slug)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(120).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(ICatalogQueries catalog)
        : IRequestHandler<Query, BusinessResult<MakerProfile>>
    {
        public async Task<BusinessResult<MakerProfile>> Handle(Query query, CancellationToken cancellationToken)
        {
            var profile = await catalog.GetMakerBySlugAsync(query.Slug, cancellationToken);
            if (profile is null)
            {
                return BusinessResult.Failure<MakerProfile>(Error.NotFound("maker"));
            }

            return BusinessResult.Success(profile);
        }
    }
}
