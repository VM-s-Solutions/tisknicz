using FluentAssertions;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Orders;
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

namespace Makables.IntegrationTests.Orders;

/// <summary>
/// T-0125 §A.3 — proves the <c>ux_disputes_order_open</c> translator entry +
/// partial-unique-index backstop. An OPEN dispute row (<c>resolved_at IS
/// NULL</c>) is inserted directly (committed in its own context) so the
/// in-flight <c>OpenDispute.Handler</c> Silent-Success re-open gate is
/// bypassed and the duplicate insert reaches Postgres — the exact post-gate
/// race a concurrent double-open produces. The loser must surface
/// <see cref="BusinessErrorMessage.OrderInvalidTransition"/> (the translated
/// 23505), as a <see cref="ErrorType.Conflict"/> (HTTP 409), NOT a raw 500.
///
/// <para>
/// Dispatched through the real Mediator pipeline on the Admin host
/// (validation + UoW commit + translator + admin-audit behavior) with a
/// swappable <see cref="IUserSessionProvider"/> so the admin command's
/// fail-closed session check is satisfied. Mirrors the
/// <c>ux_reviews_order_active</c> / <c>ux_payout_batches_open_per_country</c>
/// concurrent-loser tests.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OpenDisputeConcurrencyTests : IAsyncLifetime
{
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";
    private const string AdminUserId = "user-admin-1";
    private const string CustomerUserId = "user-customer-1";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string AddressId = "addr-1";
    private const string CategoryId = "cat-1";
    private const string ProductId = "prod-1";
    private const string OrderId = "ord-1";
    private const string WinnerDisputeId = "disp-winner-1";

    private static readonly DateTimeOffset SeedAt =
        new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;
    private readonly MutableSessionProvider _session = new();
    private WebApplicationFactory<Makables.Web.Admin.Program> _factory = default!;

    public OpenDisputeConcurrencyTests(PostgresHarness harness) => _harness = harness;

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

                    var sessionDescriptors = services
                        .Where(d => d.ServiceType == typeof(IUserSessionProvider)).ToList();
                    foreach (var d in sessionDescriptors) services.Remove(d);
                    services.AddSingleton<IUserSessionProvider>(_session);
                });
            });
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private async Task SeedDeliveredOrderAsync()
    {
        await using var db = _harness.CreateDbContext();
        const string seedActor = "test-seed";

        foreach (var (id, email, role, name) in new[]
        {
            (AdminUserId, "admin@example.cz", UserRole.Admin, "Admin"),
            (CustomerUserId, "anna@example.cz", UserRole.Customer, "Anna"),
            (MakerUserId, "maker@example.cz", UserRole.Maker, "Maker"),
        })
        {
            var u = User.Create(
                id: id, email: email, role: role, fullName: name,
                countryCodePrimary: CountryCode,
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
            registeredAddressId: AddressId, incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares",
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
            title: "Vase", description: null, price: new Money(50000, Currency),
            priceType: PriceType.Fixed, weightGrams: 300, countryCode: CountryCode);
        product.MarkCreated(seedActor, SeedAt);
        db.Set<Product>().Add(product);

        var order = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId, makerId: MakerId, productId: ProductId,
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420 723 456 789",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: Currency, vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: CountryCode);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(SeedAt.AddHours(1));
        order.MarkAsPaid(clock, "tx-1");
        order.Accept(clock);
        order.Ship(clock, "PKT-1", 7);
        order.MarkAsDelivered(clock, OrderDeliverySource.Carrier);
        order.MarkCreated(seedActor, SeedAt);
        db.Set<Order>().Add(order);

        await db.SaveChangesAsync();
    }

    private async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<ISender>();
        return await mediator.Send(request, default);
    }

    /// <summary>
    /// AC-5: an OPEN dispute row already exists for the order (the race
    /// winner), committed in its own context BUT the order state is left at
    /// Delivered — so the losing dispatch's Silent-Success gate
    /// (<c>order.State == Disputed</c>) does not fire; it flips the order to
    /// Disputed and inserts a second OPEN dispute, which loses the
    /// <c>ux_disputes_order_open</c> partial-unique race. The 23505 must
    /// translate to a typed Conflict, NOT a raw 500, and the whole UoW (state
    /// flip + dispute + outbox) must roll back.
    /// </summary>
    [Fact]
    public async Task Concurrent_double_open_loser_returns_conflict_not_500()
    {
        await SeedDeliveredOrderAsync();
        _session.UserId = AdminUserId;

        // Race winner: an OPEN dispute row already exists for ord-1, committed
        // in its own context. The losing dispatch's gate does not see it (the
        // order state is still Delivered), so it reaches the duplicate insert.
        await using (var seed = _harness.CreateDbContext())
        {
            var winner = Dispute.Open(
                id: WinnerDisputeId, orderId: OrderId,
                category: DisputeCategory.NotDelivered,
                description: "Balík se ztratil (winner).",
                source: DisputeSource.Customer, countryCode: CountryCode);
            winner.MarkCreated("test-seed", SeedAt.AddHours(2));
            seed.Set<Dispute>().Add(winner);
            await seed.SaveChangesAsync();
        }

        // Loser: admin OpenDispute for the same order. The handler's
        // Silent-Success gate passes (order is Delivered, not Disputed), it
        // flips the state and adds a second OPEN dispute; the insert hits the
        // partial-unique index and UnitOfWorkPipelineBehavior translates 23505.
        var loser = await SendAsync(new OpenDispute.Command(
            OrderId, DisputeCategory.DamagedItem, "Druhý spor (loser)."));

        loser.IsSuccess.Should().BeFalse("the duplicate OPEN-dispute insert loses the unique-index race");
        loser.Error!.Code.Should().Be(
            BusinessErrorMessage.OrderInvalidTransition,
            "the 23505 is translated to the same typed conflict the handler's already-Disputed branch returns, NOT a raw 500");
        loser.Error.Type.Should().Be(ErrorType.Conflict);

        // The loser's entire UoW rolled back atomically: exactly one OPEN
        // dispute (the winner), the order is NOT flipped to Disputed, and no
        // outbox row was committed.
        await using var db = _harness.CreateDbContext();
        var openDisputes = await db.Set<Dispute>().AsNoTracking()
            .Where(d => d.OrderId == OrderId && d.ResolvedAt == null)
            .ToListAsync();
        openDisputes.Should().ContainSingle("only the winner's OPEN dispute survives");
        openDisputes[0].Id.Should().Be(WinnerDisputeId);

        var order = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == OrderId);
        order.State.Should().Be(OrderState.Delivered, "the loser's state flip rolled back with its UoW");
    }

    private sealed class MutableSessionProvider : IUserSessionProvider
    {
        public string? UserId { get; set; }

        public string? GetUserId() => UserId;

        public string? GetUserEmail() => null;

        public string? GetUserCountryCode() => CountryCode;
    }
}
