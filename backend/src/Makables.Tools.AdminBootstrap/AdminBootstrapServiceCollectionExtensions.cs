using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Infra.Common.Auth;
using Makables.Infra.Common.Identifiers;
using Makables.Infra.Database;
using Makables.Infra.Database.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.Tools.AdminBootstrap;

/// <summary>
/// The tool's composition root, extracted from <c>Program.cs</c> so a test can
/// build the real container and prove every dependency resolves.
///
/// <para>
/// That test is not ceremony. The first version of this tool shipped without
/// registering <see cref="IIdGenerator"/> and threw
/// <c>InvalidOperationException: Unable to resolve service ...</c> on every
/// invocation — while 13 unit tests passed, because they constructed
/// <see cref="AdminBootstrapper"/> directly with substitutes. A tool whose whole
/// purpose is one high-stakes run inside a production cutover window cannot have
/// an untested composition root: the operator's fallback when it crashes is a
/// hand-written INSERT, which is exactly the unaudited privileged account this
/// tool exists to prevent.
/// </para>
/// </summary>
internal static class AdminBootstrapServiceCollectionExtensions
{
    internal static IServiceCollection AddAdminBootstrap(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<Argon2idOptions>()
            .Bind(configuration.GetSection(Argon2idOptions.SectionName));

        // Same hasher and defaults as the hosts (ADR 0012), so the account this
        // mints verifies against the normal login path.
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.AddSingleton<IIdGenerator, UlidIdGenerator>();
        services.AddSingleton<IClock, BootstrapClock>();
        services.AddSingleton<IUserSessionProvider, BootstrapUserSessionProvider>();
        services.AddScoped<AuditableSaveChangesInterceptor>();

        services.AddDbContext<MakablesDbContext>((sp, options) =>
        {
            var connectionString = configuration.GetConnectionString("Postgres");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Connection string 'Postgres' is not configured. "
                    + "Set ConnectionStrings:Postgres in appsettings or environment.");
            }

            options.UseNpgsql(connectionString);
            options.AddInterceptors(sp.GetRequiredService<AuditableSaveChangesInterceptor>());
        });

        services.AddScoped<AdminBootstrapper>();

        return services;
    }
}
