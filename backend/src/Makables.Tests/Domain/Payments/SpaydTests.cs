using FluentAssertions;
using Makables.Core.Domain.Payments;

namespace Makables.Tests.Domain.Payments;

/// <summary>
/// Pins the <see cref="Spayd"/> value-object's format-string composition,
/// IBAN normalisation, and amount conversion per the SPAYD 1.0 spec
/// (<c>SPD*1.0*ACC:&lt;iban&gt;*AM:&lt;amount&gt;*CC:&lt;ccy&gt;*X-VS:&lt;vs&gt;</c>).
/// T-0068b TDD red commit — the implementation lands in the next commit.
///
/// <para>
/// The SPAYD format is rendered into a QR code on the invoice PDF
/// (<see cref="Spayd.ForInvoice"/> output is fed verbatim to the QR
/// encoder by <c>QuestPdfInvoiceRenderer</c>). The bank apps that scan
/// the QR (Air Bank, RB, KB, ČSOB, ...) parse the format string
/// literally; minor whitespace / casing / amount-precision deviations
/// break the pay-by-QR flow silently. These tests pin the format.
/// </para>
/// </summary>
public class SpaydTests
{
    private const string IbanCz = "CZ6508000000192000145399";

    [Fact]
    public void ForInvoice_renders_the_canonical_SPAYD_string()
    {
        var spayd = Spayd.ForInvoice(
            iban: IbanCz,
            amountMinor: 121_00,
            currency: "CZK",
            variableSymbol: "20260001");

        spayd.Should().Be("SPD*1.0*ACC:CZ6508000000192000145399*AM:121.00*CC:CZK*X-VS:20260001");
    }

    [Fact]
    public void ForInvoice_strips_whitespace_from_IBAN()
    {
        // IBAN often arrives with spaces every 4 chars (display format).
        // SPAYD requires the contiguous form.
        var spayd = Spayd.ForInvoice(
            iban: "CZ65 0800 0000 1920 0014 5399",
            amountMinor: 121_00,
            currency: "CZK",
            variableSymbol: "20260001");

        spayd.Should().Contain("ACC:CZ6508000000192000145399");
        spayd.Should().NotContain(" ");
    }

    [Fact]
    public void ForInvoice_uppercases_the_IBAN()
    {
        var spayd = Spayd.ForInvoice(
            iban: "cz6508000000192000145399",
            amountMinor: 100_00,
            currency: "CZK",
            variableSymbol: "1");

        spayd.Should().Contain("ACC:CZ6508000000192000145399");
    }

    [Fact]
    public void ForInvoice_renders_amount_as_major_units_with_two_decimals()
    {
        // 1 CZK == 100 haléře (minor units). SPAYD expects major units
        // with a "." decimal separator and exactly two decimals.
        Spayd.ForInvoice(IbanCz, 1, "CZK", "1")
            .Should().Contain("AM:0.01");

        Spayd.ForInvoice(IbanCz, 100, "CZK", "1")
            .Should().Contain("AM:1.00");

        Spayd.ForInvoice(IbanCz, 1_234_56, "CZK", "1")
            .Should().Contain("AM:1234.56");
    }

    [Fact]
    public void ForInvoice_uppercases_the_currency_code()
    {
        var spayd = Spayd.ForInvoice(IbanCz, 100_00, "czk", "1");

        spayd.Should().Contain("CC:CZK");
    }

    [Fact]
    public void ForInvoice_throws_on_blank_IBAN()
    {
        var act = () => Spayd.ForInvoice(iban: "", amountMinor: 100_00, currency: "CZK", variableSymbol: "1");

        act.Should().Throw<ArgumentException>().WithParameterName("iban");
    }

    [Fact]
    public void ForInvoice_throws_on_whitespace_only_IBAN()
    {
        var act = () => Spayd.ForInvoice(iban: "   ", amountMinor: 100_00, currency: "CZK", variableSymbol: "1");

        act.Should().Throw<ArgumentException>().WithParameterName("iban");
    }

    [Fact]
    public void ForInvoice_throws_on_blank_variable_symbol()
    {
        var act = () => Spayd.ForInvoice(IbanCz, 100_00, "CZK", variableSymbol: "");

        act.Should().Throw<ArgumentException>().WithParameterName("variableSymbol");
    }

    [Fact]
    public void ForInvoice_throws_on_non_digit_variable_symbol()
    {
        // SPAYD X-VS field per the Czech bank-account spec is digits only.
        // A non-numeric VS is rejected at the SPAYD encoder boundary so
        // the QR doesn't silently bake an unscannable string.
        var act = () => Spayd.ForInvoice(IbanCz, 100_00, "CZK", variableSymbol: "ABC123");

        act.Should().Throw<ArgumentException>().WithParameterName("variableSymbol");
    }

    [Fact]
    public void ForInvoice_throws_on_negative_amount()
    {
        var act = () => Spayd.ForInvoice(IbanCz, amountMinor: -1, currency: "CZK", variableSymbol: "1");

        act.Should().Throw<ArgumentException>().WithParameterName("amountMinor");
    }
}
