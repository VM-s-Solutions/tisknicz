using FluentAssertions;
using Makables.Core.AppServices.Features.Payouts;
using Makables.Core.Domain.Payouts;

namespace Makables.Tests.AppServices.Features.Payouts;

/// <summary>
/// T-0102b red-first golden-file pins for <see cref="GenericPayoutCsvFormatter"/>.
/// The format is frozen per §C.5: header <c>account;amount;vs;message</c>,
/// CRLF line endings, semicolon delimiter, one row per maker in the order
/// the caller supplies (the artifact service orders deterministically by
/// company name then maker id). Amount is invariant-culture <c>0.00</c>
/// decimal CZK; VS is the digits of the batch number; message is
/// <c>{batchNumber} {company}</c> truncated to 140 chars. BOM is the
/// artifact service's encoding job, NOT the formatter's — the formatter is
/// string-in/string-out.
/// </summary>
public class GenericPayoutCsvFormatterTests
{
    private static readonly GenericPayoutCsvFormatter Formatter = new();

    [Fact]
    public void Format_two_maker_batch_matches_golden()
    {
        var batch = new PayoutCsvBatch(
            BatchNumber: "VYP-CZ-2026-W24",
            Lines:
            [
                new PayoutCsvLine(BankAccount: "123456789/0100", AmountMinor: 123456, MakerCompanyName: "Alpha s.r.o."),
                new PayoutCsvLine(BankAccount: "987654321/0800", AmountMinor: 50000, MakerCompanyName: "Beta s.r.o."),
            ]);

        var csv = Formatter.Format(batch);

        const string expected =
            "account;amount;vs;message\r\n" +
            "123456789/0100;1234.56;202624;VYP-CZ-2026-W24 Alpha s.r.o.\r\n" +
            "987654321/0800;500.00;202624;VYP-CZ-2026-W24 Beta s.r.o.\r\n";

        csv.Should().Be(expected);
    }

    [Theory]
    [InlineData(123456, "1234.56")]
    [InlineData(50000, "500.00")]
    [InlineData(1, "0.01")]
    [InlineData(99, "0.99")]
    [InlineData(100, "1.00")]
    public void Amount_renders_as_invariant_two_decimal(long minor, string expectedAmount)
    {
        var batch = new PayoutCsvBatch(
            "VYP-CZ-2026-W24",
            [new PayoutCsvLine("123/0100", minor, "X")]);

        var csv = Formatter.Format(batch);

        csv.Should().Contain($";{expectedAmount};");
    }

    [Theory]
    [InlineData("VYP-CZ-2026-W24", "202624")]
    [InlineData("VYP-CZ-2026-W01", "202601")]
    public void Vs_is_the_digits_of_the_batch_number(string batchNumber, string expectedVs)
    {
        var batch = new PayoutCsvBatch(
            batchNumber,
            [new PayoutCsvLine("123/0100", 100, "X")]);

        var csv = Formatter.Format(batch);

        csv.Should().Contain($";{expectedVs};");
    }

    [Fact]
    public void Message_truncates_to_140_chars()
    {
        var longName = new string('A', 200);
        var batch = new PayoutCsvBatch(
            "VYP-CZ-2026-W24",
            [new PayoutCsvLine("123/0100", 100, longName)]);

        var csv = Formatter.Format(batch);

        // Single data row; extract the message column (4th, semicolon-delimited).
        var dataRow = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1];
        var message = dataRow.Split(';')[3];

        message.Length.Should().Be(140);
        message.Should().StartWith("VYP-CZ-2026-W24 AAA");
    }

    [Fact]
    public void Format_preserves_caller_row_order()
    {
        var batch = new PayoutCsvBatch(
            "VYP-CZ-2026-W24",
            [
                new PayoutCsvLine("first/0100", 100, "First"),
                new PayoutCsvLine("second/0100", 200, "Second"),
            ]);

        var csv = Formatter.Format(batch);
        var rows = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        rows[1].Should().StartWith("first/0100;");
        rows[2].Should().StartWith("second/0100;");
    }

    [Fact]
    public void Format_throws_on_blank_bank_account()
    {
        var batch = new PayoutCsvBatch(
            "VYP-CZ-2026-W24",
            [new PayoutCsvLine("   ", 100, "X")]);

        var act = () => Formatter.Format(batch);

        act.Should().Throw<ArgumentException>();
    }
}
