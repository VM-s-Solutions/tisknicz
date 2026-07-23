using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Payments;
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

namespace Makables.IntegrationTests.Orders;

/// <summary>
/// End-to-end coverage for the T-0106 admin dispute-resolve endpoint
/// (<c>POST /api/v1/orders/{orderId}/dispute/resolve</c> on the Admin
/// host). Pins the Resumed outcome: state restored, restore pointer
/// cleared, dispute row resolved, customer-email outbox row, and the
/// <c>admin_audit_log</c> entry from <c>AdminAuditPipelineBehavior</c>.
/// AC-9 Refunded leg (review B-1): the nested <c>RefundOrder</c>
/// pipeline runs in-scope against the shared DbContext — the happy leg
/// pins the dual commit (both audit rows, both outbox emails, single
/// provider call for the full remaining amount); the inner-failure leg
/// pins that NOTHING commits and the dispute stays open.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ResolveDisputeIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";

    private const string CustomerUserId = "user-customer-1";
    private const string MakerUserId = "user-maker-1";
    private const string AdminUserId = "user-admin-1";
    private const string MakerId = "maker-1";
    private const string AddressId = "addr-1";
    private const string CategoryId = "cat-1";
    private const string ProductId = "prod-1";
    private const string OrderId = "ord-1";
    private const string DisputeId = "dsp-1";
    private const string TransId = "tx-1";
    private const long Total = 57900;

    private readonly PostgresHarness _harness;
    private readonly FakeComgatePaymentProvider _provider = new();
    private WebApplicationFactory<Makables.Web.Admin.Program> _factory = default!;

    public ResolveDisputeIntegrationTests(PostgresHarness harness)
    {
        _harness = harness;
    }

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
                    if (dbContextDescriptor is not null)
                    {
                        services.Remove(dbContextDescriptor);
                    }
                    services.AddDbContext<MakablesDbContext>(o =>
                        o.UseNpgsql(_harness.ConnectionString));

                    // Replace the keyed "comgate" IPaymentProvider with the
                    // fake (RefundOrderIntegrationTests precedent) — the
                    // Refunded outcome dispatches the nested RefundOrder
                    // pipeline, which calls the provider.
                    var keyed = services.Where(d =>
                        d.ServiceType == typeof(IPaymentProvider) &&
                        d.IsKeyedService &&
                        (d.ServiceKey as string) == "comgate").ToList();
                    foreach (var d in keyed)
                    {
                        services.Remove(d);
                    }
                    services.AddKeyedSingleton<IPaymentProvider>("comgate", _provider);
                });
            });
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Seed a Disputed order with one open dispute row. When
    /// <paramref name="shipped"/> the parenthesis state is Shipped
    /// (Paid → Accepted → Shipped → Disputed); otherwise the order was
    /// Paid when the dispute opened — the restore target the Refunded
    /// outcome needs (review B-1).
    /// </summary>
    private async Task SeedDisputedOrderAsync(bool shipped)
    {
        await using var db = _harness.CreateDbContext();
        const string seedActor = "test-seed";
        var seedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        var customer = User.Create(
            id: CustomerUserId, email: "anna@example.cz", role: UserRole.Customer,
            fullName: "Anna", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        customer.ConfirmEmail(seedAt);
        customer.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(customer);

        var makerUser = User.Create(
            id: MakerUserId, email: "maker@example.cz", role: UserRole.Maker,
            fullName: "Maker", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        makerUser.ConfirmEmail(seedAt);
        makerUser.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(makerUser);

        var adminUser = User.Create(
            id: AdminUserId, email: "admin@makables.cz", role: UserRole.Admin,
            fullName: "Admin", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        adminUser.ConfirmEmail(seedAt);
        adminUser.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(adminUser);

        var address = AddressEntity.Create(
            id: AddressId, street: "Pikrtova", houseNumber: "1737",
            city: "Praha", zip: "14000", countryCodeIso: CountryCode,
            auditCountryCode: CountryCode);
        address.MarkCreated(seedActor, seedAt);
        db.Set<AddressEntity>().Add(address);

        var maker = MakerEntity.Create(
            id: MakerId, userId: MakerUserId,
            registrationNumber: "27074358", vatId: null,
            companyName: "Avast s.r.o.", legalForm: null,
            registeredAddressId: AddressId,
            incorporatedOn: null, isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: seedAt, snapshotIsStale: false,
            countryCode: CountryCode, slug: "avast");
        maker.MarkCreated(seedActor, seedAt);
        db.Set<MakerEntity>().Add(maker);

        var category = Category.Create(
            id: CategoryId, name: "3D tisk", slug: "3d-tisk",
            icon: null, description: null, sortOrder: 10, countryCode: CountryCode);
        category.MarkCreated(seedActor, seedAt);
        db.Set<Category>().Add(category);

        var product = Product.Create(
            id: ProductId, makerId: MakerId, categoryId: CategoryId,
            title: "Vase", description: null,
            price: new Money(50000, Currency),
            priceType: PriceType.Fixed, weightGrams: 300,
            countryCode: CountryCode);
        product.MarkCreated(seedActor, seedAt);
        db.Set<Product>().Add(product);

        var order = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId, makerId: MakerId, productId: ProductId,
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420 723 456 789",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: Total, currency: Currency, vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42",
            countryCode: CountryCode);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.Zero));
        order.MarkAsPaid(clock, TransId);
        if (shipped)
        {
            order.Accept(clock);
            order.Ship(clock, "PKT-1", 7);
        }
        order.OpenDispute(clock);
        order.MarkCreated(seedActor, seedAt);
        db.Set<Order>().Add(order);

        var dispute = Dispute.Open(
            id: DisputeId, orderId: OrderId,
            category: DisputeCategory.NotDelivered,
            description: "Balík se podle zákazníka ztratil.",
            source: DisputeSource.Customer,
            countryCode: CountryCode);
        dispute.MarkCreated(seedActor, seedAt);
        db.Set<Dispute>().Add(dispute);

        await db.SaveChangesAsync();
    }

    private HttpClient CreateAdminClient()
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(
            id: AdminUserId, email: "admin@makables.cz", role: UserRole.Admin,
            fullName: "Admin", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        var token = issuer.Issue(user, MakablesAudiences.Admin, DateTimeOffset.UtcNow).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Admin_resolve_resumed_e2e_restores_state_resolves_row_and_audits()
    {
        await SeedDisputedOrderAsync(shipped: true);
        using var client = CreateAdminClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/orders/{OrderId}/dispute/resolve",
            new { outcome = "Resumed", resolutionNotes = "Zásilka se našla, pokračujeme v doručení." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var order = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == OrderId);
        order.State.Should().Be(OrderState.Shipped, "Resumed restores the pre-dispute state");
        order.PreDisputeState.Should().BeNull("the restore pointer is cleared");
        order.DisputedAt.Should().NotBeNull("DisputedAt is a kept historical marker");

        var dispute = await db.Set<Dispute>().AsNoTracking().SingleAsync(d => d.Id == DisputeId);
        dispute.ResolutionOutcome.Should().Be(DisputeResolutionOutcome.Resumed);
        dispute.ResolutionNotes.Should().Be("Zásilka se našla, pokračujeme v doručení.");
        dispute.ResolvedAt.Should().NotBeNull();

        var outboxRows = await db.Set<OutboxEvent>().AsNoTracking()
            .Where(e => e.AggregateId == OrderId)
            .ToListAsync();
        outboxRows.Should().HaveCount(1);
        outboxRows[0].EventType.Should().Be(OutboxEventTypes.OrderDisputeResolvedCustomerEmail);

        var audit = await db.Set<AdminAuditLogEntry>().AsNoTracking()
            .Where(e => e.TargetId == OrderId)
            .ToListAsync();
        audit.Should().HaveCount(1);
        audit[0].ActionCode.Should().Be("order.dispute.resolve");
        audit[0].AdminUserId.Should().Be(AdminUserId);
        // jsonb normalizes with a space after the colon. Disputed = 8,
        // Shipped = 3 (numeric State serialization in the snapshot).
        audit[0].BeforeJson.Should().Contain("\"State\": 8");
        audit[0].AfterJson.Should().Contain("\"State\": 3");
    }

    [Fact]
    public async Task Admin_resolve_refunded_e2e_refunds_full_remaining_and_writes_both_audit_rows()
    {
        // AC-9 / review B-1 happy leg: the nested RefundOrder pipeline
        // runs in the SAME scope — its inner UoW commit flushes the
        // shared DbContext (resolution + refund + inner audit atomically);
        // the outer commit lands the outer audit row.
        await SeedDisputedOrderAsync(shipped: false); // Disputed, was Paid
        _provider.EnqueueRefund(BusinessResult.Success(
            new RefundReceipt(TransId, Total, Currency, DateTimeOffset.UtcNow)));
        using var client = CreateAdminClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/orders/{OrderId}/dispute/resolve",
            new { outcome = "Refunded", resolutionNotes = "Vracíme plnou částku." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        _provider.RefundCallCount.Should().Be(1, "exactly one provider call for the full remaining amount");
        _provider.RefundCalls.Single().Should().Be((TransId, Total, Currency));

        await using var db = _harness.CreateDbContext();
        var order = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == OrderId);
        order.State.Should().Be(OrderState.Refunded, "AC-9: the order ends Refunded end-to-end");
        order.RefundedAmountMinor.Should().Be(Total, "the dispute lane refunds the FULL remaining amount");
        order.PreDisputeState.Should().BeNull("the restore pointer is cleared");

        var dispute = await db.Set<Dispute>().AsNoTracking().SingleAsync(d => d.Id == DisputeId);
        dispute.ResolvedAt.Should().NotBeNull();
        dispute.ResolutionOutcome.Should().Be(DisputeResolutionOutcome.Refunded);
        dispute.ResolutionNotes.Should().Be("Vracíme plnou částku.");

        var outboxEventTypes = await db.Set<OutboxEvent>().AsNoTracking()
            .Where(e => e.AggregateId == OrderId)
            .Select(e => e.EventType)
            .ToListAsync();
        outboxEventTypes.Should().BeEquivalentTo(new[]
        {
            OutboxEventTypes.OrderDisputeResolvedCustomerEmail,
            OutboxEventTypes.OrderRefundedCustomerEmail,
        }, "both customer emails are enqueued — dispute-resolved (outer) + order-refunded (inner)");

        var auditActionCodes = await db.Set<AdminAuditLogEntry>().AsNoTracking()
            .Where(e => e.TargetId == OrderId)
            .Select(e => e.ActionCode)
            .ToListAsync();
        auditActionCodes.Should().BeEquivalentTo(new[]
        {
            "order.dispute.resolve",
            "order.refund",
        }, "the nested RefundOrder pipeline writes its own audit row alongside the outer one");
    }

    [Fact]
    public async Task Admin_resolve_refunded_with_provider_failure_leaves_dispute_open_and_commits_nothing()
    {
        // AC-9 / review B-1 inner-failure leg (Risk §4): the provider
        // refuses → RefundOrder fails BEFORE any mutation → both UoW
        // commits are skipped → the staged resolution rolls back with
        // the transaction. The dispute stays OPEN; the order stays
        // Disputed; zero outbox rows; zero audit rows.
        await SeedDisputedOrderAsync(shipped: false);
        _provider.EnqueueRefund(BusinessResult.Failure<RefundReceipt>(
            Error.Permanent(BusinessErrorMessage.PaymentProviderRejected)));
        using var client = CreateAdminClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/orders/{OrderId}/dispute/resolve",
            new { outcome = "Refunded", resolutionNotes = "Pokus o refundaci." });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "the Permanent provider error propagates verbatim through the nested pipeline");
        using (var errorDoc = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()))
        {
            errorDoc.RootElement.GetProperty("code").GetString()
                .Should().Be(BusinessErrorMessage.PaymentProviderRejected);
        }
        _provider.RefundCallCount.Should().Be(1, "provider-first: the refund attempt did reach the gateway");

        await using var db = _harness.CreateDbContext();
        var order = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == OrderId);
        order.State.Should().Be(OrderState.Disputed, "the whole resolution rolled back");
        order.PreDisputeState.Should().Be(OrderState.Paid, "the restore pointer is untouched");
        order.RefundedAmountMinor.Should().Be(0, "no refund was recorded");

        var dispute = await db.Set<Dispute>().AsNoTracking().SingleAsync(d => d.Id == DisputeId);
        dispute.ResolvedAt.Should().BeNull("the dispute STAYS open — the admin retries or picks another outcome");
        dispute.ResolutionOutcome.Should().BeNull();

        var outboxCount = await db.Set<OutboxEvent>().AsNoTracking()
            .CountAsync(e => e.AggregateId == OrderId);
        outboxCount.Should().Be(0, "no email rides a failed resolution");

        var auditCount = await db.Set<AdminAuditLogEntry>().AsNoTracking()
            .CountAsync(e => e.TargetId == OrderId);
        auditCount.Should().Be(0, "failed commands write no audit row (ADR 0014)");
    }

    [Fact]
    public async Task Resolve_on_non_disputed_order_returns_loud_409_notOpen()
    {
        await SeedDisputedOrderAsync(shipped: true);
        using var client = CreateAdminClient();

        var first = await client.PostAsJsonAsync(
            $"/api/v1/orders/{OrderId}/dispute/resolve",
            new { outcome = "Resumed", resolutionNotes = "První vyřízení reklamace." });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.PostAsJsonAsync(
            $"/api/v1/orders/{OrderId}/dispute/resolve",
            new { outcome = "Refunded", resolutionNotes = "Druhý pokus o vyřízení." });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "re-resolve is LOUD per §C.4 — a silent success would mask an admin race");
        using var errorDoc = System.Text.Json.JsonDocument.Parse(
            await second.Content.ReadAsStringAsync());
        errorDoc.RootElement.GetProperty("code").GetString()
            .Should().Be(BusinessErrorMessage.OrderDisputeNotOpen);
    }
}
