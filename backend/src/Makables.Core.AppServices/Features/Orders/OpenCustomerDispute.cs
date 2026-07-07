using System.Text.Json;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Customer opens a dispute on their own order (T-0106 / US-admin-0011
/// AC-1). Flips the order into the <see cref="OrderState.Disputed"/>
/// parenthesis state (stamping <c>PreDisputeState</c>), records a
/// <see cref="Dispute"/> row with the customer's own words, and notifies
/// the admin mailbox via the outbox — all atomically under the UoW
/// pipeline.
///
/// <para>
/// <b>IDOR shield:</b> the customer-scoped repository predicate IS the
/// authorization — cross-customer probes get the same
/// <c>404 order.notFound</c> as unknown ids (ADR 0013).
/// <b>Idempotency (§C.4):</b> re-opening an already-Disputed order is
/// Silent-Success returning the EXISTING open dispute's id — no second
/// row (the partial unique index also enforces it), no second outbox
/// emission. <b>Carrier categories are carrier-reserved (§C.6):</b> the
/// Validator rejects them.
/// </para>
/// </summary>
public static class OpenCustomerDispute
{
    /// <summary>
    /// T-0145 (dopady §2.5 Q6–Q9): the customer's platform dispute button
    /// only works within this many days of <c>Order.DeliveredAt</c>.
    /// Applies ONLY here — see the Handler guard + the ticket's Alternatives
    /// Considered Option A for why the other three openers are untouched.
    /// </summary>
    public const int OpenWindowDays = 14;

    public sealed record Command(string OrderId, DisputeCategory Category, string Description)
        : ICommand<OpenCustomerDisputeResponse>;

    /// <summary>
    /// <b>Globally-unique name</b> per the post-PR-#38 NSwag convention.
    /// </summary>
    public sealed record OpenCustomerDisputeResponse(string OrderId, string DisputeId);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.Category)
                .Cascade(CascadeMode.Stop)
                .IsInEnum().WithErrorCode(BusinessErrorMessage.InvalidEnumValue)
                .Must(c => c is not (DisputeCategory.CarrierReturned or DisputeCategory.CarrierFailed))
                .WithErrorCode(BusinessErrorMessage.OrderDisputeCategoryNotAllowed);

            RuleFor(c => c.Description)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(Dispute.MaxDescriptionLength).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IDisputeRepository disputes,
        IOutbox outbox,
        IClock clock,
        IIdGenerator ids,
        IUserSessionProvider session,
        ILanguageResolver languageResolver,
        IOptions<PublicAppUrlsOptions> publicAppUrls,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BusinessResult<OpenCustomerDisputeResponse>>
    {
        public async Task<BusinessResult<OpenCustomerDisputeResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: Session principal — the customer id NEVER comes from
            // the request (IDOR warning on IOrderRepository.ForCustomer).
            var customerUserId = session.GetUserId();
            if (string.IsNullOrEmpty(customerUserId))
            {
                return BusinessResult.Failure<OpenCustomerDisputeResponse>(Error.Unauthorized());
            }

            // Step 2: IDOR-scoped order load — null for unknown AND
            // cross-customer ids, same 404 shape (no existence oracle).
            var order = await orders.GetByIdForCustomerAsync(
                command.OrderId, customerUserId, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<OpenCustomerDisputeResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 3: Silent-Success on re-open (§C.4) — return the
            // EXISTING open dispute's id; no mutation, no outbox.
            if (order.State == OrderState.Disputed)
            {
                return await ExistingOpenDisputeAsync(order, disputes, logger, cancellationToken);
            }

            // Step 3.5: T-0145 14-day open-window guard — ONLY meaningful
            // once the order has actually been delivered (AC-1/2). Paid /
            // Accepted / Shipped have no DeliveredAt anchor yet and sail
            // through unchanged (AC-3); the other three openers never call
            // this handler (AC-4).
            if (order.State == OrderState.Delivered
                && order.DeliveredAt is not null
                && clock.UtcNow > order.DeliveredAt.Value.AddDays(OpenWindowDays))
            {
                return BusinessResult.Failure<OpenCustomerDisputeResponse>(
                    Error.Conflict("orderId", BusinessErrorMessage.OrderDisputeWindowExpired));
            }

            // Step 4: Parenthesis-state flip (stamps PreDisputeState).
            var transition = order.OpenDispute(clock);
            if (!transition.IsSuccess)
            {
                return BusinessResult.Failure<OpenCustomerDisputeResponse>(transition.Error!);
            }

            // Step 5: Dispute row — Source is stamped server-side.
            var dispute = Dispute.Open(
                id: ids.Next(),
                orderId: order.Id,
                category: command.Category,
                description: command.Description,
                source: DisputeSource.Customer,
                countryCode: order.CountryCode);
            await disputes.AddAsync(dispute, cancellationToken);

            // Step 6: Admin notification (recipient resolves at SEND time
            // from EmailOptions — §C.9; never blocks the open).
            await EnqueueAdminEmailAsync(
                outbox, languageResolver, publicAppUrls.Value, order, dispute, cancellationToken);

            // Step 7: UoW commits flip + row + outbox atomically.
            return BusinessResult.Success(new OpenCustomerDisputeResponse(order.Id, dispute.Id));
        }

        /// <summary>
        /// Shared §C.4 Silent-Success shape. A Disputed order without an
        /// open dispute row violates the partial-unique-index invariant —
        /// Critical log + the invalid-transition conflict.
        /// </summary>
        internal static async Task<BusinessResult<OpenCustomerDisputeResponse>> ExistingOpenDisputeAsync(
            Order order,
            IDisputeRepository disputes,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var existing = await disputes.GetOpenByOrderIdAsync(order.Id, cancellationToken);
            if (existing is null)
            {
                logger.LogCritical(
                    "OpenCustomerDispute: order {OrderId} is Disputed but has NO open dispute row. " +
                    "Invariant violation — refusing the idempotent return.",
                    order.Id);
                return BusinessResult.Failure<OpenCustomerDisputeResponse>(
                    Error.Conflict("state", BusinessErrorMessage.OrderInvalidTransition));
            }

            logger.LogInformation(
                "OpenCustomerDispute: order {OrderId} already Disputed; returning open dispute {DisputeId}.",
                order.Id, existing.Id);
            return BusinessResult.Success(new OpenCustomerDisputeResponse(order.Id, existing.Id));
        }

        /// <summary>
        /// Enqueue the admin "dispute opened" digest. Shared verbatim by
        /// the maker / carrier / admin open variants so the payload shape
        /// cannot drift between sources.
        /// </summary>
        internal static async Task EnqueueAdminEmailAsync(
            IOutbox outbox,
            ILanguageResolver languageResolver,
            PublicAppUrlsOptions urls,
            Order order,
            Dispute dispute,
            CancellationToken cancellationToken)
        {
            // Admin digest language = the order country's default (admins
            // are platform staff; no per-admin user to resolve against).
            var language = await languageResolver.ResolveAsync(
                preferredLanguage: null, countryCode: order.CountryCode, cancellationToken);
            var payload = new OrderDisputedAdminEmailPayload(
                OrderId: order.Id,
                OrderNumber: order.OrderNumber,
                DisputeId: dispute.Id,
                Category: dispute.Category,
                Description: dispute.Description,
                Source: dispute.Source,
                LanguageCode: language,
                ActionUrl: $"{urls.WebBaseUrl.TrimEnd('/')}/dashboard/admin/orders/{order.Id}");
            outbox.Enqueue(
                aggregateId: order.Id,
                eventType: OutboxEventTypes.OrderDisputedAdminEmail,
                payloadJson: JsonSerializer.Serialize(payload));
        }
    }
}
