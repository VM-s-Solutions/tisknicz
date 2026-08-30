using System.Text.Json;
using FluentAssertions;

namespace Makables.IntegrationTests.HostStartup;

/// <summary>
/// Guards the ops-endpoint/contract boundary on every host.
///
/// <para>
/// Minimal-API endpoints land in the emitted OpenAPI document exactly
/// like controller actions do. When <c>/health</c> was added to all four
/// hosts it therefore entered every document, shifted all four committed
/// spec hashes in
/// <c>frontend/src/lib/api-client/.spec-hashes.json</c>, and would have
/// red-lighted the <c>api-parity</c> CI job on every subsequent PR — for
/// a liveness probe that is not part of the client contract at all.
/// </para>
///
/// <para>
/// The fix is <c>.ExcludeFromDescription()</c> on each ops endpoint. This
/// test is the guard that keeps it applied: every path a host describes
/// must be under <c>/api/</c>. A new <c>app.MapGet("/health/ready", …)</c>
/// written without the call fails here rather than in CI's parity job —
/// which matters, because that job's remediation message tells the
/// developer to regenerate and commit the client, i.e. to bake the ops
/// endpoint into the contract, which is the opposite of the intent.
/// </para>
///
/// <para>
/// Deliberately asserted as a prefix rule rather than a deny-list of
/// known ops paths: a deny-list only catches endpoints someone
/// remembered to add to it. Per ADR 0021 every versioned API route is
/// <c>/api/v{version}/...</c>, so the prefix is the real invariant.
/// </para>
/// </summary>
public class OpsEndpointsExcludedFromContractTests
{
    [Fact]
    public Task Customer_Host_Describes_Only_Api_Paths()
        => AssertOnlyApiPathsAreDescribed<Makables.Web.Customer.Program>();

    [Fact]
    public Task Maker_Host_Describes_Only_Api_Paths()
        => AssertOnlyApiPathsAreDescribed<Makables.Web.Maker.Program>();

    [Fact]
    public Task Admin_Host_Describes_Only_Api_Paths()
        => AssertOnlyApiPathsAreDescribed<Makables.Web.Admin.Program>();

    [Fact]
    public Task Public_Host_Describes_Only_Api_Paths()
        => AssertOnlyApiPathsAreDescribed<Makables.Web.Public.Program>();

    /// <summary>
    /// Ops endpoints must still be REACHABLE — excluding them from the
    /// contract must never turn into excluding them from routing. The App
    /// Service health check (<c>healthCheckPath</c> in
    /// <c>infra/bicep/modules/app-service.bicep</c>) and both deploy
    /// workflows' post-deploy smoke jobs probe <c>/health</c>; if
    /// <c>ExcludeFromDescription</c> were ever swapped for something that
    /// unmaps the route, every deploy would fail its smoke gate instead.
    /// </summary>
    [Fact]
    public async Task Health_Endpoint_Stays_Reachable_Though_Undescribed()
    {
        using var factory = HostStartupHarness.Build<Makables.Web.Public.Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.IsSuccessStatusCode.Should().BeTrue(
            "App Service healthCheckPath and the post-deploy smoke jobs probe /health; " +
            "excluding it from the OpenAPI document must not unmap the route");
    }

    private static async Task AssertOnlyApiPathsAreDescribed<TProgram>()
        where TProgram : class
    {
        using var factory = HostStartupHarness.Build<TProgram>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        response.IsSuccessStatusCode.Should().BeTrue("/openapi/v1.json must be served");

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        var describedPaths = document.RootElement
            .GetProperty("paths")
            .EnumerateObject()
            .Select(path => path.Name)
            .ToList();

        describedPaths.Should().NotBeEmpty(
            "a host that describes no paths would pass the prefix rule vacuously");

        describedPaths.Should().OnlyContain(
            path => path.StartsWith("/api/", StringComparison.Ordinal),
            "only versioned API routes belong in the client contract — an ops or " +
            "liveness endpoint that reaches the OpenAPI document shifts the committed " +
            "NSwag spec hash and breaks the api-parity gate. Add " +
            ".ExcludeFromDescription() to the offending app.MapGet/MapPost call.");
    }
}
