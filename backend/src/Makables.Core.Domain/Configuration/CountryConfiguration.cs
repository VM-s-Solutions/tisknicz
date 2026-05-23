using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Configuration;

/// <summary>
/// Per-country control plane per ADR 0004 / patterns §A.12. Every domain
/// service that varies per country reads from this entity — code MUST NOT
/// branch on country directly.
///
/// PK is <see cref="CountryId"/>. Inherits <see cref="Auditable"/>;
/// <see cref="Auditable.CountryCode"/> equals <see cref="CountryId"/>.
///
/// Provider codes (<see cref="DefaultPaymentProvider"/>, etc.) must match
/// a keyed-service registration in the DI container; the admin UI
/// validates this at write time.
/// </summary>
public sealed class CountryConfiguration : Auditable
{
    public string CountryId { get; private set; } = default!;

    // Locale + display
    public string DefaultCurrencyCode { get; private set; } = default!;   // ISO 4217 (3 chars)
    public string DefaultLanguageCode { get; private set; } = default!;   // e.g. "cs-CZ"
    public string TimeZoneId { get; private set; } = default!;            // e.g. "Europe/Prague"
    public string PhonePrefix { get; private set; } = default!;           // e.g. "+420"
    public string DateFormat { get; private set; } = default!;            // e.g. "d. M. yyyy"
    public string? ZipFormat { get; private set; }                        // regex; nullable

    // Tax / VAT (basis points to avoid floats)
    public int StandardVatRateBp { get; private set; }
    public int? ReducedVatRateBp { get; private set; }
    public InvoicingMode InvoicingMode { get; private set; } = InvoicingMode.None;

    // Platform fee
    public int PlatformFeeRateBp { get; private set; } = 1500;            // 15% by default

    // Business identifiers
    public string TaxIdLabel { get; private set; } = default!;            // e.g. "DIČ"
    public string? TaxIdFormat { get; private set; }                      // regex; nullable
    public string VatIdLabel { get; private set; } = default!;            // e.g. "DIČ DPH"
    public string? VatIdFormat { get; private set; }
    public bool VatIdRequired { get; private set; }
    public string RegistrationNumberLabel { get; private set; } = default!;   // e.g. "IČO"
    public string? RegistrationNumberFormat { get; private set; }
    public bool RegistrationNumberRequired { get; private set; } = true;

    // Provider defaults (keyed-service codes)
    public string DefaultPaymentProvider { get; private set; } = default!;
    public string DefaultShippingCarrier { get; private set; } = default!;
    public string DefaultRegistry { get; private set; } = default!;
    public string DefaultEmailProvider { get; private set; } = default!;

    // Free-form per-country legal rules (JSON)
    public string? LegalRequirementsJson { get; private set; }

    private CountryConfiguration() { }

    /// <summary>Factory for seed migrations + admin UI creation.</summary>
    public static CountryConfiguration Create(
        string countryId,
        string defaultCurrencyCode,
        string defaultLanguageCode,
        string timeZoneId,
        string phonePrefix,
        string dateFormat,
        int standardVatRateBp,
        string taxIdLabel,
        string vatIdLabel,
        string registrationNumberLabel,
        string defaultPaymentProvider,
        string defaultShippingCarrier,
        string defaultRegistry,
        string defaultEmailProvider,
        int? reducedVatRateBp = null,
        string? zipFormat = null,
        string? taxIdFormat = null,
        string? vatIdFormat = null,
        bool vatIdRequired = false,
        string? registrationNumberFormat = null,
        bool registrationNumberRequired = true,
        InvoicingMode invoicingMode = InvoicingMode.None,
        int platformFeeRateBp = 1500,
        string? legalRequirementsJson = null)
    {
        if (string.IsNullOrWhiteSpace(countryId) || countryId.Length != 2)
            throw new ArgumentException("CountryId must be 2 chars (ISO 3166-1 alpha-2).", nameof(countryId));
        if (defaultCurrencyCode is null || defaultCurrencyCode.Length != 3)
            throw new ArgumentException("DefaultCurrencyCode must be 3 chars.", nameof(defaultCurrencyCode));
        if (standardVatRateBp < 0 || standardVatRateBp > 10_000)
            throw new ArgumentOutOfRangeException(nameof(standardVatRateBp), "Must be 0..10000.");
        if (reducedVatRateBp is not null && reducedVatRateBp > standardVatRateBp)
            throw new ArgumentOutOfRangeException(nameof(reducedVatRateBp), "Must be ≤ standard rate.");
        if (platformFeeRateBp < 0 || platformFeeRateBp > 10_000)
            throw new ArgumentOutOfRangeException(nameof(platformFeeRateBp), "Must be 0..10000.");

        var normalized = countryId.ToUpperInvariant();

        return new CountryConfiguration
        {
            Id = normalized,
            CountryId = normalized,
            CountryCode = normalized,
            DefaultCurrencyCode = defaultCurrencyCode.ToUpperInvariant(),
            DefaultLanguageCode = defaultLanguageCode,
            TimeZoneId = timeZoneId,
            PhonePrefix = phonePrefix,
            DateFormat = dateFormat,
            ZipFormat = zipFormat,
            StandardVatRateBp = standardVatRateBp,
            ReducedVatRateBp = reducedVatRateBp,
            InvoicingMode = invoicingMode,
            PlatformFeeRateBp = platformFeeRateBp,
            TaxIdLabel = taxIdLabel,
            TaxIdFormat = taxIdFormat,
            VatIdLabel = vatIdLabel,
            VatIdFormat = vatIdFormat,
            VatIdRequired = vatIdRequired,
            RegistrationNumberLabel = registrationNumberLabel,
            RegistrationNumberFormat = registrationNumberFormat,
            RegistrationNumberRequired = registrationNumberRequired,
            DefaultPaymentProvider = defaultPaymentProvider,
            DefaultShippingCarrier = defaultShippingCarrier,
            DefaultRegistry = defaultRegistry,
            DefaultEmailProvider = defaultEmailProvider,
            LegalRequirementsJson = legalRequirementsJson,
        };
    }

    // === Mutation methods (admin actions; audited per ADR 0014) ===

    public CountryConfiguration UpdateVatRates(int standardBp, int? reducedBp)
    {
        if (standardBp < 0 || standardBp > 10_000)
            throw new ArgumentOutOfRangeException(nameof(standardBp));
        if (reducedBp is not null && reducedBp > standardBp)
            throw new ArgumentOutOfRangeException(nameof(reducedBp));
        StandardVatRateBp = standardBp;
        ReducedVatRateBp = reducedBp;
        return this;
    }

    public CountryConfiguration UpdateInvoicingMode(InvoicingMode mode)
    {
        InvoicingMode = mode;
        return this;
    }

    public CountryConfiguration UpdatePlatformFeeRate(int rateBp)
    {
        if (rateBp < 0 || rateBp > 10_000)
            throw new ArgumentOutOfRangeException(nameof(rateBp));
        PlatformFeeRateBp = rateBp;
        return this;
    }

    public CountryConfiguration UpdateProviders(
        string paymentProvider,
        string shippingCarrier,
        string registry,
        string emailProvider)
    {
        DefaultPaymentProvider = paymentProvider ?? throw new ArgumentNullException(nameof(paymentProvider));
        DefaultShippingCarrier = shippingCarrier ?? throw new ArgumentNullException(nameof(shippingCarrier));
        DefaultRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
        DefaultEmailProvider = emailProvider ?? throw new ArgumentNullException(nameof(emailProvider));
        return this;
    }
}
