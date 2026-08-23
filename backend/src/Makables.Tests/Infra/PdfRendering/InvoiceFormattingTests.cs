using FluentAssertions;
using Makables.Core.Domain.Invoices;
using Makables.Infra.PdfRendering;

namespace Makables.Tests.Infra.PdfRendering;

/// <summary>
/// The pure presentation logic behind the invoice PDFs. These are the
/// parts of the document that can be asserted directly: QuestPDF writes
/// its content streams as Flate-compressed glyph indices with no
/// ToUnicode CMap, so no test can read a string back out of the rendered
/// bytes — the strings have to be pinned before they reach the page.
/// </summary>
public class InvoiceFormattingTests
{
    // === Money ===

    [Theory]
    [InlineData(739_00, "739,00\u00a0Kč")]
    [InlineData(0, "0,00\u00a0Kč")]
    [InlineData(1_234_56, "1\u00a0234,56\u00a0Kč")]
    [InlineData(1_000_000_00, "1\u00a0000\u00a0000,00\u00a0Kč")]
    public void FormatAmount_renders_CZK_the_Czech_way(long minor, string expected)
    {
        InvoiceFormatting.FormatAmount(minor, "CZK").Should().Be(expected);
    }

    [Fact]
    public void FormatAmount_never_uses_a_breakable_thousands_separator()
    {
        // A legal document that line-breaks "1 234 567" mid-number is a
        // defect, and the cs-CZ group separator is a plain space on some
        // ICU builds.
        InvoiceFormatting.FormatAmount(1_234_567_89, "CZK")
            .Should().NotContain(" ").And.Contain("\u00a0");
    }

    [Theory]
    [InlineData("EUR", "€")]
    [InlineData("USD", "USD")]
    public void FormatAmount_falls_back_to_the_ISO_code_for_unknown_currencies(
        string currency, string symbol)
    {
        InvoiceFormatting.FormatAmount(100_00, currency).Should().EndWith(symbol);
    }

    // === Variable symbol ===

    [Theory]
    [InlineData("FV-CZ-20260042", "20260042")]
    [InlineData("FV-SK-20260001", "20260001")]
    [InlineData("NO-DIGITS", "NO-DIGITS")]
    public void VariableSymbol_takes_the_numeric_tail(string number, string expected)
    {
        InvoiceFormatting.VariableSymbol(number).Should().Be(expected);
    }

    // === VAT rate ===

    [Theory]
    [InlineData(2100, "21 %")]
    [InlineData(1200, "12 %")]
    [InlineData(0, "0 %")]
    public void FormatVatRate_renders_basis_points_as_a_percentage(int bp, string expected)
    {
        InvoiceFormatting.FormatVatRate(bp).Should().Be(expected);
    }

    // === Payment-method labels ===

    [Theory]
    [InlineData("CARD_CZ_CSOB_2", "platební kartou")]
    [InlineData("CARD_CZ_CS", "platební kartou")]
    [InlineData("BANK_CZ_RB", "bankovním převodem")]
    [InlineData("APPLEPAY_REDIRECT", "přes Apple Pay")]
    [InlineData("GOOGLEPAY_REDIRECT", "přes Google Pay")]
    [InlineData("LATER_TWISTO", "odloženou platbou")]
    [InlineData("dev-bypass", "testovací platbou")]
    public void PaymentMethodLabel_maps_the_provider_families(string code, string expected)
    {
        InvoiceFormatting.PaymentMethodLabel(code).Should().Be(expected);
    }

    [Fact]
    public void PaymentMethodLabel_maps_the_platforms_own_payout_deduction()
    {
        InvoiceFormatting.PaymentMethodLabel(SettlementMethods.PayoutDeduction)
            .Should().Be("srážkou z vyplacené částky");
    }

    [Fact]
    public void PaymentMethodLabel_falls_back_to_a_truthful_generic_for_unknown_codes()
    {
        // Comgate adds methods without telling us. An unrecognised code must
        // never reach a customer's invoice verbatim, and must never be
        // labelled as a channel we did not verify.
        InvoiceFormatting.PaymentMethodLabel("BTNCS").Should().Be("přes platební bránu");
        InvoiceFormatting.PaymentMethodLabel("PRSMS").Should().Be("přes platební bránu");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PaymentMethodLabel_returns_empty_when_the_channel_is_unknown(string? code)
    {
        // Backfilled rows know the settlement date but not the channel; the
        // stamp then reads "Uhrazeno 22. 8. 2026" with no invented method.
        InvoiceFormatting.PaymentMethodLabel(code).Should().BeEmpty();
    }

    // === Address ===

    [Fact]
    public void AddressLines_splits_a_registry_address_into_letterhead_lines()
    {
        InvoiceFormatting.AddressLines("Příčná 1892/4, Nové Město, 110 00 Praha 1")
            .Should().Equal("Příčná 1892/4", "Nové Město", "110 00 Praha 1");
    }

    [Fact]
    public void AddressLines_keeps_a_separator_free_address_on_one_line()
    {
        InvoiceFormatting.AddressLines("Příčná 1892/4 Praha")
            .Should().Equal("Příčná 1892/4 Praha");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AddressLines_is_empty_when_there_is_no_address(string? address)
    {
        InvoiceFormatting.AddressLines(address).Should().BeEmpty();
    }
}
