using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Makables.Core.Domain.Admin;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Orders;
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

namespace Makables.IntegrationTests.Admin;

/// <summary>
/// End-to-end coverage for the T-0111 admin read endpoints
/// (<c>GET /api/v1/admin-orders | admin-invoices | audit-log</c> on the
/// Admin host). Pins: cross-tenant Unscoped reads, soft-deleted visibility
/// (IgnoreQueryFilters on admin-orders), invoice type/country filtering,
/// audit-log action-code + date-range filtering, and the list-DTO shape
/// (privileged CustomerEmail; audit list omits before/after JSONB).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminQueriesIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";
    private const string AdminUserId = "user-admin-1";

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Admin.Program> _factory = default!;

    public AdminQueriesIntegrationTests(PostgresHarness harness) => _harness = harness;

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
                        ["AzureBlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
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

    private static readonly DateTimeOffset SeedAt =
        new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private async Task SeedCrossTenantAsync()
    {
        await using var db = _harness.CreateDbContext();
        const string actor = "test-seed";

        void Add<T>(T e) where T : class => db.Set<T>().Add(e);

        AddressEntity NewAddress(string id)
        {
            var a = AddressEntity.Create(id, "Pikrtova", "1737", "Praha", "14000", CountryCode, CountryCode);
            a.MarkCreated(actor, SeedAt);
            return a;
        }

        User NewUser(string id, string email, UserRole role)
        {
            var u = User.Create(id, email, role, "Name", CountryCode,
                passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
            u.ConfirmEmail(SeedAt);
            u.MarkCreated(actor, SeedAt);
            return u;
        }

        MakerEntity NewMaker(string id, string userId, string addressId, string company, string slug)
        {
            var m = MakerEntity.Create(id, userId, "27074358", null, company, null, addressId,
                null, true, "ares", SeedAt, false, CountryCode, slug);
            m.MarkCreated(actor, SeedAt);
            return m;
        }

        Add(NewUser(AdminUserId, "admin@makables.cz", UserRole.Admin));
        Add(NewUser("user-cust-a", "anna@example.cz", UserRole.Customer));
        Add(NewUser("user-cust-b", "bara@example.cz", UserRole.Customer));
        Add(NewUser("user-maker-x", "x@example.cz", UserRole.Maker));
        Add(NewUser("user-maker-y", "y@example.cz", UserRole.Maker));
        Add(NewAddress("addr-x"));
        Add(NewAddress("addr-y"));
        Add(NewMaker("maker-x", "user-maker-x", "addr-x", "Avast s.r.o.", "avast"));
        Add(NewMaker("maker-y", "user-maker-y", "addr-y", "Seznam a.s.", "seznam"));

        var category = Category.Create("cat-1", "3D tisk", "3d-tisk", null, null, 10, CountryCode);
        category.MarkCreated(actor, SeedAt);
        Add(category);

        var product = Product.Create("prod-1", "maker-x", "cat-1", "Vase", null,
            new Money(50000, Currency), PriceType.Fixed, 300, CountryCode);
        product.MarkCreated(actor, SeedAt);
        Add(product);

        Order NewOrder(string id, string number, string custId, string custEmail, string makerId, DateTimeOffset created, bool active)
        {
            var o = Order.Create(id, number, custId, makerId, "prod-1",
                custEmail == "anna@example.cz" ? "Anna" : "Bára", custEmail, "+420 723 456 789",
                50000, 7900, 7500, 50400, 57900, Currency, 2100,
                ShippingMethod.ZasilkovnaPickupPoint, "pp-42", CountryCode);
            o.MarkCreated(actor, created);
            if (!active) o.MarkDeactivated(actor, created.AddDays(1));
            return o;
        }

        Add(NewOrder("ord-a", "M-CZ-20260001", "user-cust-a", "anna@example.cz", "maker-x", SeedAt, true));
        Add(NewOrder("ord-b", "M-CZ-20260002", "user-cust-b", "bara@example.cz", "maker-y", SeedAt.AddHours(1), true));
        // Soft-deleted (mimics a T-0110 anonymised reconciliation row).
        Add(NewOrder("ord-c", "M-CZ-20260003", "user-cust-a", "anna@example.cz", "maker-x", SeedAt.AddHours(2), false));

        Invoice NewInvoice(string id, string number, InvoiceType type, string recipient, string country, string? orderId, string? batchId)
        {
            var inv = Invoice.Issue(id, number, type, orderId, batchId, "maker-x",
                "JVM YORE s.r.o.", "12345678", null, null, recipient, "r@example.cz",
                null, null, new DateOnly(2026, 5, 1), null, new DateOnly(2026, 5, 15),
                InvoicingMode.None, 1000, 0, 0, 1000, Currency, country);
            inv.MarkCreated(actor, SeedAt);
            return inv;
        }

        Add(NewInvoice("inv-1", "FV-CZ-20260001", InvoiceType.Customer, "Anna", "CZ", "ord-a", null));
        Add(NewInvoice("inv-2", "FV-CZ-20260002", InvoiceType.Fee, "JVM maker fee", "CZ", null, "batch-1"));
        Add(NewInvoice("inv-3", "FV-SK-20260001", InvoiceType.Fee, "SK maker fee", "SK", null, "batch-2"));

        AdminAuditLogEntry NewAudit(string id, string action, string entity, string targetId, DateTimeOffset at)
            => AdminAuditLogEntry.Record(id, AdminUserId, action, entity, targetId, "{}", "{}", at, notes: "n");

        Add(NewAudit("aud-1", "order.refund", "order", "ord-a", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)));
        Add(NewAudit("aud-2", "maker.verify", "maker", "maker-x", new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)));
        Add(NewAudit("aud-3", "order.refund", "order", "ord-b", new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)));

        await db.SaveChangesAsync();
    }

    private static string IssueToken(string userId, string email, UserRole role, string audience)
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(userId, email, role, "Test", CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(SeedAt);
        return issuer.Issue(user, audience, DateTimeOffset.UtcNow).Token;
    }

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", IssueToken(AdminUserId, "admin@makables.cz", UserRole.Admin, MakablesAudiences.Admin));
        return client;
    }

    [Fact]
    public async Task GET_admin_orders_returns_cross_tenant_rows()
    {
        await SeedCrossTenantAsync();
        using var client = CreateAdminClient();

        var response = await client.GetAsync("/api/v1/admin-orders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetAllOrdersEnvelope>();
        body!.Orders.TotalCount.Should().Be(3, "two active + one soft-deleted cross-tenant order");
        body.Orders.Items.Should().Contain(o => o.MakerId == "maker-x" && o.CustomerEmail == "anna@example.cz");
        body.Orders.Items.Should().Contain(o => o.MakerId == "maker-y" && o.CustomerEmail == "bara@example.cz");
        body.Orders.Items.Should().Contain(o => o.MakerName == "Avast s.r.o.");
        // Sorted CreatedAt DESC — ord-c (latest) first.
        body.Orders.Items[0].OrderId.Should().Be("ord-c");
    }

    [Fact]
    public async Task GET_admin_orders_surfaces_soft_deleted_rows()
    {
        await SeedCrossTenantAsync();
        using var client = CreateAdminClient();

        var body = await (await client.GetAsync("/api/v1/admin-orders"))
            .Content.ReadFromJsonAsync<GetAllOrdersEnvelope>();

        body!.Orders.Items.Should().Contain(o => o.OrderId == "ord-c" && !o.IsActive);
        body.Orders.Items.Should().Contain(o => o.OrderId == "ord-a" && o.IsActive);
    }

    [Fact]
    public async Task GET_admin_invoices_filters_by_type_and_excludes_other_countries()
    {
        await SeedCrossTenantAsync();
        using var client = CreateAdminClient();

        var body = await (await client.GetAsync("/api/v1/admin-invoices?type=Fee&country=CZ"))
            .Content.ReadFromJsonAsync<GetAllInvoicesEnvelope>();

        body!.Invoices.TotalCount.Should().Be(1, "only the CZ Fee invoice matches");
        body.Invoices.Items.Single().InvoiceNumber.Should().Be("FV-CZ-20260002");
        body.Invoices.Items.Single().Type.Should().Be(InvoiceType.Fee);
    }

    [Fact]
    public async Task GET_audit_log_filters_by_actionCode_and_dateRange()
    {
        await SeedCrossTenantAsync();
        using var client = CreateAdminClient();

        var body = await (await client.GetAsync(
                "/api/v1/audit-log?actionCode=order.refund&dateFrom=2026-04-15T00:00:00Z&dateTo=2026-06-01T00:00:00Z"))
            .Content.ReadFromJsonAsync<GetAuditLogEnvelope>();

        body!.Entries.TotalCount.Should().Be(1, "only aud-3 (order.refund, 2026-05-01) is in range");
        body.Entries.Items.Single().Id.Should().Be("aud-3");
        body.Entries.Items.Single().ActionCode.Should().Be("order.refund");
        body.Entries.Items.Single().Notes.Should().Be("n");
    }

    [Fact]
    public async Task Customer_JWT_is_rejected_on_the_admin_host()
    {
        await SeedCrossTenantAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", IssueToken("user-cust-a", "anna@example.cz", UserRole.Customer, MakablesAudiences.Customer));

        var response = await client.GetAsync("/api/v1/admin-orders");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Local mirror DTOs — the test only reads the JSON the API emits; the
    // generated client is exercised by tsc, not here.
    private sealed record GetAllOrdersEnvelope(PagedData<AdminOrderListItemDto> Orders);
    private sealed record GetAllInvoicesEnvelope(PagedData<AdminInvoiceListItemDto> Invoices);
    private sealed record GetAuditLogEnvelope(PagedData<AdminAuditLogItemDto> Entries);
}
