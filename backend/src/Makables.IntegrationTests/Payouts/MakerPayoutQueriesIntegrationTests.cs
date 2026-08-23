using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Payouts;
using Makables.Core.Domain.Products;
using Makables.Infra.Common.Auth;
using Makables.Infra.Database;
using Makables.IntegrationTests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using AddressEntity = Makables.Core.Domain.Addresses.Address;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.IntegrationTests.Payouts;

/// <summary>
/// T-0112 maker payout queries on the Maker host. Pins: the per-maker slice
/// (SUM of THIS maker's payout, NOT PayoutBatch.TotalAmountMinor); the
/// maker-scoped per-order breakdown + reconciliation invariant; cross-maker /
/// unknown 404 no-oracle; and the maker-scoped, payload-free order-events
/// drawer. Seeds ONE Completed batch claiming BOTH makers' orders — the
/// critical cross-maker IDOR case.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MakerPayoutQueriesIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";
    private const string BatchId = "pb-1";

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Maker.Program> _factory = default!;

    public MakerPayoutQueriesIntegrationTests(PostgresHarness harness) => _harness = harness;

    public async Task InitializeAsync()
    {
        await _harness.ResetMutableTablesAsync();
        _factory = new WebApplicationFactory<Makables.Web.Maker.Program>()
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

    private string IssueMakerToken(string userId, string email)
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer, SigningKeyBase64 = TestKeyBase64, AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(userId, email, UserRole.Maker, "Maker", CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        return issuer.Issue(user, MakablesAudiences.Maker, DateTimeOffset.UtcNow).Token;
    }

    private HttpClient MakerClient(string userId, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueMakerToken(userId, email));
        return client;
    }

    /// <summary>
    /// Seed a single COMPLETED PayoutBatch claiming orders of BOTH makers,
    /// per-maker Fee invoices, and outbox events on one of makerA's orders.
    /// </summary>
    private async Task SeedCompletedBatchAsync()
    {
        await using var db = _harness.CreateDbContext();
        const string actor = "test-seed";
        var at = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var completedClock = Substitute.For<IClock>();
        completedClock.UtcNow.Returns(at.AddDays(10));

        var customer = User.Create("user-customer-1", "anna@example.cz", UserRole.Customer, "Anna", CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        customer.ConfirmEmail(at); customer.MarkCreated(actor, at);
        db.Set<User>().Add(customer);

        var category = Category.Create("cat-1", "3D tisk", "3d-tisk", null, null, 10, CountryCode);
        category.MarkCreated(actor, at);
        db.Set<Category>().Add(category);

        foreach (var (makerId, userId, addrId, ico) in new[]
        {
            ("maker-a", "user-maker-a", "addr-a", "27074358"),
            ("maker-b", "user-maker-b", "addr-b", "45317054"),
        })
        {
            var mu = User.Create(userId, $"{makerId}@example.cz", UserRole.Maker, makerId, CountryCode,
                passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
            mu.ConfirmEmail(at); mu.MarkCreated(actor, at);
            db.Set<User>().Add(mu);

            var addr = AddressEntity.Create(addrId, "Pikrtova", "1737", "Praha", "14000", CountryCode, CountryCode);
            addr.MarkCreated(actor, at);
            db.Set<AddressEntity>().Add(addr);

            var maker = MakerEntity.Create(makerId, userId, ico, null, $"{makerId} s.r.o.", null,
                addrId, null, true, "ares", at, false, CountryCode, slug: makerId);
            maker.UpdateProfile(bio: null, bankAccount: "111/0100", personalPickupEnabled: null, pickupNote: null);
            maker.MarkCreated(actor, at);
            db.Set<MakerEntity>().Add(maker);

            var product = Product.Create($"prod-{makerId}", makerId, "cat-1", "Vase", null,
                new Money(50000, Currency), PriceType.Fixed, 300, CountryCode);
            product.MarkCreated(actor, at);
            db.Set<Product>().Add(product);
        }

        // A Completed batch (cross-maker total = sum of all claimed payouts).
        // makerA: o1 (payout 25500) + o2 (payout 12750); makerB: o3 (payout 17850).
        var batch = PayoutBatch.Create(BatchId, "VYP-CZ-2026-W23", CountryCode,
            totalAmountMinor: 25500 + 12750 + 17850, currency: Currency,
            orderCount: 3, makerCount: 2,
            excludedPartiallyRefundedOrderCount: 0, excludedNoBankAccountOrderCount: 0,
            excludedNoBankAccountMakerCount: 0);
        batch.Complete(completedClock, "WIRE-SEED", new DateOnly(2026, 6, 11), "user-admin-1");
        batch.MarkCreated(actor, at);
        db.Set<PayoutBatch>().Add(batch);

        foreach (var (orderId, makerId, product, fee) in new[]
        {
            ("o1", "maker-a", 30000L, 4500L),
            ("o2", "maker-a", 15000L, 2250L),
            ("o3", "maker-b", 21000L, 3150L),
        })
        {
            var payout = product - fee;
            var order = Order.Create(orderId, $"M-CZ-{orderId}", "user-customer-1", makerId, $"prod-{makerId}",
                "Anna", "anna@example.cz", "+420 723 456 789",
                product, 0, fee, payout, product, Currency, 2100,
                ShippingMethod.ZasilkovnaPickupPoint, "pp-1", CountryCode);
            order.MarkAsPaid(completedClock, $"tx-{orderId}");
            order.Accept(completedClock);
            order.Ship(completedClock, $"PKT-{orderId}", 7);
            order.MarkAsDelivered(completedClock, OrderDeliverySource.Auto);
            order.AssignToPayoutBatch(BatchId);
            order.Complete(completedClock);
            order.MarkCreated(actor, at);
            db.Set<Order>().Add(order);
        }

        // Per-maker Fee invoices (denormalised MakerId + PayoutBatchId).
        foreach (var (invId, makerId, feeTotal) in new[]
        {
            ("inv-fee-a", "maker-a", 4500L + 2250L),
            ("inv-fee-b", "maker-b", 3150L),
        })
        {
            var inv = Invoice.Issue(invId, $"FV-CZ-2026{invId}", InvoiceType.Fee,
                orderId: null, payoutBatchId: BatchId, makerId: makerId,
                orderNumber: "OBJ-20260819-0001",
                issuerName: "JVM YORE s.r.o.", issuerIco: "12345678", issuerDic: null,
                issuerBankAccount: null, issuerAddress: null, recipientName: $"{makerId} s.r.o.",
                recipientEmail: $"{makerId}@example.cz", recipientTaxId: "27074358", recipientVatId: null,
                issueDate: new DateOnly(2026, 6, 11), taxableSupplyDate: null,
                dueDate: new DateOnly(2026, 6, 25), invoicingMode: InvoicingMode.None,
                amountWithoutVatMinor: feeTotal, vatRateBp: 0, vatAmountMinor: 0,
                amountWithVatMinor: feeTotal, currency: Currency, countryCode: CountryCode);
            inv.AttachPdfBlobPath($"cz/payouts/{BatchId}/{invId}.pdf");
            inv.MarkCreated(actor, at);
            db.Set<Invoice>().Add(inv);
        }

        // Outbox events on makerA's order o1 (one processed, one stalled).
        var processed = OutboxEvent.Enqueue("evt-1", "o1", OutboxEventTypes.OrderPaidCustomerEmail, "{}", at);
        processed.MarkProcessed(at.AddMinutes(1));
        db.Set<OutboxEvent>().Add(processed);

        var stalled = OutboxEvent.Enqueue("evt-2", "o1", OutboxEventTypes.OrderShippedCustomerEmail, "{}", at.AddMinutes(2));
        stalled.RecordFailure(OutboxErrorKind.Permanent, "email.bounced", nextRetryAt: null);
        db.Set<OutboxEvent>().Add(stalled);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GET_payouts_list_returns_only_this_makers_slice()
    {
        await SeedCompletedBatchAsync();
        using var client = MakerClient("user-maker-a", "maker-a@example.cz");

        var response = await client.GetAsync("/api/v1/payout-batches?page=1&pageSize=20");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("payouts").GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        var row = items[0];
        // makerA's slice = (30000-4500) + (15000-2250) = 38250; NOT the
        // cross-maker batch total (56100).
        row.GetProperty("makerTotalPaidMinor").GetInt64().Should().Be(38250);
        row.GetProperty("orderCount").GetInt32().Should().Be(2);
        row.GetProperty("feeInvoiceId").GetString().Should().Be("inv-fee-a");
    }

    [Fact]
    public async Task GET_payout_detail_breakdown_reconciles_and_is_maker_scoped()
    {
        await SeedCompletedBatchAsync();
        using var client = MakerClient("user-maker-a", "maker-a@example.cz");

        var response = await client.GetAsync($"/api/v1/payout-batches/{BatchId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var detail = doc.RootElement.GetProperty("detail");
        var orders = detail.GetProperty("orders");
        orders.GetArrayLength().Should().Be(2, "only makerA's claimed orders");

        long sum = 0;
        foreach (var line in orders.EnumerateArray())
        {
            line.GetProperty("orderNumber").GetString().Should().NotBe("M-CZ-o3", "makerB's order must not appear");
            var product = line.GetProperty("productPriceMinor").GetInt64();
            var shipping = line.GetProperty("shippingPriceMinor").GetInt64();
            var fee = line.GetProperty("platformFeeAmountMinor").GetInt64();
            var payout = line.GetProperty("makerPayoutAmountMinor").GetInt64();
            (product - fee + shipping).Should().Be(payout, "the PricingBreakdown reconciliation invariant holds");
            sum += payout;
        }
        sum.Should().Be(detail.GetProperty("makerTotalPaidMinor").GetInt64());
    }

    [Fact]
    public async Task GET_payout_detail_cross_maker_and_unknown_both_404_no_oracle()
    {
        await SeedCompletedBatchAsync();

        // makerB requesting the batch they ARE in → 200 with only their lines.
        using var clientB = MakerClient("user-maker-b", "maker-b@example.cz");
        var ok = await clientB.GetAsync($"/api/v1/payout-batches/{BatchId}");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        using var okDoc = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        okDoc.RootElement.GetProperty("detail").GetProperty("orders").GetArrayLength().Should().Be(1);

        // A fabricated unknown batch id → 404 (same shape as cross-maker).
        var unknown = await clientB.GetAsync("/api/v1/payout-batches/does-not-exist");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_order_events_is_maker_scoped_and_payload_free()
    {
        await SeedCompletedBatchAsync();
        using var client = MakerClient("user-maker-a", "maker-a@example.cz");

        // makerA owns o1.
        var response = await client.GetAsync("/api/v1/orders/o1/events");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bodyText = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(bodyText);
        var events = doc.RootElement.GetProperty("events").GetProperty("items");
        events.GetArrayLength().Should().Be(2);
        // No payload / error / customer-email keys leak.
        bodyText.Should().NotContain("payloadJson");
        bodyText.Should().NotContain("lastErrorCode");
        bodyText.Should().NotContain("anna@example.cz");
        foreach (var e in events.EnumerateArray())
        {
            e.TryGetProperty("eventType", out _).Should().BeTrue();
            e.TryGetProperty("status", out _).Should().BeTrue();
            e.TryGetProperty("occurredAt", out _).Should().BeTrue();
        }

        // makerA requesting makerB's order o3 → empty page (cross-maker, no oracle).
        var cross = await client.GetAsync("/api/v1/orders/o3/events");
        cross.StatusCode.Should().Be(HttpStatusCode.OK);
        using var crossDoc = JsonDocument.Parse(await cross.Content.ReadAsStringAsync());
        crossDoc.RootElement.GetProperty("events").GetProperty("totalCount").GetInt32().Should().Be(0);
    }
}
