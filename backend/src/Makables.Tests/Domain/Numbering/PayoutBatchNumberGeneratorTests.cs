using FluentAssertions;
using Makables.Core.Domain.Numbering;
using Makables.Infra.Database.Numbering;

namespace Makables.Tests.Domain.NumberingTests;

public class PayoutBatchNumberGeneratorTests
{
    private readonly IPayoutBatchNumberGenerator _gen = new PayoutBatchNumberGenerator();

    [Fact]
    public void For_Produces_VYP_Format()
    {
        // 2026-05-22 is a Friday; ISO week 21 (week starting Mon 2026-05-18).
        var date = new DateOnly(2026, 5, 22);
        _gen.For("CZ", date).Should().Be("VYP-CZ-2026-W21");
    }

    [Fact]
    public void For_Pads_Single_Digit_Weeks()
    {
        // 2026-01-05 (Monday) is ISO week 2; 2026-01-01 (Thursday) is ISO week 1.
        var date = new DateOnly(2026, 1, 5);
        _gen.For("CZ", date).Should().Be("VYP-CZ-2026-W02");
    }

    [Fact]
    public void For_Uppercases_Country_Code()
    {
        var date = new DateOnly(2026, 5, 22);
        _gen.For("cz", date).Should().Be("VYP-CZ-2026-W21");
    }

    [Fact]
    public void For_Rejects_Bad_Country()
    {
        var act = () => _gen.For("CZE", new DateOnly(2026, 5, 22));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void For_Same_Week_Same_Output()
    {
        // Two days in the same ISO week produce the same batch number.
        var monday = new DateOnly(2026, 5, 18);
        var friday = new DateOnly(2026, 5, 22);

        _gen.For("CZ", monday).Should().Be(_gen.For("CZ", friday));
    }
}
