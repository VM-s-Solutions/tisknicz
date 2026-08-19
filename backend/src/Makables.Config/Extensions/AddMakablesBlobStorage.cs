using Azure.Core;
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
            var clientOptions = BuildClientOptions(opts);

            // Connection string wins when present (local dev / CI /
            // Azurite). In staging + prod the env var is empty and we
            // fall through to ServiceUri + DefaultAzureCredential.
            if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
            {
                return new BlobServiceClient(opts.ConnectionString, clientOptions);
            }

            return new BlobServiceClient(
                new Uri(opts.ServiceUri!), new DefaultAzureCredential(), clientOptions);
        });

        services.AddSingleton<IBlobStorageClient, AzureBlobStorageClient>();

        return services;
    }

    /// <summary>
    /// Bounded retry + per-request timeout for every blob call
    /// (CLAUDE.md §5: external calls get a timeout and a bounded retry
    /// with jitter). Without this the SDK defaults apply — 5 retries
    /// with a 60 s <c>MaxDelay</c> — and an unreachable storage endpoint
    /// sleeps ~25 s of exponential backoff before failing. That is not
    /// hypothetical: a maker-logo upload took 26.4 s and threw when the
    /// local emulator was down.
    /// <para>
    /// <c>RetryMode.Exponential</c> is the jittered mode; the fixed mode
    /// would synchronise every caller's retry into a thundering herd
    /// against a storage account that is already struggling.
    /// </para>
    /// </summary>
    internal static BlobClientOptions BuildClientOptions(AzureBlobStorageOptions opts)
    {
        var clientOptions = new BlobClientOptions();
        clientOptions.Retry.Mode = RetryMode.Exponential;
        clientOptions.Retry.MaxRetries = opts.MaxRetries;
        clientOptions.Retry.Delay = opts.RetryDelay;
        clientOptions.Retry.MaxDelay = opts.MaxRetryDelay;
        clientOptions.Retry.NetworkTimeout = opts.NetworkTimeout;
        return clientOptions;
    }
}
