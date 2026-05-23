using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Configuration;

/// <summary>
/// Reference data for a country we may operate in (or already do).
/// Inherits <see cref="Auditable"/> — but its own <see cref="Auditable.CountryCode"/>
/// equals its <see cref="IsoCode"/> (a country's "country" is itself).
///
/// Per ADR 0004, distinguishes two flags:
/// <list type="bullet">
///   <item><see cref="BaseEntity.IsActive"/> — admin-catalog visibility (whether the country shows up in any admin picker).</item>
///   <item><see cref="IsServiced"/> — whether customers can place orders here (open for business).</item>
/// </list>
/// </summary>
public sealed class Country : Auditable
{
    public string Name { get; private set; } = default!;
    public string IsoCode { get; private set; } = default!;
    public bool IsServiced { get; private set; }

    private Country() { }

    public static Country Create(string isoCode, string name, bool isServiced)
    {
        if (string.IsNullOrWhiteSpace(isoCode) || isoCode.Length != 2)
        {
            throw new ArgumentException("IsoCode must be 2 chars (ISO 3166-1 alpha-2).", nameof(isoCode));
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name must be non-empty.", nameof(name));
        }

        var normalized = isoCode.ToUpperInvariant();
        return new Country
        {
            Id = normalized,
            IsoCode = normalized,
            Name = name,
            IsServiced = isServiced,
            CountryCode = normalized,
        };
    }

    public Country SetServiced(bool isServiced)
    {
        IsServiced = isServiced;
        return this;
    }
}
