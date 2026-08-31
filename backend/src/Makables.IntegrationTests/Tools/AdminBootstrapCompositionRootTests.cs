using FluentAssertions;
using Makables.Tools.AdminBootstrap;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.IntegrationTests.Tools;

/// <summary>
/// Proves the bootstrap tool's container actually resolves.
///
/// <para>
/// This test exists because of a specific failure. The first version of the
/// tool never registered <c>IIdGenerator</c>, so it threw
/// <c>InvalidOperationException: Unable to resolve service for type
/// 'IIdGenerator' while attempting to activate 'AdminBootstrapper'</c> on
/// EVERY invocation — before any guard ran, before any database contact — while
/// its unit tests passed, because they constructed
/// <see cref="AdminBootstrapper"/> directly with substitutes. The behaviour was
/// fully covered; the composition root was not covered at all.
/// </para>
///
/// <para>
/// That gap matters more here than in most projects: this tool gets exactly one
/// high-stakes run inside a production cutover window, and an operator whose
/// only sanctioned path dies with a DI stack trace will improvise a hand-written
/// INSERT — no normalised email, no audit row, no password policy. Precisely the
/// unaudited privileged account the tool exists to prevent.
/// </para>
/// </summary>
public class AdminBootstrapCompositionRootTests
{
    /// <summary>
    /// Builds the real service collection and resolves the entry point. No
    /// database is contacted — <c>AddDbContext</c> is lazy, so this asserts the
    /// object graph, which is what broke.
    /// </summary>
    [Fact]
    public void Every_dependency_of_the_entry_point_resolves()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Never connected to; AddDbContext only needs it to be non-empty
                // so the guard clause in the registration does not throw.
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=unused",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAdminBootstrap(configuration);

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        using var scope = provider.CreateScope();
        var act = () => scope.ServiceProvider.GetRequiredService<AdminBootstrapper>();

        act.Should().NotThrow(
            "the tool's single sanctioned run happens during a production cutover — "
            + "a missing registration must fail here, not in front of an operator");
    }

    /// <summary>
    /// The registration deliberately fails loudly on a missing connection
    /// string rather than defaulting to something local, so a mis-set
    /// environment cannot quietly target the wrong database.
    /// </summary>
    [Fact]
    public void Missing_connection_string_fails_at_resolve_time()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAdminBootstrap(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<AdminBootstrapper>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Postgres*not configured*");
    }
}
