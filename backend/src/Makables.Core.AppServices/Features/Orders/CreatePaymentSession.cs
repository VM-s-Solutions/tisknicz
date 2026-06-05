using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Payments;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Create (or re-use) a Comgate payment session for an order in
/// <see cref="OrderState.PendingPayment"/> per T-0065 / US-customer-0010
/// AC-2 + AC-3. Mirrors <see cref="CreateOrder"/> in shape (single static
/// class with nested Command / Response / Validator / Handler).
///
/// <para>
/// <b>Q1 verify-then-recreate flow.</b> The handler implements the
/// 24h-retry-window contract:
/// </para>
/// <list type="number">
///   <item><description>Resolve customer identity from
///     <see cref="IUserSessionProvider"/>. Backstop guard — controller-level
///     <c>[Authorize]</c> should have responded 401 already.</description></item>
///   <item><description>Load the order via
///     <see cref="IOrderRepository.GetByIdForCustomerAsync"/>. Null is
///     surfaced as <see cref="BusinessErrorMessage.OrderNotFound"/> (404)
///     for BOTH unknown ids AND cross-customer ids (IDOR-shielded).</description></item>
///   <item><description>State gate: refuse with
///     <see cref="BusinessErrorMessage.OrderInvalidStateForPayment"/> for
///     anything other than <see cref="OrderState.PendingPayment"/>. The
///     entity-level <see cref="Order.ReservePaymentSession"/> guards the
///     same state but emits the generic
///     <see cref="BusinessErrorMessage.OrderInvalidTransition"/>; the
///     command-level code is more specific so the frontend i18n key
///     resolves to a UX-appropriate message.</description></item>
///   <item><description>Resolve provider via
///     <see cref="IPaymentProviderFactory.ResolveAsync"/>; propagate any
///     <c>payment.providerNotRegistered</c> / <c>countryConfiguration.notFound</c>
///     verbatim.</description></item>
///   <item><description><b>Q1 retry decision.</b> If the order already has
///     a <see cref="Order.PaymentProviderRef"/>:
///     <list type="bullet">
///       <item><description><see cref="PaymentState.Pending"/> /
///         <see cref="PaymentState.Authorized"/> → return cached
///         <see cref="Order.PaymentRedirectUrl"/> without calling
///         <see cref="IPaymentProvider.CreatePaymentAsync"/>.</description></item>
///       <item><description><see cref="PaymentState.Cancelled"/> /
///         <see cref="PaymentState.Failed"/> → fall through to step 6
///         and recreate.</description></item>
///       <item><description><see cref="PaymentState.Paid"/> /
///         <see cref="PaymentState.Refunded"/> → state-machine mismatch
///         (the webhook should already have transitioned us past
///         <see cref="OrderState.PendingPayment"/>). Log Critical and
///         return <see cref="BusinessErrorMessage.OrderPaymentAlreadyCaptured"/>.</description></item>
///     </list>
///   </description></item>
///   <item><description>Call
///     <see cref="IPaymentProvider.CreatePaymentAsync"/>. Failures
///     (provider unavailable, rejected, misconfigured) propagate verbatim
///     so the customer sees the right typed code.</description></item>
///   <item><description>Persist via
///     <see cref="Order.ReservePaymentSession"/>. The aggregate guards the
///     state gate one more time (defence-in-depth race shield); the
///     <c>UnitOfWorkPipelineBehavior</c> commits.</description></item>
/// </list>
///
/// <para>
/// <b>Q1 cached-URL safety.</b> Returning the cached redirect on the
/// Pending/Authorized branch is safe because Comgate's
/// <c>VerifyPaymentAsync</c> just confirmed the session is still live.
/// If a third party knew the URL they could complete the payment on the
/// customer's behalf — suboptimal but not catastrophic per ADR 0016
/// §"Why the redirect URL is plaintext-stored".
/// </para>
/// </summary>
public static class CreatePaymentSession
{
    public sealed record Command(string OrderId) : ICommand<Response>;

    public sealed record Response(string PaymentProviderRef, string RedirectUrl);

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
        IUserSessionProvider session,
        IOrderRepository orders,
        IPaymentProviderFactory paymentProviders,
        IClock clock,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // 1. Resolve customer identity. Backstop — the host's
            //    [Authorize] middleware should have returned 401 already.
            var customerUserId = session.GetUserId();
            if (string.IsNullOrEmpty(customerUserId))
            {
                return BusinessResult.Failure<Response>(Error.Unauthorized());
            }

            // 2. Load the order. Null covers both unknown-id and cross-
            //    customer-id (IDOR-shielded). The repo result is tracked —
            //    ReservePaymentSession will mutate the loaded aggregate.
            var order = await orders.GetByIdForCustomerAsync(
                command.OrderId, customerUserId, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<Response>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // 3. State gate. Only PendingPayment orders can have a payment
            //    session reserved. Distinct from OrderInvalidTransition
            //    (the entity-level code) so the i18n key resolves to a
            //    payment-specific message.
            if (order.State != OrderState.PendingPayment)
            {
                return BusinessResult.Failure<Response>(
                    Error.Conflict("state", BusinessErrorMessage.OrderInvalidStateForPayment));
            }

            // 4. Resolve provider per country.
            var providerResult = await paymentProviders.ResolveAsync(
                order.CountryCode, cancellationToken);
            if (!providerResult.IsSuccess)
            {
                return BusinessResult.Failure<Response>(providerResult.Error!);
            }
            var provider = providerResult.Value!;

            // 5. Q1 retry decision. If a prior session exists, verify it.
            if (!string.IsNullOrEmpty(order.PaymentProviderRef))
            {
                var verifyResult = await provider.VerifyPaymentAsync(
                    order.PaymentProviderRef, cancellationToken);
                if (!verifyResult.IsSuccess)
                {
                    // VerifyPayment failure surfaces verbatim — same Transient /
                    // Permanent / Configuration semantics as CreatePayment.
                    return BusinessResult.Failure<Response>(verifyResult.Error!);
                }

                var status = verifyResult.Value!;
                switch (status.State)
                {
                    case PaymentState.Pending:
                    case PaymentState.Authorized:
                        // Cached path. Q1: serve the stored URL so the
                        // customer's 6-hour-later retry is a pure DB read.
                        // The cached URL must be non-empty here — if it's
                        // not, we fell into a data-corruption case and the
                        // safest move is to recreate.
                        if (!string.IsNullOrEmpty(order.PaymentRedirectUrl))
                        {
                            logger.LogInformation(
                                "CreatePaymentSession: serving cached Comgate URL for order {OrderId} (status={Status}).",
                                order.Id, status.State);
                            return BusinessResult.Success(new Response(
                                order.PaymentProviderRef!,
                                order.PaymentRedirectUrl!));
                        }
                        // Fall through to recreate — cached URL missing.
                        break;

                    case PaymentState.Cancelled:
                    case PaymentState.Failed:
                        // Fall through to recreate.
                        logger.LogInformation(
                            "CreatePaymentSession: prior Comgate session for order {OrderId} is {Status}; recreating.",
                            order.Id, status.State);
                        break;

                    case PaymentState.Paid:
                    case PaymentState.Refunded:
                        // State-machine mismatch — the webhook should have
                        // already transitioned the order past PendingPayment.
                        // Critical so ops can reconcile before the customer
                        // double-pays.
                        logger.LogCritical(
                            "CreatePaymentSession: Comgate says order {OrderId} is {Status} but local state is still PendingPayment. Webhook reconciliation required.",
                            order.Id, status.State);
                        return BusinessResult.Failure<Response>(
                            Error.Conflict("state", BusinessErrorMessage.OrderPaymentAlreadyCaptured));

                    default:
                        // Defensive — every PaymentState above is covered.
                        // A new enum value would surface here.
                        return BusinessResult.Failure<Response>(
                            Error.Unknown(BusinessErrorMessage.PaymentUnknownError));
                }
            }

            // 6. Create a fresh session.
            var createResult = await provider.CreatePaymentAsync(order, cancellationToken);
            if (!createResult.IsSuccess)
            {
                return BusinessResult.Failure<Response>(createResult.Error!);
            }
            var paymentSession = createResult.Value!;

            // 7. Persist the ref + URL on the aggregate. ReservePaymentSession
            //    re-checks the state gate (defence-in-depth race shield) and
            //    allows overwrite-after-rejection. EF change tracking on the
            //    loaded order persists; the UoW pipeline commits.
            var reserveResult = order.ReservePaymentSession(
                paymentSession.ProviderRef, paymentSession.RedirectUrl, clock);
            if (!reserveResult.IsSuccess)
            {
                // The only way to reach this branch is a state mutation
                // racing the handler — surface the entity's code.
                return BusinessResult.Failure<Response>(reserveResult.Error!);
            }

            return BusinessResult.Success(new Response(
                paymentSession.ProviderRef, paymentSession.RedirectUrl));
        }
    }
}
