using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Azure.Core.Serialization;
using FluentAssertions;
using Makables.Functions.Health;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Makables.Tests.Functions.Health;

/// <summary>
/// Guards the contract the production deploy gate depends on. The smoke job in
/// both deploy workflows probes <c>GET /api/health</c> on the Functions app and
/// fails the deploy if it never answers, so the route, the verb, the
/// authorization level and the route PREFIX are load-bearing — change any of
/// them and the gate either becomes a false alarm or can never pass, blocking
/// every deploy for ~16 minutes before going red.
/// </summary>
public class HealthFunctionTests
{
    private static MethodInfo RunMethod =>
        typeof(HealthFunction).GetMethod(nameof(HealthFunction.RunAsync))!;

    private static HttpTriggerAttribute Trigger =>
        RunMethod.GetParameters()[0]
            .GetCustomAttributes(typeof(HttpTriggerAttribute), inherit: false)
            .Cast<HttpTriggerAttribute>()
            .Single();

    [Fact]
    public async Task Serialises_A_Json_Body_The_Way_The_Web_Hosts_Health_Endpoint_Does()
    {
        var response = await HealthFunction.RunAsync(new FakeHttpRequestData());

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response.Body.Position = 0;
        using var doc = JsonDocument.Parse(response.Body);

        // The assertion that matters is that the payload is the payload — NOT an
        // MVC result object. Returning IActionResult from a worker without the
        // AspNetCore integration package compiles with zero warnings and then
        // serialises the OkObjectResult itself, so the caller receives
        // {"Value":{...},"Formatters":[],"ContentTypes":[],...} as text/plain.
        // This test is what catches that regression.
        doc.RootElement.TryGetProperty("Value", out _).Should().BeFalse(
            "the body must be the payload, not a serialised MVC result object");
        doc.RootElement.GetProperty("status").GetString().Should().Be("healthy");
        doc.RootElement.TryGetProperty("version", out _).Should().BeTrue();
    }

    /// <summary>
    /// The probe must not take a dependency. A health endpoint that touched the
    /// database or the queues would report a running host as unhealthy during a
    /// transient outage, and on App Service that turns an outage into a restart
    /// loop. Asserting the handler is static is the meaningful form of this:
    /// a static method cannot receive constructor-injected services at all.
    /// </summary>
    [Fact]
    public void Is_Dependency_Free_So_A_Transient_Outage_Cannot_Fail_It()
    {
        RunMethod.IsStatic.Should().BeTrue();
        typeof(HealthFunction).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Should().BeEmpty();
    }

    /// <summary>
    /// Anonymous is required, not incidental: with
    /// <see cref="AuthorizationLevel.Function"/> the smoke gate would have to
    /// obtain a key before it could tell whether the host was alive, inverting
    /// the dependency the probe exists to test. This pins a security-relevant
    /// property to its LESS restrictive value on purpose — the compensating
    /// controls are recorded in ADR 0020's amendment and in
    /// docs/security/function-key-rotation.md.
    /// </summary>
    [Fact]
    public void Is_Anonymous_So_The_Deploy_Gate_Needs_No_Host_Key()
        => Trigger.AuthLevel.Should().Be(AuthorizationLevel.Anonymous);

    [Fact]
    public void Is_Served_At_The_Route_And_Verb_The_Deploy_Gate_Probes()
    {
        Trigger.Route.Should().Be("health");
        Trigger.Methods.Should().ContainSingle().Which.Should().BeEquivalentTo("get");
    }

    /// <summary>
    /// The other half of the URL the gate curls. `/api` is the host.json default
    /// routePrefix; setting `extensions.http.routePrefix` would move the
    /// endpoint and leave every other test in this class green while the deploy
    /// gate blocks for ~16 minutes and then fails forever. Before this test that
    /// assumption lived only in a code comment.
    /// </summary>
    [Fact]
    public void Host_Json_Does_Not_Override_The_Api_Route_Prefix()
    {
        var hostJson = Path.Combine(RepoRoot(), "backend", "src", "Makables.Functions", "host.json");
        File.Exists(hostJson).Should().BeTrue($"expected host.json at {hostJson}");

        using var doc = JsonDocument.Parse(File.ReadAllText(hostJson));
        if (doc.RootElement.TryGetProperty("extensions", out var ext)
            && ext.TryGetProperty("http", out var http))
        {
            http.TryGetProperty("routePrefix", out _).Should().BeFalse(
                "the deploy gate probes /api/health; overriding routePrefix moves it and blocks every deploy");
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".github")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    // ---- Minimal isolated-worker HTTP doubles -------------------------------
    // HttpRequestData/HttpResponseData are abstract and WriteAsJsonAsync pulls
    // its serializer out of WorkerOptions on the FunctionContext, so a real
    // round-trip needs these. Worth the lines: this is what turns the wire
    // format from an assumption into an assertion.

    private sealed class FakeHttpRequestData() : HttpRequestData(new FakeFunctionContext())
    {
        public override Stream Body { get; } = new MemoryStream();
        public override HttpHeadersCollection Headers { get; } = new();
        public override IReadOnlyCollection<IHttpCookie> Cookies { get; } = [];
        public override Uri Url { get; } = new("https://func-makables.test/api/health");
        public override IEnumerable<ClaimsIdentity> Identities { get; } = [];
        public override string Method => "GET";

        public override HttpResponseData CreateResponse() => new FakeHttpResponseData(FunctionContext);
    }

    private sealed class FakeHttpResponseData(FunctionContext context) : HttpResponseData(context)
    {
        public override System.Net.HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; set; } = new();
        public override Stream Body { get; set; } = new MemoryStream();
        public override HttpCookies Cookies => throw new NotSupportedException();
    }

    private sealed class FakeFunctionContext : FunctionContext
    {
        public override IServiceProvider InstanceServices { get; set; } = BuildServices();

        private static IServiceProvider BuildServices()
        {
            var services = new ServiceCollection();
            services.AddOptions<WorkerOptions>()
                .Configure(o => o.Serializer = new JsonObjectSerializer(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            services.AddSingleton<IOptions<WorkerOptions>>(sp =>
                sp.GetRequiredService<IOptionsSnapshot<WorkerOptions>>());
            return services.BuildServiceProvider();
        }

        public override string InvocationId => "test";
        public override string FunctionId => "test";
        public override TraceContext TraceContext => throw new NotSupportedException();
        public override BindingContext BindingContext => throw new NotSupportedException();
        public override RetryContext RetryContext => throw new NotSupportedException();
        public override FunctionDefinition FunctionDefinition => throw new NotSupportedException();
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
        public override IInvocationFeatures Features => throw new NotSupportedException();
    }
}
