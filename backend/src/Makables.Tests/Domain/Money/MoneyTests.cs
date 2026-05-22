using FluentAssertions;
using Makables.Core.Domain.Money;

namespace Makables.Tests.Domain.MoneyTests;

public class MoneyTests
{
    [Fact]
    public void Of_Uppercases_Currency()
    {
        var m = Money.Of(50000, "czk");
        m.Currency.Should().Be("CZK");
    }

    [Fact]
    public void Of_Rejects_Empty_Currency()
    {
        var act = () => Money.Of(100, "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Of_Rejects_Wrong_Length()
    {
        var act = () => Money.Of(100, "CZ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CZK_Helper_Builds_CZK_Money()
    {
        var m = Money.CZK(50000);
        m.AmountMinor.Should().Be(50000);
        m.Currency.Should().Be("CZK");
    }

    [Fact]
    public void Add_Same_Currency_Sums_Amounts()
    {
        var a = Money.CZK(50000);
        var b = Money.CZK(7900);
        a.Add(b).Should().Be(Money.CZK(57900));
    }

    [Fact]
    public void Add_Different_Currency_Throws()
    {
        var act = () => Money.CZK(100).Add(Money.Of(100, "EUR"));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Currency mismatch*CZK*EUR*");
    }

    [Fact]
    public void Subtract_Same_Currency_Works()
    {
        var a = Money.CZK(50000);
        var b = Money.CZK(7500);
        a.Subtract(b).Should().Be(Money.CZK(42500));
    }

    [Fact]
    public void Subtract_Can_Go_Negative()
    {
        var a = Money.CZK(100);
        var b = Money.CZK(200);
        a.Subtract(b).Should().Be(Money.CZK(-100));
    }

    [Fact]
    public void PercentOfBp_Computes_Platform_Fee()
    {
        // 15% of 500 CZK = 75 CZK = 7500 minor.
        Money.CZK(50000).PercentOfBp(1500)
            .Should().Be(Money.CZK(7500));
    }

    [Fact]
    public void PercentOfBp_Rounds_Half_Up()
    {
        // 21% of 1 CZK = 0.21 CZK = 21 minor (exact, no rounding).
        Money.CZK(100).PercentOfBp(2100).Should().Be(Money.CZK(21));

        // 1% of 1 CZK = 0.01 CZK = 1 minor; for 0.5 minor → 1 (half-up away from zero).
        // 50% of 1 minor = 0.5 → rounds to 1.
        Money.CZK(1).PercentOfBp(5000).Should().Be(Money.CZK(1));
    }

    [Fact]
    public void Compare_Same_Currency_Works()
    {
        Money.CZK(100).CompareTo(Money.CZK(50)).Should().BePositive();
        Money.CZK(50).CompareTo(Money.CZK(100)).Should().BeNegative();
        Money.CZK(50).CompareTo(Money.CZK(50)).Should().Be(0);
    }

    [Fact]
    public void Compare_Different_Currency_Throws()
    {
        var act = () => Money.CZK(100).CompareTo(Money.Of(100, "EUR"));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Records_Are_Value_Equal()
    {
        Money.CZK(100).Should().Be(Money.CZK(100));
        Money.CZK(100).Should().NotBe(Money.CZK(200));
        Money.CZK(100).Should().NotBe(Money.Of(100, "EUR"));
    }

    [Fact]
    public void Add_Overflow_Throws()
    {
        // checked(...) is in place; long.MaxValue + 1 should throw.
        var act = () => Money.CZK(long.MaxValue).Add(Money.CZK(1));
        act.Should().Throw<OverflowException>();
    }

    // === Reviewer T-0005 follow-up: gaps flagged on the merged commit ===

    [Fact]
    public void Subtract_Different_Currency_Throws()
    {
        // MAJOR finding #3 from T-0005 review: Subtract had no currency-mismatch test.
        var act = () => Money.CZK(100).Subtract(Money.Of(100, "EUR"));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Currency mismatch*");
    }

    [Fact]
    public void Subtract_Underflow_Throws()
    {
        // MAJOR finding #1 from T-0005 review: Subtract overflow on the negative side.
        var act = () => Money.CZK(long.MinValue).Subtract(Money.CZK(1));
        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void PercentOfBp_Overflow_Throws()
    {
        // MAJOR finding #2 from T-0005 review: PercentOfBp overflow protection.
        // 200% of long.MaxValue overflows long when cast back from decimal.
        var act = () => Money.CZK(long.MaxValue).PercentOfBp(20_000);
        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void PercentOfBp_On_Negative_Rounds_Away_From_Zero()
    {
        // MINOR finding #5 from T-0005 review: lock the half-up semantics on
        // negatives. AwayFromZero means -0.5 → -1, not -0 / 0.
        // 50% of -1 minor = -0.5 → rounds to -1 (away from zero).
        Money.CZK(-1).PercentOfBp(5000).Should().Be(Money.CZK(-1));
    }

    [Fact]
    public void Zero_Equals_CZK_Zero()
    {
        // MINOR finding #6 from T-0005 review: Money.Zero factory had no test.
        Money.Zero("CZK").Should().Be(Money.CZK(0));
        Money.Zero("CZK").AmountMinor.Should().Be(0);
        Money.Zero("CZK").Currency.Should().Be("CZK");
    }

    [Fact]
    public void Direct_Constructor_Normalizes_Currency()
    {
        // T-0006 reviewer MINOR #3: `new Money(100, "czk")` used to bypass
        // `Of()`'s normalization, breaking record equality with `Money.CZK(100)`.
        // The fix routes both paths through the same validated ctor.
        new Money(100, "czk").Should().Be(Money.CZK(100));
        new Money(100, "czk").Currency.Should().Be("CZK");
    }

    [Fact]
    public void Direct_Constructor_Rejects_Wrong_Length()
    {
        var act = () => new Money(100, "CZ");
        act.Should().Throw<ArgumentException>();
    }
}
