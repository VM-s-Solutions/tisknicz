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

    // ── CSV-injection / RFC-4180 output-boundary guard ────────────────────
    // payout-core-bundle Gate-3 MEDIUM. MakerCompanyName is an ARES snapshot
    // (not free-form), but ARES names are not an allowlisted charset and the
    // bank-transfer CSV is opened in Excel by an admin. The formatter must
    // neutralize formula chars and RFC-4180-quote special chars so a company
    // name cannot execute a formula or inject an extra payment row.

    private const string BatchNumber = "VYP-CZ-2026-W24";

    /// <summary>
    /// Extract the message column from a single-row batch by RFC-4180
    /// parsing the data row, then strip the "{batchNumber} " prefix to get
    /// back the logical company name. Asserts the round-trip is lossless.
    /// </summary>
    private static string RoundTripCompanyName(string companyName)
    {
        var csv = Formatter.Format(new PayoutCsvBatch(
            BatchNumber,
            [new PayoutCsvLine("123/0100", 100, companyName)]));

        // The data row is everything after the header line. CRLF inside a
        // quoted field belongs to the record, so split only on the header
        // boundary, not on every CRLF.
        var dataRecord = csv["account;amount;vs;message\r\n".Length..];
        // Trailing record terminator.
        dataRecord = dataRecord[..^Crlf.Length];

        var fields = ParseCsvRecord(dataRecord);
        fields.Should().HaveCount(4, "the record must have exactly four logical columns");
        var message = fields[3];
        message.Should().StartWith($"{BatchNumber} ");
        return message[(BatchNumber.Length + 1)..];
    }

    private const string Crlf = "\r\n";

    /// <summary>Minimal RFC-4180 reader for a single record (no embedded CRLF split).</summary>
    private static List<string> ParseCsvRecord(string record)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < record.Length; i++)
        {
            var c = record[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < record.Length && record[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = false;
                }
                else current.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ';') { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        return fields;
    }

    [Theory]
    [InlineData("=cmd()")]
    [InlineData("@SUM(1+1)")]
    [InlineData("+1")]
    [InlineData("-1")]
    public void Formula_bearing_company_name_round_trips_losslessly(string companyName)
    {
        // The company name rides the message column behind the "{batch} "
        // prefix, so the spreadsheet cell already starts with a letter — but
        // the round-trip must still reproduce the exact logical value.
        RoundTripCompanyName(companyName).Should().Be(companyName);
    }

    [Fact]
    public void Formula_leading_field_is_prefixed_with_single_quote()
    {
        // A field whose own first char is a formula trigger (here the bank
        // account, the only column not prefixed by the batch number) must be
        // neutralized with a leading single quote so Excel renders it inert.
        var csv = Formatter.Format(new PayoutCsvBatch(
            BatchNumber,
            [new PayoutCsvLine("=DANGER", 100, "Alpha s.r.o.")]));

        var dataRow = csv.Split(Crlf, StringSplitOptions.RemoveEmptyEntries)[1];
        dataRow.Should().StartWith("'=DANGER;");
    }

    [Fact]
    public void Semicolon_bearing_company_name_is_rfc4180_quoted_and_does_not_add_a_column()
    {
        const string company = "Alpha; s.r.o.";
        var csv = Formatter.Format(new PayoutCsvBatch(
            BatchNumber,
            [new PayoutCsvLine("123/0100", 100, company)]));

        var dataRecord = csv["account;amount;vs;message\r\n".Length..^Crlf.Length];
        // The raw record must contain a quoted message field, not a bare
        // semicolon that would split into a fifth column.
        dataRecord.Should().Contain($"\"{BatchNumber} {company}\"");
        ParseCsvRecord(dataRecord).Should().HaveCount(4);
        RoundTripCompanyName(company).Should().Be(company);
    }

    [Fact]
    public void Quote_bearing_company_name_is_doubled_and_round_trips()
    {
        const string company = "Alpha \"Best\" s.r.o.";
        RoundTripCompanyName(company).Should().Be(company);

        var csv = Formatter.Format(new PayoutCsvBatch(
            BatchNumber,
            [new PayoutCsvLine("123/0100", 100, company)]));
        // Interior quotes doubled inside a wrapped field.
        csv.Should().Contain("\"\"Best\"\"");
    }

    [Fact]
    public void Crlf_bearing_company_name_does_not_inject_a_new_payment_row()
    {
        const string company = "Alpha\r\nEVIL;999/0100;666";
        var csv = Formatter.Format(new PayoutCsvBatch(
            BatchNumber,
            [new PayoutCsvLine("123/0100", 100, company)]));

        // Header + exactly ONE data record. The embedded CRLF lives inside a
        // quoted field, so a naive line split would see extra physical lines,
        // but an RFC-4180 reader sees one record with four columns.
        var dataRecord = csv["account;amount;vs;message\r\n".Length..^Crlf.Length];
        ParseCsvRecord(dataRecord).Should().HaveCount(4,
            because: "an embedded CRLF must stay inside the quoted message field, " +
                     "never inject a second payment row into a bank-transfer file");
        RoundTripCompanyName(company).Should().Be(company);
    }
}
