using Asp.Versioning;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.Config.Extensions;

/// <summary>
/// Per ADR 0021: URL-path versioning. Every controller is decorated with
/// <c>[ApiVersion("1.0")]</c> + <c>[Route("api/v{version:apiVersion}/...")]</c>.
/// The version segment is consumed by the route template.
///
/// The default version is v1.0. Deprecated versions are flagged on the
/// controller; a <c>Sunset</c> header is sent in responses.
/// </summary>
public static class MakablesApiVersioningExtensions
{
    public static IServiceCollection AddMakablesApiVersioning(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        })
        .AddApiExplorer(options =>
        {
            // Format: "v1", "v2", etc. — used by the OpenAPI emitter to
            // name the document per version.
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });

        return services;
    }
}
