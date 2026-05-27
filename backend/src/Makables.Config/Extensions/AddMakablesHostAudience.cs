using Makables.Config.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.Config.Extensions;

public static class MakablesHostAudienceExtensions
{
    /// <summary>
    /// Registers the host's audience tag (one of <c>MakablesHosts.*</c>)
    /// so shared controllers can name their cookies and resolve the
    /// audience parameter on auth commands.
    /// </summary>
    public static IServiceCollection AddMakablesHostAudience(this IServiceCollection services, string audience)
    {
        services.AddSingleton<IHostAudience>(new HostAudience(audience));
        return services;
    }
}
