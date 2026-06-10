using System.Text.Json;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.OrderMessages;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.OrderMessages;

/// <summary>
/// Symmetric maker-host write — maker posts to the thread on one of
/// their assigned orders (T-0079, US-maker-0011 AC-1). Registered on the
/// Maker host only; the customer ↔ maker compile-time split IS the IDOR
/// shield per ADR 0013 + T-0082 precedent.
///
/// <para>
/// Recipient on this direction is the customer; the outbox event type is
/// <see cref="OutboxEventTypes.OrderMessagePostedCustomerEmail"/>. The
/// 5-min debounce reads the CUSTOMER's recipient pointer
/// (<see cref="Order.CustomerPendingNotificationEmailAt"/>).
/// </para>
/// </summary>
public static class PostMakerOrderMessage
{
    public sealed record Command(string OrderId, string Body)
        : ICommand<PostMakerOrderMessageResponse>;

    public sealed record PostMakerOrderMessageResponse(string MessageId, DateTimeOffset CreatedAt);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.Body)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.OrderMessageBodyEmpty)
                .MaximumLength(OrderMessage.MaxBodyLength)
                .WithErrorCode(BusinessErrorMessage.OrderMessageBodyTooLong);
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IOrderMessageRepository messages,
        IMakerRepository makers,
        IUserRepository users,
        IUserSessionProvider session,
        IOutbox outbox,
        IClock clock,
        IIdGenerator ids,
        ILanguageResolver languageResolver,
        IOptions<PublicAppUrlsOptions> publicAppUrls,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BusinessResult<PostMakerOrderMessageResponse>>
    {
        public async Task<BusinessResult<PostMakerOrderMessageResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: Resolve maker user → maker entity.
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<PostMakerOrderMessageResponse>(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                // A maker-audience JWT without a maker row → leak nothing
                // about whether the order exists.
                return BusinessResult.Failure<PostMakerOrderMessageResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 2: IDOR-scoped order load.
            var order = await orders.GetByIdForMakerAsync(
                command.OrderId, maker.Id, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<PostMakerOrderMessageResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 2b: State guard (review fold, user ruling on MEDIUM-2).
            // PendingPayment is the ONLY blocked state — all post-payment
            // states (incl. Cancelled / Disputed) stay open. Mirrors the
            // customer-side guard in PostCustomerOrderMessage.
            if (order.State == OrderState.PendingPayment)
            {
                return BusinessResult.Failure<PostMakerOrderMessageResponse>(
                    Error.Conflict("order", BusinessErrorMessage.OrderMessageNotAllowedInState));
            }

            // Step 3: Build + persist the message + bump customer counter.
            var now = clock.UtcNow;
            var message = OrderMessage.Create(
                id: ids.Next(),
                orderId: order.Id,
                authorRole: OrderMessageAuthorRole.Maker,
                authorUserId: userId,
                body: command.Body,
                countryCode: order.CountryCode);
            await messages.AddAsync(message, cancellationToken);
            order.IncrementUnreadFor(OrderMessageAuthorRole.Maker);

            // Step 4: 5-min debounce on the customer's recipient pointer.
            if (order.ShouldEmitNotificationFor(OrderMessageAuthorRole.Maker, now))
            {
                var customer = await users.GetByIdAsync(order.CustomerUserId, cancellationToken);
                if (customer is null)
                {
                    logger.LogCritical(
                        "PostMakerOrderMessage: customer user {UserId} not found for order {OrderId}. " +
                        "FK invariant violation — refusing to commit.",
                        order.CustomerUserId, order.Id);
                    return BusinessResult.Failure<PostMakerOrderMessageResponse>(
                        Error.NotFound("customerUserId", BusinessErrorMessage.OrderCustomerUserMissing));
                }
                var customerLanguage = await languageResolver.ResolveForUserAsync(customer, cancellationToken);
                var urls = publicAppUrls.Value;
                var payload = new OrderMessagePostedCustomerEmailPayload(
                    OrderId: order.Id,
                    OrderNumber: order.OrderNumber,
                    Email: order.ContactEmail,
                    ContactName: order.ContactName,
                    SenderName: maker.CompanyName,
                    UnreadMessageCount: order.CustomerUnreadMessageCount,
                    LanguageCode: customerLanguage,
                    ActionUrl: $"{urls.WebBaseUrl.TrimEnd('/')}/objednavka/{order.Id}");
                outbox.Enqueue(
                    aggregateId: order.Id,
                    eventType: OutboxEventTypes.OrderMessagePostedCustomerEmail,
                    payloadJson: JsonSerializer.Serialize(payload));
                order.MarkNotificationEmittedFor(OrderMessageAuthorRole.Maker, now);
            }

            return BusinessResult.Success(
                new PostMakerOrderMessageResponse(message.Id, message.CreatedAt));
        }
    }
}
