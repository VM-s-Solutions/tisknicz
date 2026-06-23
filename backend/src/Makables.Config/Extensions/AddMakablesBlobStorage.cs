using Azure.Identity;
using Azure.Storage.Blobs;
using Makables.Core.Domain.Storage;
using Makables.Infra.Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Makables.Config.Extensions;

/// <summary>
/// Registers <see cref="IBlobStorageClient"/> backed by Azure Blob
/// Storage per ADR 0011 / T-0042. Two credential modes (see
/// <see cref="AzureBlobStorageOptions"/>): connection string for local
/// dev / CI, <c>DefaultAzureCredential</c> for staging / production.
///
/// <para>
/// <c>ValidateOnStart</c> so a missing / malformed config crashes the
/// host at boot rather than failing the first blob op — same shape as
/// the T-0031 Mapbox and T-0032 ARES options registrations.
/// </para>
/// </summary>
public static class MakablesBlobStorageExtensions
{
    public static IServiceCollection AddMakablesBlobStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AzureBlobStorageOptions>()
            .Bind(configuration.GetSection(AzureBlobStorageOptions.SectionName))
            .Validate(o =>
                !string.IsNullOrWhiteSpace(o.ConnectionString)
                || !string.IsNullOrWhiteSpace(o.ServiceUri),
                "BlobStorage requires either ConnectionString (dev/CI) or ServiceUri (Managed Identity).")
            .Validate(o =>
                string.IsNullOrWhiteSpace(o.ServiceUri)
                || Uri.TryCreate(o.ServiceUri, UriKind.Absolute, out var u)
                   && u.Scheme == Uri.UriSchemeHttps,
                "BlobStorage:ServiceUri must be an absolute https URI when set.")
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureBlobStorageOptions>>().Value;

            // Connection string wins when present (local dev / CI /
            // Azurite). In staging + prod the env var is empty and we
            // fall through to ServiceUri + DefaultAzureCredential.
            if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
            {
                return new BlobServiceClient(opts.ConnectionString);
            }

            return new BlobServiceClient(new Uri(opts.ServiceUri!), new DefaultAzureCredential());
        });

        services.AddSingleton<IBlobStorageClient, AzureBlobStorageClient>();

        return services;
    }
}
