using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Makables.Core.AppServices.Features.Admin;
using static Makables.Core.AppServices.Features.Admin.GetPlatformRevenueSeries;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Orders.Queries;
using Makables.Infra.Common.Auth;
using Makables.Infra.Database;
using Makables.IntegrationTests.Common;
using Makables.TestUtilities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Makables.IntegrationTests.Admin;

/// <summary>
/// T-0192 admin earnings surface on the Admin host, against real Postgres:
/// the calendar-month aggregate (<c>GET /api/v1/platform-revenue</c>) and the
/// time series behind the chart
/// (<c>GET /api/v1/platform-revenue/series?range=…</c>). This suite is where
/// the SQL is actually proven — the handlers' arithmetic is unit-tested, but
/// the recognition rule (which orders count) and the bucketing live entirely
/// in the database, and only a real one can confirm they translate and sum
/// correctly. The series query is hand-written SQL, so this is its ONLY
/// compile-time-free safety net.
///
/// <para>Pins:</para>
/// <list type="bullet">
///   <item><description>Revenue is recognised at <c>PaidAt</c>, not <c>CreatedAt</c> — an order created in one month but paid in another counts where the money cleared.</description></item>
///   <item><description>A month is the OPERATOR'S month: 22:30 UTC on 30 April is already May in Prague, and is reported as May.</description></item>
///   <item><description><c>PendingPayment</c> (never paid), <c>Cancelled</c> and <c>Refunded</c> are out of the fee sums — in the aggregate AND in the series.</description></item>
///   <item><description>A partial refund on a still-live order shows on the refund line WITHOUT reducing the platform fee.</description></item>
///   <item><description>Soft-deleted orders are excluded — by the global query filter in the aggregate, and by the raw query's own <c>is_active</c> clause in the series (the keyless projection type is outside the filter).</description></item>
///   <item><description>Windows and buckets are half-open, so adjacent periods never double-count.</description></item>
///   <item><description>Series buckets are LOCAL days, and they sum to the same totals the single-number read gives for the same orders.</description></item>
///   <item><description>A customer/maker JWT cannot replay either cross-tenant money read (ADR 0013).</description></item>
/// </list>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminPlatformRevenueIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";
    private const string Actor = "seed";

    // Per-order snapshot: product 500 Kč + shipping 79 Kč = 579 Kč charged;
    // platform keeps 75 Kč, maker is owed 504 Kč (75 + 504 == 500 + 79, the
    // Order.Create invariant).
    private const long ProductMinor = 50_000;
    private const long ShippingMinor = 7_900;
    private const long FeeMinor = 7_500;
    private const long PayoutMinor = 50_400;
    private const long TotalMinor = 57_900;

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Admin.Program> _factory = default!;

    public AdminPlatformRevenueIntegrationTests(PostgresHarness harness) => _harness = harness;

    public async Task InitializeAsync()
    {
        await _harness.ResetMutableTablesAsync();
        _factory = new WebApplicationFactory<Makables.Web.Admin.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("IntegrationTest");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Postgres"] = _harness.ConnectionString,
                        ["Jwt:Issuer"] = TestIssuer,
                        ["Jwt:SigningKeyBase64"] = TestKeyBase64,
                        ["SendGrid:ApiKey"] = "SG.integration-test-stub",
                        ["Resend:ApiKey"] = "re_integration_test_stub",
                        ["Resend:DefaultFromAddress"] = "no-reply@makables.test",
                        ["SendGrid:DefaultFromAddress"] = "no-reply@makables.test",
                        ["PublicAppUrls:WebBaseUrl"] = "https://makables.test",
                        ["Mapbox:AccessToken"] = "pk.integration-test-stub",
                        ["Ares:BaseUrl"] = "https://ares.integration-test.local",
                        ["Comgate:MerchantId"] = "12345",
                        ["Comgate:Secret"] = "integration-test-secret",
                        ["Comgate:BaseUrl"] = "https://payments.comgate.test",
                        ["Packeta:ApiKey"] = "integration-test-packeta-key",
                        ["Packeta:PublicWidgetKey"] = "integration-test-packeta-public-key",
                        ["Packeta:BaseUrl"] = "https://api.packeta.test",
                        ["Packeta:WidgetScriptUrl"] = "https://widget.packeta.test/v6/library.js",
                        ["Packeta:SenderLabel"] = "makables-test",
                        ["BlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                        ["Cors:AllowedOrigins:customer:0"] = "https://customer.makables.test",
                        ["Cors:AllowedOrigins:maker:0"] = "https://maker.makables.test",
                        ["Cors:AllowedOrigins:admin:0"] = "https://admin.makables.test",
                        ["Cors:AllowedOrigins:public:0"] = "https://makables.test",
                    });
                });
                builder.ConfigureServices(services =>
                {
                    var dbContextDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<MakablesDbContext>));
                    if (dbContextDescriptor is not null) services.Remove(dbContextDescriptor);
                    services.AddDbContext<MakablesDbContext>(o => o.UseNpgsql(_harness.ConnectionString));
                });
            });
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private string IssueToken(string audience, UserRole role)
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer, SigningKeyBase64 = TestKeyBase64, AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create($"user-{role}", $"{role}@makables.cz", role, role.ToString(), CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(DateTimeOffset.UtcNow);
        return issuer.Issue(user, audience, DateTimeOffset.UtcNow).Token;
    }

    private HttpClient ClientWith(string audience, UserRole role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueToken(audience, role));
        return client;
    }

    /// <summary>
    /// Seeds one order and drives it to <paramref name="state"/> with its
    /// payment stamped at <paramref name="paidAt"/>. Seeding through the real
    /// aggregate methods (rather than writing columns) keeps the fixture
    /// honest: the state/timestamp pairs the query reads are exactly the ones
    /// the domain produces in production.
    /// </summary>
    private static Order SeedOrder(
        string id,
        string number,
        OrderState state,
        DateTimeOffset? paidAt,
        long partialRefundMinor = 0,
        bool active = true)
    {
        var createdAt = paidAt ?? DateTimeOffset.UtcNow.AddDays(-60);
        var order = Order.Create(id, number, "user-cust", "maker-x", "prod-1",
            "Anna", "anna@example.cz", "+420 723 456 789",
            ProductMinor, ShippingMinor, FeeMinor, PayoutMinor, TotalMinor, Currency, 2100,
            ShippingMethod.ZasilkovnaPickupPoint, "pp-42", CountryCode);
        order.MarkCreated(Actor, createdAt);

        if (paidAt is not null)
        {
            var clock = new FakeClock(paidAt.Value);
            order.MarkAsPaid(clock, $"comgate-{id}").IsSuccess.Should().BeTrue();

            switch (state)
            {
                case OrderState.Paid:
                    break;
                case OrderState.Accepted:
                    order.Accept(clock).IsSuccess.Should().BeTrue();
                    break;
                case OrderState.Shipped:
                    order.Accept(clock).IsSuccess.Should().BeTrue();
                    order.Ship(clock, "packeta-1", autoDeliverWindowDays: 14).IsSuccess.Should().BeTrue();
                    break;
                case OrderState.Delivered:
                case OrderState.Completed:
                    order.Accept(clock).IsSuccess.Should().BeTrue();
                    order.Ship(clock, "packeta-1", autoDeliverWindowDays: 14).IsSuccess.Should().BeTrue();
                    order.MarkAsDelivered(clock, OrderDeliverySource.Customer).IsSuccess.Should().BeTrue();
                    if (state == OrderState.Completed)
                        order.Complete(clock).IsSuccess.Should().BeTrue();
                    break;
                case OrderState.Disputed:
                    order.OpenDispute(clock).IsSuccess.Should().BeTrue();
                    break;
                case OrderState.Cancelled:
                    order.Cancel(clock).IsSuccess.Should().BeTrue();
                    break;
                case OrderState.Refunded:
                    // A full refund is the ONLY sanctioned path into Refunded.
                    order.Refund(clock, TotalMinor, acknowledgePostPayout: false).IsSuccess.Should().BeTrue();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, "Unsupported seed state.");
            }

            if (partialRefundMinor > 0)
                order.Refund(clock, partialRefundMinor, acknowledgePostPayout: false).IsSuccess.Should().BeTrue();
        }

        order.State.Should().Be(state, "the fixture must seed the state it claims to seed");
        if (!active) order.MarkDeactivated(Actor, createdAt.AddDays(1));
        return order;
    }

    private async Task PersistAsync(params Order[] orders)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakablesDbContext>();
        db.AddRange(orders.Cast<object>());
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The hosts serialize enums as their string name (JsonStringEnumConverter,
    /// T-0049b), so the assertion side has to read them the same way — a
    /// default reader would only ever see the numeric form the wire never uses.
    /// </summary>
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };


    /// <summary>
    /// Europe/Prague is the launch country's seeded zone. The tests below
    /// assert against it directly rather than through the production helper,
    /// so a bug in that helper cannot make its own test pass.
    /// </summary>
    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    /// <summary>The instant a Prague-local midnight names.</summary>
    private static DateTimeOffset LocalMidnight(DateTime localDate) =>
        new DateTimeOffset(localDate.Date, Prague.GetUtcOffset(localDate.Date)).ToUniversalTime();

    private static DateTime LocalToday() => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Prague).Date;

    private async Task<GetPlatformRevenue.GetPlatformRevenueResponse> ReadMonthAsync(int? year, int? month)
    {
        var query = year.HasValue && month.HasValue ? $"?year={year}&month={month}" : string.Empty;
        var response = await ClientWith("admin", UserRole.Admin)
            .GetAsync($"/api/v1/platform-revenue{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content
            .ReadFromJsonAsync<GetPlatformRevenue.GetPlatformRevenueResponse>(WireJson);
        body.Should().NotBeNull();
        return body!;
    }

    private async Task<GetPlatformRevenueSeries.GetPlatformRevenueSeriesResponse> ReadSeriesAsync(
        RevenueRange range)
    {
        var response = await ClientWith("admin", UserRole.Admin)
            .GetAsync($"/api/v1/platform-revenue/series?range={range}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content
            .ReadFromJsonAsync<GetPlatformRevenueSeries.GetPlatformRevenueSeriesResponse>(WireJson);
        body.Should().NotBeNull();
        return body!;
    }

    // ======================================================================
    // Month aggregate
    // ======================================================================

    [Fact]
    public async Task Sums_only_orders_whose_payment_cleared_and_stands()
    {
        // May 2026 — a settled month in the past, so the fixture is not racing
        // the wall clock at a month boundary.
        var may = new DateTimeOffset(2026, 5, 12, 9, 0, 0, TimeSpan.Zero);
        await PersistAsync(
            // Earned — four of the six recognised states, all paid inside the month.
            SeedOrder("ord-paid", "M-CZ-20260001", OrderState.Paid, may.AddHours(-1)),
            SeedOrder("ord-accepted", "M-CZ-20260002", OrderState.Accepted, may.AddHours(-2)),
            SeedOrder("ord-delivered", "M-CZ-20260003", OrderState.Delivered, may.AddHours(-3)),
            SeedOrder("ord-disputed", "M-CZ-20260004", OrderState.Disputed, may.AddHours(-4)),
            // Never paid — no PaidAt at all, so the window predicate drops it.
            SeedOrder("ord-pending", "M-CZ-20260005", OrderState.PendingPayment, paidAt: null),
            // Money given back — out of the fee sums.
            SeedOrder("ord-cancelled", "M-CZ-20260006", OrderState.Cancelled, may.AddHours(-5)),
            SeedOrder("ord-refunded", "M-CZ-20260007", OrderState.Refunded, may.AddHours(-6)),
            // Soft-deleted — hidden by the global Auditable query filter.
            SeedOrder("ord-deleted", "M-CZ-20260008", OrderState.Paid, may.AddHours(-7), active: false));

        var revenue = await ReadMonthAsync(2026, 5);

        revenue.PaidOrderCount.Should().Be(4);
        revenue.PlatformFeeMinor.Should().Be(4 * FeeMinor);
        revenue.GrossVolumeMinor.Should().Be(4 * TotalMinor);
        revenue.MakerPayoutMinor.Should().Be(4 * PayoutMinor);
        // The fully-refunded order contributes nothing to the fee but its
        // returned money is still visible on the refund line.
        revenue.RefundedMinor.Should().Be(TotalMinor);
        revenue.Currency.Should().Be(Currency);
    }

    [Fact]
    public async Task Partial_refund_shows_on_the_refund_line_without_reducing_the_fee()
    {
        await PersistAsync(
            SeedOrder("ord-partial", "M-CZ-20260101", OrderState.Delivered,
                new DateTimeOffset(2026, 5, 12, 9, 0, 0, TimeSpan.Zero),
                partialRefundMinor: 10_000));

        var revenue = await ReadMonthAsync(2026, 5);

        revenue.PaidOrderCount.Should().Be(1);
        // The refund column records the GROSS amount returned; it does not
        // decompose into a fee share and a payout share, so netting it into
        // the commission would understate it by the maker's portion.
        revenue.PlatformFeeMinor.Should().Be(FeeMinor);
        revenue.RefundedMinor.Should().Be(10_000);
    }

    [Fact]
    public async Task Recognises_revenue_at_PaidAt_not_at_CreatedAt()
    {
        // Both orders exist across May and June; only PaidAt decides which
        // month earned the money.
        await PersistAsync(
            SeedOrder("ord-may", "M-CZ-20260201", OrderState.Paid,
                new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero)),
            SeedOrder("ord-june", "M-CZ-20260202", OrderState.Paid,
                new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero)));

        var may = await ReadMonthAsync(2026, 5);
        var june = await ReadMonthAsync(2026, 6);

        may.PaidOrderCount.Should().Be(1);
        may.PlatformFeeMinor.Should().Be(FeeMinor);
        june.PaidOrderCount.Should().Be(1);
        june.PlatformFeeMinor.Should().Be(FeeMinor);
    }

    [Fact]
    public async Task A_month_is_the_operators_month_not_UTCs()
    {
        // 22:30 UTC on 30 April is 00:30 on 1 May in Prague (CEST, +02:00).
        // The sale belongs to MAY. Reading UTC months would file it under
        // April and understate the month the operator is actually asked about
        // — every month, for its first two hours.
        await PersistAsync(
            SeedOrder("ord-cusp", "M-CZ-20260301", OrderState.Paid,
                new DateTimeOffset(2026, 4, 30, 22, 30, 0, TimeSpan.Zero)));

        var april = await ReadMonthAsync(2026, 4);
        var may = await ReadMonthAsync(2026, 5);

        april.PaidOrderCount.Should().Be(0, "22:30 UTC on 30 April is already May in Prague");
        may.PaidOrderCount.Should().Be(1);
        may.FromInclusive.Should().Be(new DateTimeOffset(2026, 4, 30, 22, 0, 0, TimeSpan.Zero));
        may.ToExclusive.Should().Be(new DateTimeOffset(2026, 5, 31, 22, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Month_is_half_open_so_adjacent_months_never_double_count()
    {
        // One second either side of the May/June boundary (2026-05-31T22:00Z).
        await PersistAsync(
            SeedOrder("ord-last-may", "M-CZ-20260401", OrderState.Paid,
                new DateTimeOffset(2026, 5, 31, 21, 59, 59, TimeSpan.Zero)),
            SeedOrder("ord-first-june", "M-CZ-20260402", OrderState.Paid,
                new DateTimeOffset(2026, 5, 31, 22, 0, 0, TimeSpan.Zero)));

        var may = await ReadMonthAsync(2026, 5);
        var june = await ReadMonthAsync(2026, 6);

        may.PaidOrderCount.Should().Be(1);
        june.PaidOrderCount.Should().Be(1);
    }

    [Fact]
    public async Task Month_with_no_sales_returns_zeros_not_404()
    {
        var revenue = await ReadMonthAsync(2026, 2);

        revenue.PaidOrderCount.Should().Be(0);
        revenue.GrossVolumeMinor.Should().Be(0);
        revenue.PlatformFeeMinor.Should().Be(0);
        revenue.MakerPayoutMinor.Should().Be(0);
        revenue.RefundedMinor.Should().Be(0);
        revenue.Currency.Should().Be(Currency);
    }

    [Fact]
    public async Task Reports_the_month_it_actually_summed()
    {
        var revenue = await ReadMonthAsync(2026, 5);

        revenue.Year.Should().Be(2026);
        revenue.Month.Should().Be(5);
        (revenue.ToExclusive - revenue.FromInclusive).Should().Be(TimeSpan.FromDays(31));
    }

    [Fact]
    public async Task Defaults_to_the_month_in_progress_in_the_countrys_timezone()
    {
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Prague);

        var revenue = await ReadMonthAsync(null, null);

        revenue.Year.Should().Be(localNow.Year);
        revenue.Month.Should().Be(localNow.Month);
        revenue.IsCurrentMonth.Should().BeTrue();
    }

    [Fact]
    public async Task A_settled_month_is_not_flagged_as_the_month_in_progress()
    {
        // The panel uses this to stop the operator paging into the future.
        var revenue = await ReadMonthAsync(2026, 1);

        revenue.IsCurrentMonth.Should().BeFalse();
    }

    [Theory]
    [InlineData("?year=2026&month=0")]
    [InlineData("?year=2026&month=13")]
    [InlineData("?year=1999&month=5")]
    public async Task Rejects_a_month_outside_the_calendar(string query)
    {
        var response = await ClientWith("admin", UserRole.Admin)
            .GetAsync($"/api/v1/platform-revenue{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("customer", UserRole.Customer)]
    [InlineData("maker", UserRole.Maker)]
    public async Task Cross_tenant_money_aggregate_rejects_a_non_admin_audience(string audience, UserRole role)
    {
        var response = await ClientWith(audience, role).GetAsync("/api/v1/platform-revenue");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/platform-revenue");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ======================================================================
    // Time series (T-0192)
    // ======================================================================

    [Fact]
    public async Task Series_buckets_a_local_day_not_a_UTC_day()
    {
        // THE test for the raw date_trunc(field, timestamptz, zone) call.
        // An order paid 30 minutes after Prague midnight sits on the PREVIOUS
        // calendar day in UTC. It must still land in the local day's bucket —
        // otherwise every sale in the first hour or two of every day is
        // charted against yesterday.
        var localDay = LocalToday().AddDays(-2);
        var bucketStart = LocalMidnight(localDay);
        var paidAt = bucketStart.AddMinutes(30);
        paidAt.UtcDateTime.Date.Should().NotBe(localDay,
            "the fixture is only meaningful while the local and UTC dates diverge");

        await PersistAsync(SeedOrder("ord-local-day", "M-CZ-20260501", OrderState.Paid, paidAt));

        var series = await ReadSeriesAsync(RevenueRange.Week);

        var bucket = series.Points.Should().ContainSingle(p => p.PaidOrderCount == 1).Subject;
        bucket.BucketStart.Should().Be(bucketStart);
        bucket.PlatformFeeMinor.Should().Be(FeeMinor);
    }

    [Fact]
    public async Task Series_totals_match_the_single_number_read_over_the_same_orders()
    {
        // The chart and the tiles must never disagree. Same recognition rule,
        // same orders, so the buckets have to sum to the total.
        var localToday = LocalToday();
        await PersistAsync(
            SeedOrder("ord-s1", "M-CZ-20260601", OrderState.Paid, LocalMidnight(localToday).AddHours(3)),
            SeedOrder("ord-s2", "M-CZ-20260602", OrderState.Delivered, LocalMidnight(localToday.AddDays(-1)).AddHours(9)),
            SeedOrder("ord-s3", "M-CZ-20260603", OrderState.Accepted, LocalMidnight(localToday.AddDays(-3)).AddHours(14)));

        var series = await ReadSeriesAsync(RevenueRange.Week);

        series.Points.Sum(p => p.PaidOrderCount).Should().Be(3);
        series.Points.Sum(p => p.PlatformFeeMinor).Should().Be(3 * FeeMinor);
        series.Points.Sum(p => p.GrossVolumeMinor).Should().Be(3 * TotalMinor);
        series.Points.Sum(p => p.MakerPayoutMinor).Should().Be(3 * PayoutMinor);
        // Three distinct local days → three distinct buckets.
        series.Points.Count(p => p.PaidOrderCount > 0).Should().Be(3);
    }

    [Fact]
    public async Task Series_recognition_matches_the_aggregate_state_for_state()
    {
        var paidAt = LocalMidnight(LocalToday()).AddHours(2);
        await PersistAsync(
            SeedOrder("ord-earned", "M-CZ-20260701", OrderState.Paid, paidAt),
            SeedOrder("ord-never-paid", "M-CZ-20260702", OrderState.PendingPayment, paidAt: null),
            SeedOrder("ord-cancelled", "M-CZ-20260703", OrderState.Cancelled, paidAt),
            SeedOrder("ord-refunded", "M-CZ-20260704", OrderState.Refunded, paidAt),
            // The keyless projection type sits OUTSIDE the global soft-delete
            // query filter, so the raw SQL spells out `is_active` itself. This
            // is what pins that clause.
            SeedOrder("ord-deleted", "M-CZ-20260705", OrderState.Paid, paidAt, active: false));

        var series = await ReadSeriesAsync(RevenueRange.Week);

        series.Points.Sum(p => p.PaidOrderCount).Should().Be(1);
        series.Points.Sum(p => p.PlatformFeeMinor).Should().Be(FeeMinor);
        // The fully-refunded order gives its money back on the refund line
        // without ever touching the fee — same asymmetry as the aggregate.
        series.Points.Sum(p => p.RefundedMinor).Should().Be(TotalMinor);
    }

    [Fact]
    public async Task Series_partial_refund_rides_the_refund_line_only()
    {
        await PersistAsync(
            SeedOrder("ord-s-partial", "M-CZ-20260801", OrderState.Delivered,
                LocalMidnight(LocalToday()).AddHours(4), partialRefundMinor: 10_000));

        var series = await ReadSeriesAsync(RevenueRange.Week);

        series.Points.Sum(p => p.PlatformFeeMinor).Should().Be(FeeMinor);
        series.Points.Sum(p => p.RefundedMinor).Should().Be(10_000);
    }

    [Fact]
    public async Task Empty_days_are_plotted_as_zero_not_skipped()
    {
        // A line chart that drops its empty buckets draws a straight run
        // between two distant points, which reads as steady trade during a
        // week with no sales at all.
        await PersistAsync(
            SeedOrder("ord-gap", "M-CZ-20260901", OrderState.Paid,
                LocalMidnight(LocalToday().AddDays(-4)).AddHours(11)));

        var series = await ReadSeriesAsync(RevenueRange.Week);

        series.Points.Should().HaveCount(7);
        series.Points.Count(p => p.PaidOrderCount == 0).Should().Be(6);
        series.Points.Select(p => p.BucketStart).Should().BeInAscendingOrder();
        series.Points.Select(p => p.BucketStart).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(RevenueRange.Day, RevenueBucketGranularity.Hour, 24)]
    [InlineData(RevenueRange.Week, RevenueBucketGranularity.Day, 7)]
    [InlineData(RevenueRange.Month, RevenueBucketGranularity.Day, 30)]
    [InlineData(RevenueRange.Quarter, RevenueBucketGranularity.Day, 90)]
    [InlineData(RevenueRange.HalfYear, RevenueBucketGranularity.Week, 26)]
    [InlineData(RevenueRange.Year, RevenueBucketGranularity.Month, 12)]
    public async Task Every_range_returns_its_documented_shape_over_a_live_database(
        RevenueRange range, RevenueBucketGranularity granularity, int expectedPoints)
    {
        var series = await ReadSeriesAsync(range);

        series.Range.Should().Be(range);
        series.Granularity.Should().Be(granularity);
        series.Points.Should().HaveCount(expectedPoints);
        series.Currency.Should().Be(Currency);
        series.TimeZoneId.Should().Be("Europe/Prague");
        series.Points.Select(p => p.BucketStart).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task An_hourly_series_buckets_by_the_hour()
    {
        var paidAt = DateTimeOffset.UtcNow.AddMinutes(-90);
        await PersistAsync(SeedOrder("ord-hourly", "M-CZ-20261001", OrderState.Paid, paidAt));

        var series = await ReadSeriesAsync(RevenueRange.Day);

        var bucket = series.Points.Should().ContainSingle(p => p.PaidOrderCount == 1).Subject;
        bucket.BucketStart.Should().BeOnOrBefore(paidAt);
        (paidAt - bucket.BucketStart).Should().BeLessThan(TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task Series_rejects_a_range_outside_the_enum()
    {
        var response = await ClientWith("admin", UserRole.Admin)
            .GetAsync("/api/v1/platform-revenue/series?range=Decade");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("customer", UserRole.Customer)]
    [InlineData("maker", UserRole.Maker)]
    public async Task Series_rejects_a_non_admin_audience(string audience, UserRole role)
    {
        var response = await ClientWith(audience, role)
            .GetAsync("/api/v1/platform-revenue/series?range=Week");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Series_anonymous_request_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/platform-revenue/series?range=Week");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
