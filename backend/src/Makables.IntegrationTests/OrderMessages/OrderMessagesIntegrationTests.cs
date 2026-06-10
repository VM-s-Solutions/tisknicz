using FluentAssertions;
using Makables.Core.AppServices.Features.OrderMessages;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.OrderMessages;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Orders.Sorting;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Products;
using Makables.Infra.Database;
using Makables.IntegrationTests.Common;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using AddressEntity = Makables.Core.Domain.Addresses.Address;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.IntegrationTests.OrderMessages;

/// <summary>
/// T-0079 AC-13 DB-level coverage (review-fold BLOCKER-3). Real Postgres
/// via <see cref="PostgresHarness"/>; commands/queries dispatched through
/// the real Mediator pipeline (validation + UoW commit + auditable
/// interceptor) with a swappable <see cref="IUserSessionProvider"/> so a
/// single Customer-host factory can act as customer A, customer B, or
/// the maker. Pins:
/// <list type="bullet">
///   <item><description>5-min debounce end-to-end — two posts inside the
///     window yield exactly ONE outbox row (the bundle's headline locked
///     decision).</description></item>
///   <item><description>Unread denormalization — post increments
///     <c>orders.maker_unread_message_count</c> at the SQL level AND the
///     maker list projection exposes it; MarkAsRead resets to 0.</description></item>
///   <item><description>Cross-tenant isolation at the SQL layer — empty
///     page on GET; zero-row mark sweep.</description></item>
///   <item><description>Mark-as-read idempotency.</description></item>
///   <item><description>Paged ordering (newest first) + TotalCount +
///     hoisted AuthorName projection.</description></item>
/// </list>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrderMessagesIntegrationTests : IAsyncLifetime
{
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";
    private const string CustomerAUserId = "user-customer-a";
    private const string CustomerBUserId = "user-customer-b";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string AddressId = "addr-1";
    private const string CategoryId = "cat-1";
    private const string ProductId = "prod-1";
    private const string OrderId = "ord-1";

    private static readonly DateTimeOffset SeedAt =
        new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;
    private readonly MutableSessionProvider _session = new();
    private WebApplicationFactory<Makables.Web.Customer.Program> _factory = default!;

    public OrderMessagesIntegrationTests(PostgresHarness harness)
    {
        _harness = harness;
    }

    public async Task InitializeAsync()
    {
        await _harness.ResetMutableTablesAsync();
        _factory = new WebApplicationFactory<Makables.Web.Customer.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("IntegrationTest");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Postgres"] = _harness.ConnectionString,
                        ["Jwt:Issuer"] = "https://makables.test",
                        ["Jwt:SigningKeyBase64"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
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
                    if (dbContextDescriptor is not null)
                    {
                        services.Remove(dbContextDescriptor);
                    }
                    services.AddDbContext<MakablesDbContext>(o =>
                        o.UseNpgsql(_harness.ConnectionString));

                    // Mediator-level dispatch has no HTTP context, so the
                    // production HttpContextUserSessionProvider would
                    // return null. Swap in a mutable fake the test flips
                    // between customer A / customer B / the maker.
                    var sessionDescriptors = services.Where(
                        d => d.ServiceType == typeof(IUserSessionProvider)).ToList();
                    foreach (var d in sessionDescriptors)
                    {
                        services.Remove(d);
                    }
                    services.AddSingleton<IUserSessionProvider>(_session);
                });
            });
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Seeds customer A + customer B + maker (+ user/address/category/
    /// product) and one PAID order owned by customer A and the maker.
    /// Orders are seeded Paid because the review-fold state guard blocks
    /// posting on PendingPayment.
    /// </summary>
    private async Task SeedPaidOrderAsync()
    {
        await using var db = _harness.CreateDbContext();
        const string seedActor = "test-seed";

        foreach (var (id, email, role, name) in new[]
        {
            (CustomerAUserId, "anna@example.cz", UserRole.Customer, "Anna"),
            (CustomerBUserId, "boris@example.cz", UserRole.Customer, "Boris"),
            (MakerUserId, "maker@example.cz", UserRole.Maker, "Maker"),
        })
        {
            var u = User.Create(
                id: id, email: email, role: role,
                fullName: name, countryCodePrimary: CountryCode,
                passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
            u.ConfirmEmail(SeedAt);
            u.MarkCreated(seedActor, SeedAt);
            db.Set<User>().Add(u);
        }

        var address = AddressEntity.Create(
            id: AddressId, street: "Pikrtova", houseNumber: "1737",
            city: "Praha", zip: "14000", countryCodeIso: CountryCode,
            auditCountryCode: CountryCode);
        address.MarkCreated(seedActor, SeedAt);
        db.Set<AddressEntity>().Add(address);

        var maker = MakerEntity.Create(
            id: MakerId, userId: MakerUserId,
            registrationNumber: "27074358", vatId: null,
            companyName: "Avast s.r.o.", legalForm: null,
            registeredAddressId: AddressId,
            incorporatedOn: null, isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: SeedAt, snapshotIsStale: false,
            countryCode: CountryCode, slug: "avast");
        maker.MarkCreated(seedActor, SeedAt);
        db.Set<MakerEntity>().Add(maker);

        var category = Category.Create(
            id: CategoryId, name: "3D tisk", slug: "3d-tisk",
            icon: null, description: null, sortOrder: 10, countryCode: CountryCode);
        category.MarkCreated(seedActor, SeedAt);
        db.Set<Category>().Add(category);

        var product = Product.Create(
            id: ProductId, makerId: MakerId, categoryId: CategoryId,
            title: "Vase", description: null,
            price: new Money(50000, Currency),
            priceType: PriceType.Fixed, weightGrams: 300,
            countryCode: CountryCode);
        product.MarkCreated(seedActor, SeedAt);
        db.Set<Product>().Add(product);

        var order = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerAUserId, makerId: MakerId, productId: ProductId,
            contactName: "Anna", contactEmail: "anna@example.cz",
            contactPhone: "+420 723 456 789",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: Currency, vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: CountryCode);
        var paidClock = Substitute.For<IClock>();
        paidClock.UtcNow.Returns(SeedAt.AddHours(1));
        order.MarkAsPaid(paidClock, "tx-1");
        order.MarkCreated(seedActor, SeedAt);
        db.Set<Order>().Add(order);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Sends <paramref name="request"/> in a FRESH DI scope so each
    /// dispatch gets its own DbContext — mirrors one HTTP request per
    /// send (the UoW pipeline commits per scope).
    /// </summary>
    private async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<ISender>();
        return await mediator.Send(request, default);
    }

    // === Tests ===

    [Fact]
    public async Task Debounce_two_posts_within_window_emit_exactly_one_maker_email_outbox_row()
    {
        await SeedPaidOrderAsync();
        _session.UserId = CustomerAUserId;

        var first = await SendAsync(new PostCustomerOrderMessage.Command(OrderId, "First message"));
        var second = await SendAsync(new PostCustomerOrderMessage.Command(OrderId, "Quick follow-up"));

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();

        await using var db = _harness.CreateDbContext();
        var emailRows = await db.Set<OutboxEvent>().AsNoTracking()
            .Where(e => e.EventType == OutboxEventTypes.OrderMessagePostedMakerEmail)
            .ToListAsync();
        emailRows.Should().HaveCount(1,
            "the second post inside the 5-min window is debounce-suppressed");

        var order = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == OrderId);
        order.MakerUnreadMessageCount.Should().Be(2, "both messages still count as unread");
        order.MakerPendingNotificationEmailAt.Should().NotBeNull(
            "the first emit stamped the recipient's debounce pointer");

        var messageCount = await db.Set<OrderMessage>().AsNoTracking().CountAsync();
        messageCount.Should().Be(2, "debounce suppresses the EMAIL, never the message insert");
    }

    [Fact]
    public async Task Unread_denormalization_flows_to_maker_list_and_resets_on_mark_read()
    {
        await SeedPaidOrderAsync();

        // Customer posts → maker counter increments at the SQL level.
        _session.UserId = CustomerAUserId;
        (await SendAsync(new PostCustomerOrderMessage.Command(OrderId, "Hello maker")))
            .IsSuccess.Should().BeTrue();

        await using (var db = _harness.CreateDbContext())
        {
            var order = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == OrderId);
            order.MakerUnreadMessageCount.Should().Be(1);
        }

        // Maker list projection exposes the denormalized counter (AC-11).
        _session.UserId = MakerUserId;
        var list = await SendAsync(new GetMakerOrders.Query(
            Page: 1, PageSize: 20, State: null, DateFrom: null, DateTo: null,
            Sort: OrderSort.CreatedAtDesc));
        list.IsSuccess.Should().BeTrue();
        list.Value!.Orders.Items.Should().ContainSingle()
            .Which.UnreadMessageCount.Should().Be(1);

        // MarkAsRead sweeps + resets the counter back to 0.
        var marked = await SendAsync(new MarkMakerOrderMessagesAsRead.Command(OrderId));
        marked.IsSuccess.Should().BeTrue();
        marked.Value!.MarkedCount.Should().Be(1);

        await using (var db = _harness.CreateDbContext())
        {
            var order = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == OrderId);
            order.MakerUnreadMessageCount.Should().Be(0);
            order.MakerPendingNotificationEmailAt.Should().BeNull(
                "MarkAsRead clears the debounce pointer so the next post emails immediately");

            var message = await db.Set<OrderMessage>().AsNoTracking().SingleAsync();
            message.ReadByCounterpartyAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Cross_tenant_probes_yield_empty_page_and_zero_row_mark_sweep()
    {
        await SeedPaidOrderAsync();

        // Maker posts so customer A's order has 1 unread maker-authored
        // message — the row a (hypothetical) cross-tenant sweep would hit.
        _session.UserId = MakerUserId;
        (await SendAsync(new PostMakerOrderMessage.Command(OrderId, "Update on production")))
            .IsSuccess.Should().BeTrue();

        // Customer B reads customer A's thread → empty page, not 404.
        _session.UserId = CustomerBUserId;
        var page = await SendAsync(new GetCustomerOrderMessages.Query(OrderId, 1, 50));
        page.IsSuccess.Should().BeTrue();
        page.Value!.Messages.Items.Should().BeEmpty();
        page.Value.Messages.TotalCount.Should().Be(0);

        // Customer B's MarkAsRead → generic OrderNotFound at the handler.
        var marked = await SendAsync(new MarkCustomerOrderMessagesAsRead.Command(OrderId));
        marked.IsSuccess.Should().BeFalse();
        marked.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);

        // SQL-level proof: even a direct repository sweep with customer
        // B's identity touches ZERO rows (ownership EXISTS predicate).
        using (var scope = _factory.Services.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOrderMessageRepository>();
            var swept = await repo.MarkAsReadForCustomerAsync(
                OrderId, CustomerBUserId, SeedAt.AddHours(2), default);
            swept.Should().Be(0);
        }

        await using var db = _harness.CreateDbContext();
        var message = await db.Set<OrderMessage>().AsNoTracking().SingleAsync();
        message.ReadByCounterpartyAt.Should().BeNull("customer A's thread stays unread");
        var order = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == OrderId);
        order.CustomerUnreadMessageCount.Should().Be(1);
    }

    [Fact]
    public async Task Mark_as_read_twice_second_call_marks_zero_rows_without_error()
    {
        await SeedPaidOrderAsync();

        _session.UserId = MakerUserId;
        (await SendAsync(new PostMakerOrderMessage.Command(OrderId, "One")))
            .IsSuccess.Should().BeTrue();
        (await SendAsync(new PostMakerOrderMessage.Command(OrderId, "Two")))
            .IsSuccess.Should().BeTrue();

        _session.UserId = CustomerAUserId;
        var first = await SendAsync(new MarkCustomerOrderMessagesAsRead.Command(OrderId));
        first.IsSuccess.Should().BeTrue();
        first.Value!.MarkedCount.Should().Be(2);

        var second = await SendAsync(new MarkCustomerOrderMessagesAsRead.Command(OrderId));
        second.IsSuccess.Should().BeTrue();
        second.Value!.MarkedCount.Should().Be(0, "idempotent — nothing left to sweep");

        await using var db = _harness.CreateDbContext();
        var order = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == OrderId);
        order.CustomerUnreadMessageCount.Should().Be(0);
    }

    [Fact]
    public async Task Paged_thread_read_returns_newest_first_with_total_count_and_author_names()
    {
        await SeedPaidOrderAsync();

        // Seed 5 messages directly (staggered CreatedAt, alternating
        // authors) — bypasses the debounce noise and pins ordering.
        await using (var db = _harness.CreateDbContext())
        {
            for (var i = 1; i <= 5; i++)
            {
                var fromCustomer = i % 2 == 1;
                var message = OrderMessage.Create(
                    id: $"msg-{i}",
                    orderId: OrderId,
                    authorRole: fromCustomer
                        ? OrderMessageAuthorRole.Customer
                        : OrderMessageAuthorRole.Maker,
                    authorUserId: fromCustomer ? CustomerAUserId : MakerUserId,
                    body: $"Message {i}",
                    countryCode: CountryCode);
                message.MarkCreated("test-seed", SeedAt.AddHours(2).AddMinutes(i));
                db.Set<OrderMessage>().Add(message);
            }
            await db.SaveChangesAsync();
        }

        _session.UserId = CustomerAUserId;
        var page1 = await SendAsync(new GetCustomerOrderMessages.Query(OrderId, 1, 2));

        page1.IsSuccess.Should().BeTrue();
        var paged = page1.Value!.Messages;
        paged.TotalCount.Should().Be(5);
        paged.Items.Should().HaveCount(2);
        // Newest first: msg-5 (customer-authored), then msg-4 (maker).
        paged.Items[0].Id.Should().Be("msg-5");
        paged.Items[1].Id.Should().Be("msg-4");
        // Hoisted AuthorName projection (Gate 8 M-1): customer rows carry
        // the checkout ContactName snapshot, maker rows the CompanyName.
        paged.Items[0].AuthorName.Should().Be("Anna");
        paged.Items[0].IsMine.Should().BeTrue("customer host + customer-authored row");
        paged.Items[1].AuthorName.Should().Be("Avast s.r.o.");
        paged.Items[1].IsMine.Should().BeFalse();
    }

    /// <summary>
    /// Mutable <see cref="IUserSessionProvider"/> stand-in — mediator
    /// dispatch has no HTTP context, so tests flip the acting identity
    /// between sends.
    /// </summary>
    private sealed class MutableSessionProvider : IUserSessionProvider
    {
        public string? UserId { get; set; }

        public string? GetUserId() => UserId;

        public string? GetUserEmail() => null;

        public string? GetUserCountryCode() => CountryCode;
    }
}
