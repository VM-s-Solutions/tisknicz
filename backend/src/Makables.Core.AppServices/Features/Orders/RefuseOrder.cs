using System.Text.Json;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Payments;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// The maker REFUSES a paid order they cannot fulfil (T-0181, audit
/// MAKER-H3).
///
/// <para>
/// Until now a maker who ran out of material or got the specs wrong could
/// only accept or ignore a paid order — T-0071 locked "no DeclineOrder;
/// admin handles it via T-0107", and the dashboard offered no hint of
/// what to do instead. T-0174 shipped interim guidance copy; this is the
/// real action.
/// </para>
///
/// <para>
/// <b>Scope (Q-0041, user-confirmed 2026-08-22 + the 2026-06-03 role
/// decision).</b> From <see cref="OrderState.Paid"/> ONLY, and only
/// within <c>CountryConfiguration.MakerRefusalWindowHours</c> of
/// <c>PaidAt</c> ("two days, for example"). Past the window the maker
/// goes through admin support — the pre-T-0181 status quo — so this
/// window only ever WIDENS what a maker may do, it never narrows the
/// customer's protection.
/// </para>
///
/// <para>
/// <b>Money moves first, then the record</b> (the T-0105 ordering): the
/// provider refund is issued before the aggregate is mutated, so a
/// gateway failure leaves the order untouched. The reverse order could
/// cancel an order whose money never came back.
/// </para>
/// </summary>
public static class RefuseOrder
{
    public sealed record Command(string OrderId, string Reason)
        : ICommand<RefuseOrderResponse>;

    /// <summary>Globally-unique name (NSwag convention).</summary>
    public sealed record RefuseOrderResponse(
        string OrderId,
        OrderState State,
        long RefundedAmountMinor);

    public const int ReasonMaxLength = 2000;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            // A refusal returns the customer's money — the maker must say
            // why, both for the customer email and the dispute trail.
            RuleFor(c => c.Reason)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(ReasonMaxLength).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IMakerRepository makers,
        IUserRepository users,
        ICountryConfigurationRepository countryConfigurations,
        IPaymentProviderFactory providerFactory,
        IUserSessionProvider session,
        IOutbox outbox,
        IClock clock,
        ILanguageResolver languageResolver,
        IOptions<PublicAppUrlsOptions> publicAppUrls,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<RefuseOrderResponse>>
    {
        public async Task<BusinessResult<RefuseOrderResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<RefuseOrderResponse>(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<RefuseOrderResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Owner-scoped load — a cross-maker id resolves to null, so it
            // reads as not-found rather than confirming the order exists.
            var order = await orders.GetByIdForMakerAsync(
                command.OrderId, maker.Id, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<RefuseOrderResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Idempotent re-run: already cancelled → Success, no second
            // provider call, no second outbox row.
            if (order.State == OrderState.Cancelled)
            {
                logger.LogInformation(
                    "RefuseOrder: order {OrderId} already cancelled; idempotent skip.", order.Id);
                return BusinessResult.Success(new RefuseOrderResponse(
                    order.Id, order.State, order.RefundedAmountMinor));
            }

            if (order.State != OrderState.Paid)
            {
                return BusinessResult.Failure<RefuseOrderResponse>(
                    Error.Conflict("state", BusinessErrorMessage.OrderInvalidTransition));
            }

            // Window check BEFORE the provider is touched. The duration is
            // a tunable policy row, never a constant (ADR 0004).
            var config = await countryConfigurations.GetByCodeAsync(order.CountryCode, cancellationToken);
            if (config is null)
            {
                return BusinessResult.Failure<RefuseOrderResponse>(
                    Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigurationNotFound));
            }
            if (order.PaidAt is not { } paidAt)
            {
                // Paid state without PaidAt is a data invariant break, not a
                // user error — refuse rather than refund against a guess.
                logger.LogCritical(
                    "RefuseOrder: order {OrderId} is Paid but PaidAt is null — refusing.", order.Id);
                return BusinessResult.Failure<RefuseOrderResponse>(
                    Error.Conflict("paidAt", BusinessErrorMessage.OrderInvalidTransition));
            }
            var deadline = paidAt.AddHours(config.MakerRefusalWindowHours);
            if (clock.UtcNow > deadline)
            {
                return BusinessResult.Failure<RefuseOrderResponse>(
                    Error.Conflict("paidAt", BusinessErrorMessage.OrderRefusalWindowExpired));
            }

            if (order.PaymentProviderRef is null)
            {
                return BusinessResult.Failure<RefuseOrderResponse>(
                    Error.Conflict("paymentProviderRef", BusinessErrorMessage.PaymentRefundNoProviderRef));
            }

            // === Money first (T-0105 ordering) ===
            var refundAmount = order.RemainingRefundableMinor;
            var providerResult = await providerFactory.ResolveAsync(order.CountryCode, cancellationToken);
            if (!providerResult.IsSuccess)
            {
                return BusinessResult.Failure<RefuseOrderResponse>(providerResult.Error!);
            }
            var refundResult = await providerResult.Value!.RefundAsync(
                order.PaymentProviderRef, refundAmount, order.Currency, cancellationToken);
            if (!refundResult.IsSuccess)
            {
                // Order untouched, no outbox — the maker can retry.
                return BusinessResult.Failure<RefuseOrderResponse>(refundResult.Error!);
            }

            // === Then the record ===
            var mutation = order.RefuseByMaker(clock, refundAmount);
            if (!mutation.IsSuccess)
            {
                logger.LogCritical(
                    "RefuseOrder: provider refunded {AmountMinor} {Currency} on order {OrderId} " +
                    "but RefuseByMaker refused with {Code}. Money moved without a record — " +
                    "reconcile manually.",
                    refundAmount, order.Currency, order.Id, mutation.Error!.Code);
                return BusinessResult.Failure<RefuseOrderResponse>(mutation.Error!);
            }

            var customer = await users.GetByIdAsync(order.CustomerUserId, cancellationToken);
            if (customer is null)
            {
                logger.LogCritical(
                    "RefuseOrder: customer user {UserId} not found for order {OrderId}. " +
                    "FK invariant violation — refusing to commit.",
                    order.CustomerUserId, order.Id);
                return BusinessResult.Failure<RefuseOrderResponse>(
                    Error.NotFound("customerUserId", BusinessErrorMessage.OrderCustomerUserMissing));
            }
            var customerLanguage = await languageResolver.ResolveForUserAsync(customer, cancellationToken);
            var urls = publicAppUrls.Value;
            // Reuses the cancellation email shape: to the customer this IS a
            // cancellation, with the refund noted; `Reason: Maker` lets the
            // template say who refused.
            var payload = new OrderCancelledCustomerEmailPayload(
                OrderId: order.Id,
                OrderNumber: order.OrderNumber,
                Email: order.ContactEmail,
                ContactName: order.ContactName,
                Reason: OrderCancellationSource.Maker,
                LanguageCode: customerLanguage,
                ActionUrl: $"{urls.WebBaseUrl.TrimEnd('/')}/objednavka/{order.Id}");
            outbox.Enqueue(
                aggregateId: order.Id,
                eventType: OutboxEventTypes.OrderCancelledCustomerEmail,
                payloadJson: JsonSerializer.Serialize(payload));

            logger.LogInformation(
                "RefuseOrder: maker {MakerId} refused order {OrderId}; refunded {AmountMinor}.",
                maker.Id, order.Id, refundAmount);

            return BusinessResult.Success(new RefuseOrderResponse(
                order.Id, order.State, order.RefundedAmountMinor));
        }
    }
}
