using Makables.Core.Domain.Configuration;

namespace Makables.Infra.Database.Seeding;

/// <summary>
/// Static factory for the Czech Republic country + configuration seed.
/// Called by the initial migration to populate both rows. Per ADR 0004
/// (CountryConfiguration is seeded via migration; admin UI edits later).
///
/// Provider codes ("comgate", "packeta", "ares", "sendgrid") are the
/// keyed-service codes the adapter tickets will register. The email
/// provider was switched from Resend → SendGrid in T-0028 (see
/// ADR 0019 §"Amendment 2026-05-24"). Until each Phase-2/4 adapter
/// lands, runtime lookups via the per-provider factories throw —
/// expected, because no concrete adapter exists yet.
/// </summary>
public static class CountrySeed
{
    public static Country Czechia(DateTimeOffset seededAt)
    {
        return Country
            .Create("CZ", "Česká republika", isServiced: true)
            .MarkCreatedAs("seed", seededAt);
    }

    public static CountryConfiguration CzechiaConfiguration(DateTimeOffset seededAt)
    {
        return CountryConfiguration
            .Create(
                countryId: "CZ",
                defaultCurrencyCode: "CZK",
                defaultLanguageCode: "cs-CZ",
                timeZoneId: "Europe/Prague",
                phonePrefix: "+420",
                dateFormat: "d. M. yyyy",
                zipFormat: @"^\d{3}\s?\d{2}$",
                standardVatRateBp: 2100,  // 21%
                reducedVatRateBp: 1200,   // 12%
                invoicingMode: InvoicingMode.None,  // platform not yet VAT-registered at MVP launch
                platformFeeRateBp: 1500,  // 15%
                taxIdLabel: "DIČ",
                taxIdFormat: @"^CZ\d{8,10}$",
                vatIdLabel: "DIČ",
                vatIdFormat: @"^CZ\d{8,10}$",
                vatIdRequired: false,
                registrationNumberLabel: "IČO",
                registrationNumberFormat: @"^\d{8}$",
                registrationNumberRequired: true,
                defaultPaymentProvider: "comgate",
                defaultShippingCarrier: "packeta",
                defaultRegistry: "ares",
                defaultEmailProvider: "sendgrid")
            .MarkCreatedAs("seed", seededAt);
    }

    // Convenience extensions so the seed factory reads cleanly.
    private static T MarkCreatedAs<T>(this T entity, string by, DateTimeOffset at)
        where T : Core.Domain.Common.Auditable
    {
        entity.MarkCreated(by, at);
        return entity;
    }
}
