using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Reviews;
using MediatR;

namespace Makables.Core.AppServices.Features.Reviews;

/// <summary>
/// Maker-host write: attach (or overwrite) the single public reply to a
/// review on one of the maker's own orders (T-0100, US-maker-0014). Per
/// ADR 0013 the command is registered on the Maker host ONLY. The
/// maker-scoped review load
/// (<see cref="IReviewRepository.GetByIdForMakerAsync"/>) is the IDOR
/// shield — a maker cannot reply to a review whose order's <c>maker_id</c>
/// ≠ theirs; cross-tenant → 404 (<c>ReviewNotFound</c>), no existence
/// leak. The reply OVERWRITES the prior one (Q4 — one reply per review);
/// the customer's rating/body are never touched.
/// </summary>
public static class RespondToReview
{
    public sealed record Command(string ReviewId, string Reply)
        : ICommand<RespondToReviewResponse>;

    public sealed record RespondToReviewResponse(string ReviewId, string MakerReply, DateTimeOffset MakerReplyAt);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.ReviewId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.Reply)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.ReviewReplyEmpty)
                .MaximumLength(Review.MaxReplyLength).WithErrorCode(BusinessErrorMessage.ReviewReplyTooLong);
        }
    }

    public sealed class Handler(
        IReviewRepository reviews,
        IMakerRepository makers,
        IUserSessionProvider session,
        IClock clock)
        : IRequestHandler<Command, BusinessResult<RespondToReviewResponse>>
    {
        public async Task<BusinessResult<RespondToReviewResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: Resolve maker session → maker row.
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<RespondToReviewResponse>(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                // Maker-audience JWT without a maker row → 404 (same shape
                // as a foreign review; no existence oracle).
                return BusinessResult.Failure<RespondToReviewResponse>(
                    Error.NotFound("reviewId", BusinessErrorMessage.ReviewNotFound));
            }

            // Step 2: IDOR-scoped review load. Null → 404, cross-tenant
            // surfaces identically to "unknown review".
            var review = await reviews.GetByIdForMakerAsync(
                command.ReviewId, maker.Id, cancellationToken);
            if (review is null)
            {
                return BusinessResult.Failure<RespondToReviewResponse>(
                    Error.NotFound("reviewId", BusinessErrorMessage.ReviewNotFound));
            }

            // Step 3: Overwrite the single reply (Q4). UoW pipeline commits.
            review.AddReply(command.Reply, clock.UtcNow);

            return BusinessResult.Success(
                new RespondToReviewResponse(review.Id, review.MakerReply!, review.MakerReplyAt!.Value));
        }
    }
}
