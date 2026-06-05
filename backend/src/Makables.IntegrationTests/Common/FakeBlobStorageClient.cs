using System.Collections.Concurrent;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Storage;

namespace Makables.IntegrationTests.Common;

/// <summary>
/// In-memory <see cref="IBlobStorageClient"/> for integration tests so
/// the upload + download flows in <c>OrderAttachmentUploadTests</c> /
/// <c>OrderAttachmentDownloadTests</c> can exercise the real controllers
/// without requiring Azurite / a real Azure storage account.
///
/// <para>
/// Stores blob bytes keyed by <c>{container}/{path}</c>. Each blob keeps
/// the content type for download round-trip parity and a deterministic
/// ETag (hash-shaped string) so the conditional-GET path can be tested.
/// </para>
/// </summary>
public sealed class FakeBlobStorageClient : IBlobStorageClient
{
    private readonly ConcurrentDictionary<string, StoredBlob> _store = new();

    public IReadOnlyDictionary<string, StoredBlob> Store => _store;

    public Task<BusinessResult> UploadAsync(
        string container, string path, Stream content, string contentType,
        CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        var bytes = ms.ToArray();
        var key = Key(container, path);
        var etag = $"\"{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16]}\"";
        _store[key] = new StoredBlob(bytes, contentType, etag);
        return Task.FromResult(BusinessResult.Success());
    }

    public Task<BusinessResult<BlobDownload>> DownloadAsync(
        string container, string path, CancellationToken cancellationToken)
    {
        var key = Key(container, path);
        if (!_store.TryGetValue(key, out var blob))
        {
            return Task.FromResult(BusinessResult.Failure<BlobDownload>(
                Error.NotFound("blob", Makables.Core.Domain.Common.BusinessErrorMessage.BlobNotFound)));
        }
        var download = new BlobDownload(
            Content: new MemoryStream(blob.Bytes),
            ContentType: blob.ContentType,
            ContentLength: blob.Bytes.LongLength,
            ETag: blob.ETag);
        return Task.FromResult(BusinessResult.Success(download));
    }

    public Task<BusinessResult> DeleteAsync(
        string container, string path, CancellationToken cancellationToken)
    {
        _store.TryRemove(Key(container, path), out _);
        return Task.FromResult(BusinessResult.Success());
    }

    public Task<BusinessResult<bool>> ExistsAsync(
        string container, string path, CancellationToken cancellationToken)
    {
        var exists = _store.ContainsKey(Key(container, path));
        return Task.FromResult(BusinessResult.Success(exists));
    }

    private static string Key(string container, string path) => $"{container}/{path}";

    public sealed record StoredBlob(byte[] Bytes, string ContentType, string ETag);
}
