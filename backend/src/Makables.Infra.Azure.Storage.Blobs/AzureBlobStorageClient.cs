using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Storage;
using Microsoft.Extensions.Logging;

namespace Makables.Infra.Azure.Storage.Blobs;

/// <summary>
/// Azure Blob Storage implementation of <see cref="IBlobStorageClient"/>
/// per ADR 0011. Translates Azure SDK exceptions into typed
/// <see cref="BusinessResult"/> failures so no exception crosses the
/// boundary (same shape as <c>AresCompanyRegistry</c> T-0032 and
/// <c>MapboxAddressGeocoder</c> T-0031).
///
/// <para>
/// Container validation: only the four containers in
/// <see cref="BlobContainer.All"/> are accepted; anything else returns
/// <see cref="BusinessErrorMessage.BlobInvalidContainer"/> so a typo'd
/// caller can't silently create a new container at runtime (the launch
/// containers are provisioned by Bicep with the correct public/private
/// access policy — runtime-created containers would default to private
/// and break the public product-images endpoint).
/// </para>
///
/// <para>
/// Path validation is conservative: no leading slash, no <c>..</c>
/// segments, no <c>\</c>. The caller is still responsible for the ADR
/// 0011 path convention <c>{country_code}/{entity}/{entity_id}/{filename}</c>;
/// this adapter only enforces that the path is well-formed enough that
/// Azure won't reject it or interpret it ambiguously.
/// </para>
/// </summary>
public sealed class AzureBlobStorageClient(
    BlobServiceClient serviceClient,
    ILogger<AzureBlobStorageClient> logger) : IBlobStorageClient
{
    public async Task<BusinessResult> UploadAsync(
        string container,
        string path,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (!IsValidContainer(container))
            return BusinessResult.Failure(Error.Validation(nameof(container), BusinessErrorMessage.BlobInvalidContainer));
        if (!IsValidPath(path))
            return BusinessResult.Failure(Error.Validation(nameof(path), BusinessErrorMessage.BlobInvalidPath));

        try
        {
            var blob = serviceClient.GetBlobContainerClient(container).GetBlobClient(path);
            await blob.UploadAsync(
                content,
                new BlobHttpHeaders { ContentType = contentType },
                cancellationToken: cancellationToken);
            return BusinessResult.Success();
        }
        catch (RequestFailedException ex)
        {
            logger.LogWarning(ex,
                "Blob upload failed (container {Container}, path {Path}, status {Status}).",
                container, path, ex.Status);
            return BusinessResult.Failure(Error.Transient(BusinessErrorMessage.BlobUploadFailed));
        }
    }

    public async Task<BusinessResult<BlobDownload>> DownloadAsync(
        string container,
        string path,
        CancellationToken cancellationToken)
    {
        if (!IsValidContainer(container))
            return BusinessResult.Failure<BlobDownload>(Error.Validation(nameof(container), BusinessErrorMessage.BlobInvalidContainer));
        if (!IsValidPath(path))
            return BusinessResult.Failure<BlobDownload>(Error.Validation(nameof(path), BusinessErrorMessage.BlobInvalidPath));

        try
        {
            var blob = serviceClient.GetBlobContainerClient(container).GetBlobClient(path);
            var response = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
            var details = response.Value.Details;
            return BusinessResult.Success(new BlobDownload(
                Content: response.Value.Content,
                ContentType: string.IsNullOrEmpty(details.ContentType) ? "application/octet-stream" : details.ContentType,
                ContentLength: details.ContentLength,
                ETag: details.ETag.ToString()));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return BusinessResult.Failure<BlobDownload>(Error.NotFound("blob"));
        }
        catch (RequestFailedException ex)
        {
            logger.LogWarning(ex,
                "Blob download failed (container {Container}, path {Path}, status {Status}).",
                container, path, ex.Status);
            return BusinessResult.Failure<BlobDownload>(Error.Transient(BusinessErrorMessage.BlobDownloadFailed));
        }
    }

    public async Task<BusinessResult> DeleteAsync(
        string container,
        string path,
        CancellationToken cancellationToken)
    {
        if (!IsValidContainer(container))
            return BusinessResult.Failure(Error.Validation(nameof(container), BusinessErrorMessage.BlobInvalidContainer));
        if (!IsValidPath(path))
            return BusinessResult.Failure(Error.Validation(nameof(path), BusinessErrorMessage.BlobInvalidPath));

        try
        {
            var blob = serviceClient.GetBlobContainerClient(container).GetBlobClient(path);
            // DeleteIfExistsAsync is idempotent — returns false if the
            // blob didn't exist, which is success from our perspective.
            await blob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
            return BusinessResult.Success();
        }
        catch (RequestFailedException ex)
        {
            logger.LogWarning(ex,
                "Blob delete failed (container {Container}, path {Path}, status {Status}).",
                container, path, ex.Status);
            return BusinessResult.Failure(Error.Transient(BusinessErrorMessage.BlobDownloadFailed));
        }
    }

    public async Task<BusinessResult<bool>> ExistsAsync(
        string container,
        string path,
        CancellationToken cancellationToken)
    {
        if (!IsValidContainer(container))
            return BusinessResult.Failure<bool>(Error.Validation(nameof(container), BusinessErrorMessage.BlobInvalidContainer));
        if (!IsValidPath(path))
            return BusinessResult.Failure<bool>(Error.Validation(nameof(path), BusinessErrorMessage.BlobInvalidPath));

        try
        {
            var blob = serviceClient.GetBlobContainerClient(container).GetBlobClient(path);
            var response = await blob.ExistsAsync(cancellationToken);
            return BusinessResult.Success(response.Value);
        }
        catch (RequestFailedException ex)
        {
            logger.LogWarning(ex,
                "Blob exists-check failed (container {Container}, path {Path}, status {Status}).",
                container, path, ex.Status);
            return BusinessResult.Failure<bool>(Error.Transient(BusinessErrorMessage.BlobDownloadFailed));
        }
    }

    private static bool IsValidContainer(string container) =>
        !string.IsNullOrEmpty(container) && Array.IndexOf(BlobContainer.All, container) >= 0;

    /// <summary>
    /// Conservative path-safety check. Azure accepts most strings but
    /// we reject leading slashes (so the path is unambiguous relative
    /// to the container root), <c>..</c> traversal segments (so a
    /// malformed caller can't accidentally escape its intended prefix
    /// after a future normalisation), and backslashes (path separator
    /// on Windows — disallowed in Azure blob names).
    /// </summary>
    private static bool IsValidPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.StartsWith('/')) return false;
        if (path.Contains('\\')) return false;
        if (path.Length > 1024) return false;  // Azure blob name limit is 1024 chars
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0) return false;  // catches "//" double-slash
            if (segment == "." || segment == "..") return false;
        }
        return true;
    }
}
