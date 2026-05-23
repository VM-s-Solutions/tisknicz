using FluentAssertions;
using Makables.Core.Domain.Numbering;

namespace Makables.Tests.Domain.NumberingTests;

public class NumberingSequenceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-22T10:00:00Z");

    [Fact]
    public void StartAt_Normalizes_Country_Code()
    {
        var seq = NumberingSequence.StartAt("cz", NumberingScope.Order, 2026, Now);

        seq.CountryCode.Should().Be("CZ");
        seq.Scope.Should().Be(NumberingScope.Order);
        seq.Year.Should().Be(2026);
        seq.LastUsedValue.Should().Be(0);
        seq.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void StartAt_Rejects_Bad_Country()
    {
        var act = () => NumberingSequence.StartAt("CZE", NumberingScope.Order, 2026, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void StartAt_Rejects_Empty_Scope()
    {
        var act = () => NumberingSequence.StartAt("CZ", "", 2026, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void StartAt_Rejects_OutOfRange_Year()
    {
        var act = () => NumberingSequence.StartAt("CZ", NumberingScope.Order, 1999, Now);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Increment_Bumps_LastUsedValue_And_Returns_Formatted_Order()
    {
        var seq = NumberingSequence.StartAt("CZ", NumberingScope.Order, 2026, Now);

        var first = seq.Increment(Now);
        var second = seq.Increment(Now);

        first.Should().Be("M-CZ-20260001");
        second.Should().Be("M-CZ-20260002");
        seq.LastUsedValue.Should().Be(2);
    }

    [Fact]
    public void Increment_Formats_Invoice_With_FV_Prefix()
    {
        var seq = NumberingSequence.StartAt("CZ", NumberingScope.Invoice, 2026, Now);
        seq.Increment(Now).Should().Be("FV-CZ-20260001");
    }

    [Fact]
    public void Format_Throws_For_Unsupported_Scope()
    {
        var seq = NumberingSequence.StartAt("CZ", "custom-scope", 2026, Now);
        var act = () => seq.Increment(Now);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Format_Pads_Year_And_Sequence_To_Four_Digits()
    {
        var seq = NumberingSequence.StartAt("CZ", NumberingScope.Order, 2026, Now);
        for (int i = 0; i < 9; i++) seq.Increment(Now);

        seq.Increment(Now).Should().Be("M-CZ-20260010");
    }

    [Fact]
    public void Increment_Updates_Timestamp()
    {
        var seq = NumberingSequence.StartAt("CZ", NumberingScope.Order, 2026, Now);
        var laterClock = Now.AddMinutes(5);

        seq.Increment(laterClock);

        seq.UpdatedAt.Should().Be(laterClock);
    }
}
