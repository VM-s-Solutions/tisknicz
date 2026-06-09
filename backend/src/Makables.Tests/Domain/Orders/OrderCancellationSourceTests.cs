using FluentAssertions;
using Makables.Core.Domain.Orders;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// T-0083 pure-logic surface: pin <see cref="OrderCancellationSource"/>
/// wire codes. Stable explicit numeric values are part of the persisted
/// contract — silent renames cannot drift past this test.
/// </summary>
public class OrderCancellationSourceTests
{
    [Fact]
    public void Customer_is_zero()
    {
        ((short)OrderCancellationSource.Customer).Should().Be(0);
    }

    [Fact]
    public void AutoExpiry_is_one()
    {
        ((short)OrderCancellationSource.AutoExpiry).Should().Be(1);
    }

    [Fact]
    public void Admin_is_two()
    {
        ((short)OrderCancellationSource.Admin).Should().Be(2);
    }
}
