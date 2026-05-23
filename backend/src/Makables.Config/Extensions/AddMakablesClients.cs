using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.Config.Extensions;

/// <summary>
/// Registers typed <see cref="HttpClient"/>s for every external adapter
/// (Comgate, Packeta, ARES, Resend, Mapbox) and the keyed implementations
/// of the adapter interfaces (<c>IPaymentProvider</c>, etc.). Per ADR
/// 0008 / patterns §A.15 (provider adapter pattern with keyed services).
///
/// Phase 1 stub — every concrete adapter ships in its own ticket
/// (T-0065 Comgate, T-0070 Packeta, T-0032 ARES, T-0028 Resend, T-0031
/// Mapbox in the backlog). This method is the registration site that
/// those tickets extend.
/// </summary>
public static class MakablesClientsExtensions
{
    public static IServiceCollection AddMakablesClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Concrete adapters added by their respective Phase 2/4 tickets.
        // Example shape once Comgate lands:
        //   services.AddHttpClient<ComgatePaymentProvider>(c => { ... });
        //   services.AddKeyedScoped<IPaymentProvider, ComgatePaymentProvider>("comgate");
        return services;
    }
}
