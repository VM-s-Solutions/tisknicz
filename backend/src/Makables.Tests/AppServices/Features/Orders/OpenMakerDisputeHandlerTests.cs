using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// T-0106 <see cref="OpenMakerDispute"/> mirror pins: maker resolved
/// from the JWT (never the request), maker-scoped IDOR shield,
/// Source=Maker stamp, and the no-maker-row masking (404, no existence
/// leak). Shared semantics (Silent-Success re-open, category gate) are
/// pinned on the customer variant — this file covers the deltas.
/// </summary>
public class OpenMakerDisputeHandlerTests
{
    private const string OrderId = "ord-1";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-12T10:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IDisputeRepository _disputes = Substitute.For<IDisputeRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();
    private readonly OpenMakerDispute.Handler _sut;

    public OpenMakerDisputeHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _ids.Next().Returns("dsp-1");
        _session.GetUserId().Returns(MakerUserId);
        _languageResolver.ResolveAsync(null, "CZ", Arg.Any<CancellationToken>())
            .Returns("cs-CZ");
        var urls = Options.Create(new PublicAppUrlsOptions { WebBaseUrl = "https://makables.test" });
        _sut = new OpenMakerDispute.Handler(
            _orders, _makers, _disputes, _outbox, _clock, _ids, _session,
            _languageResolver, urls,
            NullLogger<OpenMakerDispute.Handler>.Instance);
    }

    private static MakerEntity BuildMaker() => MakerEntity.Create(
        id: MakerId, userId: MakerUserId,
        registrationNumber: "27074358", vatId: null,
        companyName: "Avast s.r.o.", legalForm: null,
        registeredAddressId: "addr-1",
        incorporatedOn: null, isActiveInRegistry: true,
        sourceRegistry: "ares",
        snapshotFetchedAt: Now.AddDays(-30), snapshotIsStale: false,
        countryCode: "CZ", slug: "avast");

    private static Order BuildAcceptedOrder()
    {
        var o = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: "user-cust-1", makerId: MakerId, productId: "prod-1",
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.AddDays(-2));
        o.MarkAsPaid(clock, "tx-1");
        o.Accept(clock);
        return o;
    }

    [Fact]
    public async Task Happy_path_stamps_Maker_source_and_PreDisputeState_Accepted()
    {
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>()).Returns(BuildMaker());
        var order = BuildAcceptedOrder();
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>()).Returns(order);
        Dispute? addedDispute = null;
        await _disputes.AddAsync(Arg.Do<Dispute>(d => addedDispute = d), Arg.Any<CancellationToken>());

        var result = await _sut.Handle(
            new OpenMakerDispute.Command(OrderId, DisputeCategory.Other, "Zákazník nekomunikuje."),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.State.Should().Be(OrderState.Disputed);
        order.PreDisputeState.Should().Be(OrderState.Accepted);
        addedDispute!.Source.Should().Be(DisputeSource.Maker);
        _outbox.Received(1).Enqueue(
            OrderId, OutboxEventTypes.OrderDisputedAdminEmail, Arg.Any<string>());
    }

    [Fact]
    public async Task User_without_maker_row_gets_notFound_and_order_lookup_never_runs()
    {
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>())
            .Returns((MakerEntity?)null);

        var result = await _sut.Handle(
            new OpenMakerDispute.Command(OrderId, DisputeCategory.Other, "Pokus."),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        await _orders.DidNotReceiveWithAnyArgs().GetByIdForMakerAsync(default!, default!, default);
    }

    [Fact]
    public async Task Cross_maker_probe_returns_notFound_without_writes()
    {
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>()).Returns(BuildMaker());
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(
            new OpenMakerDispute.Command(OrderId, DisputeCategory.Other, "Cizí objednávka."),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        await _disputes.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void Validator_rejects_carrier_reserved_categories()
    {
        var validator = new OpenMakerDispute.Validator();
        var result = validator.Validate(
            new OpenMakerDispute.Command(OrderId, DisputeCategory.CarrierReturned, "Popis."));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorCode == BusinessErrorMessage.OrderDisputeCategoryNotAllowed);
    }
}
