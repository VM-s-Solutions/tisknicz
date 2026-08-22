using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Makables.Core.AppServices.Features.Admin;
using static Makables.Core.AppServices.Features.Admin.GetPlatformRevenue;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Orders;
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
/// T-0186 admin earnings panel (<c>GET /api/v1/platform-revenue?window=…</c>)
/// on the Admin host, against real Postgres. This suite is where the
/// conditional-aggregate SQL is actually proven: the handler's arithmetic is
/// unit-tested, but the recognition rule (which orders count) lives entirely
/// in the EF projection and only a real database can confirm it translates
/// and sums correctly.
///
/// <para>Pins:</para>
/// <list type="bullet">
///   <item><description>Revenue is recognised at <c>PaidAt</c>, not <c>CreatedAt</c> — an order created inside the window but paid before it does not count.</description></item>
///   <item><description><c>PendingPayment</c> (never paid), <c>Cancelled</c> and <c>Refunded</c> are out of the fee sums.</description></item>
///   <item><description>A partial refund on a still-live order shows on the refund line WITHOUT reducing the platform fee.</description></item>
///   <item><description>Soft-deleted orders are excluded by the global query filter.</description></item>
///   <item><description>The window is half-open — an order paid exactly at <c>from</c> counts, one paid at <c>to</c> does not.</description></item>
///   <item><description>A customer/maker JWT cannot replay the cross-tenant money aggregate (ADR 0013).</description></item>
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

    private async Task<GetPlatformRevenue.GetPlatformRevenueResponse> ReadAsync(RevenueWindow window)
    {
        var response = await ClientWith("admin", UserRole.Admin)
            .GetAsync($"/api/v1/platform-revenue?window={window}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content
            .ReadFromJsonAsync<GetPlatformRevenue.GetPlatformRevenueResponse>(WireJson);
        body.Should().NotBeNull();
        return body!;
    }

    [Fact]
    public async Task Sums_only_orders_whose_payment_cleared_and_stands()
    {
        var now = DateTimeOffset.UtcNow;
        await PersistAsync(
            // Earned — four of the six recognised states, all paid inside the day window.
            SeedOrder("ord-paid", "M-CZ-20260001", OrderState.Paid, now.AddHours(-1)),
            SeedOrder("ord-accepted", "M-CZ-20260002", OrderState.Accepted, now.AddHours(-2)),
            SeedOrder("ord-delivered", "M-CZ-20260003", OrderState.Delivered, now.AddHours(-3)),
            SeedOrder("ord-disputed", "M-CZ-20260004", OrderState.Disputed, now.AddHours(-4)),
            // Never paid — no PaidAt at all, so the window predicate drops it.
            SeedOrder("ord-pending", "M-CZ-20260005", OrderState.PendingPayment, paidAt: null),
            // Money given back — out of the fee sums.
            SeedOrder("ord-cancelled", "M-CZ-20260006", OrderState.Cancelled, now.AddHours(-5)),
            SeedOrder("ord-refunded", "M-CZ-20260007", OrderState.Refunded, now.AddHours(-6)),
            // Soft-deleted — hidden by the global Auditable query filter.
            SeedOrder("ord-deleted", "M-CZ-20260008", OrderState.Paid, now.AddHours(-7), active: false));

        var revenue = await ReadAsync(RevenueWindow.Day);

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
        var now = DateTimeOffset.UtcNow;
        await PersistAsync(
            SeedOrder("ord-partial", "M-CZ-20260101", OrderState.Delivered, now.AddHours(-1),
                partialRefundMinor: 10_000));

        var revenue = await ReadAsync(RevenueWindow.Day);

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
        var now = DateTimeOffset.UtcNow;
        await PersistAsync(
            // Paid 3 days ago: inside the week window, outside the day window.
            SeedOrder("ord-older", "M-CZ-20260201", OrderState.Paid, now.AddDays(-3)),
            SeedOrder("ord-today", "M-CZ-20260202", OrderState.Paid, now.AddHours(-2)));

        var day = await ReadAsync(RevenueWindow.Day);
        var week = await ReadAsync(RevenueWindow.Week);
        var month = await ReadAsync(RevenueWindow.Month);

        day.PaidOrderCount.Should().Be(1);
        day.PlatformFeeMinor.Should().Be(FeeMinor);
        week.PaidOrderCount.Should().Be(2);
        week.PlatformFeeMinor.Should().Be(2 * FeeMinor);
        month.PaidOrderCount.Should().Be(2);
    }

    [Fact]
    public async Task Window_is_half_open_so_adjacent_windows_never_double_count()
    {
        var now = DateTimeOffset.UtcNow;
        await PersistAsync(
            // A hair older than 24 h — must fall OUT of the day window even
            // though it is only seconds beyond the boundary.
            SeedOrder("ord-edge-out", "M-CZ-20260301", OrderState.Paid, now.AddDays(-1).AddSeconds(-30)),
            SeedOrder("ord-edge-in", "M-CZ-20260302", OrderState.Paid, now.AddDays(-1).AddMinutes(5)));

        var day = await ReadAsync(RevenueWindow.Day);

        day.PaidOrderCount.Should().Be(1);
        day.PlatformFeeMinor.Should().Be(FeeMinor);
    }

    [Fact]
    public async Task Window_with_no_sales_returns_zeros_not_404()
    {
        var revenue = await ReadAsync(RevenueWindow.Month);

        revenue.PaidOrderCount.Should().Be(0);
        revenue.GrossVolumeMinor.Should().Be(0);
        revenue.PlatformFeeMinor.Should().Be(0);
        revenue.MakerPayoutMinor.Should().Be(0);
        revenue.RefundedMinor.Should().Be(0);
        revenue.Currency.Should().Be(Currency);
    }

    [Fact]
    public async Task Reports_the_window_it_actually_summed()
    {
        var revenue = await ReadAsync(RevenueWindow.Week);

        revenue.Window.Should().Be(RevenueWindow.Week);
        (revenue.ToExclusive - revenue.FromInclusive).Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public async Task Rejects_a_window_outside_the_enum()
    {
        var response = await ClientWith("admin", UserRole.Admin)
            .GetAsync("/api/v1/platform-revenue?window=Decade");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("customer", UserRole.Customer)]
    [InlineData("maker", UserRole.Maker)]
    public async Task Cross_tenant_money_aggregate_rejects_a_non_admin_audience(string audience, UserRole role)
    {
        var response = await ClientWith(audience, role).GetAsync("/api/v1/platform-revenue?window=Day");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/platform-revenue?window=Day");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
