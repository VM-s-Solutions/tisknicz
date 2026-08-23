using FluentAssertions;
using Makables.Config.Extensions;
using Makables.Core.Domain.Common;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.Tests.Functions.Composition;

/// <summary>
/// Composition test for the <c>Makables.Functions</c> host per ADR 0020.
///
/// <para>
/// The Functions host deliberately does not register auth — a queue or
/// timer trigger has no inbound authenticated HTTP request — so it also
/// misses the <see cref="IUserSessionProvider"/> that
/// <c>AddMakablesAuth</c> supplies to the Web hosts. Every MediatR handler
/// that takes the provider then fails container validation, the isolated
/// worker exits (code 134) before indexing a single function, and the host
/// crash-loops. Because <b>all</b> transactional email ships through
/// <c>ProcessOutboxTimer</c> → <c>send-email</c> queue →
/// <c>SendEmailFunction</c>, a host that cannot boot means outbox rows pile
/// up unprocessed and <b>no email is ever sent</b> — with a perfectly valid
/// Resend key and a verified sender domain, and no error anywhere on the
/// Web hosts to point at the cause.
/// </para>
///
/// <para>
/// Every project reference and extension call below mirrors
/// <c>Makables.Functions/Program.cs</c>. If that file gains a registration,
/// mirror it here — the point of the test is that the graph the Functions
/// host actually builds resolves.
/// </para>
/// </summary>
public class FunctionsHostCompositionTests
{
    /// <summary>
    /// Minimum configuration for the host's <c>ValidateOnStart</c> options
    /// (Resend, SendGrid, Mapbox, Comgate, Packeta, blob storage, outbox
    /// queues, public app URLs). Values are placeholders — the test asserts
    /// the DI graph resolves, never that a provider is reachable.
    /// </summary>
    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] =
                    "Host=localhost;Port=5432;Database=makables_test;Username=postgres;Password=postgres",
                ["Jwt:Issuer"] = "https://makables.test",
                ["Jwt:Audience"] = "customer",
                ["Jwt:SigningKeyBase64"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                ["PublicAppUrls:WebBaseUrl"] = "http://localhost:3000",
                ["OutboxQueues:ConnectionString"] = "UseDevelopmentStorage=true",
                ["BlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                ["Resend:ApiKey"] = "re_test_stub",
                ["Resend:DefaultFromAddress"] = "no-reply@makables.test",
                ["SendGrid:ApiKey"] = "SG.test-stub",
                ["SendGrid:DefaultFromAddress"] = "no-reply@makables.test",
                ["Mapbox:AccessToken"] = "pk.test-stub",
                ["Comgate:MerchantId"] = "test-merchant",
                ["Comgate:Secret"] = "test-secret",
                ["Comgate:BaseUrl"] = "https://payments.comgate.cz",
                ["Packeta:ApiKey"] = "test-packeta-key",
                ["Packeta:PublicWidgetKey"] = "test-packeta-widget-key",
                ["Packeta:BaseUrl"] = "https://api.packeta.com",
                ["Packeta:SenderLabel"] = "makables-cz",
                ["Packeta:WidgetScriptUrl"] = "https://widget.packeta.com/v6/www/js/library.js",
            })
            .Build();

    /// <summary>Mirrors Makables.Functions/Program.cs.</summary>
    private static ServiceProvider BuildFunctionsHostProvider()
    {
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();

        services.AddMakablesInfrastructure(configuration);
        services.AddMakablesSystemSession();
        services.AddMakablesMediator();
        services.AddMakablesClients(configuration);
        services.AddMakablesBlobStorage(configuration);
        services.AddMakablesPdfRendering();

        // ValidateOnBuild is what the isolated worker's own container does;
        // running it here is what turns a crash-loop into a red test.
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    [Fact]
    public void FunctionsHostContainer_Builds_And_Validates()
    {
        var act = () => BuildFunctionsHostProvider().Dispose();

        act.Should().NotThrow(
            "the Functions host must resolve every handler it can dispatch — "
            + "a validation failure exits the isolated worker before any "
            + "function is indexed, silently stopping all outbox email");
    }

    [Fact]
    public void FunctionsHost_Resolves_UserSessionProvider_As_System_Actor()
    {
        using var provider = BuildFunctionsHostProvider();
        using var scope = provider.CreateScope();

        var session = scope.ServiceProvider.GetRequiredService<IUserSessionProvider>();

        session.Should().BeOfType<SystemUserSessionProvider>();
        session.GetUserId().Should().Be("system",
            "background work is audited as the platform, matching the "
            + "AuditableSaveChangesInterceptor fallback actor");
        session.GetUserEmail().Should().BeNull();
        session.GetUserCountryCode().Should().BeNull(
            "a queue/timer trigger takes country from the aggregate it "
            + "loaded, never from a caller");
    }

    [Fact]
    public void FunctionsHost_Resolves_The_Mediator_Used_By_Every_Trigger()
    {
        using var provider = BuildFunctionsHostProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ISender>().Should().NotBeNull();
    }
}
