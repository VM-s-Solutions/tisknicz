using System.Globalization;

namespace Makables.Core.Domain.Numbering;

/// <summary>
/// Per <c>(countryCode, scope, year)</c> sequence row that backs
/// <see cref="IOrderNumberGenerator"/>, <see cref="IInvoiceNumberGenerator"/>,
/// and (indirectly) <see cref="IPayoutBatchNumberGenerator"/>. Per ADR 0009.
///
/// Composite primary key: <c>(CountryCode, Scope, Year)</c>. Not
/// <see cref="Common.Auditable"/> — this is internal bookkeeping, not a
/// transactional entity. No audit columns, no soft delete.
///
/// Concurrent allocation safety is provided by <c>SELECT ... FOR UPDATE</c>
/// inside the surrounding command's transaction; that's the responsibility
/// of the generator implementations in <c>Infra.Database</c>.
/// </summary>
public sealed class NumberingSequence
{
    public string CountryCode { get; private set; } = default!;
    public string Scope { get; private set; } = default!;
    public int Year { get; private set; }
    public int LastUsedValue { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private NumberingSequence() { }

    public static NumberingSequence StartAt(string countryCode, string scope, int year, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(countryCode) || countryCode.Length != 2)
        {
            throw new ArgumentException("CountryCode must be a 2-letter ISO 3166-1 alpha-2 code.", nameof(countryCode));
        }
        if (string.IsNullOrEmpty(scope))
        {
            throw new ArgumentException("Scope must be non-empty.", nameof(scope));
        }
        if (year < 2026 || year > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year must be between 2026 and 9999.");
        }

        return new NumberingSequence
        {
            CountryCode = countryCode.ToUpperInvariant(),
            Scope = scope,
            Year = year,
            LastUsedValue = 0,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Increment the sequence and return the formatted number. Callers must
    /// hold a row-level lock (acquired via <c>SELECT ... FOR UPDATE</c>) before
    /// calling this in a multi-writer setting.
    /// </summary>
    public string Increment(DateTimeOffset now)
    {
        LastUsedValue = checked(LastUsedValue + 1);
        UpdatedAt = now;
        return Format();
    }

    /// <summary>
    /// Format the *current* <see cref="LastUsedValue"/> as a human-readable
    /// number string. Format is scope-dependent per ADR 0009:
    /// <list type="bullet">
    ///   <item><c>M-{CC}-{YYYY}{NNNN}</c> for <c>order</c></item>
    ///   <item><c>FV-{CC}-{YYYY}{NNNN}</c> for <c>invoice</c></item>
    /// </list>
    /// PayoutBatch does not use this sequence table; its number is derived
    /// directly from the ISO week (see <see cref="IPayoutBatchNumberGenerator"/>).
    /// </summary>
    public string Format()
    {
        var prefix = Scope switch
        {
            NumberingScope.Order => "M",
            NumberingScope.Invoice => "FV",
            _ => throw new InvalidOperationException(
                $"NumberingSequence does not support scope '{Scope}' format. " +
                "Custom scopes must format themselves.")
        };

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}-{CountryCode}-{Year:D4}{LastUsedValue:D4}");
    }
}
