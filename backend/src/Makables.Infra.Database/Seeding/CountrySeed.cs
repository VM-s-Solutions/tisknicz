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
                platformFeeRateBp: 700,   // 7% (loyalty-discounted rate of 3.5% is a separate feature)
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
                defaultEmailProvider: "sendgrid",
                // T-0068b locked decision 8: CZ seed issuer = JVM YORE s.r.o.
                // IČO ships with placeholder '00000000' per user direction;
                // a one-line data migration replaces it pre-production-launch
                // (tracked by manual_step "country-config-ico-replace-placeholder-pre-launch").
                // Invoice.Issue validates length only (8 chars), not mod-11,
                // so the placeholder is build-safe.
                issuerName: "JVM YORE s.r.o.",
                issuerIco: "00000000",
                // T-0068a locked decision 2: JVM YORE not VAT-registered at
                // MVP launch — DIČ null until cross 2M CZK threshold.
                // T-0068b locked decision 4: platform IBAN null at MVP —
                // bank-account decision is open; renderer skips SPAYD QR
                // when null. Admin populates later via a one-line update.
                issuerDic: null,
                platformIban: null)
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
