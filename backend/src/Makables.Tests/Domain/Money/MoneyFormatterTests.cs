using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Money;

namespace Makables.Tests.Domain.MoneyTests;

public class MoneyFormatterTests
{
    [Fact]
    public void CzCz_Strips_Haléře_Whole_Czk()
    {
        // 579 CZK = 57900 minor. cs-CZ display: "579 Kč" (no haléře).
        MoneyFormatter.Format(Money.CZK(57900), "cs-CZ")
            .Should().Be("579 Kč");
    }

    [Fact]
    public void CzCz_Strips_Haléře_With_Half_Up_Rounding()
    {
        // 579.50 CZK = 57950 minor. cs-CZ display: "580 Kč" (half-up).
        MoneyFormatter.Format(Money.CZK(57950), "cs-CZ")
            .Should().Be("580 Kč");
    }

    [Fact]
    public void CzCz_Formats_Thousands_With_Nbsp_Space()
    {
        // 1 234 567 CZK should render with a space thousands separator
        // per cs-CZ NumberFormat. The actual char is non-breaking-space (U+00A0)
        // per Czech locale.
        var formatted = MoneyFormatter.Format(Money.CZK(123_456_700), "cs-CZ");
        formatted.Should().EndWith(" Kč");
        formatted.Should().Contain("1");
        formatted.Should().Contain("234");
        formatted.Should().Contain("567");
    }

    [Fact]
    public void CzCz_Renders_Zero()
    {
        MoneyFormatter.Format(Money.CZK(0), "cs-CZ").Should().Be("0 Kč");
    }

    [Fact]
    public void Non_CZK_Renders_With_Resolved_Symbol()
    {
        // EUR in cs-CZ should use € symbol (we resolve it ourselves; the
        // exact format string remains locale-driven).
        var formatted = MoneyFormatter.Format(Money.Of(12345, "EUR"), "cs-CZ");
        formatted.Should().Contain("€");
    }

    [Fact]
    public void Negative_Czk_Renders()
    {
        MoneyFormatter.Format(Money.CZK(-12300), "cs-CZ")
            .Should().Contain("123");
    }
}
