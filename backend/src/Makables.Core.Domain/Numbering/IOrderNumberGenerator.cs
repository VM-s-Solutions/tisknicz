namespace Makables.Core.Domain.Numbering;

/// <summary>
/// Hand out the next order number per ADR 0009. Format
/// <c>M-{CC}-{YYYY}{NNNN}</c>. Not legally gap-free (orders that fail to
/// pay leave gaps); concurrent safety via <c>FOR UPDATE</c> in the
/// generator impl.
///
/// <para>
/// The year segment is <b>country-local</b>: it is derived from the
/// country's <c>CountryConfiguration.TimeZoneId</c> applied to the
/// current UTC moment, not from the caller's clock-year. This means a
/// 23:30 Prague order on December 31 buckets into the local-year
/// sequence (the year the customer sees on the invoice), not the
/// previous UTC year. Per ADR 0009 TZ-aware-year amendment.
/// </para>
///
/// <para>
/// The <c>int year</c> parameter that earlier versions of this
/// interface exposed has been removed deliberately: a caller passing
/// <c>clock.UtcNow.Year</c> would compile but ship the wrong year for
/// the 30 minutes per year between 23:00 UTC Dec 31 and midnight local
/// Prague. Removing it forces every caller into the TZ-aware contract.
/// </para>
/// </summary>
public interface IOrderNumberGenerator
{
    Task<string> NextAsync(string countryCode, CancellationToken cancellationToken);
}
