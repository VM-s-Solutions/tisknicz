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

            sb.Append(line.BankAccount).Append(Delimiter)
              .Append(amount).Append(Delimiter)
              .Append(vs).Append(Delimiter)
              .Append(message).Append(Crlf);
        }

        return sb.ToString();
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
