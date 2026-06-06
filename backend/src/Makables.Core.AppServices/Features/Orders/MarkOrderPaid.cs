using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Transition a <see cref="Order"/> from
/// <see cref="OrderState.PendingPayment"/> to <see cref="OrderState.Paid"/>
/// in response to a verified Comgate webhook per T-0066. Dispatched only
/// by <c>ComgateWebhookController</c> after IP allowlist + re-fetch +
/// ref-mismatch checks have passed.
///
/// <para>
/// <b>T-0066 stub scope (user decision Q1).</b> The handler does the
/// state transition only — no outbox event emission, no email side
/// effects, no invoice generation. T-0067 ships the outbox plumbing for
/// customer/maker emails + invoice PDF generation; the comment in the
/// handler marks the insertion point.
/// </para>
///
/// <para>
/// <b>PaymentMethod and PaidAt are accepted but ignored.</b> The Command
/// signature carries them for T-0067's benefit (T-0067 ships the
/// migration adding nullable columns to <c>orders</c> and the handler
/// update that persists them). Accepting them now keeps the Command
/// signature stable across the T-0066 → T-0067 transition.
/// </para>
///
/// <para>
/// <b>Defence-in-depth ref check.</b> The webhook controller already
/// verifies the body's <c>refId</c> matches the order found by
/// <c>transId</c>. The handler repeats the check (a future caller may
/// dispatch this command without the controller's vetting) and refuses
/// to mutate an order that does not match.
/// </para>
/// </summary>
public static class MarkOrderPaid
{
    public sealed record Command(
        string OrderId,
        string ProviderRef,
        string? PaymentMethod,
        DateTimeOffset? PaidAt) : ICommand<Response>;

    public sealed record Response(string OrderId);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.ProviderRef)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(200).WithErrorCode(BusinessErrorMessage.MaxLength);

            // PaymentMethod + PaidAt accepted as-is — T-0067 will use them
            // once the persistence migration ships.
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IClock clock,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: Load order by provider ref. Null-shielded — the
            // webhook controller already did this lookup; the handler does
            // it again so a future direct caller without the controller's
            // vetting still gets a typed NotFound.
            var order = await orders.GetByPaymentProviderRefAsync(
                command.ProviderRef, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<Response>(
                    Error.NotFound("providerRef", BusinessErrorMessage.OrderNotFound));
            }

            // Step 2: Defence-in-depth ref check. The controller already
            // verified that order.Id == body.refId; if a future caller
            // bypasses the controller and dispatches a Command whose
            // OrderId doesn't match the order resolved by ProviderRef,
            // we refuse to mutate and log Critical so ops investigates.
            if (!string.Equals(order.Id, command.OrderId, StringComparison.Ordinal))
            {
                logger.LogCritical(
                    "MarkOrderPaid: order.Id={ResolvedOrderId} does not match Command.OrderId={CommandOrderId} for providerRef={ProviderRef}. Possible spoof or programmer error.",
                    order.Id, command.OrderId, command.ProviderRef);
                return BusinessResult.Failure<Response>(
                    Error.Conflict("orderId", BusinessErrorMessage.PaymentWebhookRefIdMismatch));
            }

            // Step 3: State transition via the aggregate. Order.MarkAsPaid
            // enforces PendingPayment → Paid + the set-once invariant on
            // PaymentProviderRef. Race-loser (a second webhook arriving
            // after the first already transitioned) surfaces as
            // OrderInvalidTransition; the controller maps that to 200.
            var transitionResult = order.MarkAsPaid(clock, command.ProviderRef);
            if (!transitionResult.IsSuccess)
            {
                return BusinessResult.Failure<Response>(transitionResult.Error!);
            }

            // Step 4: T-0067 will persist command.PaymentMethod +
            // command.PaidAt once the migration ships (new nullable
            // columns on `orders`). At T-0066 these fields are
            // accepted-and-ignored on purpose — the Command signature
            // stays stable across the transition.

            // Step 5: No SaveChangesAsync — UoW pipeline behavior commits
            // at the end of the request.
            return BusinessResult.Success(new Response(order.Id));
        }
    }
}
