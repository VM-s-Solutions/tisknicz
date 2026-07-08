using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using NSubstitute;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// T-0146 pins for <see cref="Dispute.SetReturnShipment"/> (set-once,
/// same-value Silent Success, different-value loud conflict — mirrors
/// <c>PayoutBatch.AttachCsvBlobPath</c>) and
/// <see cref="Dispute.MarkReturnReceived"/> (requires a return shipment
/// first; set-once; loud re-ack conflict — mirrors <c>Dispute.Resolve</c>'s
/// re-resolve posture).
/// </summary>
public class DisputeReturnShipmentTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-01T10:00:00Z");

    private static IClock FixedClock(DateTimeOffset? at = null)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(at ?? Now);
        return clock;
    }

    private static Dispute OpenDamagedItemDispute() => Dispute.Open(
        id: "disp-1",
        orderId: "ord-1",
        category: DisputeCategory.DamagedItem,
        description: "Item arrived cracked.",
        source: DisputeSource.Customer,
        countryCode: "CZ");

    // ---- SetReturnShipment ----

    [Fact]
    public void SetReturnShipment_first_call_sets_ref_and_tracking_url()
    {
        var dispute = OpenDamagedItemDispute();

        var result = dispute.SetReturnShipment("999888777", "https://tracking.packeta.com/Z999888777");

        result.IsSuccess.Should().BeTrue();
        dispute.ReturnCarrierRef.Should().Be("999888777");
        dispute.ReturnTrackingUrl.Should().Be("https://tracking.packeta.com/Z999888777");
    }

    [Fact]
    public void SetReturnShipment_same_value_twice_is_silent_success()
    {
        var dispute = OpenDamagedItemDispute();
        dispute.SetReturnShipment("999888777", "https://tracking.packeta.com/Z999888777");

        var result = dispute.SetReturnShipment("999888777", "https://tracking.packeta.com/Z999888777");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void SetReturnShipment_different_value_is_loud_conflict()
    {
        var dispute = OpenDamagedItemDispute();
        dispute.SetReturnShipment("999888777", "https://tracking.packeta.com/Z999888777");

        var result = dispute.SetReturnShipment("111222333", "https://tracking.packeta.com/Z111222333");

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.DisputeReturnShipmentAlreadySet);
        dispute.ReturnCarrierRef.Should().Be("999888777", "the first shipment stays authoritative");
    }

    // ---- MarkReturnReceived ----

    [Fact]
    public void MarkReturnReceived_before_shipment_exists_is_conflict()
    {
        var dispute = OpenDamagedItemDispute();

        var result = dispute.MarkReturnReceived(FixedClock(), "maker-user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.DisputeReturnShipmentNotGenerated);
    }

    [Fact]
    public void MarkReturnReceived_after_shipment_records_timestamp_and_recorder()
    {
        var dispute = OpenDamagedItemDispute();
        dispute.SetReturnShipment("999888777", "https://tracking.packeta.com/Z999888777");

        var result = dispute.MarkReturnReceived(FixedClock(), "maker-user-1");

        result.IsSuccess.Should().BeTrue();
        dispute.ReturnReceivedAt.Should().Be(Now);
        dispute.ReturnReceivedBy.Should().Be("maker-user-1");
    }

    [Fact]
    public void MarkReturnReceived_twice_is_loud_conflict()
    {
        var dispute = OpenDamagedItemDispute();
        dispute.SetReturnShipment("999888777", "https://tracking.packeta.com/Z999888777");
        dispute.MarkReturnReceived(FixedClock(), "maker-user-1");

        var result = dispute.MarkReturnReceived(FixedClock(Now.AddHours(1)), "admin:admin-1");

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.DisputeReturnAlreadyReceived);
        dispute.ReturnReceivedBy.Should().Be("maker-user-1", "the first acknowledgment is immutable");
    }
}
