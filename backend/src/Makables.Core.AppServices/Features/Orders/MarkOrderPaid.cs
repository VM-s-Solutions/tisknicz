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
/// Transition a <see cref="Order"/> from
/// <see cref="OrderState.PendingPayment"/> to <see cref="OrderState.Paid"/>
/// in response to a verified Comgate webhook, persist the provider's
/// payment-method + capture timestamp, and emit the customer +
/// maker email outbox events. Dispatched only by
/// <c>ComgateWebhookController</c> after IP allowlist + re-fetch +
/// ref-mismatch checks have passed.
///
/// <para>
/// <b>T-0067 + T-0068b scope.</b> The handler does the state transition
/// AND enqueues exactly three outbox events:
/// <list type="bullet">
///   <item><see cref="OutboxEventTypes.OrderPaidCustomerEmail"/> — customer "thanks" email.</item>
///   <item><see cref="OutboxEventTypes.OrderPlacedMakerEmail"/> — maker "new order" email.</item>
///   <item><see cref="OutboxEventTypes.InvoiceGenerate"/> — T-0069's
///     GenerateInvoiceFunction picks it up and dispatches IssueInvoice
///     via Mediator (T-0068b locked decision 7). Routes to a separate
///     queue from the email events so a render/upload failure cannot
///     contaminate the email-send retry budget.</item>
/// </list>
/// Both email payloads carry pre-baked action URLs (Q4) and pre-resolved
/// language codes (T-0028 pattern); the invoice-generate payload carries
/// the customer's resolved language so T-0069's attachment step knows
/// which filename to use.
/// </para>
///
/// <para>
/// <b>Atomicity.</b> The order mutation (State → Paid, PaymentProviderRef,
/// PaymentMethod, PaidAt) AND the 3 outbox rows ship in a single Postgres
/// transaction via <c>UnitOfWorkPipelineBehavior</c> per ADR 0014. If
/// anything fails, nothing commits; the webhook returns a failure and
/// Comgate retries — we never end up with "order is Paid but no email
/// queued" / "no invoice queued" or vice versa.
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
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IUserRepository users,
        IMakerRepository makers,
        IOutbox outbox,
        IClock clock,
        ILanguageResolver languageResolver,
        IOptions<PublicAppUrlsOptions> publicAppUrls,
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
            // enforces PendingPayment → Paid + the set-once invariants on
            // PaymentProviderRef AND PaymentMethod. Race-loser (a second
            // webhook arriving after the first already transitioned)
            // surfaces as OrderInvalidTransition; the controller maps that
            // to 200.
            var transitionResult = order.MarkAsPaid(
                clock,
                command.ProviderRef,
                command.PaymentMethod,
                command.PaidAt);
            if (!transitionResult.IsSuccess)
            {
                return BusinessResult.Failure<Response>(transitionResult.Error!);
            }

            // Step 4: Enqueue the customer "thanks for your order" email.
            // Language resolved at enqueue time (T-0028 pattern); ActionUrl
            // pre-baked from the configured WebBaseUrl (Q4) so the consumer
            // never has to know the URL convention.
            //
            // T-0067 reviewer M-3: customer-user row must exist (FK
            // invariant). If it's null something is genuinely corrupt — we
            // refuse to commit (symmetric with the maker-user-missing path
            // below) and let Comgate retry while ops investigates.
            var urls = publicAppUrls.Value;
            var customer = await users.GetByIdAsync(order.CustomerUserId, cancellationToken);
            if (customer is null)
            {
                logger.LogCritical(
                    "MarkOrderPaid: customer user {UserId} not found for order {OrderId}. " +
                    "FK invariant violation — refusing to commit.",
                    order.CustomerUserId, order.Id);
                return BusinessResult.Failure<Response>(
                    Error.NotFound("customerUserId", BusinessErrorMessage.OrderCustomerUserMissing));
            }
            var customerLanguage = await languageResolver.ResolveForUserAsync(customer, cancellationToken);
            var customerPayload = new OrderPaidCustomerEmailPayload(
                OrderId: order.Id,
                OrderNumber: order.OrderNumber,
                Email: order.ContactEmail,
                ContactName: order.ContactName,
                TotalAmountMinor: order.TotalAmountMinor,
                Currency: order.Currency,
                LanguageCode: customerLanguage,
                ActionUrl: $"{urls.WebBaseUrl.TrimEnd('/')}/objednavka/{order.Id}");
            outbox.Enqueue(
                aggregateId: order.Id,
                eventType: OutboxEventTypes.OrderPaidCustomerEmail,
                payloadJson: JsonSerializer.Serialize(customerPayload));

            // Step 5: Enqueue the maker "new order arrived" email. The
            // maker aggregate has no separate notification email; the
            // linked User.Email is the channel. Language pre-resolved
            // from the maker's user account.
            var maker = await makers.GetByIdAsync(order.MakerId, cancellationToken);
            if (maker is null)
            {
                // Order has a Maker FK at creation; a null here means the
                // maker was soft-deleted between order creation and now.
                // Refuse to commit because the maker email would silently
                // drop on the floor.
                logger.LogCritical(
                    "MarkOrderPaid: maker {MakerId} not found for order {OrderId}. " +
                    "Soft-deleted between order creation and webhook? — refusing to commit.",
                    order.MakerId, order.Id);
                return BusinessResult.Failure<Response>(
                    Error.NotFound("makerId", BusinessErrorMessage.MakerNotFound));
            }
            var makerUser = await users.GetByIdAsync(maker.UserId, cancellationToken);
            if (makerUser is null)
            {
                // T-0067 reviewer M-2: distinct error code from MakerNotFound
                // so ops triage separates "maker soft-deleted" from "maker
                // exists but its user row is missing" (genuine FK invariant
                // violation, much more alarming).
                logger.LogCritical(
                    "MarkOrderPaid: maker {MakerId} linked user {UserId} not found for order {OrderId}. " +
                    "FK invariant violation — refusing to commit, maker email would silently drop.",
                    maker.Id, maker.UserId, order.Id);
                return BusinessResult.Failure<Response>(
                    Error.NotFound("makerUserId", BusinessErrorMessage.MakerUserMissing));
            }
            var makerLanguage = await languageResolver.ResolveForUserAsync(makerUser, cancellationToken);
            var makerPayload = new OrderPlacedMakerEmailPayload(
                OrderId: order.Id,
                OrderNumber: order.OrderNumber,
                MakerId: maker.Id,
                MakerEmail: makerUser.Email,
                TotalAmountMinor: order.TotalAmountMinor,
                Currency: order.Currency,
                LanguageCode: makerLanguage,
                ActionUrl: $"{urls.WebBaseUrl.TrimEnd('/')}/dashboard/maker/objednavky/{order.Id}");
            outbox.Enqueue(
                aggregateId: order.Id,
                eventType: OutboxEventTypes.OrderPlacedMakerEmail,
                payloadJson: JsonSerializer.Serialize(makerPayload));

            // Step 6: Enqueue the invoice-generate event (T-0068b). The
            // T-0069 GenerateInvoiceFunction consumes this off a separate
            // queue (NOT the send-email queue — see
            // OutboxEventTypes.IsEmailSend) and dispatches IssueInvoice
            // via Mediator. LanguageCode is pre-resolved here so T-0069's
            // invoice-email attachment can name the PDF in the customer's
            // language without a second resolution. Per T-0068b locked
            // decisions 7 + 10.
            var invoicePayload = new InvoiceGenerateOutboxPayload(
                OrderId: order.Id,
                LanguageCode: customerLanguage);
            outbox.Enqueue(
                aggregateId: order.Id,
                eventType: OutboxEventTypes.InvoiceGenerate,
                payloadJson: JsonSerializer.Serialize(invoicePayload));

            // Step 7: No SaveChangesAsync — UoW pipeline behavior commits
            // the order mutation AND the 3 outbox rows atomically per ADR
            // 0014 (patterns §A.20).
            return BusinessResult.Success(new Response(order.Id));
        }
    }
}
