using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Products;
using MediatR;
using DomainMoney = Makables.Core.Domain.Money.Money;

namespace Makables.Core.AppServices.Features.Products;

/// <summary>
/// Maker creates a product (US-maker-0004 AC-1). The product is
/// initially active and immediately visible in the public catalog
/// (subject to the maker being verified + active — the catalog query
/// joins on those).
///
/// <para>
/// The owning maker is resolved from
/// <see cref="IUserSessionProvider.GetUserId"/> — there is NO makerId
/// in the command shape (IDOR shield, same pattern as T-0034
/// UpdateMakerProfile). Currency is NOT caller-supplied: it's read
/// from the maker's tenant <c>CountryConfiguration.DefaultCurrencyCode</c>
/// so a product can't be priced in a currency the platform doesn't
/// settle in (role/product.md invariant).
/// </para>
///
/// <para>
/// Images are NOT part of Create — the maker uploads them after via
/// <see cref="AddProductImage"/> (the multipart upload returns the
/// blob path, then AddImage records it). Create returns the new id so
/// the frontend can immediately POST images to it.
/// </para>
/// </summary>
public static class CreateProduct
{
    public sealed record Command(
        string CategoryId,
        string Title,
        string? Description,
        long PriceAmountMinor,
        PriceType PriceType,
        int WeightGrams)
        : ICommand<Response>;

    public sealed record Response(string Id);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.CategoryId)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.Title)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(Product.MaxTitleLength).WithErrorCode(BusinessErrorMessage.MaxLength);

            When(c => c.Description is not null, () =>
            {
                RuleFor(c => c.Description!)
                    .MaximumLength(Product.MaxDescriptionLength).WithErrorCode(BusinessErrorMessage.MaxLength);
            });

            RuleFor(c => c.PriceAmountMinor)
                .GreaterThanOrEqualTo(0).WithErrorCode(BusinessErrorMessage.ProductPriceNegative);

            RuleFor(c => c.WeightGrams)
                .GreaterThanOrEqualTo(0).WithErrorCode(BusinessErrorMessage.Required);
        }
    }

    public sealed class Handler(
        IProductRepository products,
        IMakerRepository makers,
        ICategoryRepository categories,
        ICountryConfigurationRepository countryConfig,
        IUserSessionProvider session,
        IIdGenerator ids)
        : IRequestHandler<Command, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<Response>(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<Response>(Error.NotFound("maker"));
            }

            // Category must exist + be active (the global query filter
            // hides soft-deleted categories, so GetByIdAsync returns null
            // for a deactivated one — surface as NotActive, since the
            // category id IS a known value the maker just couldn't pick).
            var category = await categories.GetByIdAsync(command.CategoryId, cancellationToken);
            if (category is null)
            {
                return BusinessResult.Failure<Response>(
                    Error.Validation("categoryId", BusinessErrorMessage.CategoryNotActive));
            }

            // Currency from the maker's tenant config — never caller-supplied.
            var config = await countryConfig.GetByCodeAsync(maker.CountryCode, cancellationToken);
            if (config is null)
            {
                return BusinessResult.Failure<Response>(
                    Error.Configuration(BusinessErrorMessage.CountryConfigMissing));
            }

            // Free-product invariant is enforced by the entity; surface
            // it as a typed validation error instead of letting the
            // ArgumentException bubble.
            if (command.PriceAmountMinor == 0 && command.PriceType != PriceType.OnRequest)
            {
                return BusinessResult.Failure<Response>(
                    Error.Validation("priceType", BusinessErrorMessage.ProductFreeRequiresOnRequest));
            }

            var product = Product.Create(
                id: ids.Next(),
                makerId: maker.Id,
                categoryId: command.CategoryId,
                title: command.Title,
                description: command.Description,
                price: new DomainMoney(command.PriceAmountMinor, config.DefaultCurrencyCode),
                priceType: command.PriceType,
                weightGrams: command.WeightGrams,
                countryCode: maker.CountryCode);

            products.Add(product);

            return BusinessResult.Success(new Response(product.Id));
        }
    }
}
