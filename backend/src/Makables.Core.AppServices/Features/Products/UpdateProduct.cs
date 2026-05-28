using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Products;
using MediatR;
using DomainMoney = Makables.Core.Domain.Money.Money;

namespace Makables.Core.AppServices.Features.Products;

/// <summary>
/// Maker edits a product (US-maker-0004 AC-3). Existing orders are
/// unaffected — they hold the pricing snapshot from order time.
///
/// <para>
/// <b>Ownership / IDOR.</b> The handler loads the product by id (from
/// the request) but then verifies <c>product.MakerId</c> matches the
/// maker resolved from the session. A mismatch returns NotFound (not
/// Forbidden) so a maker can't probe which product ids exist on the
/// platform. Currency is immutable after creation — the command can't
/// change it.
/// </para>
/// </summary>
public static class UpdateProduct
{
    public sealed record Command(
        string ProductId,
        string CategoryId,
        string Title,
        string? Description,
        long PriceAmountMinor,
        PriceType PriceType,
        int WeightGrams)
        : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.ProductId)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

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
        IUserSessionProvider session)
        : IRequestHandler<Command, BusinessResult>
    {
        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure(Error.NotFound("maker"));
            }

            var product = await products.GetByIdAsync(command.ProductId, cancellationToken);
            // IDOR shield: a product owned by another maker is reported as
            // NotFound so ids aren't enumerable across makers.
            if (product is null || product.MakerId != maker.Id)
            {
                return BusinessResult.Failure(Error.NotFound("product"));
            }

            var category = await categories.GetByIdAsync(command.CategoryId, cancellationToken);
            if (category is null)
            {
                return BusinessResult.Failure(
                    Error.Validation("categoryId", BusinessErrorMessage.CategoryNotActive));
            }

            if (command.PriceAmountMinor == 0 && command.PriceType != PriceType.OnRequest)
            {
                return BusinessResult.Failure(
                    Error.Validation("priceType", BusinessErrorMessage.ProductFreeRequiresOnRequest));
            }

            product.Update(
                categoryId: command.CategoryId,
                title: command.Title,
                description: command.Description,
                price: new DomainMoney(command.PriceAmountMinor, product.PriceCurrency),
                priceType: command.PriceType,
                weightGrams: command.WeightGrams);

            return BusinessResult.Success();
        }
    }
}
