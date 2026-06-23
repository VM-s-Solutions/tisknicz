namespace Makables.Infra.Azure.Storage.Blobs;

/// <summary>
/// Configuration for <see cref="AzureBlobStorageClient"/>. Two
/// authentication modes:
///
/// <list type="bullet">
///   <item><description><b>Connection string</b> — set <see cref="ConnectionString"/>
///     for local dev (Azurite emulator) and CI. Highest priority.</description></item>
///   <item><description><b>Managed Identity</b> — set <see cref="ServiceUri"/> only
///     (e.g. <c>https://makablesprod.blob.core.windows.net</c>); the
///     adapter uses <c>DefaultAzureCredential</c>. The App Service
///     identity must have the <c>Storage Blob Data Contributor</c>
///     role on the account.</description></item>
/// </list>
///
/// At least one of the two MUST be configured. <c>ValidateOnStart</c>
/// catches a missing config at boot.
/// </summary>
public sealed class AzureBlobStorageOptions
{
    // NOT "AzureBlobStorage": Azure Functions/App Service reserves that prefix
    // for its storage-binding connection convention and rejects any app setting
    // named AzureBlobStorage__* (error 04072). "BlobStorage" is safe and maps to
    // the BlobStorage__ConnectionString app setting the Bicep injects.
    public const string SectionName = "BlobStorage";

    /// <summary>
    /// Azure storage connection string. Local dev only — points at
    /// Azurite when <c>UseDevelopmentStorage=true</c>.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Storage account endpoint, e.g. <c>https://makablesprod.blob.core.windows.net</c>.
    /// Used with <c>DefaultAzureCredential</c> in staging / production.
    /// </summary>
    public string? ServiceUri { get; set; }
}
