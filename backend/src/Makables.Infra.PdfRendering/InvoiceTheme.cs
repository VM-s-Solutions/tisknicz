using System.Globalization;
using Makables.Core.Domain.Invoices;

namespace Makables.Infra.PdfRendering;

/// <summary>
/// The design tokens the invoice PDFs draw with, transcribed from the
/// LIGHT half of <c>frontend/src/app/globals.css</c>. Paper is white, so
/// the light palette is the correct one — a PDF cannot follow a theme,
/// and printing the dark palette would put near-black fills on a page.
///
/// <para>
/// These are literals rather than a lookup because a PDF renderer has no
/// CSS layer to resolve through. That is a deliberate duplication of
/// exactly nine values; if the light palette is retuned, this file is the
/// one place the documents follow it from. The colour ratio the design
/// language mandates (~60 white / 30 quiet neutral / 10 primary) is what
/// the templates spend them on: the paper stays white, structure is drawn
/// in hairlines and near-black ink, and the brand teal appears only on
/// the logo mark and the single rule under the masthead.
/// </para>
/// </summary>
internal static class InvoiceTheme
{
    // Ink — the light ramp's near-black end (--lt-ink-50/100) plus the two
    // muted steps that clear the contrast floor on white (--lt-ink-400/500).
    public const string InkTitle = "#08191c";
    public const string InkBody = "#0e2429";
    public const string InkMuted = "#395459";
    public const string InkFaint = "#526f77";

    // Structure. Hairlines only — the design language has no gradients,
    // no shadows and no filled "cards" beyond a flat band.
    public const string Hairline = "#ccd8dc";   // --lt-ink-700
    public const string HairlineSoft = "#e7edef";   // --lt-ink-800
    public const string BandFill = "#f7f9fa";   // --lt-surface-secondary

    // The primary. Spent on the mark and the masthead rule, nothing else.
    public const string Brand = "#00786c";   // --lt-brand-400
    public const string BrandLine = "#0d9488";   // --lt-brand-line

    // The settlement stamp. A status is a semantic annotation, so the fill
    // stays pale and the ink is near-black in the fill's own hue — the
    // paired --lt-tint-success / --lt-on-tint-success.
    public const string TintSuccess = "#d7f2e0";
    public const string OnTintSuccess = "#075c26";

    /// <summary>
    /// The Makables mark — the same two-ellipse tunnel as
    /// <c>frontend/public/logo-star.svg</c>, re-inked in the light theme's
    /// brand teal (the web asset carries the dark theme's <c>#2dd4bf</c>,
    /// which is a 1.9:1 wash on white).
    /// </summary>
    public const string LogoSvg = $"""
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none">
          <path d="M8.2 12c0-3.55 1.7-6 3.8-6s3.8 2.45 3.8 6-1.7 6-3.8 6-3.8-2.45-3.8-6z" stroke="{Brand}" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"/>
          <path d="M12 8.2c3.55 0 6 1.7 6 3.8s-2.45 3.8-6 3.8-6-1.7-6-3.8 2.45-3.8 6-3.8z" stroke="{Brand}" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"/>
          <circle cx="12" cy="12" r="1.05" fill="{Brand}"/>
        </svg>
        """;
}

/// <summary>
/// Czech formatting choices for the invoice documents, kept next to the
/// renderer so they stay co-located with the templates that use them.
/// </summary>
internal static class InvoiceFormatting
{
    private static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    /// <summary>
    /// Extract the variable symbol from an invoice number. Invoice
    /// numbers carry the format <c>FV-CZ-YYYYNNNN</c>; the VS used for
    /// the SPAYD QR + payment matching is the trailing numeric tail.
    /// Falls back to the full string if the format doesn't parse —
    /// defensive against future format changes.
    /// </summary>
    public static string VariableSymbol(string invoiceNumber)
    {
        // Trailing digits — Spayd.ForInvoice requires digits-only.
        var i = invoiceNumber.Length;
        while (i > 0 && char.IsDigit(invoiceNumber[i - 1])) i--;
        var tail = invoiceNumber[i..];
        return tail.Length > 0 ? tail : invoiceNumber;
    }

    /// <summary>Format <c>VatRateBp</c> as "21 %", "12 %", "0 %".</summary>
    public static string FormatVatRate(int vatRateBp)
    {
        var pct = vatRateBp / 100m;
        return $"{pct.ToString("0.##", CultureInfo.InvariantCulture)} %";
    }

    /// <summary>
    /// Render minor units as a major-units amount with the currency
    /// suffix, Czech conventions throughout (NBSP thousands separator,
    /// "," decimal separator, "Kč" for CZK). The separator is forced to a
    /// non-breaking space because the .NET cs-CZ group separator is a
    /// plain space on some ICU versions, and a line-broken "1 234" on a
    /// legal document is not acceptable.
    /// </summary>
    public static string FormatAmount(long amountMinor, string currency)
    {
        var major = amountMinor / 100m;
        var amountStr = major.ToString("N2", Cs)
            .Replace(' ', '\u00a0')
            .Replace('\u202f', '\u00a0');
        var symbol = currency.ToUpperInvariant() switch
        {
            "CZK" => "Kč",
            "EUR" => "€",
            _ => currency,
        };
        // NBSP before the unit too — Czech typography never breaks between
        // a number and its currency symbol.
        return $"{amountStr}\u00a0{symbol}";
    }

    public static string FormatDate(DateOnly date) => date.ToString("d. M. yyyy", Cs);

    /// <summary>
    /// Human label for <see cref="Invoice.PaymentMethod"/>. The stored
    /// value is the payment provider's own vocabulary — Comgate returns
    /// codes like <c>CARD_CZ_CSOB_2</c> or <c>BANK_CZ_RB</c> — so the
    /// match is on the family prefix, and an unrecognised code falls back
    /// to a truthful generic rather than being printed raw at a customer.
    /// </summary>
    public static string PaymentMethodLabel(string? method)
    {
        if (string.IsNullOrWhiteSpace(method)) return "";

        var code = method.Trim();
        if (string.Equals(code, SettlementMethods.PayoutDeduction, StringComparison.OrdinalIgnoreCase))
            return "srážkou z vyplacené částky";

        var upper = code.ToUpperInvariant();
        if (upper.StartsWith("CARD", StringComparison.Ordinal)) return "platební kartou";
        if (upper.StartsWith("APPLE", StringComparison.Ordinal)) return "přes Apple Pay";
        if (upper.StartsWith("GOOGLE", StringComparison.Ordinal) ||
            upper.StartsWith("GPAY", StringComparison.Ordinal)) return "přes Google Pay";
        if (upper.StartsWith("BANK", StringComparison.Ordinal) ||
            upper.StartsWith("TRANSFER", StringComparison.Ordinal)) return "bankovním převodem";
        if (upper.StartsWith("LATER", StringComparison.Ordinal) ||
            upper.StartsWith("TWISTO", StringComparison.Ordinal)) return "odloženou platbou";
        if (upper.StartsWith("DEV-BYPASS", StringComparison.Ordinal)) return "testovací platbou";

        return "přes platební bránu";
    }

    /// <summary>
    /// Split a one-line registry address ("Příčná 1892/4, Nové Město,
    /// 110 00 Praha 1") into the lines a letterhead block wants. Falls
    /// back to the single line when there are no separators.
    /// </summary>
    public static IReadOnlyList<string> AddressLines(string? address) =>
        string.IsNullOrWhiteSpace(address)
            ? []
            : address.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
