using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Privacy;
using Makables.Core.Domain.Products;
using Makables.Core.Domain.Reviews;
using Makables.Infra.Common.Auth;
using Makables.Infra.Database;
using Makables.Infra.Database.Privacy;
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

namespace Makables.IntegrationTests.Users;

/// <summary>
/// End-to-end coverage for the T-0110 GDPR hard-delete
/// (<c>POST /api/v1/users/{id}/erase</c>) — the only irreversible op in the
/// system. Pins the FULL erasure matrix: user gone, orders anonymized,
/// review anonymized, maker anonymized (IČO + bank retained, flag set),
/// refresh tokens gone, unreferenced address gone, INVOICE UNTOUCHED, audit
/// row survives. Plus: in-flight blocks (409, nothing mutated), retype
/// mismatch blocks (409), re-call resolves to user.notFound (no
/// Silent-Success).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DeleteUserPermanentlyIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CZ = "CZ";
    private const string Currency = "CZK";

    private const string AdminUserId = "user-admin-1";
    private const string TargetUserId = "user-maker-1";
    private const string TargetEmail = "maker@example.cz";
    private const string MakerId = "maker-1";
    private const string MakerSeatAddressId = "addr-seat";
    private const string UserAddressId = "addr-user";

    private static readonly DateTimeOffset SeedAt = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Admin.Program> _factory = default!;

    public DeleteUserPermanentlyIntegrationTests(PostgresHarness harness) => _harness = harness;

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

    private static IClock FixedClock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.Zero));
        return clock;
    }

    /// <param name="inFlight">When true, the maker-user's order stays in Paid (blocks the erase).</param>
    /// <param name="disputed">When true, the maker-user's order is moved to Disputed (blocks the erase).</param>
    private async Task SeedAsync(bool inFlight = false, bool disputed = false)
    {
        await using var db = _harness.CreateDbContext();
        const string actor = "test-seed";
        var clock = FixedClock();

        var admin = User.Create(AdminUserId, "admin@makables.cz", UserRole.Admin, "Admin", CZ,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        admin.ConfirmEmail(SeedAt); admin.MarkCreated(actor, SeedAt);
        db.Set<User>().Add(admin);

        var customer = User.Create("user-cust-1", "anna@example.cz", UserRole.Customer, "Anna", CZ,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        customer.ConfirmEmail(SeedAt); customer.MarkCreated(actor, SeedAt);
        db.Set<User>().Add(customer);

        // The target is a maker-user (authored a review too, as customer).
        var target = User.Create(TargetUserId, TargetEmail, UserRole.Maker, "Maker", CZ,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        target.ConfirmEmail(SeedAt); target.MarkCreated(actor, SeedAt);
        db.Set<User>().Add(target);

        // The maker legal-seat address (referenced → must stay).
        var seat = AddressEntity.Create(MakerSeatAddressId, "Pikrtova", "1737", "Praha", "14000", CZ, CZ);
        seat.MarkCreated(actor, SeedAt);
        db.Set<AddressEntity>().Add(seat);

        // An address CREATED BY the target user, unreferenced (→ deleted).
        var userAddr = AddressEntity.Create(UserAddressId, "Dlouhá", "5", "Brno", "60200", CZ, CZ);
        userAddr.MarkCreated(TargetUserId, SeedAt);
        db.Set<AddressEntity>().Add(userAddr);

        var maker = MakerEntity.Create(MakerId, TargetUserId, "27074358", "CZ27074358",
            "Avast s.r.o.", "s.r.o.", MakerSeatAddressId, null, true, "ares", SeedAt, false, CZ, "avast");
        maker.UpdateProfile(bio: "Tiskneme od 2010", bankAccount: "123456789/0100",
            personalPickupEnabled: false, pickupNote: null);
        maker.MarkCreated(actor, SeedAt);
        db.Set<MakerEntity>().Add(maker);

        var category = Category.Create("cat-1", "3D tisk", "3d-tisk", null, null, 10, CZ);
        category.MarkCreated(actor, SeedAt);
        db.Set<Category>().Add(category);

        var product = Product.Create("prod-1", MakerId, "cat-1", "Vase", null,
            new Money(50000, Currency), PriceType.Fixed, 300, CZ);
        product.MarkCreated(actor, SeedAt);
        db.Set<Product>().Add(product);

        // The target user is the CUSTOMER on these orders (their contact
        // snapshot gets anonymized).
        Order BuildOrder(string id, string number)
        {
            var o = Order.Create(id, number, TargetUserId, MakerId, "prod-1",
                "Maker Person", TargetEmail, "+420 723 456 789",
                50000, 7900, 7500, 50400, 57900, Currency, 2100,
                ShippingMethod.ZasilkovnaPickupPoint, "pp-42", CZ);
            o.MarkAsPaid(clock, $"TR-{id}");
            o.MarkCreated(actor, SeedAt);
            return o;
        }

        var delivered = BuildOrder("ord-delivered", "M-CZ-20260001");
        if (disputed)
        {
            // Disputed: escrowed money + an unresolved dispute → blocks the erase.
            delivered.OpenDispute(clock);
        }
        else if (!inFlight)
        {
            delivered.Accept(clock);
            delivered.Ship(clock, "PKT-1", autoDeliverWindowDays: 7, trackingUrl: null);
            delivered.MarkAsDelivered(clock, OrderDeliverySource.Customer);
        }
        db.Set<Order>().Add(delivered);

        var cancelled = BuildOrder("ord-cancelled", "M-CZ-20260002");
        cancelled.Cancel(clock, OrderCancellationSource.Customer);
        db.Set<Order>().Add(cancelled);

        // A review authored by the target user.
        var review = Review.Create("rev-1", "ord-delivered", MakerId, TargetUserId, 5,
            "Skvělá kvalita.", CZ);
        review.MarkCreated(actor, SeedAt);
        db.Set<Review>().Add(review);

        // Two refresh tokens for the target.
        for (var i = 0; i < 2; i++)
        {
            var rt = RefreshToken.IssueNew($"rt-{i}", TargetUserId, $"hash-{i}", $"fam-{i}",
                SeedAt.AddDays(30), CZ, userAgent: null, ipAddress: null);
            rt.MarkCreated(actor, SeedAt);
            db.Set<RefreshToken>().Add(rt);
        }

        // Two one-time tokens for the target (magic-link + reset) carrying an
        // IpAddress — PII residue that erasure must purge (SecOps M-1).
        var magicLink = OneTimeToken.Issue("ott-hash-magic", TargetUserId,
            OneTimeTokenPurpose.MagicLink, SeedAt.AddMinutes(15), SeedAt, ipAddress: "203.0.113.7");
        db.Set<OneTimeToken>().Add(magicLink);
        var reset = OneTimeToken.Issue("ott-hash-reset", TargetUserId,
            OneTimeTokenPurpose.PasswordReset, SeedAt.AddMinutes(15), SeedAt, ipAddress: "203.0.113.8");
        db.Set<OneTimeToken>().Add(reset);

        // A login-attempt bucket keyed by the target's normalized email —
        // orphaned PII (email PK) once the user row is gone (SecOps M-2).
        var bucket = LoginAttemptBucket.Create(User.NormalizeEmail(TargetEmail), SeedAt);
        db.Set<LoginAttemptBucket>().Add(bucket);

        // An issued invoice for the order (retained untouched).
        var invoice = Invoice.Issue("inv-1", "FV-CZ-20260001", InvoiceType.Customer, "ord-delivered", null,
            MakerId, "JVM YORE s.r.o.", "12345678", null, null, "Maker Person", TargetEmail,
            null, null, new DateOnly(2026, 5, 1), null, new DateOnly(2026, 5, 15),
            InvoicingMode.None, 57900, 0, 0, 57900, Currency, CZ);
        invoice.MarkCreated(actor, SeedAt);
        db.Set<Invoice>().Add(invoice);

        await db.SaveChangesAsync();
    }

    private static string IssueAdminToken()
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(AdminUserId, "admin@makables.cz", UserRole.Admin, "Admin", CZ,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(SeedAt);
        return issuer.Issue(user, MakablesAudiences.Admin, DateTimeOffset.UtcNow).Token;
    }

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueAdminToken());
        return client;
    }

    [Fact]
    public async Task POST_erase_runs_full_matrix_user_gone_orders_anonymized_invoices_intact()
    {
        await SeedAsync();
        using var client = CreateAdminClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/users/{TargetUserId}/erase",
            new { confirmedEmail = TargetEmail, reason = "GDPR-2026-014" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();

        // User gone (even under IgnoreQueryFilters).
        (await db.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.Id == TargetUserId))
            .Should().BeFalse();

        // Orders anonymized; pricing untouched.
        var orders = await db.Set<Order>().IgnoreQueryFilters()
            .Where(o => o.CustomerUserId == TargetUserId).ToListAsync();
        orders.Should().HaveCount(2);
        orders.Should().OnlyContain(o =>
            o.ContactName == "Anonymized" && o.ContactEmail == "Anonymized" && o.ContactPhone == "Anonymized");
        orders.Should().OnlyContain(o => o.TotalAmountMinor == 57900);

        // Review author anonymized; content kept.
        var review = await db.Set<Review>().IgnoreQueryFilters().FirstAsync(r => r.Id == "rev-1");
        review.CustomerUserId.Should().Be("Anonymized");
        review.Rating.Should().Be((short)5);
        review.Body.Should().Be("Skvělá kvalita.");

        // Maker anonymized; IČO + bank retained; flag set.
        var maker = await db.Set<MakerEntity>().IgnoreQueryFilters().FirstAsync(m => m.Id == MakerId);
        maker.CompanyName.Should().Be("Anonymized");
        maker.RegistrationNumber.Should().Be("27074358");
        maker.BankAccount.Should().Be("123456789/0100");
        maker.IsRetainedForLegal.Should().BeTrue();

        // Refresh tokens gone.
        (await db.Set<RefreshToken>().IgnoreQueryFilters().AnyAsync(rt => rt.UserId == TargetUserId))
            .Should().BeFalse();

        // One-time tokens gone (UserId + IpAddress PII purged — SecOps M-1).
        (await db.Set<OneTimeToken>().AnyAsync(t => t.UserId == TargetUserId))
            .Should().BeFalse();

        // Login-attempt bucket gone (email-PK PII purged — SecOps M-2).
        (await db.Set<LoginAttemptBucket>().AnyAsync(b => b.Id == User.NormalizeEmail(TargetEmail)))
            .Should().BeFalse();

        // Unreferenced user address gone; the maker legal-seat address stays.
        (await db.Set<AddressEntity>().IgnoreQueryFilters().AnyAsync(a => a.Id == UserAddressId))
            .Should().BeFalse();
        (await db.Set<AddressEntity>().IgnoreQueryFilters().AnyAsync(a => a.Id == MakerSeatAddressId))
            .Should().BeTrue();

        // Invoice byte-for-byte unchanged.
        var invoice = await db.Set<Invoice>().IgnoreQueryFilters().FirstAsync(i => i.Id == "inv-1");
        invoice.RecipientName.Should().Be("Maker Person");
        invoice.RecipientEmail.Should().Be(TargetEmail);
        invoice.AmountWithVatMinor.Should().Be(57900);

        // Audit row survives, referencing the now-deleted user id.
        var audit = await db.Set<AdminAuditLogEntry>().AsNoTracking()
            .Where(a => a.TargetId == TargetUserId && a.ActionCode == "user.erase").ToListAsync();
        audit.Should().HaveCount(1);
        audit[0].TargetEntity.Should().Be("user");
        audit[0].Notes.Should().Be("GDPR-2026-014");
        audit[0].AdminUserId.Should().Be(AdminUserId);
    }

    [Fact]
    public async Task In_flight_order_blocks_409_and_nothing_is_mutated()
    {
        await SeedAsync(inFlight: true);
        using var client = CreateAdminClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/users/{TargetUserId}/erase",
            new { confirmedEmail = TargetEmail, reason = "GDPR-2026-014" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString()
            .Should().Be(BusinessErrorMessage.UserCannotDeleteWithInFlightOrders);

        await using var db = _harness.CreateDbContext();
        (await db.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.Id == TargetUserId)).Should().BeTrue();
        (await db.Set<RefreshToken>().IgnoreQueryFilters().AnyAsync(rt => rt.UserId == TargetUserId))
            .Should().BeTrue();
        var order = await db.Set<Order>().IgnoreQueryFilters().FirstAsync(o => o.Id == "ord-delivered");
        order.ContactEmail.Should().Be(TargetEmail, "nothing was anonymized");
        (await db.Set<AdminAuditLogEntry>().AsNoTracking().AnyAsync(a => a.TargetId == TargetUserId))
            .Should().BeFalse("a blocked command writes no audit row");
    }

    [Fact]
    public async Task Disputed_order_blocks_409_and_nothing_is_mutated()
    {
        await SeedAsync(disputed: true);
        using var client = CreateAdminClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/users/{TargetUserId}/erase",
            new { confirmedEmail = TargetEmail, reason = "GDPR-2026-014" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString()
            .Should().Be(BusinessErrorMessage.UserCannotDeleteWithInFlightOrders);

        await using var db = _harness.CreateDbContext();
        // Order really is Disputed, and nothing was erased / anonymized.
        var order = await db.Set<Order>().IgnoreQueryFilters().FirstAsync(o => o.Id == "ord-delivered");
        order.State.Should().Be(OrderState.Disputed);
        order.ContactEmail.Should().Be(TargetEmail, "nothing was anonymized");
        (await db.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.Id == TargetUserId)).Should().BeTrue();
        (await db.Set<AdminAuditLogEntry>().AsNoTracking().AnyAsync(a => a.TargetId == TargetUserId))
            .Should().BeFalse("a blocked command writes no audit row");
    }

    [Fact]
    public async Task Forced_throw_mid_erasure_rolls_back_everything_user_and_data_intact()
    {
        await SeedAsync();

        // Replace the erasure seam with a decorator that runs the REAL matrix
        // (staging every anonymize + hard-delete in the request's tracked
        // context) then THROWS before returning — so the UoW pipeline never
        // commits. Proves the single-UoW all-or-nothing structurally: a
        // mid-matrix failure leaves the database byte-for-byte untouched.
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IUserDataDeletionService));
                services.Remove(descriptor);
                services.AddScoped<IUserDataDeletionService>(sp =>
                    new ThrowingDeletionDecorator(
                        new UserDataDeletionService(sp.GetRequiredService<MakablesDbContext>())));
            }));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueAdminToken());

        // The injected throw aborts the request before the UoW pipeline reaches
        // SaveChangesAsync; the TestServer surfaces it to the caller. The point
        // is the DB state below: nothing committed.
        var act = async () => await client.PostAsJsonAsync(
            $"/api/v1/users/{TargetUserId}/erase",
            new { confirmedEmail = TargetEmail, reason = "GDPR-2026-014" });
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Injected mid-erasure failure*");

        await using var db = _harness.CreateDbContext();

        // User still present — no hard-delete committed.
        (await db.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.Id == TargetUserId)).Should().BeTrue();

        // Orders NOT anonymized — contact snapshots intact.
        var orders = await db.Set<Order>().IgnoreQueryFilters()
            .Where(o => o.CustomerUserId == TargetUserId).ToListAsync();
        orders.Should().OnlyContain(o => o.ContactEmail == TargetEmail);

        // Maker NOT anonymized.
        var maker = await db.Set<MakerEntity>().IgnoreQueryFilters().FirstAsync(m => m.Id == MakerId);
        maker.CompanyName.Should().Be("Avast s.r.o.");
        maker.IsRetainedForLegal.Should().BeFalse();

        // Credential infra NOT purged.
        (await db.Set<RefreshToken>().IgnoreQueryFilters().AnyAsync(rt => rt.UserId == TargetUserId))
            .Should().BeTrue();
        (await db.Set<OneTimeToken>().AnyAsync(t => t.UserId == TargetUserId)).Should().BeTrue();
        (await db.Set<LoginAttemptBucket>().AnyAsync(b => b.Id == User.NormalizeEmail(TargetEmail)))
            .Should().BeTrue();

        // Unreferenced address NOT deleted.
        (await db.Set<AddressEntity>().IgnoreQueryFilters().AnyAsync(a => a.Id == UserAddressId))
            .Should().BeTrue();

        // No audit row (the command never returned success).
        (await db.Set<AdminAuditLogEntry>().AsNoTracking().AnyAsync(a => a.TargetId == TargetUserId))
            .Should().BeFalse();
    }

    /// <summary>
    /// Runs the real erasure matrix (staging all mutations) then throws,
    /// forcing the UoW pipeline to abort before <c>SaveChangesAsync</c>.
    /// </summary>
    private sealed class ThrowingDeletionDecorator(UserDataDeletionService inner) : IUserDataDeletionService
    {
        public async Task<BusinessResult> EraseAsync(string userId, CancellationToken ct)
        {
            await inner.EraseAsync(userId, ct);
            throw new InvalidOperationException("Injected mid-erasure failure (rollback test).");
        }
    }

    [Fact]
    public async Task Retype_mismatch_blocks_409_no_mutation()
    {
        await SeedAsync();
        using var client = CreateAdminClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/users/{TargetUserId}/erase",
            new { confirmedEmail = "wrong@example.cz", reason = "GDPR-2026-014" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString()
            .Should().Be(BusinessErrorMessage.UserDeleteConfirmationMismatch);

        await using var db = _harness.CreateDbContext();
        (await db.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.Id == TargetUserId)).Should().BeTrue();
    }

    [Fact]
    public async Task Re_call_after_successful_erase_returns_404_and_first_audit_row_survives()
    {
        await SeedAsync();
        using var client = CreateAdminClient();

        var first = await client.PostAsJsonAsync(
            $"/api/v1/users/{TargetUserId}/erase",
            new { confirmedEmail = TargetEmail, reason = "GDPR-2026-014" });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.PostAsJsonAsync(
            $"/api/v1/users/{TargetUserId}/erase",
            new { confirmedEmail = TargetEmail, reason = "GDPR-2026-014" });

        second.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var doc = System.Text.Json.JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be(BusinessErrorMessage.UserNotFound);

        await using var db = _harness.CreateDbContext();
        (await db.Set<AdminAuditLogEntry>().AsNoTracking()
            .CountAsync(a => a.TargetId == TargetUserId && a.ActionCode == "user.erase"))
            .Should().Be(1, "the first erasure's audit row survives; the failed re-call writes none");
    }

    [Fact]
    public async Task Customer_JWT_is_rejected_on_the_admin_host()
    {
        await SeedAsync();
        using var client = _factory.CreateClient();
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer, SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var custUser = User.Create("user-cust-1", "anna@example.cz", UserRole.Customer, "Anna", CZ,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        custUser.ConfirmEmail(SeedAt);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", issuer.Issue(custUser, MakablesAudiences.Customer, DateTimeOffset.UtcNow).Token);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/users/{TargetUserId}/erase",
            new { confirmedEmail = TargetEmail, reason = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
