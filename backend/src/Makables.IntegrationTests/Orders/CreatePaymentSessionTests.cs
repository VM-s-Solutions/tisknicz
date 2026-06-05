using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Orders;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using AddressEntity = Makables.Core.Domain.Addresses.Address;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.IntegrationTests.Orders;

/// <summary>
/// End-to-end coverage for the T-0065 customer-host payment-session
/// endpoint (<c>POST /api/v1/orders/{orderId}/payment-session</c>). Real
/// Postgres (via <see cref="PostgresHarness"/>) + a
/// <see cref="FakeComgatePaymentProvider"/> injected over the keyed
/// <see cref="IPaymentProvider"/> registration so the verify-then-recreate
/// decision tree (Q1) traverses the real handler + UoW pipeline.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CreatePaymentSessionTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";

    private const string CustomerUserId = "user-customer-1";
    private const string OtherCustomerUserId = "user-customer-2";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string AddressId = "addr-1";
    private const string CategoryId = "cat-1";
    private const string ProductId = "prod-1";
    private const string OrderId = "ord-1";

    private const string TransId1 = "AB1C-D34E";
    private const string TransId2 = "FFFF-9999";
    private const string Redirect1 = "https://payments.comgate.test/pay/AB1C-D34E";
    private const string Redirect2 = "https://payments.comgate.test/pay/FFFF-9999";

    private readonly PostgresHarness _harness;
    private readonly FakeComgatePaymentProvider _provider = new();
    private WebApplicationFactory<Makables.Web.Customer.Program> _factory = default!;

    public CreatePaymentSessionTests(PostgresHarness harness)
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
                        ["Jwt:Issuer"] = TestIssuer,
                        ["Jwt:SigningKeyBase64"] = TestKeyBase64,
                        ["SendGrid:ApiKey"] = "SG.integration-test-stub",
                        ["SendGrid:DefaultFromAddress"] = "no-reply@makables.test",
                        ["PublicAppUrls:WebBaseUrl"] = "https://makables.test",
                        ["Mapbox:AccessToken"] = "pk.integration-test-stub",
                        ["Ares:BaseUrl"] = "https://ares.integration-test.local",
                        // T-0065 Comgate options — these must satisfy
                        // ValidateOnStart even though the SUT uses the
                        // fake provider (the real ComgatePaymentProvider
                        // is still registered as the keyed service we
                        // replace below).
                        ["Comgate:MerchantId"] = "12345",
                        ["Comgate:Secret"] = "integration-test-secret",
                        ["Comgate:BaseUrl"] = "https://payments.comgate.test",
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

                    // Replace the keyed "comgate" IPaymentProvider with our
                    // fake. RemoveAll on a keyed service requires matching
                    // the ServiceKey on the descriptor; the .NET 8+ APIs
                    // accept the key directly via Remove + KeyedSingleton.
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

    // === Seed helpers ===

    private async Task SeedAsync(
        bool emailConfirmed = true,
        OrderState orderState = OrderState.PendingPayment,
        string? existingPaymentRef = null,
        string? existingRedirectUrl = null)
    {
        await using var db = _harness.CreateDbContext();
        var seedActor = "test-seed";
        var seedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        var customer = BuildCustomerUser(CustomerUserId, "anna@example.cz", emailConfirmed);
        customer.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(customer);

        var otherCustomer = BuildCustomerUser(OtherCustomerUserId, "other@example.cz", emailConfirmed: true);
        otherCustomer.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(otherCustomer);

        var makerUser = User.Create(
            id: MakerUserId,
            email: "maker@example.cz",
            role: UserRole.Maker,
            fullName: "Maker User",
            countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        makerUser.ConfirmEmail(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        makerUser.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(makerUser);

        var address = AddressEntity.Create(
            id: AddressId,
            street: "Pikrtova",
            houseNumber: "1737",
            city: "Praha",
            zip: "14000",
            countryCodeIso: CountryCode,
            auditCountryCode: CountryCode);
        address.MarkCreated(seedActor, seedAt);
        db.Set<AddressEntity>().Add(address);

        var maker = MakerEntity.Create(
            id: MakerId,
            userId: MakerUserId,
            registrationNumber: "27074358",
            vatId: null,
            companyName: "Avast Software s.r.o.",
            legalForm: null,
            registeredAddressId: AddressId,
            incorporatedOn: null,
            isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            snapshotIsStale: false,
            countryCode: CountryCode,
            slug: "avast");
        maker.MarkVerified();
        maker.UpdateProfile(bio: null, bankAccount: null, personalPickupEnabled: true, pickupNote: null);
        maker.MarkCreated(seedActor, seedAt);
        db.Set<MakerEntity>().Add(maker);

        var category = Category.Create(
            id: CategoryId, name: "3D tisk", slug: "3d-tisk",
            icon: null, description: null, sortOrder: 10, countryCode: CountryCode);
        category.MarkCreated(seedActor, seedAt);
        db.Set<Category>().Add(category);

        var product = Product.Create(
            id: ProductId,
            makerId: MakerId,
            categoryId: CategoryId,
            title: "Vase",
            description: null,
            price: new Money(50000, Currency),
            priceType: PriceType.Fixed,
            weightGrams: 300,
            countryCode: CountryCode);
        product.MarkCreated(seedActor, seedAt);
        db.Set<Product>().Add(product);

        var order = BuildOrderInState(orderState);
        if (existingPaymentRef is not null && existingRedirectUrl is not null
            && orderState == OrderState.PendingPayment)
        {
            order.ReservePaymentSession(existingPaymentRef, existingRedirectUrl,
                new FixedClock(new DateTimeOffset(2026, 6, 5, 9, 0, 0, TimeSpan.Zero)));
        }
        order.MarkCreated(seedActor, seedAt);
        db.Set<Order>().Add(order);

        await db.SaveChangesAsync();
    }

    private static Order BuildOrderInState(OrderState target)
    {
        var o = Order.Create(
            id: OrderId,
            orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId,
            makerId: MakerId,
            productId: ProductId,
            contactName: "Anna",
            contactEmail: "a@b.cz",
            contactPhone: "+420 723 456 789",
            productPriceAmountMinor: 50000,
            shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500,
            makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900,
            currency: Currency,
            vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42",
            countryCode: CountryCode);

        var fixedClock = new FixedClock(new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.Zero));
        if (target == OrderState.PendingPayment) return o;
        o.MarkAsPaid(fixedClock, "preset-tx");
        if (target == OrderState.Paid) return o;
        o.Accept(fixedClock);
        if (target == OrderState.Accepted) return o;
        throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported");
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static User BuildCustomerUser(string id, string email, bool emailConfirmed)
    {
        var user = User.Create(
            id: id,
            email: email,
            role: UserRole.Customer,
            fullName: "Anna Nováková",
            countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        if (emailConfirmed)
        {
            user.ConfirmEmail(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        }
        return user;
    }

    private string IssueCustomerToken(string userId = CustomerUserId, bool emailConfirmed = true)
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = BuildCustomerUser(userId, $"{userId}@example.cz", emailConfirmed);
        return issuer.Issue(user, MakablesAudiences.Customer, DateTimeOffset.UtcNow).Token;
    }

    private sealed record CreatePaymentSessionResponse(
        string PaymentProviderRef,
        string RedirectUrl);

    // === Tests ===

    [Fact]
    public async Task POST_happy_path_persists_payment_ref_and_redirect_on_order()
    {
        await SeedAsync();
        _provider.EnqueueCreatePayment(
            BusinessResult.Success(new PaymentSession(TransId1, Redirect1)));

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken());

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/payment-session", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CreatePaymentSessionResponse>();
        body.Should().NotBeNull();
        body!.PaymentProviderRef.Should().Be(TransId1);
        body.RedirectUrl.Should().Be(Redirect1);

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == OrderId);
        row.Should().NotBeNull();
        row!.PaymentProviderRef.Should().Be(TransId1);
        row.PaymentRedirectUrl.Should().Be(Redirect1);
        row.State.Should().Be(OrderState.PendingPayment, "state stays put — webhook owns the transition");
    }

    [Fact]
    public async Task POST_without_bearer_token_returns_401()
    {
        await SeedAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/payment-session", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _provider.CreatePaymentCallCount.Should().Be(0);
    }

    [Fact]
    public async Task POST_for_other_customers_order_returns_404_OrderNotFound()
    {
        await SeedAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(OtherCustomerUserId));

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/payment-session", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"\"code\":\"{BusinessErrorMessage.OrderNotFound}\"");
        _provider.CreatePaymentCallCount.Should().Be(0);
    }

    [Fact]
    public async Task POST_on_Paid_order_returns_409_OrderInvalidStateForPayment()
    {
        await SeedAsync(orderState: OrderState.Paid);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken());

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/payment-session", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"\"code\":\"{BusinessErrorMessage.OrderInvalidStateForPayment}\"");
    }

    [Fact]
    public async Task POST_when_provider_unavailable_returns_503_with_typed_code()
    {
        await SeedAsync();
        _provider.EnqueueCreatePayment(
            BusinessResult.Failure<PaymentSession>(
                Error.Transient(BusinessErrorMessage.PaymentProviderUnavailable)));

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken());

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/payment-session", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"\"code\":\"{BusinessErrorMessage.PaymentProviderUnavailable}\"");
    }

    [Fact]
    public async Task POST_twice_within_window_serves_cached_URL_without_second_CreatePayment_call()
    {
        await SeedAsync();
        _provider.EnqueueCreatePayment(
            BusinessResult.Success(new PaymentSession(TransId1, Redirect1)));
        // Second call: VerifyPayment returns Pending → handler returns cached URL.
        _provider.EnqueueVerifyPayment(
            BusinessResult.Success(new PaymentStatus(PaymentState.Pending, null, null)));

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken());

        var first = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/payment-session", content: null);
        var second = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/payment-session", content: null);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstBody = await first.Content.ReadFromJsonAsync<CreatePaymentSessionResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<CreatePaymentSessionResponse>();
        secondBody!.PaymentProviderRef.Should().Be(firstBody!.PaymentProviderRef);
        secondBody.RedirectUrl.Should().Be(firstBody.RedirectUrl);

        _provider.CreatePaymentCallCount.Should().Be(1, "second call must serve cached URL");
        _provider.VerifyPaymentCallCount.Should().Be(1);
    }

    [Fact]
    public async Task POST_after_provider_session_Cancelled_recreates_and_overwrites_ref()
    {
        await SeedAsync(existingPaymentRef: TransId1, existingRedirectUrl: Redirect1);
        // Verify says the prior session is Cancelled → handler recreates.
        _provider.EnqueueVerifyPayment(
            BusinessResult.Success(new PaymentStatus(PaymentState.Cancelled, null, null)));
        _provider.EnqueueCreatePayment(
            BusinessResult.Success(new PaymentSession(TransId2, Redirect2)));

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken());

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/payment-session", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CreatePaymentSessionResponse>();
        body!.PaymentProviderRef.Should().Be(TransId2);
        body.RedirectUrl.Should().Be(Redirect2);

        _provider.VerifyPaymentCallCount.Should().Be(1);
        _provider.CreatePaymentCallCount.Should().Be(1);

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == OrderId);
        row!.PaymentProviderRef.Should().Be(TransId2, "ref must be overwritten with the new session");
        row.PaymentRedirectUrl.Should().Be(Redirect2);
    }
}
