using Makables.Core.Domain.Identity;
using Makables.Infra.Clients.Google;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.Config.Extensions;

/// <summary>
/// Registers typed <see cref="HttpClient"/>s for every external adapter
/// (Comgate, Packeta, ARES, SendGrid, Mapbox, Google OAuth) and the
/// keyed implementations of the adapter interfaces. Per ADR 0008 /
/// patterns §A.15 (provider adapter pattern with keyed services).
///
/// Each Phase-2/4 ticket adds its own provider here. Concrete adapters
/// land per their ticket: T-0026 Google OAuth, T-0028 SendGrid,
/// T-0031 Mapbox, T-0032 ARES, T-0065 Comgate, T-0070 Packeta.
/// </summary>
public static class MakablesClientsExtensions
{
    public static IServiceCollection AddMakablesClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // === Google OAuth (T-0026) ===
        services.AddOptions<GoogleOAuthOptions>()
            .Bind(configuration.GetSection(GoogleOAuthOptions.SectionName));
        services.AddHttpClient(GoogleOAuthClient.HttpClientName);
        services.AddScoped<IGoogleOAuthClient, GoogleOAuthClient>();

        return services;
    }
}
