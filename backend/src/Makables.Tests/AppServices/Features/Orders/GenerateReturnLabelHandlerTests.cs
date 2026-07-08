using FluentAssertions;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Payouts;
using Makables.Core.Domain.Shipping;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// T-0146 <see cref="GenerateReturnLabel.Handler"/> contract: the
/// category gate (AC-6 — only <see cref="DisputeCategory.DamagedItem"/> /
/// <see cref="DisputeCategory.NotAsDescribed"/> are eligible), the
/// reverse-shipment creation + set-once stamp (AC-1), the transient-error
/// classification passthrough (AC-4), idempotent re-run (no second
/// PayoutDeduction), and the payout-batch deduction recorded for the
/// maker-borne cost (AC-2 / Q-0037).
/// </summary>
public class GenerateReturnLabelHandlerTests
{
    private const string DisputeId = "disp-1";
    private const string OrderId = "ord-1";
    private const string TestMakerId = "maker-1";
    private const string TestMakerUserId = "user-maker-1";
    private const string AddressId = "addr-1";
    private const string AdminUserId = "user-admin-1";
    private const string CarrierRef = "112233445";

    private readonly IDisputeRepository _disputes = Substitute.For<IDisputeRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IAddressRepository _addresses = Substitute.For<IAddressRepository>();
    private readonly ICountryConfigurationRepository _countries = Substitute.For<ICountryConfigurationRepository>();
    private readonly IShippingCarrierFactory _carrierFactory = Substitute.For<IShippingCarrierFactory>();
    private readonly IShippingCarrier _carrier = Substitute.For<IShippingCarrier>();
    private readonly IPayoutDeductionRepository _payoutDeductions = Substitute.For<IPayoutDeductionRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly GenerateReturnLabel.Handler _sut;

    public GenerateReturnLabelHandlerTests()
    {
        _session.GetUserId().Returns(AdminUserId);
        _ids.Next().Returns("pd-1");
        _carrierFactory.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(_carrier));
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(BuildOrder());
        _makers.GetByIdAsync(TestMakerId, Arg.Any<CancellationToken>()).Returns(BuildMaker());
        _addresses.GetByIdAsync(AddressId, Arg.Any<CancellationToken>()).Returns(BuildAddress());
        _users.GetByIdAsync(TestMakerUserId, Arg.Any<CancellationToken>()).Returns(BuildMakerUser());
        _countries.GetByCodeAsync("CZ", Arg.Any<CancellationToken>()).Returns(BuildConfig());

        _sut = new GenerateReturnLabel.Handler(
            _disputes, _orders, _makers, _users, _addresses, _countries,
            _carrierFactory, _payoutDeductions, _outbox, _ids, _session,
            NullLogger<GenerateReturnLabel.Handler>.Instance);
    }

    private static Dispute BuildDispute(DisputeCategory category) => Dispute.Open(
        id: DisputeId, orderId: OrderId, category: category,
        description: "Problem with the item.", source: DisputeSource.Customer, countryCode: "CZ");

    private static Order BuildOrder() => Order.Create(
        id: OrderId, orderNumber: "M-CZ-20260099",
        customerUserId: "user-customer-1", makerId: TestMakerId, productId: "prod-1",
        contactName: "Anna Nováková", contactEmail: "anna@example.cz", contactPhone: "+420777123456",
        productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
        platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
        totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
        shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
        zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");

    private static Makables.Core.Domain.Makers.Maker BuildMaker() => Makables.Core.Domain.Makers.Maker.Create(
        id: TestMakerId, userId: TestMakerUserId, registrationNumber: "27074358", vatId: null,
        companyName: "Studio Keramika s.r.o.", legalForm: null, registeredAddressId: AddressId,
        incorporatedOn: null, isActiveInRegistry: true, sourceRegistry: "ares",
        snapshotFetchedAt: DateTimeOffset.Parse("2026-05-01T10:00:00Z"), snapshotIsStale: false,
        countryCode: "CZ", slug: "studio-keramika");

    private static Address BuildAddress() => Address.Create(
        id: AddressId, street: "Dílenská", houseNumber: "12", city: "Brno",
        zip: "60200", countryCodeIso: "CZ", auditCountryCode: "CZ");

    private static User BuildMakerUser()
    {
        var user = User.Create(
            id: TestMakerUserId, email: "maker@example.cz", role: UserRole.Maker,
            fullName: "Karel Novotný", countryCodePrimary: "CZ");
        user.UpdateProfile("Karel Novotný", "+420606111222");
        return user;
    }

    private static CountryConfiguration BuildConfig() => CountryConfiguration.Create(
        countryId: "CZ", defaultCurrencyCode: "CZK", defaultLanguageCode: "cs-CZ",
        timeZoneId: "Europe/Prague", phonePrefix: "+420", dateFormat: "d. M. yyyy",
        standardVatRateBp: 2100, taxIdLabel: "DIČ", vatIdLabel: "DIČ", registrationNumberLabel: "IČO",
        defaultPaymentProvider: "comgate", defaultShippingCarrier: "packeta",
        defaultRegistry: "ares", defaultEmailProvider: "sendgrid",
        issuerName: "JVM YORE s.r.o.", issuerIco: "00000000",
        defaultShippingPriceMinor: 7900);

    [Fact]
    public async Task Happy_path_creates_shipment_stamps_dispute_and_records_deduction()
    {
        var dispute = BuildDispute(DisputeCategory.DamagedItem);
        _disputes.GetByIdUnscopedAsync(DisputeId, Arg.Any<CancellationToken>()).Returns(dispute);
        _carrier.CreateReturnShipmentAsync(Arg.Any<Order>(), Arg.Any<ReturnRecipient>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new Shipment(CarrierRef, $"https://tracking.packeta.com/Z{CarrierRef}")));

        var result = await _sut.Handle(new GenerateReturnLabel.Command(DisputeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CarrierRef.Should().Be(CarrierRef);
        result.Value.AlreadyExisted.Should().BeFalse();
        dispute.ReturnCarrierRef.Should().Be(CarrierRef);
        _outbox.Received(1).Enqueue(
            DisputeId, OutboxEventTypes.ShippingGenerateReturnLabel, Arg.Any<string>());
        await _payoutDeductions.Received(1).AddAsync(
            Arg.Is<PayoutDeduction>(d => d.MakerId == TestMakerId && d.DisputeId == DisputeId && d.AmountMinor > 0),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(DisputeCategory.NotDelivered)]
    [InlineData(DisputeCategory.CarrierReturned)]
    [InlineData(DisputeCategory.CarrierFailed)]
    [InlineData(DisputeCategory.Other)]
    public async Task Non_return_warranting_category_is_rejected_AC6(DisputeCategory category)
    {
        var dispute = BuildDispute(category);
        _disputes.GetByIdUnscopedAsync(DisputeId, Arg.Any<CancellationToken>()).Returns(dispute);

        var result = await _sut.Handle(new GenerateReturnLabel.Command(DisputeId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.DisputeReturnCategoryNotEligible);
        await _carrierFactory.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Already_generated_dispute_is_silent_success_no_second_deduction()
    {
        var dispute = BuildDispute(DisputeCategory.NotAsDescribed);
        dispute.SetReturnShipment(CarrierRef, $"https://tracking.packeta.com/Z{CarrierRef}");
        _disputes.GetByIdUnscopedAsync(DisputeId, Arg.Any<CancellationToken>()).Returns(dispute);

        var result = await _sut.Handle(new GenerateReturnLabel.Command(DisputeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AlreadyExisted.Should().BeTrue();
        await _payoutDeductions.DidNotReceive().AddAsync(Arg.Any<PayoutDeduction>(), Arg.Any<CancellationToken>());
        await _carrierFactory.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Carrier_transient_error_propagates_AC4()
    {
        var dispute = BuildDispute(DisputeCategory.DamagedItem);
        _disputes.GetByIdUnscopedAsync(DisputeId, Arg.Any<CancellationToken>()).Returns(dispute);
        _carrier.CreateReturnShipmentAsync(Arg.Any<Order>(), Arg.Any<ReturnRecipient>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<Shipment>(
                Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable)));

        var result = await _sut.Handle(new GenerateReturnLabel.Command(DisputeId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingCarrierUnavailable);
        result.Error.Type.Should().Be(ErrorType.Transient);
        dispute.ReturnCarrierRef.Should().BeNull("a retryable failure leaves nothing stamped");
    }

    [Fact]
    public async Task Unauthenticated_session_returns_Unauthorized()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(new GenerateReturnLabel.Command(DisputeId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Unknown_dispute_returns_NotFound()
    {
        _disputes.GetByIdUnscopedAsync(DisputeId, Arg.Any<CancellationToken>()).Returns((Dispute?)null);

        var result = await _sut.Handle(new GenerateReturnLabel.Command(DisputeId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderDisputeNotFound);
    }
}
