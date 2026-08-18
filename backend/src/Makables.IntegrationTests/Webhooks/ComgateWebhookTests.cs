using System.Net;
using FluentAssertions;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Observability;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Payments;
using Makables.Core.Domain.Products;
using Makables.Infra.Database;
using Makables.IntegrationTests.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AddressEntity = Makables.Core.Domain.Addresses.Address;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.IntegrationTests.Webhooks;

/// <summary>
/// End-to-end coverage for the T-0066 Public-host Comgate webhook
/// endpoint (<c>POST /api/v1/public/webhooks/comgate</c>). Real Postgres
/// (via <see cref="PostgresHarness"/>) + a <see cref="FakeComgatePaymentProvider"/>
/// injected over the keyed Comgate registration so the
/// <see cref="IPaymentProvider.ParseAndVerifyWebhookAsync"/> scripted
/// responses traverse the real controller + Mediator pipeline + UoW
/// commit.
///
/// <para>
/// IP allowlist: the harness configures <c>WebhookAllowedIps</c> to
/// include the TestServer loopback addresses so the happy path runs;
/// the "disallowed IP" test uses an empty list which the filter
/// rejects unconditionally.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ComgateWebhookTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";

    private const string CustomerUserId = "user-customer-1";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string AddressId = "addr-1";
    private const string CategoryId = "cat-1";
    private const string ProductId = "prod-1";
    private const string OrderId = "ord-1";

    private const string TransId1 = "AB1C-D34E";
    private const string Redirect1 = "https://payments.comgate.test/pay/AB1C-D34E";

    /// <summary>
    /// T-0067: the Comgate-authoritative PAID timestamp returned via
    /// VerifyPaymentAsync. The fake provider passes this through into
    /// the WebhookPayload; the controller forwards it to MarkOrderPaid;
    /// the handler persists it as <c>Order.PaidAt</c> (NOT the test
    /// clock's UtcNow). The happy-path test asserts the persisted value
    /// equals this constant.
    /// </summary>
    private static readonly DateTimeOffset PaidAtFromProvider =
        new DateTimeOffset(2026, 6, 5, 9, 59, 30, TimeSpan.Zero);

    /// <summary>
    /// Synthetic remote IP the test middleware stamps on every request
    /// so the IP allowlist filter has something to match against — by
    /// default <c>WebApplicationFactory</c>'s TestServer leaves
    /// <c>Connection.RemoteIpAddress</c> null. This matches a documented
    /// Comgate webhook source range.
    /// </summary>
    private const string SyntheticRemoteIp = "203.0.113.5";
    private static readonly string[] LoopbackAllowlist =
    {
        SyntheticRemoteIp + "/32",
    };

    private readonly PostgresHarness _harness;
    private readonly FakeComgatePaymentProvider _provider = new();
    private WebApplicationFactory<Makables.Web.Public.Program> _factory = default!;

    /// <summary>Records every (provider, outcome) pair the controller emits.</summary>
    private readonly RecordingWebhookMetrics _webhookMetrics = new();

    public ComgateWebhookTests(PostgresHarness harness)
    {
        _harness = harness;
    }

    public Task InitializeAsync()
    {
        // Reset the malformed-entry dedupe set so the malformed-allowlist
        // test logs its Critical line on every run.
        Makables.Web.Public.Filters.ComgateWebhookIpAllowlistFilter
            .ResetLoggedMalformedEntriesForTesting();
        return ResetAndBuildFactory(LoopbackAllowlist);
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Truncates the DB and rebuilds the WebApplicationFactory with the
    /// given allowlist. The factory is stable for the lifetime of a
    /// single test; tests that need a custom allowlist call this with
    /// the override.
    /// </summary>
    private async Task ResetAndBuildFactory(string[] allowlist)
    {
        await _harness.ResetMutableTablesAsync();
        _factory?.Dispose();

        _factory = new WebApplicationFactory<Makables.Web.Public.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("IntegrationTest");

                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var dict = new Dictionary<string, string?>
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
                        // T-0070 PacketaOptions ValidateOnStart stubs.
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
                    };
                    for (var i = 0; i < allowlist.Length; i++)
                    {
                        dict[$"Comgate:WebhookAllowedIps:{i}"] = allowlist[i];
                    }
                    config.AddInMemoryCollection(dict);
                });

                builder.ConfigureServices(services =>
                {
                    // Swap Postgres registration to the harness container.
                    var dbContextDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<MakablesDbContext>));
                    if (dbContextDescriptor is not null)
                    {
                        services.Remove(dbContextDescriptor);
                    }
                    services.AddDbContext<MakablesDbContext>(o =>
                        o.UseNpgsql(_harness.ConnectionString));

                    // Replace the keyed "comgate" IPaymentProvider with
                    // our fake (same pattern as CreatePaymentSessionTests).
                    var keyed = services.Where(d =>
                        d.ServiceType == typeof(IPaymentProvider) &&
                        d.IsKeyedService &&
                        (d.ServiceKey as string) == "comgate").ToList();
                    foreach (var d in keyed)
                    {
                        services.Remove(d);
                    }
                    services.AddKeyedSingleton<IPaymentProvider>("comgate", _provider);

                    // T-0165: capture the ADR 0023 §4 ingest counter so the
                    // per-path outcome can be asserted. Recording is the
                    // point of the instrument — an under-counted reject path
                    // looks exactly like a provider that stopped calling.
                    var metricsDescriptors = services
                        .Where(d => d.ServiceType == typeof(IWebhookMetrics)).ToList();
                    foreach (var d in metricsDescriptors)
                    {
                        services.Remove(d);
                    }
                    services.AddSingleton<IWebhookMetrics>(_webhookMetrics);

                    // TestServer leaves Connection.RemoteIpAddress null
                    // by default; inject middleware that stamps the
                    // synthetic IP so the allowlist filter has something
                    // to match. Registered as an IStartupFilter so it
                    // runs before the routing/auth pipeline configured by
                    // UseMakablesPipeline.
                    services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(
                        new RemoteIpStartupFilter(SyntheticRemoteIp));
                });
            });
    }

    // === Seed helpers ===

    private async Task SeedOrderInStateAsync(
        OrderState state,
        string? paymentProviderRef = TransId1)
    {
        await using var db = _harness.CreateDbContext();
        var seedActor = "test-seed";
        var seedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        var customer = User.Create(
            id: CustomerUserId,
            email: "anna@example.cz",
            role: UserRole.Customer,
            fullName: "Anna",
            countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        customer.ConfirmEmail(seedAt);
        customer.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(customer);

        var makerUser = User.Create(
            id: MakerUserId,
            email: "maker@example.cz",
            role: UserRole.Maker,
            fullName: "Maker",
            countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        makerUser.ConfirmEmail(seedAt);
        makerUser.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(makerUser);

        var address = AddressEntity.Create(
            id: AddressId,
            street: "Pikrtova", houseNumber: "1737",
            city: "Praha", zip: "14000",
            countryCodeIso: CountryCode,
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
            id: ProductId, makerId: MakerId, categoryId: CategoryId,
            title: "Vase", description: null,
            price: new Money(50000, Currency),
            priceType: PriceType.Fixed,
            weightGrams: 300,
            countryCode: CountryCode);
        product.MarkCreated(seedActor, seedAt);
        db.Set<Product>().Add(product);

        var order = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId, makerId: MakerId, productId: ProductId,
            contactName: "Anna", contactEmail: "a@b.cz", contactPhone: "+420 723 456 789",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: Currency, vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42",
            countryCode: CountryCode);

        var fixedClock = new FixedClock(new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.Zero));
        if (paymentProviderRef is not null && state == OrderState.PendingPayment)
        {
            order.ReservePaymentSession(paymentProviderRef, Redirect1, fixedClock);
        }
        else if (paymentProviderRef is not null && state != OrderState.PendingPayment)
        {
            order.MarkAsPaid(fixedClock, paymentProviderRef);
            if (state == OrderState.Accepted) order.Accept(fixedClock);
            if (state == OrderState.Cancelled) throw new ArgumentException(
                "Cancelled-after-Paid is not a valid seed combination here; seed Cancelled separately.");
        }
        order.MarkCreated(seedActor, seedAt);
        db.Set<Order>().Add(order);

        await db.SaveChangesAsync();
    }

    private async Task SeedCancelledOrderAsync()
    {
        // Pre-payment cancellation path (Cancel from PendingPayment).
        await SeedOrderInStateAsync(OrderState.PendingPayment, paymentProviderRef: null);
        await using var db = _harness.CreateDbContext();
        var order = await db.Set<Order>().FirstAsync(o => o.Id == OrderId);
        order.Cancel(new FixedClock(new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static FormUrlEncodedContent BuildWebhookBody(
        string transId = TransId1,
        string refId = OrderId,
        string status = "PAID",
        string testFlag = "false")
    {
        return new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("transId", transId),
            new KeyValuePair<string, string>("refId", refId),
            new KeyValuePair<string, string>("status", status),
            new KeyValuePair<string, string>("test", testFlag),
        });
    }

    private const string WebhookPath = "/api/v1/public/webhooks/comgate";

    // === Tests ===

    [Fact]
    public async Task POST_happy_path_transitions_order_to_Paid()
    {
        await SeedOrderInStateAsync(OrderState.PendingPayment);
        _provider.EnqueueWebhook(BusinessResult.Success(
            new WebhookPayload(TransId1, PaymentState.Paid, "CARD_CZ", PaidAtFromProvider)));

        // Sanity: confirm the seed left the row with the expected ref so
        // the controller's GetByPaymentProviderRefAsync lookup will find it.
        await using (var seedCheck = _harness.CreateDbContext())
        {
            var seeded = await seedCheck.Set<Order>().AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == OrderId);
            seeded.Should().NotBeNull();
            seeded!.PaymentProviderRef.Should().Be(TransId1,
                "the seed must persist the providerRef for the webhook lookup to succeed");
        }

        using var client = _factory.CreateClient();
        var response = await client.PostAsync(WebhookPath, BuildWebhookBody());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _provider.WebhookCallCount.Should().Be(1, "the fake adapter must have been invoked");

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == OrderId);
        row.Should().NotBeNull();
        row!.State.Should().Be(OrderState.Paid);
        row.PaymentProviderRef.Should().Be(TransId1);

        // T-0067 — payment_method persisted from the WebhookPayload
        // (Comgate's label, not derived from anything we computed).
        row.PaymentMethod.Should().Be("CARD_CZ");

        // T-0067 — paid_at is the provider's authoritative timestamp,
        // NOT the test clock's UtcNow (the gap is intentional; the
        // provider-driven value is what the customer-facing invoice
        // shows).
        row.PaidAt.Should().Be(PaidAtFromProvider);

        // T-0067 + T-0068b — exactly three outbox events queued, both
        // keyed on the order id, with the documented event types. The
        // third (invoice.generate) lands per T-0068b locked decision 10.
        var outboxRows = await db.Set<Makables.Core.Domain.Outbox.OutboxEvent>()
            .AsNoTracking()
            .Where(e => e.AggregateId == OrderId)
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.EventType)
            .ToListAsync();
        outboxRows.Should().HaveCount(3);
        outboxRows.Select(e => e.EventType).Should().BeEquivalentTo(new[]
        {
            Makables.Core.Domain.Outbox.OutboxEventTypes.OrderPaidCustomerEmail,
            Makables.Core.Domain.Outbox.OutboxEventTypes.OrderPlacedMakerEmail,
            Makables.Core.Domain.Outbox.OutboxEventTypes.InvoiceGenerate,
        });

        // Payload JSONs deserialize cleanly (T-0067 ticket §"Tests").
        var customerRow = outboxRows.Single(e =>
            e.EventType == Makables.Core.Domain.Outbox.OutboxEventTypes.OrderPaidCustomerEmail);
        var customerPayload = System.Text.Json.JsonSerializer
            .Deserialize<Makables.Core.Domain.Outbox.OrderPaidCustomerEmailPayload>(
                customerRow.PayloadJson);
        customerPayload.Should().NotBeNull();
        customerPayload!.OrderId.Should().Be(OrderId);
        customerPayload.OrderNumber.Should().Be("M-CZ-20260042");
        customerPayload.Email.Should().Be("a@b.cz");
        customerPayload.ActionUrl.Should().Be($"https://makables.test/objednavka/{OrderId}");

        var makerRow = outboxRows.Single(e =>
            e.EventType == Makables.Core.Domain.Outbox.OutboxEventTypes.OrderPlacedMakerEmail);
        var makerPayload = System.Text.Json.JsonSerializer
            .Deserialize<Makables.Core.Domain.Outbox.OrderPlacedMakerEmailPayload>(
                makerRow.PayloadJson);
        makerPayload.Should().NotBeNull();
        makerPayload!.OrderId.Should().Be(OrderId);
        makerPayload.MakerId.Should().Be(MakerId);
        makerPayload.MakerEmail.Should().Be("maker@example.cz");
        makerPayload.ActionUrl.Should().Be($"https://makables.test/dashboard/maker/objednavky/{OrderId}");
    }

    [Fact]
    public async Task POST_happy_path_persists_payment_method_and_provider_PaidAt()
    {
        // Focused T-0067 pin in addition to the broader happy-path test
        // above. Isolates "payment_method column is non-null" and
        // "paid_at == provider's PaidAt, not test clock" so a future
        // refactor of the bigger test still leaves the persistence
        // invariants explicitly under test.
        await SeedOrderInStateAsync(OrderState.PendingPayment);
        _provider.EnqueueWebhook(BusinessResult.Success(
            new WebhookPayload(TransId1, PaymentState.Paid, "BANK_CZ_RB", PaidAtFromProvider)));

        using var client = _factory.CreateClient();
        var response = await client.PostAsync(WebhookPath, BuildWebhookBody());

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == OrderId);
        row!.PaymentMethod.Should().Be("BANK_CZ_RB",
            "exact value from the WebhookPayload is persisted unchanged");
        row.PaidAt.Should().Be(PaidAtFromProvider,
            "Q1 — Comgate's PAID timestamp wins; not clock.UtcNow");
    }

    [Fact]
    public async Task POST_twice_idempotency_does_NOT_re_enqueue_outbox_events()
    {
        // T-0067 — companion to the existing idempotency test that asserts
        // state stability. This one pins the outbox table: a re-delivered
        // webhook MUST NOT double-enqueue the side-effect events. The
        // controller's IsAlreadyInTargetState short-circuit returns 200
        // BEFORE dispatching the Mediator command, so no second pair of
        // rows lands.
        await SeedOrderInStateAsync(OrderState.PendingPayment);
        _provider.EnqueueWebhook(BusinessResult.Success(
            new WebhookPayload(TransId1, PaymentState.Paid, "CARD_CZ", PaidAtFromProvider)));
        _provider.EnqueueWebhook(BusinessResult.Success(
            new WebhookPayload(TransId1, PaymentState.Paid, "CARD_CZ", PaidAtFromProvider)));

        using var client = _factory.CreateClient();
        var first = await client.PostAsync(WebhookPath, BuildWebhookBody());
        var second = await client.PostAsync(WebhookPath, BuildWebhookBody());

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var outboxRows = await db.Set<Makables.Core.Domain.Outbox.OutboxEvent>()
            .AsNoTracking()
            .Where(e => e.AggregateId == OrderId)
            .ToListAsync();
        outboxRows.Should().HaveCount(3,
            "still 3 rows total — the second webhook was a no-op idempotency short-circuit. " +
            "T-0068b locked decision 10 bumps the count from 2 (T-0067) to 3 by adding " +
            "invoice.generate; idempotency math holds.");
    }

    [Fact]
    public async Task POST_from_disallowed_IP_returns_401_with_no_DB_writes()
    {
        // Empty allowlist rejects everything.
        await ResetAndBuildFactory(Array.Empty<string>());
        await SeedOrderInStateAsync(OrderState.PendingPayment);
        _provider.EnqueueWebhook(BusinessResult.Success(
            new WebhookPayload(TransId1, PaymentState.Paid, "CARD_CZ", PaidAtFromProvider)));

        using var client = _factory.CreateClient();
        var response = await client.PostAsync(WebhookPath, BuildWebhookBody());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _provider.WebhookCallCount.Should().Be(0, "filter rejects BEFORE the adapter parses the body");

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == OrderId);
        row!.State.Should().Be(OrderState.PendingPayment);
    }

    [Fact]
    public async Task POST_with_malformed_body_returns_400()
    {
        await SeedOrderInStateAsync(OrderState.PendingPayment);
        // Adapter returns PaymentWebhookMalformed → Validation → 400.
        _provider.EnqueueWebhook(BusinessResult.Failure<WebhookPayload>(
            Error.Validation("body", BusinessErrorMessage.PaymentWebhookMalformed)));

        using var client = _factory.CreateClient();
        var response = await client.PostAsync(WebhookPath, BuildWebhookBody());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(BusinessErrorMessage.PaymentWebhookMalformed);
    }

    [Fact]
    public async Task POST_with_unknown_transId_returns_200_no_DB_writes()
    {
        // No order is seeded with this providerRef; the controller logs
        // Critical and returns 200 per Q3.
        await SeedOrderInStateAsync(OrderState.PendingPayment, paymentProviderRef: "different-tx");
        _provider.EnqueueWebhook(BusinessResult.Success(
            new WebhookPayload(TransId1, PaymentState.Paid, "CARD_CZ", PaidAtFromProvider)));

        using var client = _factory.CreateClient();
        var response = await client.PostAsync(WebhookPath, BuildWebhookBody());

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == OrderId);
        row!.State.Should().Be(OrderState.PendingPayment);
    }

    [Fact]
    public async Task POST_with_refId_mismatch_returns_401_no_DB_writes()
    {
        // Seed an order with PaymentProviderRef = TransId1 and Id = OrderId.
        // Body says refId = "ord-spoof"; the controller compares
        // order.Id ("ord-1") vs body.refId ("ord-spoof") and refuses.
        await SeedOrderInStateAsync(OrderState.PendingPayment);
        _provider.EnqueueWebhook(BusinessResult.Success(
            new WebhookPayload(TransId1, PaymentState.Paid, "CARD_CZ", PaidAtFromProvider)));

        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            WebhookPath, BuildWebhookBody(refId: "ord-spoof"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(BusinessErrorMessage.PaymentWebhookRefIdMismatch);

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == OrderId);
        row!.State.Should().Be(OrderState.PendingPayment);
    }

    // ---- T-0165 (Q-0033): ADR 0023 §4 ingest counter ----

    [Fact]
    public async Task Accepted_delivery_is_counted_once_as_accepted()
    {
        await SeedOrderInStateAsync(OrderState.PendingPayment);
        _provider.EnqueueWebhook(BusinessResult.Success(
            new WebhookPayload(TransId1, PaymentState.Paid, "CARD_CZ", PaidAtFromProvider)));

        using var client = _factory.CreateClient();
        await client.PostAsync(WebhookPath, BuildWebhookBody());

        _webhookMetrics.Records.Should().ContainSingle()
            .Which.Should().Be(("comgate", WebhookOutcome.Accepted));
    }

    [Fact]
    public async Task Idempotent_re_delivery_is_counted_as_duplicate_not_accepted()
    {
        // The distinction the operator actually needs: a retry storm shows as
        // duplicates, a real payment flow shows as accepted.
        await SeedOrderInStateAsync(OrderState.PendingPayment);
        _provider.EnqueueWebhook(BusinessResult.Success(
            new WebhookPayload(TransId1, PaymentState.Paid, "CARD_CZ", PaidAtFromProvider)));
        _provider.EnqueueWebhook(BusinessResult.Success(
            new WebhookPayload(TransId1, PaymentState.Paid, "CARD_CZ", PaidAtFromProvider)));

        using var client = _factory.CreateClient();
        await client.PostAsync(WebhookPath, BuildWebhookBody());
        await client.PostAsync(WebhookPath, BuildWebhookBody());

        _webhookMetrics.Records.Should().Equal(
            ("comgate", WebhookOutcome.Accepted),
            ("comgate", WebhookOutcome.Duplicate));
    }

    [Fact]
    public async Task Unknown_provider_ref_is_counted_as_rejected_despite_the_200()
    {
        // Returns 200 by design (no retry storm), so the HTTP status alone
        // cannot tell an operator this happened — the counter is the signal.
        await SeedCancelledOrderAsync();
        _provider.EnqueueWebhook(BusinessResult.Success(
            new WebhookPayload(TransId1, PaymentState.Paid, "CARD_CZ", PaidAtFromProvider)));

        using var client = _factory.CreateClient();
        var response = await client.PostAsync(WebhookPath, BuildWebhookBody());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _webhookMetrics.Records.Should().ContainSingle()
            .Which.Should().Be(("comgate", WebhookOutcome.Rejected));
    }

    [Fact]
    public async Task POST_twice_idempotency_short_circuits_second_call()
    {
        await SeedOrderInStateAsync(OrderState.PendingPayment);
        _provider.EnqueueWebhook(BusinessResult.Success(
            new WebhookPayload(TransId1, PaymentState.Paid, "CARD_CZ", PaidAtFromProvider)));
        _provider.EnqueueWebhook(BusinessResult.Success(
            new WebhookPayload(TransId1, PaymentState.Paid, "CARD_CZ", PaidAtFromProvider)));

        using var client = _factory.CreateClient();
        var first = await client.PostAsync(WebhookPath, BuildWebhookBody());
        var second = await client.PostAsync(WebhookPath, BuildWebhookBody());

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == OrderId);
        row!.State.Should().Be(OrderState.Paid, "still Paid — second call was an idempotent no-op");
    }

    [Fact]
    public async Task POST_when_order_already_Cancelled_returns_200_no_transition()
    {
        await SeedCancelledOrderAsync();
        // Cancelled order has no PaymentProviderRef; the controller will
        // try to look up the order by TransId1 and find null → 200 + Critical.
        _provider.EnqueueWebhook(BusinessResult.Success(
            new WebhookPayload(TransId1, PaymentState.Paid, "CARD_CZ", PaidAtFromProvider)));

        using var client = _factory.CreateClient();
        var response = await client.PostAsync(WebhookPath, BuildWebhookBody());

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == OrderId);
        row!.State.Should().Be(OrderState.Cancelled, "no transition — webhook found nothing actionable");
    }

    // T-0083 review fold (SecOps Gate 3 check 7): the pay-after-auto-
    // cancel race. The order was cancelled by the 02:00 UTC sweep AFTER a
    // payment session was reserved; Comgate then delivers PAID. Contract:
    // 200 (stop the retries — a 4xx would only re-deliver the same
    // refusal), the order STAYS Cancelled (MarkAsPaid state guard), and
    // the controller logs the refund liability at Warning level.
    [Fact]
    public async Task POST_PAID_for_cancelled_order_with_providerRef_returns_200_and_stays_Cancelled()
    {
        // Reserve the payment session first so the providerRef lookup
        // resolves the order, then auto-cancel it (the T-0083 sweep path).
        await SeedOrderInStateAsync(OrderState.PendingPayment, paymentProviderRef: TransId1);
        await using (var seedDb = _harness.CreateDbContext())
        {
            var seeded = await seedDb.Set<Order>().FirstAsync(o => o.Id == OrderId);
            seeded.Cancel(
                new FixedClock(new DateTimeOffset(2026, 6, 6, 2, 0, 0, TimeSpan.Zero)),
                OrderCancellationSource.AutoExpiry);
            await seedDb.SaveChangesAsync();
        }

        _provider.EnqueueWebhook(BusinessResult.Success(
            new WebhookPayload(TransId1, PaymentState.Paid, "CARD_CZ", PaidAtFromProvider)));

        using var client = _factory.CreateClient();
        var response = await client.PostAsync(WebhookPath, BuildWebhookBody());

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "idempotency contract — Comgate retries must stop even though the transition was refused");

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == OrderId);
        row!.State.Should().Be(OrderState.Cancelled, "MarkAsPaid refuses Cancelled → Paid");
        row.CancellationSource.Should().Be(OrderCancellationSource.AutoExpiry);
        row.PaidAt.Should().BeNull("the refused webhook must not stamp a payment timestamp");
    }

    [Fact]
    public async Task POST_when_adapter_returns_Transient_propagates_as_503()
    {
        await SeedOrderInStateAsync(OrderState.PendingPayment);
        _provider.EnqueueWebhook(BusinessResult.Failure<WebhookPayload>(
            Error.Transient(BusinessErrorMessage.PaymentProviderUnavailable)));

        using var client = _factory.CreateClient();
        var response = await client.PostAsync(WebhookPath, BuildWebhookBody());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task POST_when_adapter_returns_Configuration_propagates_as_500()
    {
        await SeedOrderInStateAsync(OrderState.PendingPayment);
        _provider.EnqueueWebhook(BusinessResult.Failure<WebhookPayload>(
            Error.Configuration(BusinessErrorMessage.PaymentProviderMisconfigured)));

        using var client = _factory.CreateClient();
        var response = await client.PostAsync(WebhookPath, BuildWebhookBody());

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    /// <summary>
    /// <see cref="IStartupFilter"/> that stamps a synthetic remote IP on
    /// every request. Required because the TestServer leaves
    /// <c>Connection.RemoteIpAddress</c> null, which the allowlist filter
    /// (correctly) rejects.
    /// </summary>
    private sealed class RemoteIpStartupFilter(string syntheticIp) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (ctx, nxt) =>
                {
                    ctx.Connection.RemoteIpAddress = IPAddress.Parse(syntheticIp);
                    await nxt();
                });
                next(app);
            };
    }

    [Fact]
    public async Task POST_with_Cancelled_payload_state_returns_200_no_transition()
    {
        // T-0066 doesn't action Cancelled payload (T-0083 territory).
        // Order stays in PendingPayment; controller logs Information.
        await SeedOrderInStateAsync(OrderState.PendingPayment);
        _provider.EnqueueWebhook(BusinessResult.Success(
            new WebhookPayload(TransId1, PaymentState.Cancelled, null, null)));

        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            WebhookPath, BuildWebhookBody(status: "CANCELLED"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == OrderId);
        row!.State.Should().Be(OrderState.PendingPayment);
    }
    /// <summary>
    /// Test double for <see cref="IWebhookMetrics"/> that keeps what was
    /// recorded. A substitute would do, but this reads better in the
    /// assertions and makes "exactly one record per delivery" checkable.
    /// </summary>
    private sealed class RecordingWebhookMetrics : IWebhookMetrics
    {
        private readonly List<(string Provider, string Outcome)> _records = [];

        public IReadOnlyList<(string Provider, string Outcome)> Records => _records;

        public void RecordReceived(string provider, string outcome) =>
            _records.Add((provider, outcome));
    }

}
