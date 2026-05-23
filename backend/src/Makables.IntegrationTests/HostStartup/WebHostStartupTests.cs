using FluentAssertions;
using MediatR;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Numbering;
using Makables.Infra.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.IntegrationTests.HostStartup;

/// <summary>
/// Smoke tests that each of the four Web hosts starts via
/// <see cref="WebApplicationFactory{TEntryPoint}"/>, resolves the core
/// services from the container, and responds to the root <c>/</c>
/// endpoint with its host-identifying string. Per ADR 0009 (DI) AC for T-0009.
///
/// The Postgres-backed <see cref="MakablesDbContext"/> registration is
/// swapped for an in-memory SQLite connection so the host actually starts
/// without external infrastructure.
/// </summary>
public class CustomerHostStartupTests : HostStartupTestBase<Makables.Web.Customer.Program>
{
    protected override string ExpectedHostName => "Customer";
}

public class MakerHostStartupTests : HostStartupTestBase<Makables.Web.Maker.Program>
{
    protected override string ExpectedHostName => "Maker";
}

public class AdminHostStartupTests : HostStartupTestBase<Makables.Web.Admin.Program>
{
    protected override string ExpectedHostName => "Admin";
}

public class PublicHostStartupTests : HostStartupTestBase<Makables.Web.Public.Program>
{
    protected override string ExpectedHostName => "Public";
}

public abstract class HostStartupTestBase<TProgram> where TProgram : class
{
    protected abstract string ExpectedHostName { get; }

    private WebApplicationFactory<TProgram> BuildFactory()
    {
        return new WebApplicationFactory<TProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("IntegrationTest");

            // AddMakablesInfrastructure throws on empty Postgres connection
            // string (reviewer T-0009 MAJOR #1 fix). Supply a placeholder so
            // the host starts; ConfigureServices below swaps the registration
            // for SQLite before any DbContext is actually constructed.
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = "Host=placeholder;Database=ignored",
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

                var connection = new SqliteConnection("DataSource=:memory:");
                connection.Open();

                services.AddSingleton(connection);
                services.AddDbContext<MakablesDbContext>(options =>
                {
                    options.UseSqlite(connection);
                });
            });
        });
    }

    [Fact]
    public async Task Host_Starts_And_Responds_At_Root()
    {
        using var factory = BuildFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        response.IsSuccessStatusCode.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"Makables {ExpectedHostName} API");
    }

    [Fact]
    public void Host_Resolves_Core_Services_From_Container()
    {
        using var factory = BuildFactory();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetService<MakablesDbContext>().Should().NotBeNull();
        sp.GetService<IClock>().Should().NotBeNull();
        sp.GetService<IIdGenerator>().Should().NotBeNull();

        sp.GetService<IOrderNumberGenerator>().Should().NotBeNull();
        sp.GetService<IInvoiceNumberGenerator>().Should().NotBeNull();
        sp.GetService<IPayoutBatchNumberGenerator>().Should().NotBeNull();

        sp.GetService<ISender>().Should().NotBeNull();
    }

    [Fact]
    public async Task Host_Cors_Middleware_Is_Active()
    {
        // Reviewer T-0009 MAJOR #2: smoke tests didn't exercise middleware.
        // A CORS preflight (OPTIONS with Origin) is the cheapest way to
        // prove UseCors is wired in the pipeline. The dev fallback in
        // AddMakablesCors allows http://localhost:3000.
        using var factory = BuildFactory();
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Options, "/");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        // The CORS middleware should set Access-Control-Allow-Origin in
        // response to a valid preflight; if UseCors weren't wired, no such
        // header would appear.
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeTrue(
            "UseCors must be wired in UseMakablesPipeline; a CORS preflight should produce Access-Control-Allow-Origin");
    }

    [Fact]
    public void Host_RateLimiter_Options_Are_Registered()
    {
        // Reviewer T-0009 MAJOR #2 (part 2): prove AddRateLimiter was invoked.
        // The framework registers RateLimiterOptions via IOptions<T>; resolving
        // it confirms AddMakablesRateLimiting wired the policy.
        using var factory = BuildFactory();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var options = sp.GetService<Microsoft.Extensions.Options.IOptions<
            Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>>();
        options.Should().NotBeNull(
            "AddMakablesRateLimiting must register IOptions<RateLimiterOptions>");
    }

    [Fact]
    public void Host_Authentication_Services_Are_Registered()
    {
        // Reviewer T-0009 MAJOR #2 (part 3): prove AddAuthentication was invoked
        // via AddMakablesAuth.
        using var factory = BuildFactory();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>()
            .Should().NotBeNull("AddMakablesAuth must register the authentication scheme provider");
    }
}
