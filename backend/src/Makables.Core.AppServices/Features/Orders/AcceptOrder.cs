using System.Text.Json;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Transition an <see cref="Order"/> from <see cref="OrderState.Paid"/>
/// to <see cref="OrderState.Accepted"/> when the maker clicks
/// <b>"Accept"</b> on the maker dashboard. T-0071 (US-maker-0006).
///
/// <para>
/// First maker-initiated state transition in the order lifecycle.
/// Enqueues a single <c>order.accepted.customerEmail</c> outbox event so
/// the customer learns the maker has committed. Mirrors the T-0067
/// MarkOrderPaid blueprint at smaller scope (1 email vs 3 events) — no
/// decline counter-action at MVP per locked decision A.1; no auto-cancel
/// timer per locked decision A.2.
/// </para>
///
/// <para>
/// <b>Atomicity.</b> The state mutation + the outbox row commit in one
/// UoW transaction per ADR 0014. The handler NEVER calls
/// <c>SaveChangesAsync</c>.
/// </para>
///
/// <para>
/// <b>IDOR shield.</b> Owner-scoped lookup via
/// <see cref="IOrderRepository.GetByIdForMakerAsync"/>; cross-maker /
/// unknown ids return null → 404 (same shape so ids aren't enumerable).
/// </para>
/// </summary>
public static class AcceptOrder
{
    public sealed record Command(string OrderId) : ICommand<Response>;

    public sealed record Response(string OrderId);

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
        IUserRepository users,
        IMakerRepository makers,
        IUserSessionProvider session,
        IOutbox outbox,
        IClock clock,
        ILanguageResolver languageResolver,
        IOptions<PublicAppUrlsOptions> publicAppUrls,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: Resolve maker session. [Authorize] handles 401 before
            // the handler runs; the defensive guard catches mid-flight loss.
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<Response>(Error.Unauthorized());
            }

            // Step 2: Resolve maker for the authenticated user. Defensive —
            // a maker-audience JWT without a maker row is a 404 here so
            // we don't leak the existence of any order.
            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<Response>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 3: Owner-scoped order load (IDOR shield per ADR 0013).
            var order = await orders.GetByIdForMakerAsync(
                command.OrderId, maker.Id, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<Response>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 4: State transition via the aggregate.
            var transitionResult = order.Accept(clock);
            if (!transitionResult.IsSuccess)
            {
                return BusinessResult.Failure<Response>(transitionResult.Error!);
            }

            // Step 5: Resolve customer + language + build payload.
            var customer = await users.GetByIdAsync(order.CustomerUserId, cancellationToken);
            if (customer is null)
            {
                logger.LogCritical(
                    "AcceptOrder: customer user {UserId} not found for order {OrderId}. " +
                    "FK invariant violation — refusing to commit.",
                    order.CustomerUserId, order.Id);
                return BusinessResult.Failure<Response>(
                    Error.NotFound("customerUserId", BusinessErrorMessage.OrderCustomerUserMissing));
            }
            var customerLanguage = await languageResolver.ResolveForUserAsync(customer, cancellationToken);
            var urls = publicAppUrls.Value;
            var payload = new OrderAcceptedCustomerEmailPayload(
                OrderId: order.Id,
                OrderNumber: order.OrderNumber,
                Email: order.ContactEmail,
                ContactName: order.ContactName,
                LanguageCode: customerLanguage,
                ActionUrl: $"{urls.WebBaseUrl.TrimEnd('/')}/objednavka/{order.Id}");
            outbox.Enqueue(
                aggregateId: order.Id,
                eventType: OutboxEventTypes.OrderAcceptedCustomerEmail,
                payloadJson: JsonSerializer.Serialize(payload));

            // Step 6: No SaveChangesAsync — UoW pipeline behavior commits
            // the order mutation AND the outbox row atomically per ADR 0014.
            return BusinessResult.Success(new Response(order.Id));
        }
    }
}
