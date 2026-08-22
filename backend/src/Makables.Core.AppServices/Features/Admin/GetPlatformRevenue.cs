using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using MediatR;

namespace Makables.Core.AppServices.Features.Admin;

/// <summary>
/// Rolling reporting window for <see cref="GetPlatformRevenue"/>. Rolling
/// (last N × 24 h back from "now") rather than calendar-aligned: a calendar
/// month would need a per-country civil timezone to know where the day
/// starts, and the admin surface is a live operational readout, not a
/// bookkeeping period. The invoice + payout surfaces remain the record for
/// accounting periods.
/// </summary>
public enum RevenueWindow
{
    /// <summary>Last 24 hours.</summary>
    Day = 0,

    /// <summary>Last 7 days.</summary>
    Week = 1,

    /// <summary>Last 30 days.</summary>
    Month = 2,
}

/// <summary>
/// Admin overview earnings panel (T-0182): what the platform earned on
/// sales over a rolling <see cref="RevenueWindow"/>. Read-only, admin-host
/// only — the aggregate spans every maker and customer, and ADR 0013 puts
/// that boundary on the host audience (a customer/maker JWT 401s here).
///
/// <para>
/// The handler decides nothing about money: it converts the window enum
/// into a half-open <c>[from, to)</c> instant pair off <see cref="IClock"/>
/// and hands it to the read side, which owns the recognition rule (see
/// <see cref="IOrderQueries.GetPlatformRevenueAsync"/>). No audit row
/// (reads are not audited, ADR 0014); no failure mode — a window with no
/// sales returns zeros, never 404.
/// </para>
/// </summary>
public static class GetPlatformRevenue
{
    public sealed record Query(RevenueWindow Window) : IQuery<GetPlatformRevenueResponse>;

    /// <summary>Globally-unique name (NSwag PR #38 convention).</summary>
    /// <param name="Window">Echoed back so the caller can confirm which window it is reading.</param>
    /// <param name="FromInclusive">Start of the window (inclusive).</param>
    /// <param name="ToExclusive">End of the window (exclusive) — "now" at read time.</param>
    /// <param name="PaidOrderCount">Orders whose payment cleared inside the window and was not reversed.</param>
    /// <param name="GrossVolumeMinor">What customers were charged, minor units.</param>
    /// <param name="PlatformFeeMinor">What the platform earned, minor units — the headline number.</param>
    /// <param name="MakerPayoutMinor">What the makers are owed, minor units.</param>
    /// <param name="RefundedMinor">Gross refunded on orders paid in the window, minor units.</param>
    /// <param name="Currency">ISO 4217 code. CZK at launch.</param>
    public sealed record GetPlatformRevenueResponse(
        RevenueWindow Window,
        DateTimeOffset FromInclusive,
        DateTimeOffset ToExclusive,
        int PaidOrderCount,
        long GrossVolumeMinor,
        long PlatformFeeMinor,
        long MakerPayoutMinor,
        long RefundedMinor,
        string Currency);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.Window)
                .Cascade(CascadeMode.Stop)
                .IsInEnum()
                .WithErrorCode(BusinessErrorMessage.InvalidEnumValue);
        }
    }

    /// <summary>Window length in days. The single place the enum becomes a duration.</summary>
    private static int DaysFor(RevenueWindow window) => window switch
    {
        RevenueWindow.Day => 1,
        RevenueWindow.Week => 7,
        RevenueWindow.Month => 30,
        _ => 1,
    };

    public sealed class Handler(IOrderQueries orders, IClock clock)
        : IRequestHandler<Query, BusinessResult<GetPlatformRevenueResponse>>
    {
        public async Task<BusinessResult<GetPlatformRevenueResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var to = clock.UtcNow;
            var from = to.AddDays(-DaysFor(query.Window));

            var revenue = await orders.GetPlatformRevenueAsync(from, to, cancellationToken);

            return BusinessResult.Success(new GetPlatformRevenueResponse(
                query.Window,
                from,
                to,
                revenue.PaidOrderCount,
                revenue.GrossVolumeMinor,
                revenue.PlatformFeeMinor,
                revenue.MakerPayoutMinor,
                revenue.RefundedMinor,
                revenue.Currency));
        }
    }
}
