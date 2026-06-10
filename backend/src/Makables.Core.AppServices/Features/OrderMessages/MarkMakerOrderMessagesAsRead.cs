using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.OrderMessages;
using Makables.Core.Domain.Orders;
using MediatR;

namespace Makables.Core.AppServices.Features.OrderMessages;

/// <summary>
/// Symmetric maker-host MarkAsRead (T-0079 US-maker-0011). Same
/// two-transaction commit shape as <see cref="MarkCustomerOrderMessagesAsRead"/>:
/// the bulk sweep commits immediately via <c>ExecuteUpdateAsync</c>; the
/// counter reset + pointer clear commit later via the UoW pipeline.
/// Self-healing on counter drift because the reset is unconditional.
/// </summary>
public static class MarkMakerOrderMessagesAsRead
{
    public sealed record Command(string OrderId)
        : ICommand<MarkMakerOrderMessagesAsReadResponse>;

    public sealed record MarkMakerOrderMessagesAsReadResponse(int MarkedCount);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IOrderMessageRepository messages,
        IMakerRepository makers,
        IUserSessionProvider session,
        IClock clock)
        : IRequestHandler<Command, BusinessResult<MarkMakerOrderMessagesAsReadResponse>>
    {
        public async Task<BusinessResult<MarkMakerOrderMessagesAsReadResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<MarkMakerOrderMessagesAsReadResponse>(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<MarkMakerOrderMessagesAsReadResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            var order = await orders.GetByIdForMakerAsync(
                command.OrderId, maker.Id, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<MarkMakerOrderMessagesAsReadResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            var marked = await messages.MarkAsReadForMakerAsync(
                order.Id, maker.Id, clock.UtcNow, cancellationToken);

            order.ResetUnreadFor(OrderMessageAuthorRole.Maker);
            order.ClearPendingNotificationFor(OrderMessageAuthorRole.Maker);

            return BusinessResult.Success(
                new MarkMakerOrderMessagesAsReadResponse(marked));
        }
    }
}
