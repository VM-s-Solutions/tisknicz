using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Numbering;

namespace Makables.Infra.Database.Numbering;

/// <summary>
/// Postgres-backed allocator using <c>SELECT ... FOR UPDATE</c> per ADR 0009.
/// The lock is held for the duration of the surrounding transaction, so
/// concurrent allocators serialize and the increment only commits if the
/// command succeeds. Order numbers are NOT legally gap-free, but the
/// pattern is the same as for invoices to keep one implementation strategy
/// across all numbering scopes.
///
/// <para>
/// Year segment is computed from the country's
/// <see cref="CountryConfiguration.TimeZoneId"/> applied to
/// <see cref="IClock.UtcNow"/>. See <see cref="IOrderNumberGenerator"/>
/// for the rationale. The TZ lookup is microseconds; the FOR UPDATE
/// row-lock already dominates per-call cost.
/// </para>
/// </summary>
public sealed class OrderNumberGenerator(
    MakablesDbContext db,
    IClock clock,
    ICountryConfigurationRepository configs)
    : IOrderNumberGenerator
{
    public async Task<string> NextAsync(string countryCode, CancellationToken cancellationToken)
    {
        // CountryConfiguration is seed-required (see InitialSchema migration's
        // CZ insert). A missing row at this point is a programmer error /
        // seed-data integrity bug — same shape as the T-0061 currency mismatch
        // contract — so we surface it as InvalidOperationException rather than
        // a typed BusinessResult failure. The caller (T-0063 CreateOrder) has
        // already validated countryCode through the validator pipeline.
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
            db, clock, countryCode, NumberingScope.Order, nowLocal.Year, cancellationToken);
    }
}
