using System.Text.RegularExpressions;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Services;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Numbering;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Products;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Create a customer order in <see cref="OrderState.PendingPayment"/>
/// per T-0063 / US-customer-0010. Mirrors the RegisterMaker shape
/// (single static class with nested Command / Response / Validator /
/// Handler), 8-step happy-path flow:
///
/// <list type="number">
///   <item>Resolve customer identity from
///     <see cref="IUserSessionProvider"/>. Backstop guard — the host's
///     <c>[Authorize]</c> middleware should have returned 401 already;
///     arriving here without a session means the handler was reached
///     outside the controller path (a cron / Functions caller without a
///     session).</item>
///   <item>Load <see cref="Product"/> (TOCTOU pre-check). Missing →
///     <see cref="BusinessErrorMessage.ProductNotFound"/>. Soft-deleted
///     (handled by the global query filter — null lookup) and active=false
///     are both surfaced as
///     <see cref="BusinessErrorMessage.ProductNotActive"/>.</item>
///   <item>Load <see cref="Maker"/>, defence-in-depth on all maker-state
///     gates per user decision Q4. Missing / inactive →
///     <see cref="BusinessErrorMessage.MakerDeactivated"/>; not verified
///     → <see cref="BusinessErrorMessage.MakerNotVerified"/>; chose
///     <see cref="ShippingMethod.PersonalPickup"/> but
///     <see cref="Maker.PersonalPickupEnabled"/> is false →
///     <see cref="BusinessErrorMessage.MakerPersonalPickupDisabled"/>.</item>
///   <item>Compute pricing via
///     <see cref="IPricingService.ComputeForProductAsync"/>. Failures
///     (<see cref="BusinessErrorMessage.ProductNotOrderable"/>,
///     <see cref="BusinessErrorMessage.CountryConfigurationNotFound"/>,
///     <see cref="BusinessErrorMessage.ProductNotFound"/>) propagate
///     verbatim.</item>
///   <item>Reserve the order number via
///     <see cref="IOrderNumberGenerator.NextAsync(string,CancellationToken)"/>
///     — T-0062 TZ-aware signature, no <c>int year</c> parameter (the
///     generator derives the local year from the country's
///     <c>CountryConfiguration.TimeZoneId</c>).</item>
///   <item>Build the aggregate via <see cref="Order.Create"/>. Trim every
///     customer-supplied string; pricing snapshot comes from the
///     <see cref="PricingBreakdown"/> the service returned (every
///     <c>.AmountMinor</c> + the shared <c>.Currency</c>).</item>
///   <item>Persist via <see cref="IOrderRepository.AddAsync"/>. No
///     <c>SaveChangesAsync</c>; the
///     <c>UnitOfWorkPipelineBehavior</c> commits.</item>
///   <item>Return <see cref="Response"/> with the four fields the
///     frontend needs to navigate to <c>/objednavka/&lt;id&gt;</c> and
///     trigger T-0065's <c>CreatePaymentSession</c>.</item>
/// </list>
///
/// <para>
/// <b>Out of scope.</b> No Comgate call (T-0065 follow-up — user
/// decision Q1), no attachments upload (T-0064 follow-up — Q3), no
/// outbox <c>order-placed</c> event (T-0067 emits it when the order is
/// actually paid). Idempotency is a frontend concern per user decision
/// Q2 (disabled-button + in-flight guard); the backend does no
/// dedup.
/// </para>
///
/// <para>
/// <b>IDOR.</b> <see cref="IUserSessionProvider.GetUserId"/> is the
/// only source for <c>customerUserId</c>; the controller never accepts
/// it from the body or path.
/// </para>
/// </summary>
public static class CreateOrder
{
    public sealed record Command(
        string ProductId,
        int Quantity,
        ShippingMethod ShippingMethod,
        string? ZasilkovnaPickupPointId,
        string CustomerName,
        string CustomerEmail,
        string CustomerPhone,
        string? CustomerNotes)
        : ICommand<Response>;

    public sealed record Response(
        string OrderId,
        string OrderNumber,
        long TotalPriceMinor,
        string Currency);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            // Stop on the first failing rule per field — every consumer
            // (frontend form) only renders one message per field anyway,
            // and chaining length-after-format on a blank value is noisy.
            RuleFor(c => c.ProductId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(64).WithErrorCode(BusinessErrorMessage.MaxLength);

            // Quantity == 1 is the MVP invariant (T-0061 user decision Q4).
            // Fail LOUD here rather than silently truncating in the handler —
            // a buggy client sending 2 should learn about it, not have a
            // surprise single-item order.
            RuleFor(c => c.Quantity)
                .Equal(1).WithErrorCode(BusinessErrorMessage.OrderInvalidQuantity);

            RuleFor(c => c.ShippingMethod)
                .IsInEnum().WithErrorCode(BusinessErrorMessage.InvalidEnumValue);

            When(c => c.ShippingMethod == ShippingMethod.ZasilkovnaPickupPoint, () =>
            {
                RuleFor(c => c.ZasilkovnaPickupPointId)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                    .MaximumLength(64).WithErrorCode(BusinessErrorMessage.MaxLength);
            });

            RuleFor(c => c.CustomerName)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MinimumLength(2).WithErrorCode(BusinessErrorMessage.MinLength)
                .MaximumLength(100).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.CustomerEmail)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .EmailAddress().WithErrorCode(BusinessErrorMessage.InvalidEmailFormat)
                .MaximumLength(254).WithErrorCode(BusinessErrorMessage.MaxLength);

            // Czech mobile / landline pattern. Matches `+420 9XX XXX XXX`,
            // `9XX XXX XXX`, with or without spaces. Drafted on T-0063;
            // PM to review on the PR — if a tighter/looser pattern is
            // preferred, swap the character class. T-0030 Address
            // validation uses a different field; we keep the regex local
            // to the order command so phone format on a future
            // self-service profile edit doesn't inherit it accidentally.
            RuleFor(c => c.CustomerPhone)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .Matches(CzechPhoneRegex.Pattern())
                    .WithErrorCode(BusinessErrorMessage.InvalidPhoneFormat);

            // CustomerNotes is optional; only length-bound when present.
            // 2000 chars matches Order.MaxCustomerNotesLength — drafted
            // on T-0063, PM to review on the PR.
            When(c => c.CustomerNotes is not null, () =>
            {
                RuleFor(c => c.CustomerNotes!)
                    .MaximumLength(2000).WithErrorCode(BusinessErrorMessage.MaxLength);
            });
        }
    }

    public sealed class Handler(
        IUserSessionProvider session,
        IProductRepository products,
        IMakerRepository makers,
        IPricingService pricing,
        IOrderNumberGenerator orderNumbers,
        IOrderRepository orders,
        IIdGenerator ids,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            // 1. Resolve customer identity. Backstop — the host's
            //    [Authorize] middleware should have returned 401 already.
            var customerUserId = session.GetUserId();
            if (string.IsNullOrEmpty(customerUserId))
            {
                return BusinessResult.Failure<Response>(Error.Unauthorized());
            }

            // 2. Load product (TOCTOU pre-check). The global soft-delete
            //    query filter on Auditable already hides deleted rows;
            //    a null lookup AND IsActive == false both surface as
            //    ProductNotActive so the customer's UX is consistent
            //    whether the maker just deactivated or whether the row is
            //    being soft-purged.
            var product = await products.GetByIdAsync(command.ProductId, cancellationToken);
            if (product is null)
            {
                return BusinessResult.Failure<Response>(
                    Error.NotFound("productId", BusinessErrorMessage.ProductNotFound));
            }
            if (!product.IsActive)
            {
                return BusinessResult.Failure<Response>(
                    Error.Conflict("productId", BusinessErrorMessage.ProductNotActive));
            }

            // 3. Load maker; defence-in-depth on all maker-state gates
            //    (user decision Q4). The frontend gate alone isn't enough —
            //    a customer with a stale cache, a curl client, or a
            //    cross-tab race could otherwise sneak an order against a
            //    deactivated / unverified / pickup-disabled maker.
            var maker = await makers.GetByIdAsync(product.MakerId, cancellationToken);
            if (maker is null || !maker.IsActive)
            {
                return BusinessResult.Failure<Response>(
                    Error.Conflict("makerId", BusinessErrorMessage.MakerDeactivated));
            }
            if (!maker.IsVerified)
            {
                return BusinessResult.Failure<Response>(
                    Error.Conflict("makerId", BusinessErrorMessage.MakerNotVerified));
            }
            if (command.ShippingMethod == ShippingMethod.PersonalPickup
                && !maker.PersonalPickupEnabled)
            {
                return BusinessResult.Failure<Response>(
                    Error.Conflict("shippingMethod", BusinessErrorMessage.MakerPersonalPickupDisabled));
            }

            // 4. Compute pricing via IPricingService. The service surfaces
            //    ProductNotOrderable (PriceType == OnRequest),
            //    CountryConfigurationNotFound and ProductNotFound (the
            //    last is normally beaten by step 2 but is repeated here
            //    for the race-window where the row is deactivated between
            //    our load and the service's load — propagate verbatim
            //    either way).
            var pricingResult = await pricing.ComputeForProductAsync(
                command.ProductId, command.ShippingMethod, cancellationToken);
            if (!pricingResult.IsSuccess)
            {
                return BusinessResult.Failure<Response>(pricingResult.Error!);
            }
            var breakdown = pricingResult.Value!;

            // 5. Reserve order number. T-0062 contract — TZ-aware year
            //    derived from the country's CountryConfiguration.TimeZoneId,
            //    no `int year` parameter on the wire. The generator opens
            //    a FOR UPDATE row lock under the surrounding UoW
            //    transaction so concurrent CreateOrder calls don't
            //    duplicate.
            var orderNumber = await orderNumbers.NextAsync(product.CountryCode, cancellationToken);

            // 6. Build the aggregate. Trim every customer-supplied string;
            //    Order.Create re-trims defensively but doing it here keeps
            //    the snapshot canonical (e.g. log messages, downstream
            //    serialisation).
            var order = Order.Create(
                id: ids.Next(),
                orderNumber: orderNumber,
                customerUserId: customerUserId,
                makerId: maker.Id,
                productId: product.Id,
                contactName: command.CustomerName.Trim(),
                contactEmail: command.CustomerEmail.Trim(),
                contactPhone: command.CustomerPhone.Trim(),
                productPriceAmountMinor: breakdown.ProductPrice.AmountMinor,
                shippingPriceAmountMinor: breakdown.ShippingPrice.AmountMinor,
                platformFeeAmountMinor: breakdown.PlatformFee.AmountMinor,
                makerPayoutAmountMinor: breakdown.MakerPayout.AmountMinor,
                totalAmountMinor: breakdown.TotalPrice.AmountMinor,
                currency: breakdown.TotalPrice.Currency,
                vatRateBp: breakdown.VatRateBp,
                shippingMethod: command.ShippingMethod,
                zasilkovnaPickupPointId: command.ZasilkovnaPickupPointId?.Trim(),
                countryCode: product.CountryCode,
                customerNotes: command.CustomerNotes?.Trim());

            // 7. Persist. The UnitOfWorkPipelineBehavior commits when the
            //    handler returns a successful BusinessResult.
            await orders.AddAsync(order, cancellationToken);

            logger.LogInformation(
                "Order {OrderId} ({OrderNumber}) created in PendingPayment for customer {CustomerId}.",
                order.Id, order.OrderNumber, customerUserId);

            // 8. Return the four fields the frontend uses to navigate +
            //    trigger T-0065's payment-session creation.
            return BusinessResult.Success(new Response(
                OrderId: order.Id,
                OrderNumber: order.OrderNumber,
                TotalPriceMinor: order.TotalAmountMinor,
                Currency: order.Currency));
        }
    }
}

/// <summary>
/// Source-generated Czech phone-number regex used by
/// <see cref="CreateOrder.Validator"/>. Internal to the feature so a
/// future self-service profile edit doesn't accidentally inherit it —
/// each call site re-declares its own pattern locally per T-0063.
///
/// <para>
/// Matches <c>+420 9XX XXX XXX</c>, <c>9XX XXX XXX</c>, with or without
/// spaces. Mobile (<c>6</c>/<c>7</c>) and most landline (<c>9</c>) prefixes.
/// PM to review on the PR — swap the character class if a tighter or
/// looser pattern is preferred.
/// </para>
/// </summary>
internal static partial class CzechPhoneRegex
{
    [GeneratedRegex(@"^(\+420\s?)?[6-9]\d{2}\s?\d{3}\s?\d{3}$")]
    public static partial Regex Pattern();
}
