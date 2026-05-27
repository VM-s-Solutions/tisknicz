using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.Config.Extensions;

/// <summary>
/// Per-host MVC controller registration with the shared JSON shape every
/// Makables host emits.
///
/// <para>
/// <b>String enums on the wire.</b> ASP.NET Core MVC's default
/// <c>System.Text.Json</c> serializer emits enums as numbers, but
/// every Makables consumer (NSwag client + the hand-written
/// <c>lib/api-client-helpers/*.ts</c>) types enums as string unions
/// (<c>'Customer' | 'Maker' | 'Admin'</c>). Without a global
/// <see cref="JsonStringEnumConverter"/> the wire and the TS types
/// diverge. T-0036 Copilot review.
/// </para>
///
/// <para>
/// Hosts call this in place of <c>AddControllers()</c>. New host? Use
/// this extension so the JSON shape stays consistent across the four
/// audiences.
/// </para>
/// </summary>
public static class MakablesControllersExtensions
{
    public static IMvcBuilder AddMakablesControllers(this IServiceCollection services)
    {
        return services.AddControllers()
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
    }
}
