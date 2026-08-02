using Makables.Core.Domain.Makers;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Registry;
using Makables.Infra.Database;
using Makables.IntegrationTests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AddressEntity = Makables.Core.Domain.Addresses.Address;

namespace Makables.IntegrationTests.Auth;

/// <summary>
/// End-to-end T-0162 coverage of <c>POST /api/v1/auth/register</c> on the
/// Customer host backed by the harness Postgres: model binding of the
/// optional <c>companyRegistrationNumber</c>, DI resolution of the
/// handler's <see cref="ICompanyRegistryFactory"/> dependency, and the
/// persisted company-snapshot columns on <c>users</c>. The registry
/// factory is stubbed at the DI layer — ARES is never touched; the
/// authoritative-lookup semantics themselves are pinned by
/// RegisterHandlerTests.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RegisterCompanyIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string ValidIco = "27074358";

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Customer.Program> _factory = default!;

    public RegisterCompanyIntegrationTests(PostgresHarness harness)
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

                    // T-0162: stub the registry factory so the register
                    // company branch resolves without ARES. The stub honors
                    // ValidIco only; anything else is NotFound.
                    var factoryDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(ICompanyRegistryFactory));
                    if (factoryDescriptor is not null)
                    {
                        services.Remove(factoryDescriptor);
                    }
                    services.AddScoped<ICompanyRegistryFactory, StubCompanyRegistryFactory>();
                });
            });
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Register_without_company_persists_null_snapshot()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "anna@example.cz",
            password = "abcd1234567",
            fullName = "Anna Nováková",
            countryCodePrimary = "CZ",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var user = await db.Set<User>()
            .SingleAsync(u => u.EmailNormalized == "anna@example.cz");
        user.CompanyRegistrationNumber.Should().BeNull();
        user.CompanyName.Should().BeNull();
        user.CompanyVatId.Should().BeNull();
        user.CompanySnapshotFetchedAt.Should().BeNull();
    }

    [Fact]
    public async Task Register_with_company_persists_registry_snapshot()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "firma@example.cz",
            password = "abcd1234567",
            fullName = "Firemní Anna",
            countryCodePrimary = "CZ",
            companyRegistrationNumber = ValidIco,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var user = await db.Set<User>()
            .SingleAsync(u => u.EmailNormalized == "firma@example.cz");
        user.CompanyRegistrationNumber.Should().Be(ValidIco);
        user.CompanyName.Should().Be("Avast Software s.r.o.");
        user.CompanyVatId.Should().Be("CZ27074358");
        user.CompanySnapshotFetchedAt.Should().Be(
            new DateTimeOffset(2026, 7, 25, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Register_with_malformed_ico_returns_400_and_creates_nothing()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "spatne@example.cz",
            password = "abcd1234567",
            fullName = "Anna Nováková",
            countryCodePrimary = "CZ",
            companyRegistrationNumber = "1234567",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = _harness.CreateDbContext();
        var exists = await db.Set<User>()
            .AnyAsync(u => u.EmailNormalized == "spatne@example.cz");
        exists.Should().BeFalse();
    }

    private sealed class StubCompanyRegistryFactory : ICompanyRegistryFactory
    {
        public Task<BusinessResult<ICompanyRegistry>> ResolveAsync(
            string countryCode, CancellationToken cancellationToken) =>
            Task.FromResult(BusinessResult.Success<ICompanyRegistry>(new StubCompanyRegistry()));
    }

    private sealed class StubCompanyRegistry : ICompanyRegistry
    {
        public string Code => "stub";

        public Task<BusinessResult<CompanyRecord>> LookupByRegistrationNumberAsync(
            string registrationNumber, CancellationToken cancellationToken)
        {
            if (registrationNumber != ValidIco)
            {
                return Task.FromResult(BusinessResult.Failure<CompanyRecord>(
                    new Error("registrationNumber", BusinessErrorMessage.CompanyNotFound, ErrorType.NotFound)));
            }

            return Task.FromResult(BusinessResult.Success(new CompanyRecord(
                RegistrationNumber: ValidIco,
                VatId: "CZ27074358",
                CompanyName: "Avast Software s.r.o.",
                LegalForm: "Společnost s ručením omezeným",
                LegalType: MakerLegalType.LegalEntity,
                RegisteredAddress: AddressEntity.Create(
                    id: $"ares-snapshot-{ValidIco}",
                    street: "Pikrtova", houseNumber: "1737", city: "Praha", zip: "14000",
                    countryCodeIso: "CZ", auditCountryCode: "CZ"),
                IncorporatedOn: new DateOnly(2006, 9, 4),
                IsActiveInRegistry: true,
                SourceRegistry: "stub",
                FetchedAt: new DateTimeOffset(2026, 7, 25, 10, 0, 0, TimeSpan.Zero),
                IsStale: false)));
        }
    }
}
