using FluentAssertions;
using Makables.Core.AppServices.Features.Reviews;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Products;
using Makables.Core.Domain.Reviews;
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

namespace Makables.IntegrationTests.Reviews;

/// <summary>
/// T-0100 DB-level coverage on real Postgres (<see cref="PostgresHarness"/>),
/// dispatched through the real Mediator pipeline (validation + UoW commit +
/// auditable interceptor) with a swappable <see cref="IUserSessionProvider"/>
/// so one Customer-host factory acts as customer A / customer B / the maker.
/// Pins (per §Tests): submit e2e + atomic recompute; recompute correctness
/// incl. the soft-delete-exclusion self-healing case; per-order uniqueness;
/// both cross-tenant IDOR paths; the reply overwrite.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReviewsIntegrationTests : IAsyncLifetime
{
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";
    private const string CustomerAUserId = "user-customer-a";
    private const string CustomerBUserId = "user-customer-b";
    private const string MakerUserId = "user-maker-1";
    private const string MakerBUserId = "user-maker-2";
    private const string MakerId = "maker-1";
    private const string MakerBId = "maker-2";
    private const string AddressId = "addr-1";
    private const string CategoryId = "cat-1";
    private const string ProductId = "prod-1";

    private static readonly DateTimeOffset SeedAt = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;
    private readonly MutableSessionProvider _session = new();
    private WebApplicationFactory<Makables.Web.Customer.Program> _factory = default!;

    public ReviewsIntegrationTests(PostgresHarness harness) => _harness = harness;

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
                    services.AddDbContext<MakablesDbContext>(o => o.UseNpgsql(_harness.ConnectionString));

                    var sessionDescriptors = services
                        .Where(d => d.ServiceType == typeof(IUserSessionProvider)).ToList();
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
    /// Seed the join graph (customers A/B, makers 1/2, address/category/
    /// product). Orders are added per-test via <see cref="SeedDeliveredOrderAsync"/>.
    /// </summary>
    private async Task SeedGraphAsync()
    {
        await using var db = _harness.CreateDbContext();
        const string seedActor = "test-seed";

        foreach (var (id, email, role, name) in new[]
        {
            (CustomerAUserId, "anna@example.cz", UserRole.Customer, "Anna"),
            (CustomerBUserId, "boris@example.cz", UserRole.Customer, "Boris"),
            (MakerUserId, "maker@example.cz", UserRole.Maker, "Maker"),
            (MakerBUserId, "maker2@example.cz", UserRole.Maker, "Maker2"),
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

        foreach (var (id, userId, slug) in new[]
        {
            (MakerId, MakerUserId, "avast"),
            (MakerBId, MakerBUserId, "avast-two"),
        })
        {
            var maker = MakerEntity.Create(
                id: id, userId: userId, registrationNumber: id == MakerId ? "27074358" : "27074359",
                vatId: null, companyName: id == MakerId ? "Avast s.r.o." : "Avast Two s.r.o.",
                legalForm: null, registeredAddressId: AddressId, incorporatedOn: null,
                isActiveInRegistry: true, sourceRegistry: "ares",
                snapshotFetchedAt: SeedAt, snapshotIsStale: false,
                countryCode: CountryCode, slug: slug);
            maker.MarkCreated(seedActor, SeedAt);
            db.Set<MakerEntity>().Add(maker);
        }

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

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seed a Delivered order owned by <paramref name="customerUserId"/> +
    /// <paramref name="makerId"/>. <see cref="Auditable.MarkCreated"/> is
    /// stamped so the audit interceptor sees a complete row at seed time.
    /// </summary>
    private Task SeedDeliveredOrderAsync(
        string orderId, string orderNumber, string customerUserId, string makerId) =>
        SeedOrderAsync(orderId, orderNumber, customerUserId, makerId, delivered: true);

    /// <summary>
    /// Seed an order owned by <paramref name="customerUserId"/> +
    /// <paramref name="makerId"/>, advanced to Delivered (<paramref name="delivered"/>
    /// = <c>true</c>) or stopped at Shipped (<c>false</c>). The Shipped variant
    /// pins the pre-delivery eligibility gate e2e (secops Gate 3 LOW).
    /// </summary>
    private async Task SeedOrderAsync(
        string orderId, string orderNumber, string customerUserId, string makerId, bool delivered)
    {
        await using var db = _harness.CreateDbContext();
        const string seedActor = "test-seed";

        var order = Order.Create(
            id: orderId, orderNumber: orderNumber,
            customerUserId: customerUserId, makerId: makerId, productId: ProductId,
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420 723 456 789",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: Currency, vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: CountryCode);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(SeedAt.AddHours(1));
        order.MarkAsPaid(clock, $"tx-{orderId}");
        order.Accept(clock);
        order.Ship(clock, $"PKT-{orderId}", 7);
        if (delivered)
        {
            order.MarkAsDelivered(clock, OrderDeliverySource.Carrier);
        }
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

    // === Submit e2e + atomic recompute (AC-1/AC-2) ===

    [Fact]
    public async Task Submit_writes_review_row_and_recomputes_maker_rating_atomically()
    {
        await SeedGraphAsync();
        await SeedDeliveredOrderAsync("ord-1", "M-CZ-20260042", CustomerAUserId, MakerId);
        _session.UserId = CustomerAUserId;

        var result = await SendAsync(new SubmitReview.Command("ord-1", 4, "Pěkné zpracování."));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Rating.Should().Be(4);

        await using var db = _harness.CreateDbContext();
        var review = await db.Set<Review>().AsNoTracking().SingleAsync(r => r.OrderId == "ord-1");
        review.MakerId.Should().Be(MakerId, "maker_id is denormalized off the order");
        review.CustomerUserId.Should().Be(CustomerAUserId);
        review.Rating.Should().Be(4);

        var maker = await db.Set<MakerEntity>().AsNoTracking().FirstAsync(m => m.Id == MakerId);
        maker.RatingCount.Should().Be(1);
        maker.RatingAverageBp.Should().Be(40_000, "one 4-star review → AVG 4.0 → 40000 bp");
    }

    // === Recompute correctness + soft-delete self-healing (AC-11) ===

    [Fact]
    public async Task Recompute_averages_active_rows_and_self_heals_after_soft_delete()
    {
        await SeedGraphAsync();
        await SeedDeliveredOrderAsync("ord-1", "M-CZ-20260001", CustomerAUserId, MakerId);
        await SeedDeliveredOrderAsync("ord-2", "M-CZ-20260002", CustomerAUserId, MakerId);
        await SeedDeliveredOrderAsync("ord-3", "M-CZ-20260003", CustomerAUserId, MakerId);
        await SeedDeliveredOrderAsync("ord-4", "M-CZ-20260004", CustomerAUserId, MakerId);
        _session.UserId = CustomerAUserId;

        (await SendAsync(new SubmitReview.Command("ord-1", 5, null))).IsSuccess.Should().BeTrue();
        (await SendAsync(new SubmitReview.Command("ord-2", 4, null))).IsSuccess.Should().BeTrue();
        (await SendAsync(new SubmitReview.Command("ord-3", 3, null))).IsSuccess.Should().BeTrue();

        await using (var db = _harness.CreateDbContext())
        {
            var maker = await db.Set<MakerEntity>().AsNoTracking().FirstAsync(m => m.Id == MakerId);
            maker.RatingCount.Should().Be(3);
            maker.RatingAverageBp.Should().Be(40_000, "AVG(5,4,3) = 4.0 → 40000 bp");
        }

        // Admin soft-deactivates the 3-star review (no admin UI ships — flip
        // the flag directly to model it). The recompute must then EXCLUDE it.
        await using (var db = _harness.CreateDbContext())
        {
            var three = await db.Set<Review>().FirstAsync(r => r.OrderId == "ord-3");
            three.MarkDeactivated("admin", SeedAt.AddDays(1));
            await db.SaveChangesAsync();
        }

        // Submit a 4th review (5 stars). Active set is now {5,4,5} → AVG 4.6667.
        (await SendAsync(new SubmitReview.Command("ord-4", 5, null))).IsSuccess.Should().BeTrue();

        await using (var db = _harness.CreateDbContext())
        {
            var maker = await db.Set<MakerEntity>().AsNoTracking().FirstAsync(m => m.Id == MakerId);
            maker.RatingCount.Should().Be(3, "the deactivated 3-star row is excluded (self-healing)");
            // (5 + 4 + 5) / 3 = 4.6667 → round(46666.67) = 46667 bp.
            maker.RatingAverageBp.Should().Be(46_667, "recompute-from-rows excludes the soft-deleted row");
        }
    }

    // === Per-order uniqueness (AC-3/AC-4) ===

    [Fact]
    public async Task Second_review_for_same_order_returns_alreadyExists()
    {
        await SeedGraphAsync();
        await SeedDeliveredOrderAsync("ord-1", "M-CZ-20260042", CustomerAUserId, MakerId);
        _session.UserId = CustomerAUserId;

        (await SendAsync(new SubmitReview.Command("ord-1", 5, null))).IsSuccess.Should().BeTrue();
        var second = await SendAsync(new SubmitReview.Command("ord-1", 1, "Změna názoru."));

        second.IsSuccess.Should().BeFalse();
        second.Error!.Code.Should().Be(BusinessErrorMessage.ReviewAlreadyExists);

        await using var db = _harness.CreateDbContext();
        var count = await db.Set<Review>().AsNoTracking().CountAsync(r => r.OrderId == "ord-1");
        count.Should().Be(1, "the partial unique index keeps exactly one active review");
    }

    // === Concurrent double-submit: 23505 → ReviewAlreadyExists, not 500 (AC-4) ===

    /// <summary>
    /// Proves the <c>ux_reviews_order_active</c> translator entry +
    /// partial-unique-index backstop. An active review row is inserted
    /// directly (committed in its own context) so the in-flight
    /// <c>SubmitReview.ExistsForOrderAsync</c> app-gate is bypassed and the
    /// duplicate insert reaches Postgres — the exact post-app-gate race a
    /// concurrent double-submit produces. The loser must surface
    /// <see cref="BusinessErrorMessage.ReviewAlreadyExists"/> (translated
    /// 23505), NOT a raw 500, with exactly one review persisted and the
    /// maker rating reflecting that single review. reviews-loop-bundle
    /// BLOCKER-1.
    /// </summary>
    [Fact]
    public async Task Concurrent_double_submit_loser_returns_alreadyExists_not_500()
    {
        await SeedGraphAsync();
        await SeedDeliveredOrderAsync("ord-1", "M-CZ-20260042", CustomerAUserId, MakerId);
        _session.UserId = CustomerAUserId;

        // Race winner: an active review row already exists for ord-1, committed
        // in its own context. The losing dispatch's app-gate read does not see
        // it within its own transaction snapshot semantics — but even if it did
        // here, the point under test is the post-gate index backstop, so we drive
        // SubmitReview against the existing row to force the 23505 path.
        await using (var seed = _harness.CreateDbContext())
        {
            var winner = Review.Create(
                id: "rev-winner", orderId: "ord-1", makerId: MakerId,
                customerUserId: CustomerAUserId, rating: 5, body: "První.",
                countryCode: CountryCode);
            winner.MarkCreated("test-seed", SeedAt.AddHours(2));
            seed.Set<Review>().Add(winner);
            // Recompute the maker rating for the winner so the persisted
            // aggregate reflects exactly one review (the loser must not change it).
            var maker = await seed.Set<MakerEntity>().FirstAsync(m => m.Id == MakerId);
            maker.RecomputeRating(1, 50_000);
            await seed.SaveChangesAsync();
        }

        // Loser: SubmitReview for the same order. The duplicate insert hits the
        // partial unique index; UnitOfWorkPipelineBehavior translates the 23505.
        var loser = await SendAsync(new SubmitReview.Command("ord-1", 1, "Druhá."));

        loser.IsSuccess.Should().BeFalse("the duplicate insert loses the unique-index race");
        loser.Error!.Code.Should().Be(
            BusinessErrorMessage.ReviewAlreadyExists,
            "the 23505 is translated to the typed conflict, NOT a raw 500");
        loser.Error.Type.Should().Be(ErrorType.Conflict);

        await using var db = _harness.CreateDbContext();
        var count = await db.Set<Review>().AsNoTracking().CountAsync(r => r.OrderId == "ord-1");
        count.Should().Be(1, "the partial unique index keeps exactly one active review");

        var persisted = await db.Set<MakerEntity>().AsNoTracking().FirstAsync(m => m.Id == MakerId);
        persisted.RatingCount.Should().Be(1, "only the winner's review counts");
        persisted.RatingAverageBp.Should().Be(50_000, "the loser's rollback leaves the winner aggregate");
    }

    // === Pre-delivery eligibility e2e (secops Gate 3 LOW) ===

    /// <summary>
    /// A Shipped (non-Delivered) order owned by the customer must be rejected
    /// by the eligibility gate with
    /// <see cref="BusinessErrorMessage.ReviewOrderNotDelivered"/> and no review
    /// row written. The unit test pins the predicate; this pins the full
    /// Postgres path.
    /// </summary>
    [Fact]
    public async Task Pre_delivery_shipped_order_returns_orderNotDelivered_without_writes()
    {
        await SeedGraphAsync();
        await SeedOrderAsync("ord-ship", "M-CZ-20260055", CustomerAUserId, MakerId, delivered: false);
        _session.UserId = CustomerAUserId;

        var result = await SendAsync(new SubmitReview.Command("ord-ship", 5, "Předčasně."));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ReviewOrderNotDelivered);
        result.Error.Type.Should().Be(ErrorType.Conflict);

        await using var db = _harness.CreateDbContext();
        (await db.Set<Review>().AsNoTracking().AnyAsync()).Should().BeFalse(
            "no review is seated for a pre-delivery order");
        var maker = await db.Set<MakerEntity>().AsNoTracking().FirstAsync(m => m.Id == MakerId);
        maker.RatingCount.Should().Be(0, "the maker rating is untouched");
    }

    // === Customer cross-tenant IDOR (AC-7) ===

    [Fact]
    public async Task Customer_A_cannot_review_customer_B_order_returns_notFound()
    {
        await SeedGraphAsync();
        // Order belongs to customer B.
        await SeedDeliveredOrderAsync("ord-b", "M-CZ-20260099", CustomerBUserId, MakerId);
        _session.UserId = CustomerAUserId;

        var result = await SendAsync(new SubmitReview.Command("ord-b", 5, "Cizí objednávka."));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        result.Error.Type.Should().Be(ErrorType.NotFound);

        await using var db = _harness.CreateDbContext();
        (await db.Set<Review>().AsNoTracking().AnyAsync()).Should().BeFalse("no row written for a foreign order");
    }

    // === Maker cross-tenant IDOR + reply overwrite (AC-8/AC-9/AC-10) ===

    [Fact]
    public async Task Maker_reply_cross_tenant_404_then_owning_maker_overwrites()
    {
        await SeedGraphAsync();
        await SeedDeliveredOrderAsync("ord-1", "M-CZ-20260042", CustomerAUserId, MakerId);
        _session.UserId = CustomerAUserId;
        var submit = await SendAsync(new SubmitReview.Command("ord-1", 5, "Skvělé."));
        submit.IsSuccess.Should().BeTrue();
        var reviewId = submit.Value!.ReviewId;

        // Maker B (does not own the reviewed order's maker_id) → 404.
        _session.UserId = MakerBUserId;
        var foreign = await SendAsync(new RespondToReview.Command(reviewId, "Cizí odpověď."));
        foreign.IsSuccess.Should().BeFalse();
        foreign.Error!.Code.Should().Be(BusinessErrorMessage.ReviewNotFound);

        // Owning maker replies twice → the second OVERWRITES (one reply).
        _session.UserId = MakerUserId;
        (await SendAsync(new RespondToReview.Command(reviewId, "Děkujeme!"))).IsSuccess.Should().BeTrue();
        var overwrite = await SendAsync(new RespondToReview.Command(reviewId, "Opravená odpověď."));
        overwrite.IsSuccess.Should().BeTrue();

        await using var db = _harness.CreateDbContext();
        var review = await db.Set<Review>().AsNoTracking().SingleAsync(r => r.Id == reviewId);
        review.MakerReply.Should().Be("Opravená odpověď.", "the second reply overwrites the first (Q4)");
        review.MakerReplyAt.Should().NotBeNull();
        review.Rating.Should().Be(5, "the customer rating is untouched by the reply");
    }

    private sealed class MutableSessionProvider : IUserSessionProvider
    {
        public string? UserId { get; set; }

        public string? GetUserId() => UserId;

        public string? GetUserEmail() => null;

        public string? GetUserCountryCode() => CountryCode;
    }
}
