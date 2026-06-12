using System.Text.Json;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Payments;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Admin refunds an order — full or partial — via the country's payment
/// provider. T-0105 (US-admin-0008). The ONLY sanctioned path into
/// <see cref="OrderState.Refunded"/>: T-0107's manual state change
/// blocks the target and names this command; T-0106's ResolveDispute
/// restores <c>PreDisputeState</c> first, then dispatches this command
/// for the full remaining amount.
///
/// <para>
/// <b>Provider-first order of operations (locked decision A.5).</b> The
/// handler pre-flights via the pure <see cref="Order.ValidateRefund"/>
/// predicate, calls <see cref="IPaymentProvider.RefundAsync"/>, and only
/// THEN mutates the order. A recorded-but-not-refunded order lies to the
/// customer; a refunded-but-unrecorded order (provider success, commit
/// failure) is recoverable — Comgate caps cumulative refunds at the
/// captured amount, so an admin retry cannot over-refund at the gateway.
/// </para>
///
/// <para>
/// <b>Silent Success</b> on an already-<see cref="OrderState.Refunded"/>
/// order (T-0067/T-0076 precedent): 200 without provider call, mutation,
/// or outbox emission. Audited via <see cref="IAdminAuditableCommand"/>
/// — before/after JSONB + the reason (and the post-payout
/// acknowledgement marker, Q5) land in <c>admin_audit_log</c>
/// atomically with the mutation + outbox row (ADR 0014).
/// </para>
/// </summary>
public static class RefundOrder
{
    /// <summary>Marker appended to the audit notes when the admin
    /// acknowledged refunding an already-paid-out (Completed) order.</summary>
    public const string PostPayoutAcknowledgedMarker = "[post-payout refund acknowledged]";

    public sealed record Command(
        string OrderId,
        long AmountMinor,
        string Reason,
        bool AcknowledgePostPayout)
        : ICommand<RefundOrderResponse>, IAdminAuditableCommand
    {
        public string ActionCode => "order.refund";
        public string TargetEntity => "order";
        public string TargetId => OrderId;
        public string? Notes =>
            AcknowledgePostPayout ? $"{Reason} {PostPayoutAcknowledgedMarker}" : Reason;
    }

    /// <summary>
    /// <b>Globally-unique name</b> (not nested <c>Response</c>) per the
    /// post-PR-#38 NSwag convention — avoids the TS schema collision.
    /// </summary>
    public sealed record RefundOrderResponse(
        string OrderId,
        OrderState State,
        long RefundedAmountMinor,
        long RemainingRefundableMinor,
        bool IsFullRefund);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.AmountMinor)
                .GreaterThan(0).WithErrorCode(BusinessErrorMessage.MinValue);

            // Reason caps at the audit-log notes column width (VerifyMaker
            // m-3 precedent) — without this an oversize payload dies at
            // SaveChanges as a raw 500 instead of a clean 400.
            RuleFor(c => c.Reason)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(2000).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IPaymentProviderFactory providerFactory,
        IUserRepository users,
        IOutbox outbox,
        IClock clock,
        ILanguageResolver languageResolver,
        IOptions<PublicAppUrlsOptions> publicAppUrls,
        IUserSessionProvider session,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BusinessResult<RefundOrderResponse>>
    {
        public async Task<BusinessResult<RefundOrderResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: Fail-closed session check (VerifyMaker / T-0034
            // precedent) — a privileged money movement must never be
            // attributed to "system" in the audit log.
            if (string.IsNullOrEmpty(session.GetUserId()))
            {
                return BusinessResult.Failure<RefundOrderResponse>(Error.Unauthorized());
            }

            // Step 2: Tracked unscoped load (admin host only, ADR 0013).
            var order = await orders.GetByIdUnscopedAsync(command.OrderId, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<RefundOrderResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 3: Silent Success on an already-Refunded order — no
            // provider call, no mutation, no outbox (AC-6).
            if (order.State == OrderState.Refunded)
            {
                logger.LogInformation(
                    "RefundOrder: order {OrderId} already Refunded; idempotent skip.",
                    order.Id);
                return BusinessResult.Success(BuildResponse(order));
            }

            // Step 4: Pre-flight — every refusal must surface BEFORE the
            // provider is touched (locked decision A.5).
            if (order.PaymentProviderRef is null)
            {
                return BusinessResult.Failure<RefundOrderResponse>(
                    Error.Conflict("paymentProviderRef", BusinessErrorMessage.PaymentRefundNoProviderRef));
            }
            var preFlight = order.ValidateRefund(command.AmountMinor, command.AcknowledgePostPayout);
            if (!preFlight.IsSuccess)
            {
                return BusinessResult.Failure<RefundOrderResponse>(preFlight.Error!);
            }

            // Step 5: Money moves FIRST. Provider selection via the
            // CountryConfiguration lookup — never branch on country.
            var providerResult = await providerFactory.ResolveAsync(order.CountryCode, cancellationToken);
            if (!providerResult.IsSuccess)
            {
                return BusinessResult.Failure<RefundOrderResponse>(providerResult.Error!);
            }
            var refundResult = await providerResult.Value!.RefundAsync(
                order.PaymentProviderRef, command.AmountMinor, order.Currency, cancellationToken);
            if (!refundResult.IsSuccess)
            {
                // Transient/Permanent/Configuration — surfaced verbatim;
                // order unchanged, no outbox, no audit row (the pipeline
                // skips failures). US-admin-0008 AC-3.
                return BusinessResult.Failure<RefundOrderResponse>(refundResult.Error!);
            }

            // Step 6: Record. Belt-and-braces — the pre-flight covered
            // every refusal; a failure here means money moved at the
            // gateway but the record was refused. Critical: ops must
            // reconcile from this log line.
            var mutation = order.Refund(clock, command.AmountMinor, command.AcknowledgePostPayout);
            if (!mutation.IsSuccess)
            {
                logger.LogCritical(
                    "RefundOrder: Comgate refunded {AmountMinor} {Currency} on order {OrderId} " +
                    "(transId={TransId}) but Order.Refund refused with {Code}. " +
                    "Money moved without a record — reconcile manually.",
                    command.AmountMinor, order.Currency, order.Id,
                    order.PaymentProviderRef, mutation.Error!.Code);
                return BusinessResult.Failure<RefundOrderResponse>(mutation.Error!);
            }

            // Step 7: Customer email — enrichment-at-enqueue (pending
            // Q-0012); FK violation refuses the commit so the admin sees
            // the inconsistency instead of a half-recorded refund email.
            var customer = await users.GetByIdAsync(order.CustomerUserId, cancellationToken);
            if (customer is null)
            {
                logger.LogCritical(
                    "RefundOrder: customer user {UserId} not found for order {OrderId}. " +
                    "FK invariant violation — refusing to commit.",
                    order.CustomerUserId, order.Id);
                return BusinessResult.Failure<RefundOrderResponse>(
                    Error.NotFound("customerUserId", BusinessErrorMessage.OrderCustomerUserMissing));
            }
            var customerLanguage = await languageResolver.ResolveForUserAsync(customer, cancellationToken);
            var payload = new OrderRefundedCustomerEmailPayload(
                OrderId: order.Id,
                OrderNumber: order.OrderNumber,
                Email: order.ContactEmail,
                ContactName: order.ContactName,
                RefundedAmountMinor: command.AmountMinor,
                Currency: order.Currency,
                IsFullRefund: order.State == OrderState.Refunded,
                LanguageCode: customerLanguage,
                ActionUrl: $"{publicAppUrls.Value.WebBaseUrl.TrimEnd('/')}/objednavka/{order.Id}");
            outbox.Enqueue(
                aggregateId: order.Id,
                eventType: OutboxEventTypes.OrderRefundedCustomerEmail,
                payloadJson: JsonSerializer.Serialize(payload));

            // Step 8: UoW pipeline commits mutation + outbox + audit row
            // atomically (ADR 0014).
            return BusinessResult.Success(BuildResponse(order));
        }

        private static RefundOrderResponse BuildResponse(Order order) => new(
            OrderId: order.Id,
            State: order.State,
            RefundedAmountMinor: order.RefundedAmountMinor,
            RemainingRefundableMinor: order.RemainingRefundableMinor,
            IsFullRefund: order.State == OrderState.Refunded);
    }
}
