using FluentAssertions;
using Makables.Core.Domain.Orders;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// Wire-code pins for the T-0106 dispute enums and the T-0107
/// <see cref="OrderDeliverySource.AdminManual"/> appendix. All three
/// dispute enums use stable <c>: short</c> backing (the
/// <see cref="OrderCancellationSource"/> precedent) — persisted as
/// SMALLINT columns and serialized as integers in outbox payloads, so a
/// reorder would silently rewrite stored rows. New values APPEND.
/// </summary>
public class DisputeEnumsTests
{
    [Fact]
    public void DisputeCategory_wire_codes_are_stable()
    {
        ((short)DisputeCategory.NotDelivered).Should().Be(0);
        ((short)DisputeCategory.DamagedItem).Should().Be(1);
        ((short)DisputeCategory.NotAsDescribed).Should().Be(2);
        ((short)DisputeCategory.CarrierReturned).Should().Be(3);
        ((short)DisputeCategory.CarrierFailed).Should().Be(4);
        ((short)DisputeCategory.Other).Should().Be(5);
        Enum.GetValues<DisputeCategory>().Should().HaveCount(6,
            "new categories append — update this pin deliberately");
    }

    [Fact]
    public void DisputeSource_wire_codes_are_stable()
    {
        ((short)DisputeSource.Customer).Should().Be(0);
        ((short)DisputeSource.Maker).Should().Be(1);
        ((short)DisputeSource.Carrier).Should().Be(2);
        ((short)DisputeSource.Admin).Should().Be(3);
        Enum.GetValues<DisputeSource>().Should().HaveCount(4);
    }

    [Fact]
    public void DisputeResolutionOutcome_wire_codes_are_stable()
    {
        ((short)DisputeResolutionOutcome.Refunded).Should().Be(0);
        ((short)DisputeResolutionOutcome.Resumed).Should().Be(1);
        ((short)DisputeResolutionOutcome.Cancelled).Should().Be(2);
        Enum.GetValues<DisputeResolutionOutcome>().Should().HaveCount(3);
    }

    [Fact]
    public void OrderDeliverySource_AdminManual_appends_at_3()
    {
        // T-0107: the enum's own doc-comment anticipated exactly this
        // value; stamped on manual Shipped → Delivered transitions.
        ((short)OrderDeliverySource.AdminManual).Should().Be(3);
    }
}
