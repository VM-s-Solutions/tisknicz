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
/// Customer-host write: post a message to the customer↔maker thread on
/// one of the customer's own orders (T-0079, US-customer-0014 AC-1).
///
/// <para>
/// Per ADR 0013 + T-0082 precedent, the feature surface is split
/// per-audience at compile time: this command is registered on the
/// Customer host only — a maker JWT cannot dispatch it. The
/// scoped repository lookup is the IDOR shield (WHERE
/// <c>CustomerUserId == session</c> baked into SQL).
/// </para>
///
/// <para>
/// <b>5-min debounce.</b> If the maker's recipient pointer
/// (<see cref="Order.MakerPendingNotificationEmailAt"/>) is null OR
/// strictly older than 5 minutes, the handler enqueues a
/// <see cref="OutboxEventTypes.OrderMessagePostedMakerEmail"/> outbox row
/// AND sets the pointer to <c>clock.UtcNow</c>. Otherwise the second + Nth
/// post inside the 5-min window are silenced (digest semantics).
/// </para>
///
/// <para>
/// <b>Atomicity.</b> OrderMessage insert + Order counter increment +
/// outbox row + pointer update all commit in one UoW transaction per
/// ADR 0014. The handler NEVER calls <c>SaveChangesAsync</c>.
/// </para>
/// </summary>
public static class PostCustomerOrderMessage
{
    public sealed record Command(string OrderId, string Body)
        : ICommand<PostCustomerOrderMessageResponse>;

    public sealed record PostCustomerOrderMessageResponse(string MessageId, DateTimeOffset CreatedAt);

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
        : IRequestHandler<Command, BusinessResult<PostCustomerOrderMessageResponse>>
    {
        public async Task<BusinessResult<PostCustomerOrderMessageResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: Resolve customer session.
            var customerUserId = session.GetUserId();
            if (string.IsNullOrEmpty(customerUserId))
            {
                return BusinessResult.Failure<PostCustomerOrderMessageResponse>(Error.Unauthorized());
            }

            // Step 2: IDOR-scoped order load. Null surfaces as OrderNotFound
            // for both "unknown order" and "another customer's order" so
            // the response shape leaks no enumeration oracle.
            var order = await orders.GetByIdForCustomerAsync(
                command.OrderId, customerUserId, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<PostCustomerOrderMessageResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 3: Build + persist the message + bump maker counter.
            var now = clock.UtcNow;
            var message = OrderMessage.Create(
                id: ids.Next(),
                orderId: order.Id,
                authorRole: OrderMessageAuthorRole.Customer,
                authorUserId: customerUserId,
                body: command.Body,
                countryCode: order.CountryCode);
            await messages.AddAsync(message, cancellationToken);
            order.IncrementUnreadFor(OrderMessageAuthorRole.Customer);

            // Step 4: 5-min debounce decision + maybe enqueue outbox row.
            // The pointer read + conditional update happen inside the EF
            // UoW (single Postgres transaction); MVCC + row-level lock
            // serializes racing post handlers per risk note in §Risk.
            if (order.ShouldEmitNotificationFor(OrderMessageAuthorRole.Customer, now))
            {
                // Resolve maker + maker's user (for email + language).
                var maker = await makers.GetByIdAsync(order.MakerId, cancellationToken);
                if (maker is null)
                {
                    logger.LogCritical(
                        "PostCustomerOrderMessage: maker {MakerId} not found for order {OrderId}. " +
                        "FK invariant violation — refusing to commit.",
                        order.MakerId, order.Id);
                    return BusinessResult.Failure<PostCustomerOrderMessageResponse>(
                        Error.NotFound("makerId", BusinessErrorMessage.MakerNotFound));
                }
                var makerUser = await users.GetByIdAsync(maker.UserId, cancellationToken);
                if (makerUser is null)
                {
                    logger.LogCritical(
                        "PostCustomerOrderMessage: maker user {UserId} not found for maker {MakerId}. " +
                        "FK invariant violation — refusing to commit.",
                        maker.UserId, maker.Id);
                    return BusinessResult.Failure<PostCustomerOrderMessageResponse>(
                        Error.NotFound("makerUserId", BusinessErrorMessage.MakerUserMissing));
                }
                var makerLanguage = await languageResolver.ResolveForUserAsync(makerUser, cancellationToken);
                var urls = publicAppUrls.Value;
                var payload = new OrderMessagePostedMakerEmailPayload(
                    OrderId: order.Id,
                    OrderNumber: order.OrderNumber,
                    MakerEmail: makerUser.Email,
                    SenderName: order.ContactName,
                    UnreadMessageCount: order.MakerUnreadMessageCount,
                    LanguageCode: makerLanguage,
                    ActionUrl: $"{urls.WebBaseUrl.TrimEnd('/')}/maker/orders/{order.Id}");
                outbox.Enqueue(
                    aggregateId: order.Id,
                    eventType: OutboxEventTypes.OrderMessagePostedMakerEmail,
                    payloadJson: JsonSerializer.Serialize(payload));
                order.MarkNotificationEmittedFor(OrderMessageAuthorRole.Customer, now);
            }

            // Step 5: UoW pipeline commits everything atomically.
            return BusinessResult.Success(
                new PostCustomerOrderMessageResponse(message.Id, message.CreatedAt));
        }
    }
}
