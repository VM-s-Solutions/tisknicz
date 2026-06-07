using System.Globalization;

namespace Makables.Core.Domain.Payments;

/// <summary>
/// SPAYD (Short Payment Descriptor) 1.0 encoder for Czech pay-by-QR. The
/// output is a single ASCII string that an issuing bank's mobile app
/// (Air Bank, Raiffeisenbank, KB, ČSOB, ...) decodes from a QR code into
/// a pre-filled payment order. Per the SPAYD spec
/// (<see href="https://www.cnb.cz/cs/platebni-styk/spaye/"/>) used by
/// Czech banks via the QR Platba standard.
///
/// <para>
/// <b>Format (T-0068b locked decision 4).</b>
/// <c>SPD*1.0*ACC:&lt;iban&gt;*AM:&lt;amount&gt;*CC:&lt;ccy&gt;*X-VS:&lt;vs&gt;</c>
/// — fields separated by <c>*</c>; <c>SPD*1.0</c> is the magic header;
/// <c>ACC</c> = IBAN; <c>AM</c> = amount in major units with a "."
/// decimal separator + exactly 2 decimals; <c>CC</c> = ISO 4217
/// currency; <c>X-VS</c> = Czech bank-account variable symbol
/// (digits only — used as the invoice's payment matching key).
/// </para>
///
/// <para>
/// <b>Why a static factory + format-string composition.</b> The output is
/// rendered into a QR by <c>QuestPdfInvoiceRenderer</c> when the
/// per-country <c>platform_iban</c> is non-null (T-0068b locked decision
/// 4). The QR encoder's input is the exact string emitted here — minor
/// whitespace / casing / amount-precision deviations break the pay-by-QR
/// flow silently because the bank's parser is strict. The value object
/// owns the normalisation (IBAN spaces stripped, IBAN + currency
/// uppercased, amount formatted with InvariantCulture so a Czech machine
/// doesn't emit <c>121,00</c> via comma-decimal-locale).
/// </para>
///
/// <para>
/// <b>Why this lives in <c>Core.Domain.Payments</c>.</b> It carries no
/// infrastructure dependency (no QR encoder, no HttpClient) — it is a
/// pure value-object encoder, the same as <see cref="Money"/>. The
/// renderer in <c>Infra.PdfRendering</c> consumes the output; the
/// domain owns the format invariants.
/// </para>
/// </summary>
public static class Spayd
{
    /// <summary>
    /// Compose a SPAYD string for an invoice payment. The IBAN is
    /// whitespace-stripped + uppercased; the currency is uppercased; the
    /// amount is formatted with InvariantCulture to two decimals; the
    /// variable symbol is validated as digits-only.
    /// </summary>
    /// <param name="iban">IBAN of the payee. Whitespace is stripped.</param>
    /// <param name="amountMinor">Amount to pay in minor units (haléře for CZK). Must be ≥ 0.</param>
    /// <param name="currency">ISO 4217 3-char currency code.</param>
    /// <param name="variableSymbol">Czech VS — digits only (typically the invoice's order number).</param>
    /// <exception cref="ArgumentException">Blank / invalid input.</exception>
    public static string ForInvoice(
        string iban,
        long amountMinor,
        string currency,
        string variableSymbol)
    {
        if (string.IsNullOrWhiteSpace(iban))
            throw new ArgumentException("IBAN is required.", nameof(iban));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required.", nameof(currency));
        if (string.IsNullOrWhiteSpace(variableSymbol))
            throw new ArgumentException("Variable symbol is required.", nameof(variableSymbol));
        if (amountMinor < 0)
            throw new ArgumentException("Amount must be non-negative.", nameof(amountMinor));

        // VS is digits only per the Czech bank-account spec. A non-numeric
        // VS would generate a QR the bank app refuses to scan; reject at
        // the encoder boundary so the failure surfaces in tests, not in
        // a customer's banking app.
        var trimmedVs = variableSymbol.Trim();
        foreach (var ch in trimmedVs)
        {
            if (!char.IsDigit(ch))
                throw new ArgumentException(
                    $"Variable symbol must be digits only (got '{trimmedVs}').",
                    nameof(variableSymbol));
        }

        // Strip every whitespace char from the IBAN (banks display
        // "CZ65 0800 0000 1920 0014 5399" but the SPAYD spec wants the
        // contiguous form) and uppercase.
        var normalisedIban = StripWhitespace(iban).ToUpperInvariant();
        var normalisedCurrency = currency.Trim().ToUpperInvariant();

        // Amount in major units, exactly two decimals, "." separator.
        // InvariantCulture so a Czech machine never emits "121,00".
        var major = amountMinor / 100m;
        var amountStr = major.ToString("F2", CultureInfo.InvariantCulture);

        return $"SPD*1.0*ACC:{normalisedIban}*AM:{amountStr}*CC:{normalisedCurrency}*X-VS:{trimmedVs}";
    }

    private static string StripWhitespace(string value)
    {
        // Single allocation path — Span<char> over the input, write
        // non-whitespace chars to a stackalloc buffer, materialise once.
        Span<char> buffer = stackalloc char[value.Length];
        var written = 0;
        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch))
            {
                buffer[written++] = ch;
            }
        }
        return new string(buffer[..written]);
    }
}
