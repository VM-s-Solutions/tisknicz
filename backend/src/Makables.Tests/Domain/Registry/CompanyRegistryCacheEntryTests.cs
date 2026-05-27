using FluentAssertions;
using Makables.Core.Domain.Registry;

namespace Makables.Tests.Domain.Registry;

public class CompanyRegistryCacheEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_initialises_fields_and_keeps_expires_after_fetched()
    {
        var e = CompanyRegistryCacheEntry.Create(
            registryCode: "ares",
            registrationNumber: "27074358",
            payloadJson: """{"x":1}""",
            fetchedAt: Now,
            expiresAt: Now.AddHours(24));

        e.RegistryCode.Should().Be("ares");
        e.RegistrationNumber.Should().Be("27074358");
        e.PayloadJson.Should().Be("""{"x":1}""");
        e.FetchedAt.Should().Be(Now);
        e.ExpiresAt.Should().Be(Now.AddHours(24));
    }

    [Theory]
    [InlineData("", "27074358", "{}")]
    [InlineData("ares", "", "{}")]
    [InlineData("ares", "27074358", "")]
    public void Create_rejects_blank_required_fields(string registry, string ico, string payload)
    {
        var act = () => CompanyRegistryCacheEntry.Create(registry, ico, payload, Now, Now.AddHours(1));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_expires_at_or_before_fetched_at()
    {
        var act = () => CompanyRegistryCacheEntry.Create("ares", "27074358", "{}", Now, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Refresh_swaps_payload_and_extends_expires()
    {
        var e = CompanyRegistryCacheEntry.Create("ares", "27074358", "{}", Now, Now.AddHours(1));

        var later = Now.AddHours(48);
        e.Refresh("""{"y":2}""", later, later.AddHours(24));

        e.PayloadJson.Should().Be("""{"y":2}""");
        e.FetchedAt.Should().Be(later);
        e.ExpiresAt.Should().Be(later.AddHours(24));
    }

    [Fact]
    public void Refresh_rejects_expires_at_or_before_fetched_at()
    {
        var e = CompanyRegistryCacheEntry.Create("ares", "27074358", "{}", Now, Now.AddHours(1));
        var act = () => e.Refresh("{}", Now.AddHours(2), Now.AddHours(2));
        act.Should().Throw<ArgumentException>();
    }
}
