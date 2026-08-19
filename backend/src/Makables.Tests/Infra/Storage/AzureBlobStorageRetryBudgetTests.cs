using System.Diagnostics;
using Azure.Core;
using Azure.Storage.Blobs;
using FluentAssertions;
using Makables.Config.Extensions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Storage;
using Makables.Infra.Azure.Storage.Blobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Makables.Tests.Infra.Storage;

/// <summary>
/// Regression tests for the blob retry budget. A maker-logo upload was
/// observed taking 26.4 s and then returning an unhandled 500 while the
/// storage emulator was down: the SDK's default policy (5 retries, 60 s
/// MaxDelay) spent ~25 s asleep, and the resulting
/// <c>AggregateException</c> escaped every <c>catch</c> in
/// <see cref="AzureBlobStorageClient"/>.
///
/// <para>
/// Both halves are pinned here — the bounded policy
/// (<see cref="MakablesBlobStorageExtensions.BuildClientOptions"/>) and
/// the adapter's translation of a transport failure into the documented
/// <c>blob.*Failed</c> transient.
/// </para>
/// </summary>
public class AzureBlobStorageRetryBudgetTests
{
    /// <summary>
    /// Port 1 is reserved (tcpmux) and never bound by a test agent, so a
    /// connection here is refused immediately. That isolates *backoff*
    /// time from network time: whatever the call costs is what the retry
    /// policy chose to sleep.
    /// </summary>
    private const string DeadEndpointConnectionString =
        "DefaultEndpointsProtocol=http;"
        + "AccountName=devstoreaccount1;"
        + "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;"
        + "BlobEndpoint=http://127.0.0.1:1/devstoreaccount1;";

    private static AzureBlobStorageClient NewSutPointedAtDeadEndpoint()
    {
        var options = MakablesBlobStorageExtensions.BuildClientOptions(new AzureBlobStorageOptions());
        return new AzureBlobStorageClient(
            new BlobServiceClient(DeadEndpointConnectionString, options),
            NullLogger<AzureBlobStorageClient>.Instance);
    }

    [Fact]
    public void Default_retry_policy_is_bounded_and_jittered()
    {
        var options = MakablesBlobStorageExtensions.BuildClientOptions(new AzureBlobStorageOptions());

        // Exponential is the SDK's jittered mode. Fixed would synchronise
        // every caller into a thundering herd (CLAUDE.md §5).
        options.Retry.Mode.Should().Be(RetryMode.Exponential);

        // The Azure defaults are 5 retries / 60 s MaxDelay / 100 s
        // NetworkTimeout. Each must be tightened or the 26 s hang returns.
        options.Retry.MaxRetries.Should().Be(3).And.BeLessThan(5);
        options.Retry.MaxDelay.Should().BeLessThan(TimeSpan.FromSeconds(60));
        options.Retry.NetworkTimeout.Should().BeLessThan(TimeSpan.FromSeconds(100));

        // Worst case = (attempts x NetworkTimeout) + total backoff. It has
        // to land inside the frontend's 120 s UPLOAD_TIMEOUT_MS, otherwise
        // the browser aborts first and the user sees a bare "cancelled"
        // instead of a localised blob.uploadFailed.
        var attempts = options.Retry.MaxRetries + 1;
        var worstCase = attempts * options.Retry.NetworkTimeout
                        + attempts * options.Retry.MaxDelay;
        worstCase.Should().BeLessThan(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public async Task UploadAsync_against_a_dead_endpoint_fails_fast_with_the_transient_code()
    {
        var sut = NewSutPointedAtDeadEndpoint();
        using var content = new MemoryStream([1, 2, 3, 4]);

        var stopwatch = Stopwatch.StartNew();
        var result = await sut.UploadAsync(
            container: BlobContainer.ProfileImages,
            path: "cz/makers/m1/logo.png",
            content: content,
            contentType: "image/png",
            cancellationToken: CancellationToken.None);
        stopwatch.Stop();

        // The bug: this threw AggregateException past the adapter.
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.BlobUploadFailed);
        result.Error.Type.Should().Be(ErrorType.Transient);

        // The other half of the bug: 26.4 s of backoff. Connection-refused
        // returns instantly, so this measures sleeping only. Generous
        // ceiling — the point is that it is nowhere near 26 s.
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task DownloadAsync_against_a_dead_endpoint_returns_the_transient_code()
    {
        var sut = NewSutPointedAtDeadEndpoint();

        var result = await sut.DownloadAsync(
            container: BlobContainer.ProfileImages,
            path: "cz/makers/m1/logo.png",
            cancellationToken: CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.BlobDownloadFailed);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }

    [Fact]
    public async Task ExistsAsync_against_a_dead_endpoint_returns_the_transient_code()
    {
        var sut = NewSutPointedAtDeadEndpoint();

        var result = await sut.ExistsAsync(
            container: BlobContainer.ProfileImages,
            path: "cz/makers/m1/logo.png",
            cancellationToken: CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.BlobOperationFailed);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }

    [Fact]
    public async Task DeleteAsync_against_a_dead_endpoint_returns_the_transient_code()
    {
        var sut = NewSutPointedAtDeadEndpoint();

        var result = await sut.DeleteAsync(
            container: BlobContainer.ProfileImages,
            path: "cz/makers/m1/logo.png",
            cancellationToken: CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.BlobOperationFailed);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }
}
