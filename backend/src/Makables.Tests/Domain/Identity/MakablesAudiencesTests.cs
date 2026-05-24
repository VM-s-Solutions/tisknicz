using FluentAssertions;
using Makables.Core.Domain.Identity;

namespace Makables.Tests.Domain.Identity;

public class MakablesAudiencesTests
{
    [Theory]
    [InlineData("customer")]
    [InlineData("maker")]
    [InlineData("admin")]
    public void IsValid_accepts_each_canonical_audience(string audience)
    {
        MakablesAudiences.IsValid(audience).Should().BeTrue();
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("CUSTOMER")]
    [InlineData(" customer")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("guest")]
    public void IsValid_rejects_anything_else(string? audience)
    {
        MakablesAudiences.IsValid(audience).Should().BeFalse();
    }

    [Fact]
    public void All_contains_exactly_the_three_named_constants()
    {
        MakablesAudiences.All.Should().BeEquivalentTo(new[]
        {
            MakablesAudiences.Customer,
            MakablesAudiences.Maker,
            MakablesAudiences.Admin,
        });
    }
}
