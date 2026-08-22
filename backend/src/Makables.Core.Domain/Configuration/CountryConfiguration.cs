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

    /// <summary>
    /// Default shipping price in minor currency units (haléře for CZK)
    /// applied to <c>ShippingMethod.ZasilkovnaPickupPoint</c> orders when
    /// no per-tariff lookup is available. Admin-editable (T-0108);
    /// T-0061 reads this as the single source of truth for the
    /// platform-wide Zásilkovna default. Non-negative. Seed CZ value is
    /// 7900 (79 CZK, midpoint of the 69–89 CZK Zásilkovna range per
    /// <c>PROJEKT-VIZE.md</c>).
    /// </summary>
    public long DefaultShippingPriceMinor { get; private set; }

    /// <summary>
    /// How long after <c>Order.PaidAt</c> a maker may still REFUSE a paid
    /// order they cannot fulfil (T-0181 / Q-0041 — "two days, for
    /// example"). Past the window the maker must go through admin support,
    /// which is the pre-T-0181 status quo, so the window only ever widens
    /// what a maker can do.
    ///
    /// <para>
    /// A row rather than a constant on purpose (ADR 0004): this is a
    /// business policy that will be tuned, and tuning it must not need a
    /// deploy. Seed CZ value is 48 (two days).
    /// </para>
    /// </summary>
    public int MakerRefusalWindowHours { get; private set; } = 48;

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

    // Invoice issuer + IBAN (T-0068b locked decisions 4 + 8) ===================

    /// <summary>
    /// Legal name of the platform's invoicing entity for this country —
    /// snapshotted onto every <see cref="Invoices.Invoice"/> at issuance
    /// time. CZ seed value: <c>"JVM YORE s.r.o."</c>. Required; not
    /// nullable in DB.
    /// </summary>
    public string IssuerName { get; private set; } = default!;

    /// <summary>
    /// Platform issuer's IČO (Czech business registration number — 8
    /// chars). CZ seed ships with the placeholder <c>"00000000"</c> per
    /// T-0068b user direction; replaced pre-production-launch via a
    /// one-line data migration tracked by the
    /// <c>country-config-ico-replace-placeholder-pre-launch</c>
    /// manual_step. <see cref="Invoices.Invoice.Issue"/> validates length
    /// only (NOT mod-11) — the platform's own IČO is not subject to ARES
    /// validation. Required; not nullable in DB.
    /// </summary>
    public string IssuerIco { get; private set; } = default!;

    /// <summary>
    /// Platform issuer's DIČ (Czech VAT-payer id, e.g. <c>CZ12345678</c>).
    /// Nullable: JVM YORE is not VAT-registered at MVP launch per
    /// T-0068a locked decision 2. When JVM YORE crosses the 2M CZK
    /// threshold and registers for VAT, this gets populated and new
    /// invoices snapshot the value. Historical rows keep the null they
    /// were issued with.
    /// </summary>
    public string? IssuerDic { get; private set; }

    /// <summary>
    /// Platform's IBAN for pay-by-QR (SPAYD) rendering on invoice PDFs
    /// per T-0068b locked decision 4. Nullable at MVP — JVM YORE's
    /// bank-account decision is open, so the renderer skips SPAYD QR
    /// rendering when this is null and renders the invoice without a
    /// QR code. When admin later populates this (via DB seed or admin
    /// UI in a downstream ticket), SPAYD QR codes automatically appear
    /// on new invoices. Already-issued invoices are unaffected — PDFs
    /// are blob-stored and frozen.
    /// </summary>
    public string? PlatformIban { get; private set; }

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
        string issuerName,
        string issuerIco,
        int? reducedVatRateBp = null,
        string? zipFormat = null,
        string? taxIdFormat = null,
        string? vatIdFormat = null,
        bool vatIdRequired = false,
        string? registrationNumberFormat = null,
        bool registrationNumberRequired = true,
        InvoicingMode invoicingMode = InvoicingMode.None,
        int platformFeeRateBp = 1500,
        long defaultShippingPriceMinor = 0,
        string? legalRequirementsJson = null,
        string? issuerDic = null,
        string? platformIban = null)
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
        if (defaultShippingPriceMinor < 0)
            throw new ArgumentException(
                "DefaultShippingPriceMinor cannot be negative.",
                nameof(defaultShippingPriceMinor));
        if (string.IsNullOrWhiteSpace(issuerName))
            throw new ArgumentException("IssuerName is required.", nameof(issuerName));
        if (string.IsNullOrWhiteSpace(issuerIco))
            throw new ArgumentException("IssuerIco is required.", nameof(issuerIco));

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
            DefaultShippingPriceMinor = defaultShippingPriceMinor,
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
            IssuerName = issuerName.Trim(),
            IssuerIco = issuerIco.Trim(),
            IssuerDic = string.IsNullOrWhiteSpace(issuerDic) ? null : issuerDic.Trim(),
            PlatformIban = string.IsNullOrWhiteSpace(platformIban) ? null : platformIban.Trim(),
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

    /// <summary>
    /// Admin self-service patch (wired through T-0108
    /// <c>UpdateCountryConfiguration</c>) for the per-country Zásilkovna
    /// default shipping price. Rejects negative values — the entity
    /// guards programmer-error inputs, the command-layer validator covers
    /// user-input messaging. Existing orders are unaffected (they hold a
    /// pricing snapshot taken at order time per <c>Order.Create</c>).
    /// </summary>
    public CountryConfiguration UpdateDefaultShippingPrice(long minor)
    {
        if (minor < 0)
            throw new ArgumentException(
                "DefaultShippingPriceMinor cannot be negative.",
                nameof(minor));
        DefaultShippingPriceMinor = minor;
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
