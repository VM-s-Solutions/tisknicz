using System.Text.Json;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Payouts;
using Makables.Core.Domain.Shipping;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// T-0146 (US-customer-0023). Admin triggers "Vygenerovat vratkový
/// štítek" on an open <see cref="Dispute"/> in a return-warranting
/// category (<see cref="DisputeCategory.DamagedItem"/> /
/// <see cref="DisputeCategory.NotAsDescribed"/>, AC-6). Mirrors the
/// admin-gated posture of <see cref="RefundOrder"/> — every other
/// money/logistics-affecting dispute outcome in this model is
/// admin-triggered, never automatic (Alternatives Considered Option A).
///
/// <para>
/// <b>Sequencing.</b> Resolve the order + maker + maker's registered
/// address → resolve the carrier for the order's country → create the
/// reverse Packeta shipment (customer's address as sender, maker's
/// address as recipient) → stamp <see cref="Dispute.ReturnCarrierRef"/> /
/// <see cref="Dispute.ReturnTrackingUrl"/> → enqueue the
/// <see cref="OutboxEventTypes.ShippingGenerateReturnLabel"/> label-fetch
/// event (T-0074 pattern, distinct blob path) → record a
/// <see cref="PayoutDeduction"/> for the maker-borne return cost (Q-0037:
/// deducted from the maker's NEXT payout batch, never a customer charge —
/// AC-2). Idempotent — a re-run against a dispute that already has a
/// return shipment is Silent Success (same shape as
/// <see cref="Dispute.SetReturnShipment"/>'s set-once contract) and does
/// NOT create a second deduction.
/// </para>
///
/// <para>
/// <b>Cost basis.</b> Packeta's v6 <c>createPacket</c> response doesn't
/// itemize a reverse-leg price at MVP, so the deduction amount is
/// <c>CountryConfiguration.DefaultShippingPriceMinor</c> — the platform's
/// existing shipping-cost stand-in (Technical notes).
/// </para>
/// </summary>
public static class GenerateReturnLabel
{
    public sealed record Command(string DisputeId)
        : ICommand<GenerateReturnLabelResponse>, IAdminAuditableCommand
    {
        public string ActionCode => "dispute.return.generateLabel";
        public string TargetEntity => "dispute";
        public string TargetId => DisputeId;
        public string? Notes => null;
    }

    /// <summary>
    /// <b>Globally-unique name</b> per the post-PR-#38 NSwag convention.
    /// </summary>
    public sealed record GenerateReturnLabelResponse(
        string DisputeId,
        string CarrierRef,
        string TrackingUrl,
        bool AlreadyExisted);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.DisputeId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IDisputeRepository disputes,
        IOrderRepository orders,
        IMakerRepository makers,
        IUserRepository users,
        IAddressRepository addresses,
        ICountryConfigurationRepository countries,
        IShippingCarrierFactory shippingCarrierFactory,
        IPayoutDeductionRepository payoutDeductions,
        IOutbox outbox,
        IIdGenerator idGenerator,
        IUserSessionProvider session,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BusinessResult<GenerateReturnLabelResponse>>
    {
        private static readonly IReadOnlySet<DisputeCategory> ReturnWarrantingCategories =
            new HashSet<DisputeCategory> { DisputeCategory.DamagedItem, DisputeCategory.NotAsDescribed };

        public async Task<BusinessResult<GenerateReturnLabelResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: fail-closed session check (RefundOrder precedent).
            if (string.IsNullOrEmpty(session.GetUserId()))
            {
                return BusinessResult.Failure<GenerateReturnLabelResponse>(Error.Unauthorized());
            }

            // Step 2: tracked unscoped dispute load (admin host, ADR 0013).
            var dispute = await disputes.GetByIdUnscopedAsync(command.DisputeId, cancellationToken);
            if (dispute is null)
            {
                return BusinessResult.Failure<GenerateReturnLabelResponse>(
                    Error.NotFound("disputeId", BusinessErrorMessage.OrderDisputeNotFound));
            }

            // Step 3: idempotent re-run — a return shipment already exists.
            // Silent Success, no second PayoutDeduction.
            if (dispute.ReturnCarrierRef is not null)
            {
                return BusinessResult.Success(new GenerateReturnLabelResponse(
                    dispute.Id, dispute.ReturnCarrierRef, dispute.ReturnTrackingUrl!, AlreadyExisted: true));
            }

            // Step 4: category gate (AC-6) — only categories that plausibly
            // warrant a physical return offer this action.
            if (!ReturnWarrantingCategories.Contains(dispute.Category))
            {
                return BusinessResult.Failure<GenerateReturnLabelResponse>(
                    Error.Validation("category", BusinessErrorMessage.DisputeReturnCategoryNotEligible));
            }

            // Step 5: resolve the order (customer contact + country).
            var order = await orders.GetByIdUnscopedAsync(dispute.OrderId, cancellationToken);
            if (order is null)
            {
                logger.LogCritical(
                    "GenerateReturnLabel: dispute {DisputeId} has no order {OrderId}. Invariant violation.",
                    dispute.Id, dispute.OrderId);
                return BusinessResult.Failure<GenerateReturnLabelResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 6: resolve the maker + its recipient details (registered
            // address per Technical notes — makers aren't Zásilkovna box
            // holders, so the reverse leg is a door-delivery address).
            var maker = await makers.GetByIdAsync(order.MakerId, cancellationToken);
            if (maker is null)
            {
                logger.LogCritical(
                    "GenerateReturnLabel: order {OrderId} has no maker {MakerId}. Invariant violation.",
                    order.Id, order.MakerId);
                return BusinessResult.Failure<GenerateReturnLabelResponse>(
                    Error.NotFound("makerId", BusinessErrorMessage.OrderNotFound));
            }

            var makerAddress = await addresses.GetByIdAsync(maker.RegisteredAddressId, cancellationToken);
            if (makerAddress is null)
            {
                logger.LogCritical(
                    "GenerateReturnLabel: maker {MakerId} has no registered address {AddressId}. Invariant violation.",
                    maker.Id, maker.RegisteredAddressId);
                return BusinessResult.Failure<GenerateReturnLabelResponse>(
                    Error.Permanent(BusinessErrorMessage.ShippingCarrierAddressIdNotFound));
            }

            var makerUser = await users.GetByIdAsync(maker.UserId, cancellationToken);
            if (makerUser is null)
            {
                logger.LogCritical(
                    "GenerateReturnLabel: maker {MakerId} has no user {UserId}. Invariant violation.",
                    maker.Id, maker.UserId);
                return BusinessResult.Failure<GenerateReturnLabelResponse>(
                    Error.NotFound("userId", BusinessErrorMessage.OrderNotFound));
            }
            if (string.IsNullOrWhiteSpace(makerUser.Phone))
            {
                logger.LogError(
                    "GenerateReturnLabel: maker {MakerId} has no phone on file — cannot address the reverse label.",
                    maker.Id);
                return BusinessResult.Failure<GenerateReturnLabelResponse>(
                    Error.Configuration(BusinessErrorMessage.ShippingCarrierConfigurationError));
            }

            var recipient = new ReturnRecipient(
                Name: maker.CompanyName,
                Email: makerUser.Email,
                Phone: makerUser.Phone,
                Street: makerAddress.Street,
                HouseNumber: makerAddress.HouseNumber,
                City: makerAddress.City,
                Zip: makerAddress.Zip,
                CountryCodeIso: makerAddress.CountryCodeIso);

            // Step 7: resolve carrier + create the reverse shipment.
            var carrierResult = await shippingCarrierFactory.ResolveAsync(order.CountryCode, cancellationToken);
            if (!carrierResult.IsSuccess)
            {
                return BusinessResult.Failure<GenerateReturnLabelResponse>(carrierResult.Error!);
            }

            var shipmentResult = await carrierResult.Value!.CreateReturnShipmentAsync(
                order, recipient, cancellationToken);
            if (!shipmentResult.IsSuccess)
            {
                return BusinessResult.Failure<GenerateReturnLabelResponse>(shipmentResult.Error!);
            }
            var shipment = shipmentResult.Value!;

            // Step 8: stamp the dispute (set-once invariant).
            var setResult = dispute.SetReturnShipment(shipment.CarrierRef, shipment.TrackingUrl);
            if (!setResult.IsSuccess)
            {
                return BusinessResult.Failure<GenerateReturnLabelResponse>(setResult.Error!);
            }

            // Step 9: enqueue the label-fetch event (T-0074 pattern, reverse
            // blob path, shared generate-label queue per OutboxEventTypes.IsGenerateLabel).
            var labelPayload = new GenerateReturnLabelOutboxPayload(dispute.Id);
            outbox.Enqueue(
                aggregateId: dispute.Id,
                eventType: OutboxEventTypes.ShippingGenerateReturnLabel,
                payloadJson: JsonSerializer.Serialize(labelPayload));

            // Step 10: record the maker-borne cost as a payout-batch
            // deduction (Q-0037 resolution) — cost basis is the country's
            // DefaultShippingPriceMinor stand-in (Packeta doesn't itemize
            // the reverse leg). Never a customer charge (AC-2).
            var config = await countries.GetByCodeAsync(order.CountryCode, cancellationToken);
            if (config is null)
            {
                logger.LogCritical(
                    "GenerateReturnLabel: CountryConfiguration {CountryCode} not found — refusing to commit.",
                    order.CountryCode);
                return BusinessResult.Failure<GenerateReturnLabelResponse>(
                    Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigMissing));
            }

            if (config.DefaultShippingPriceMinor > 0)
            {
                var deduction = PayoutDeduction.Create(
                    id: idGenerator.Next(),
                    makerId: maker.Id,
                    disputeId: dispute.Id,
                    reason: PayoutDeductionReason.ReturnShippingCost,
                    amountMinor: config.DefaultShippingPriceMinor,
                    currency: config.DefaultCurrencyCode,
                    countryCode: order.CountryCode);
                await payoutDeductions.AddAsync(deduction, cancellationToken);
            }

            logger.LogInformation(
                "GenerateReturnLabel: reverse shipment created for dispute {DisputeId} (carrierRef={CarrierRef}).",
                dispute.Id, shipment.CarrierRef);

            // UoW commits the dispute mutation + outbox row + payout
            // deduction + admin audit atomically (ADR 0014).
            return BusinessResult.Success(new GenerateReturnLabelResponse(
                dispute.Id, shipment.CarrierRef, shipment.TrackingUrl, AlreadyExisted: false));
        }
    }
}
