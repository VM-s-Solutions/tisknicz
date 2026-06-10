using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.OrderMessages;
using Makables.Core.Domain.Orders;
using MediatR;

namespace Makables.Core.AppServices.Features.OrderMessages;

/// <summary>
/// Customer marks all unread maker-authored messages on one of their
/// orders as read (T-0079 US-customer-0014 AC-6). Bulk UPDATE via the
/// repository's <see cref="IOrderMessageRepository.MarkAsReadForCustomerAsync"/>
/// — single SQL round-trip regardless of thread length.
///
/// <para>
/// <b>Commit shape — TWO transactions, not one</b> (Gate 8 M-2 fold).
/// <c>ExecuteUpdateAsync</c> executes immediately in its own implicit
/// transaction; the side effects on the parent <see cref="Order"/>
/// (<c>CustomerUnreadMessageCount = 0</c> +
/// <c>CustomerPendingNotificationEmailAt = null</c>, the locked-decision
/// A.2 reset rule) commit LATER via the UoW pipeline's
/// <c>SaveChangesAsync</c>. A crash between the two commits can leave
/// messages stamped read while the counter is still &gt; 0 — transient
/// drift that self-heals on the next MarkAsRead because
/// <see cref="Order.ResetUnreadFor"/> is an unconditional reset (not a
/// decrement).
/// </para>
///
/// <para>
/// No outbox event on MarkAsRead — the reader is the one acting; nothing
/// to notify.
/// </para>
/// </summary>
public static class MarkCustomerOrderMessagesAsRead
{
    public sealed record Command(string OrderId)
        : ICommand<MarkCustomerOrderMessagesAsReadResponse>;

    public sealed record MarkCustomerOrderMessagesAsReadResponse(int MarkedCount);

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
        IUserSessionProvider session,
        IClock clock)
        : IRequestHandler<Command, BusinessResult<MarkCustomerOrderMessagesAsReadResponse>>
    {
        public async Task<BusinessResult<MarkCustomerOrderMessagesAsReadResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            var customerUserId = session.GetUserId();
            if (string.IsNullOrEmpty(customerUserId))
            {
                return BusinessResult.Failure<MarkCustomerOrderMessagesAsReadResponse>(Error.Unauthorized());
            }

            var order = await orders.GetByIdForCustomerAsync(
                command.OrderId, customerUserId, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<MarkCustomerOrderMessagesAsReadResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Bulk-UPDATE all unread maker-authored messages on this order.
            // Repository predicate also enforces customer scope (defence-in-depth).
            var marked = await messages.MarkAsReadForCustomerAsync(
                order.Id, customerUserId, clock.UtcNow, cancellationToken);

            // Idempotent reset of the denormalized counter + debounce pointer.
            // Reset is unconditional (not a decrement) so the counter cannot
            // drift positive after a successful MarkAsRead.
            order.ResetUnreadFor(OrderMessageAuthorRole.Customer);
            order.ClearPendingNotificationFor(OrderMessageAuthorRole.Customer);

            return BusinessResult.Success(
                new MarkCustomerOrderMessagesAsReadResponse(marked));
        }
    }
}
