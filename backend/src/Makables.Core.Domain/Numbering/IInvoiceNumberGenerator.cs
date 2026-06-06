namespace Makables.Core.Domain.Numbering;

/// <summary>
/// Hand out the next invoice number per ADR 0009. Format
/// <c>FV-{CC}-{YYYY}{NNNN}</c>. <b>Gap-free</b> by mechanism (allocation
/// happens inside the surrounding <c>IssueInvoice</c> command's
/// transaction; any failure rolls back without consuming a number) —
/// CZ legal requirement under § 29 zákon o DPH.
///
/// <para>
/// The year segment is <b>country-local</b>: it is derived from the
/// country's <c>CountryConfiguration.TimeZoneId</c> applied to the
/// current UTC moment, not from the caller's clock-year. A 23:30 Prague
/// invoice on December 31 buckets into the local-year sequence (the year
/// the customer sees on the document), not the previous UTC year. Per
/// ADR 0009 T-0068a amendment — this generator follows the same
/// TZ-aware contract that <see cref="IOrderNumberGenerator"/> migrated to
/// in T-0062.
/// </para>
///
/// <para>
/// The <c>int year</c> parameter that earlier versions of this interface
/// exposed has been removed deliberately at T-0068a: a caller passing
/// <c>clock.UtcNow.Year</c> would compile but ship the wrong year for
/// the 1-hour window per year between 23:00 UTC Dec 31 and 00:00 UTC
/// Jan 1 (midnight local Prague in winter, since CET = UTC+1). For
/// invoices that is a real CZ tax-law compliance issue, not a cosmetic
/// numbering bug. Forcing the country-local conversion at the generator
/// boundary eliminates the footgun. Zero callers exist at T-0068a (the
/// first caller lands in T-0068b's <c>IInvoiceService</c>), so the
/// signature change is risk-free.
/// </para>
/// </summary>
public interface IInvoiceNumberGenerator
{
    Task<string> NextAsync(string countryCode, CancellationToken cancellationToken);
}
