using FluentAssertions;
using MediatR;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Numbering;
using Makables.Infra.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
}
