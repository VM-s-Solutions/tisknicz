using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Products;
using Makables.Core.Domain.Reviews;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Reviews;

/// <summary>
/// Customer-host write: submit a review for one of the customer's own
/// Delivered/Completed orders (T-0100, US-customer-0015). Per ADR 0013 +
/// T-0079/T-0082 precedent the command is registered on the Customer host
/// ONLY — a maker JWT cannot dispatch it. The scoped order load
/// (<see cref="IOrderRepository.GetByIdForCustomerAsync"/>) is the IDOR
/// shield (cross-tenant → 404, no existence leak).
///
/// <para>
/// <b>Atomic recompute (Q5).</b> The Review insert + the Maker
/// rating recompute commit in ONE UoW (ADR 0014). The handler reads the
/// <c>COUNT</c>/<c>AVG</c> aggregate over the maker's active reviews
/// (including the just-added one) and writes it via
/// <see cref="Maker.RecomputeRating"/> against a row-locked Maker so
/// concurrent submits to the same maker serialize. The handler NEVER
/// calls <c>SaveChangesAsync</c>.
/// </para>
/// </summary>
public static class SubmitReview
{
    public sealed record Command(string OrderId, short Rating, string? Body)
        : ICommand<SubmitReviewResponse>;

    public sealed record SubmitReviewResponse(string ReviewId, short Rating, DateTimeOffset CreatedAt);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.Rating)
                .Cascade(CascadeMode.Stop)
                .InclusiveBetween(Review.MinRating, Review.MaxRating)
                .WithErrorCode(BusinessErrorMessage.ReviewRatingOutOfRange);

            RuleFor(c => c.Body)
                .Cascade(CascadeMode.Stop)
                .MaximumLength(Review.MaxBodyLength)
                .WithErrorCode(BusinessErrorMessage.ReviewBodyTooLong)
                .When(c => c.Body is not null);
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IReviewRepository reviews,
        IMakerRepository makers,
        IProductRepository products,
        IUserSessionProvider session,
        IIdGenerator ids,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BusinessResult<SubmitReviewResponse>>
    {
        public async Task<BusinessResult<SubmitReviewResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: Resolve customer session.
            var customerUserId = session.GetUserId();
            if (string.IsNullOrEmpty(customerUserId))
            {
                return BusinessResult.Failure<SubmitReviewResponse>(Error.Unauthorized());
            }

            // Step 2: IDOR-scoped order load — null for unknown OR foreign
            // orders (no existence oracle). OrderNotFound is reused.
            var order = await orders.GetByIdForCustomerAsync(
                command.OrderId, customerUserId, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<SubmitReviewResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 3: Eligibility gate — Delivered/Completed only (Q3).
            if (!ReviewEligibility.IsReviewableState(order.State))
            {
                return BusinessResult.Failure<SubmitReviewResponse>(
                    Error.Conflict("order", BusinessErrorMessage.ReviewOrderNotDelivered));
            }

            // Step 4: One-review-per-order guard (the partial unique index
            // is the hard backstop for the concurrent race).
            if (await reviews.ExistsForOrderAsync(order.Id, cancellationToken))
            {
                return BusinessResult.Failure<SubmitReviewResponse>(
                    Error.Conflict("orderId", BusinessErrorMessage.ReviewAlreadyExists));
            }

            // Step 5: Build + persist the review.
            var review = Review.Create(
                id: ids.Next(),
                orderId: order.Id,
                makerId: order.MakerId,
                customerUserId: customerUserId,
                rating: command.Rating,
                body: command.Body,
                countryCode: order.CountryCode,
                productId: order.ProductId);
            await reviews.AddAsync(review, cancellationToken);

            // Step 6: Recompute-from-rows (Q5). Row-lock the Maker so
            // concurrent submits to the same maker serialize, then read the
            // aggregate over the active reviews INCLUDING the just-added
            // one (the new row is visible inside the same UoW transaction
            // once tracked + flushed by the FOR UPDATE read's command).
            var maker = await makers.GetByIdForUpdateAsync(order.MakerId, cancellationToken);
            if (maker is null)
            {
                // FK invariant violation — refuse to commit (the order's
                // maker_id must resolve to an active maker).
                logger.LogCritical(
                    "SubmitReview: maker {MakerId} not found for order {OrderId}. " +
                    "FK invariant violation — refusing to commit.",
                    order.MakerId, order.Id);
                return BusinessResult.Failure<SubmitReviewResponse>(
                    Error.NotFound("makerId", BusinessErrorMessage.MakerNotFound));
            }

            // The aggregate query reads COMMITTED rows only — the just-added
            // review is tracked (Added) but not yet flushed, so it is NOT in
            // the DB count/avg. Fold its contribution in memory so the stored
            // bp/count include it deterministically (the off-by-one §C.8 step
            // 7 flags; RatingRecomputeCorrectnessTests pins the result). This
            // is exact: (sumExisting + newRating) / (countExisting + 1).
            var (existingCount, existingAverage) = await reviews.GetMakerRatingAggregateAsync(
                order.MakerId, cancellationToken);
            var totalCount = existingCount + 1;
            var foldedAverage = ((existingAverage * existingCount) + review.Rating) / totalCount;
            var ratingAverageBp = Math.Clamp(
                (int)Math.Round(foldedAverage * 10_000, MidpointRounding.AwayFromZero),
                0,
                50_000);
            maker.RecomputeRating(totalCount, ratingAverageBp);

            // Step 7: Per-product recompute — same fold discipline as the
            // maker's (the just-added review is tracked but unflushed, so
            // its contribution is folded in memory). Custom orders carry
            // no ProductId and never touch a product aggregate. A product
            // soft-deleted after the order was placed skips silently — the
            // recompute-from-rows model self-heals on the next review if
            // the product is reactivated.
            if (order.ProductId is not null)
            {
                var product = await products.GetByIdForUpdateAsync(order.ProductId, cancellationToken);
                if (product is not null)
                {
                    var (productCount, productAverage) = await reviews.GetProductRatingAggregateAsync(
                        order.ProductId, cancellationToken);
                    var productTotalCount = productCount + 1;
                    var productFoldedAverage =
                        ((productAverage * productCount) + review.Rating) / productTotalCount;
                    var productAverageBp = Math.Clamp(
                        (int)Math.Round(productFoldedAverage * 10_000, MidpointRounding.AwayFromZero),
                        0,
                        50_000);
                    product.RecomputeRating(productTotalCount, productAverageBp);
                }
                else
                {
                    logger.LogWarning(
                        "SubmitReview: product {ProductId} for order {OrderId} is missing or inactive — " +
                        "skipping the product rating recompute (maker recompute already applied).",
                        order.ProductId, order.Id);
                }
            }

            // Step 8: UoW pipeline commits the insert + recomputes atomically.
            return BusinessResult.Success(
                new SubmitReviewResponse(review.Id, review.Rating, review.CreatedAt));
        }
    }
}
