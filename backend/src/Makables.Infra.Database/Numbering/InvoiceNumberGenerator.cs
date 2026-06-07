using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Numbering;

namespace Makables.Infra.Database.Numbering;

/// <summary>
/// Postgres-backed gap-free invoice number allocator per ADR 0009. CZ tax
/// law requires gap-free invoice numbering; this guarantee comes from the
/// allocation being inside the surrounding <c>IssueInvoice</c> command's
/// transaction — any failure rolls back without consuming the number.
///
/// <para>
/// Year segment is computed from the country's
/// <see cref="CountryConfiguration.TimeZoneId"/> applied to
/// <see cref="IClock.UtcNow"/>. See <see cref="IInvoiceNumberGenerator"/>
/// for the rationale. Mirrors <see cref="OrderNumberGenerator"/>
/// verbatim — the TZ lookup is microseconds; the FOR UPDATE row-lock
/// already dominates per-call cost.
/// </para>
/// </summary>
public sealed class InvoiceNumberGenerator(
    MakablesDbContext db,
    IClock clock,
    ICountryConfigurationRepository configs)
    : IInvoiceNumberGenerator
{
    public async Task<string> NextAsync(string countryCode, CancellationToken cancellationToken)
    {
        // Same contract as OrderNumberGenerator — CountryConfiguration is
        // seed-required, a missing row is a programmer error / seed-data
        // integrity bug, surfaced as InvalidOperationException rather than
        // a typed BusinessResult failure. T-0068b's IInvoiceService caller
        // has already validated countryCode through the validator pipeline.
        var config = await configs.GetByCodeAsync(countryCode, cancellationToken)
            ?? throw new InvalidOperationException(
                $"CountryConfiguration missing for country code '{countryCode}'. " +
                "Seed-data integrity bug: every serviced country must have a " +
                "row in country_configuration with a populated TimeZoneId.");

        // IANA TZ IDs (e.g. "Europe/Prague") work on .NET 10 cross-platform:
        // ICU on Linux containers, native on Windows hosts. We don't call
        // TryConvertIanaIdToWindowsId — the framework resolves either form.
        var tz = TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow.UtcDateTime, tz);

        return await NumberingSequenceAllocator.AllocateAsync(
            db, clock, countryCode, NumberingScope.Invoice, nowLocal.Year, cancellationToken);
    }
}
