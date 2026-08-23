using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Orders;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Admin;

/// <summary>
/// Admin overview earnings panel: what the platform earned on sales in ONE
/// CALENDAR MONTH. Read-only, admin-host only — the aggregate spans every
/// maker and customer, and ADR 0013 puts that boundary on the host audience
/// (a customer/maker JWT 401s here).
///
/// <para>
/// T-0192 replaced T-0186's rolling day/week/month windows with a real
/// month. The rolling version was chosen to avoid needing a civil timezone;
/// what it actually produced was a number nobody could reconcile — "the last
/// 30 days" never matches an invoice run, a VAT period or the question the
/// operator was asked, and it silently changes every time the page is
/// refreshed. A month is the unit the business already accounts in, so the
/// panel now answers for a month and the caller navigates between them. The
/// timezone that a rolling window dodged is read from
/// <c>CountryConfiguration.TimeZoneId</c> (see
/// <see cref="RevenueReportingTimeZone"/>); "August" means August where the
/// operator lives, which in Prague starts at 22:00 UTC on 31 July.
/// </para>
///
/// <para>
/// The handler decides nothing about money: it turns a year/month pair into
/// a half-open <c>[from, to)</c> instant pair and hands it to the read side,
/// which owns the recognition rule (see
/// <see cref="IOrderQueries.GetPlatformRevenueAsync"/>). No audit row (reads
/// are not audited, ADR 0014); no failure mode — a month with no sales
/// returns zeros, never 404. The month in progress reports what has cleared
/// so far, because <c>ToExclusive</c> is the month's end and no order can be
/// paid in the future.
/// </para>
/// </summary>
public static class GetPlatformRevenue
{
    /// <summary>
    /// Accepted year bounds. Not a business rule — a sanity clamp so a
    /// hand-typed <c>?year=999999999</c> is a 400 rather than a
    /// <see cref="DateTime"/> overflow inside the calendar helper. The
    /// platform has no orders before 2020 and this code will not outlive 2100.
    /// </summary>
    public const int MinYear = 2020;

    /// <inheritdoc cref="MinYear"/>
    public const int MaxYear = 2100;

    /// <param name="Year">Calendar year. Omit (with <paramref name="Month"/>) for the month in progress.</param>
    /// <param name="Month">Calendar month, 1–12. Omit (with <paramref name="Year"/>) for the month in progress.</param>
    public sealed record Query(int? Year, int? Month) : IQuery<GetPlatformRevenueResponse>;

    /// <summary>Globally-unique name (NSwag PR #38 convention).</summary>
    /// <param name="Year">The month actually reported — echoed so the caller can label the number it got.</param>
    /// <param name="Month">The month actually reported, 1–12.</param>
    /// <param name="FromInclusive">Start of the month (inclusive), as an instant.</param>
    /// <param name="ToExclusive">Start of the following month (exclusive), as an instant.</param>
    /// <param name="PaidOrderCount">Orders whose payment cleared inside the month and was not reversed.</param>
    /// <param name="GrossVolumeMinor">What customers were charged, minor units.</param>
    /// <param name="PlatformFeeMinor">What the platform earned, minor units — the headline number.</param>
    /// <param name="MakerPayoutMinor">What the makers are owed, minor units.</param>
    /// <param name="RefundedMinor">Gross refunded on orders paid in the month, minor units.</param>
    /// <param name="Currency">ISO 4217 code. CZK at launch.</param>
    /// <param name="IsCurrentMonth">
    /// True when this is the month in progress in the country's timezone.
    /// The caller uses it to stop the operator paging into the future — the
    /// alternative is the frontend deciding what "this month" means, which
    /// needs the civil timezone it deliberately does not carry.
    /// </param>
    public sealed record GetPlatformRevenueResponse(
        int Year,
        int Month,
        DateTimeOffset FromInclusive,
        DateTimeOffset ToExclusive,
        int PaidOrderCount,
        long GrossVolumeMinor,
        long PlatformFeeMinor,
        long MakerPayoutMinor,
        long RefundedMinor,
        string Currency,
        bool IsCurrentMonth);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            // Nullable on purpose: absent means "the month in progress", which
            // is a legitimate request, not a missing field. Only a value that
            // IS supplied has to be in range.
            RuleFor(q => q.Year)
                .InclusiveBetween(MinYear, MaxYear)
                .WithErrorCode(BusinessErrorMessage.MinValue)
                .When(q => q.Year.HasValue);

            RuleFor(q => q.Month)
                .InclusiveBetween(1, 12)
                .WithErrorCode(BusinessErrorMessage.MinValue)
                .When(q => q.Month.HasValue);
        }
    }

    public sealed class Handler(
        IOrderQueries orders,
        IClock clock,
        ICountryConfigurationRepository countries,
        IOptions<AuthDefaultCountryOptions> defaultCountry,
        ILogger<Handler> logger)
        : IRequestHandler<Query, BusinessResult<GetPlatformRevenueResponse>>
    {
        public async Task<BusinessResult<GetPlatformRevenueResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);

            var countryCode = defaultCountry.Value.CountryCodePrimary;
            var config = await countries.GetByCodeAsync(countryCode, cancellationToken);
            var (_, zone) = RevenueReportingTimeZone.Resolve(config, countryCode, logger);

            // Both or neither. A half-supplied pair ("?month=3" with no year)
            // is treated as "no month chosen" rather than guessed against the
            // current year — the response echoes what was actually reported,
            // so the caller can never mislabel the number.
            var current = RevenueReportingCalendar.CurrentMonth(clock.UtcNow, zone);
            var (year, month) = query is { Year: { } y, Month: { } m } ? (y, m) : current;

            var (from, to) = RevenueReportingCalendar.MonthWindow(year, month, zone);

            var revenue = await orders.GetPlatformRevenueAsync(from, to, cancellationToken);

            return BusinessResult.Success(new GetPlatformRevenueResponse(
                year,
                month,
                from,
                to,
                revenue.PaidOrderCount,
                revenue.GrossVolumeMinor,
                revenue.PlatformFeeMinor,
                revenue.MakerPayoutMinor,
                revenue.RefundedMinor,
                revenue.Currency,
                (year, month) == current));
        }
    }
}
