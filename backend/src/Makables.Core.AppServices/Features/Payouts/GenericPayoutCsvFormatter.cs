using System.Globalization;
using System.Text;
using Makables.Core.Domain.Payouts;

namespace Makables.Core.AppServices.Features.Payouts;

/// <summary>
/// Default <see cref="IPayoutCsvFormatter"/> — the generic documented
/// format (Q1), frozen by golden-file tests per §C.5. Pure, zero DI
/// dependencies. Header <c>account;amount;vs;message</c>, CRLF line
/// endings, semicolon delimiter; one row per maker in caller order. Amount
/// is invariant-culture <c>0.00</c> decimal CZK; VS is the digits of the
/// batch number; message is <c>{batchNumber} {company}</c> truncated to 140
/// chars. A blank bank account is a programmer error (the Q5 claim
/// guarantee) → <see cref="ArgumentException"/>.
///
/// <para>
/// <b>Output-boundary hardening</b> (payout-core-bundle Gate-3 MEDIUM).
/// Every emitted field passes through <see cref="EscapeField"/>, which
/// (1) neutralizes spreadsheet formula injection — a field whose first
/// (post-trim) char is <c>= + - @ \t \r</c> is prefixed with a single
/// quote per OWASP CSV-injection guidance — and (2) applies RFC-4180
/// quoting — a field containing the <c>;</c> delimiter, a <c>"</c>, CR, or
/// LF is wrapped in double quotes with interior quotes doubled. This guards
/// the maker-supplied company name carried in the message column (an ARES
/// registry snapshot that legitimately contains <c>+ - &amp; /</c>) so the
/// bank-transfer CSV an admin opens in Excel cannot execute a formula or
/// inject an extra payment row. Amount/VS/batch-number are system-shaped;
/// quoting them is harmless and kept uniform.
/// </para>
///
/// <para>
/// String-in/string-out — the UTF-8 BOM is the artifact service's encoding
/// job (<c>new UTF8Encoding(true)</c>), NOT this formatter's.
/// </para>
/// </summary>
public sealed class GenericPayoutCsvFormatter : IPayoutCsvFormatter
{
    private const string Crlf = "\r\n";
    private const char Delimiter = ';';
    private const int MaxVsDigits = 10;     // Czech VS limit.
    private const int MaxMessageLength = 140;

    public string Format(PayoutCsvBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var vs = ExtractVariableSymbol(batch.BatchNumber);

        var sb = new StringBuilder();
        sb.Append("account").Append(Delimiter)
          .Append("amount").Append(Delimiter)
          .Append("vs").Append(Delimiter)
          .Append("message").Append(Crlf);

        foreach (var line in batch.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.BankAccount))
                throw new ArgumentException(
                    "PayoutCsvLine.BankAccount is required — the Q5 claim invariant guarantees a validated account.",
                    nameof(batch));

            var amount = FormatAmount(line.AmountMinor);
            var message = BuildMessage(batch.BatchNumber, line.MakerCompanyName);

            sb.Append(EscapeField(line.BankAccount)).Append(Delimiter)
              .Append(EscapeField(amount)).Append(Delimiter)
              .Append(EscapeField(vs)).Append(Delimiter)
              .Append(EscapeField(message)).Append(Crlf);
        }

        return sb.ToString();
    }

    /// <summary>
    /// CSV output-boundary guard: formula-char neutralization followed by
    /// RFC-4180 quoting. The two passes compose — a value that both starts
    /// with a formula char AND contains a delimiter is prefixed with the
    /// single quote first, then the whole (now formula-safe) value is
    /// wrapped. A benign value (no formula lead, no special char) is
    /// returned verbatim, so the existing golden file is unchanged.
    /// </summary>
    private static string EscapeField(string value)
    {
        var neutralized = NeutralizeFormula(value);
        return QuoteIfNeeded(neutralized);
    }

    /// <summary>
    /// OWASP CSV-injection guard: if the field's first non-trimmed char is a
    /// formula trigger (<c>= + - @</c>) or a leading control char Excel
    /// treats as a formula start (<c>\t \r</c>), prefix a single quote so
    /// the spreadsheet renders it as inert text. An empty/whitespace value
    /// has no trigger char and is returned unchanged.
    /// </summary>
    private static string NeutralizeFormula(string value)
    {
        if (value.Length == 0) return value;
        var first = value[0];
        return first is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? "'" + value
            : value;
    }

    /// <summary>
    /// RFC-4180 quoting: a field containing the delimiter, a double quote,
    /// CR, or LF is wrapped in double quotes with interior quotes doubled.
    /// Other fields are returned verbatim.
    /// </summary>
    private static string QuoteIfNeeded(string value)
    {
        var needsQuoting = value.IndexOf(Delimiter) >= 0
            || value.IndexOf('"') >= 0
            || value.IndexOf('\r') >= 0
            || value.IndexOf('\n') >= 0;
        if (!needsQuoting) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>Minor units → invariant <c>0.00</c> decimal (123456 → "1234.56").</summary>
    private static string FormatAmount(long amountMinor)
    {
        var major = amountMinor / 100m;
        return major.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// VS = the digits of the batch number, capped at the 10-digit Czech VS
    /// limit (VYP-CZ-2026-W24 → "202624"). Falls back to the full digit run
    /// if format ever changes.
    /// </summary>
    private static string ExtractVariableSymbol(string batchNumber)
    {
        var digits = new StringBuilder(batchNumber.Length);
        foreach (var ch in batchNumber)
        {
            if (char.IsDigit(ch)) digits.Append(ch);
        }
        var s = digits.ToString();
        return s.Length > MaxVsDigits ? s[^MaxVsDigits..] : s;
    }

    private static string BuildMessage(string batchNumber, string companyName)
    {
        var message = $"{batchNumber} {companyName}";
        return message.Length > MaxMessageLength ? message[..MaxMessageLength] : message;
    }
}
