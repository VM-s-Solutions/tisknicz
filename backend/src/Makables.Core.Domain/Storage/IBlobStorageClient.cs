using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Storage;

/// <summary>
/// File-storage adapter per ADR 0011 / role <c>docs/architecture/roles/blob-storage.md</c>.
/// Stores, retrieves, and streams byte content addressed by
/// <c>(container, path)</c>. No authorization (caller checks ownership);
/// no content interpretation (caller validates MIME type + size).
///
/// <para>
/// <b>Path convention</b> per ADR 0011:
/// <c>{country_code}/{entity}/{entity_id}/{filename}</c> — e.g.
/// <c>cz/orders/01HX.../label.pdf</c>. The country prefix keeps cross-country
/// exports trivial and is enforced by the calling controller (this
/// adapter treats the path as opaque).
/// </para>
///
/// <para>
/// <b>Implementations:</b> <see cref="Storage.BlobContainer"/> names the
/// four launch containers; the active implementation is
/// <c>AzureBlobStorageClient</c> in <c>Makables.Infra.Azure.Storage.Blobs/</c>.
/// </para>
/// </summary>
public interface IBlobStorageClient
{
    /// <summary>
    /// Upload <paramref name="content"/> to <paramref name="container"/>/<paramref name="path"/>.
    /// Overwrites if the blob already exists — the caller is responsible
    /// for path uniqueness (typically via a random suffix on the
    /// filename, per ADR 0011 §"Uploads").
    /// </summary>
    Task<BusinessResult> UploadAsync(
        string container,
        string path,
        Stream content,
        string contentType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Open a read stream for <paramref name="container"/>/<paramref name="path"/>.
    /// The returned <see cref="BlobDownload"/> wraps the stream + the
    /// metadata the controller needs to set the HTTP response headers
    /// (<c>Content-Type</c>, <c>Content-Length</c>, <c>ETag</c>).
    /// The CALLER MUST DISPOSE the stream after writing the response.
    /// </summary>
    Task<BusinessResult<BlobDownload>> DownloadAsync(
        string container,
        string path,
        CancellationToken cancellationToken);

    /// <summary>Delete the blob if it exists. Idempotent — no error on a missing blob.</summary>
    Task<BusinessResult> DeleteAsync(
        string container,
        string path,
        CancellationToken cancellationToken);

    /// <summary>True when the blob exists; used by uniqueness checks before generating a new random suffix.</summary>
    Task<BusinessResult<bool>> ExistsAsync(
        string container,
        string path,
        CancellationToken cancellationToken);
}

/// <summary>
/// What the controller needs to stream a blob through to the HTTP
/// response. The <see cref="Stream"/> is owned by the caller and MUST
/// be disposed after the response is written.
/// </summary>
public sealed record BlobDownload(
    Stream Content,
    string ContentType,
    long ContentLength,
    string? ETag);
