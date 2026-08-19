using System.Threading.RateLimiting;
using FluentAssertions;
using Makables.Config.Extensions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Makables.Tests.Config;

/// <summary>
/// Blob-stream traffic must not share the per-caller API envelope.
///
/// <para>
/// <b>The defect this pins.</b> The Public host's global limiter is a
/// single per-IP fixed window, and the Public host serves BOTH the
/// catalog JSON (<c>/api/v1/catalog/*</c>) and every image byte
/// (<c>/api/v1/files/*</c> — maker logos, product photos, avatars).
/// One catalog page renders a full page of maker logos, a maker profile
/// adds every product thumbnail, a product detail adds the whole
/// gallery — so ordinary browsing spent the whole envelope on images
/// and the very next server render of <c>/katalog</c> got a 429. The
/// frontend folds 429 into a transient error, so the page returned
/// HTTP 200 with "Katalog se nepodařilo načíst / Server je momentálně
/// nedostupný" — a green request and a broken page.
/// </para>
///
/// <para>
/// Images are anonymous, immutable, <c>max-age=86400</c> byte streams;
/// they belong in their own generously-sized bucket, not in the budget
/// that has to cover the API call that renders the page referencing
/// them. These tests pin the classification and the partition split.
/// </para>
/// </summary>
public sealed class RateLimitBlobStreamPartitionTests
{
    [Theory]
    [InlineData("/api/v1/files/products/CZ/prod-1/photo.jpg")]
    [InlineData("/api/v1/files/makers/CZ/maker-1/logo.png")]
    [InlineData("/api/v1/files/avatars/CZ/user-1/avatar.webp")]
    // Case-insensitive: the path arrives verbatim from the wire.
    [InlineData("/API/V1/Files/Products/CZ/prod-1/photo.jpg")]
    // Version-agnostic: the route template is api/v{version:apiVersion}/files.
    [InlineData("/api/v2/files/products/CZ/prod-1/photo.jpg")]
    // The authenticated FilesControllers carry an audience segment before
    // "files" — a maker's product grid and invoice list stream through these
    // and would otherwise have gone on spending the API envelope.
    [InlineData("/api/v1/maker/files/orders/order-1/label")]
    [InlineData("/api/v1/maker/files/invoices/invoice-1")]
    [InlineData("/api/v1/customer/files/disputes/dispute-1/return-label")]
    public void IsBlobStreamPath_recognises_the_file_streaming_routes(string path)
    {
        MakablesRateLimitingExtensions.IsBlobStreamPath(new PathString(path)).Should().BeTrue();
    }

    [Theory]
    // The API surface the images were starving — must stay on the envelope.
    [InlineData("/api/v1/catalog/makers")]
    [InlineData("/api/v1/catalog/categories")]
    [InlineData("/api/v1/auth/login")]
    // Prefix-collision guard: "files" must be a whole segment.
    [InlineData("/api/v1/filesystem/x")]
    [InlineData("/api/v1/files-export/x")]
    // Only the known audience prefixes may precede "files".
    [InlineData("/api/v1/orders/files/x")]
    [InlineData("/api/v1/maker/filesystem/x")]
    // Audience prefix present but nothing to stream.
    [InlineData("/api/v1/maker/files")]
    // Not under the versioned API root.
    [InlineData("/files/products/CZ/prod-1/photo.jpg")]
    [InlineData("/api/files/products/CZ/prod-1/photo.jpg")]
    // A bare collection path streams nothing.
    [InlineData("/api/v1/files")]
    [InlineData("/api/v1/files/")]
    // Malformed version segment.
    [InlineData("/api/version1/files/x")]
    [InlineData("/")]
    [InlineData("")]
    public void IsBlobStreamPath_rejects_everything_else(string path)
    {
        MakablesRateLimitingExtensions.IsBlobStreamPath(new PathString(path)).Should().BeFalse();
    }

    [Fact]
    public void Blob_streams_get_their_own_partition_key_and_a_far_larger_permit_limit()
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        http.Request.Path = "/api/v1/files/makers/CZ/maker-1/logo.png";

        var partition = MakablesRateLimitingExtensions.DefaultPartition(
            http, permitLimit: 60, window: TimeSpan.FromMinutes(1));

        partition.PartitionKey.Should().Be("files:ip:203.0.113.7");
        PermitLimitOf(partition)
            .Should().Be(MakablesRateLimitingExtensions.BlobStreamPermitLimit);
    }

    [Fact]
    public void Api_calls_keep_the_host_envelope_and_a_separate_bucket()
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        http.Request.Path = "/api/v1/catalog/makers";

        var partition = MakablesRateLimitingExtensions.DefaultPartition(
            http, permitLimit: 60, window: TimeSpan.FromMinutes(1));

        partition.PartitionKey.Should().Be("ip:203.0.113.7");
        PermitLimitOf(partition).Should().Be(60);
    }

    [Fact]
    public void A_page_of_images_no_longer_exhausts_the_api_envelope()
    {
        // The regression in one assertion: the image bucket and the API
        // bucket are different keys for the same caller, so N image
        // requests leave the API budget untouched.
        var ip = System.Net.IPAddress.Parse("203.0.113.7");

        string KeyFor(string path)
        {
            var http = new DefaultHttpContext();
            http.Connection.RemoteIpAddress = ip;
            http.Request.Path = path;
            return MakablesRateLimitingExtensions
                .DefaultPartition(http, 60, TimeSpan.FromMinutes(1)).PartitionKey;
        }

        KeyFor("/api/v1/files/makers/CZ/m/logo.png")
            .Should().NotBe(KeyFor("/api/v1/catalog/makers"));
    }

    [Fact]
    public void Authenticated_blob_streams_partition_per_user_not_per_shared_ip()
    {
        // Customer/Maker file downloads are [Authorize]d; keeping the
        // sub-claim partition means one user's download spree cannot
        // spend another's stream budget.
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        http.Request.Path = "/api/v1/maker/files/invoices/invoice-1";
        http.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.NameIdentifier, "user-42")],
                authenticationType: "test"));

        MakablesRateLimitingExtensions
            .DefaultPartition(http, 60, TimeSpan.FromMinutes(1))
            .PartitionKey.Should().Be("files:user:user-42");
    }

    [Fact]
    public void The_public_envelope_clears_a_realistic_page_view()
    {
        // Sizing guard, not a style preference: behind the T-0153
        // same-origin proxy every anonymous request reaches the Public
        // host from the frontend App Service's single egress IP, so this
        // one bucket covers the WHOLE site's anonymous traffic. The old
        // 60/min broke with a couple of concurrent visitors.
        MakablesRateLimitingExtensions.PublicEnvelopePermitLimit
            .Should().BeGreaterThanOrEqualTo(300);
    }

    /// <summary>
    /// Reads the configured permit limit back off the partition through
    /// PUBLIC API only: build the limiter the factory would build and ask
    /// it how many permits a fresh window offers. Reflecting into the
    /// limiter's private options would pin an implementation detail of
    /// the BCL instead of our configuration.
    /// </summary>
    private static long PermitLimitOf(RateLimitPartition<string> partition)
    {
        using var limiter = partition.Factory(partition.PartitionKey);
        var available = limiter.GetStatistics()?.CurrentAvailablePermits;
        available.Should().NotBeNull("a fixed-window limiter reports its statistics");
        return available!.Value;
    }
}
