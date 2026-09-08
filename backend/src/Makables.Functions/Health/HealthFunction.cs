using System.Net;
using System.Reflection;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Makables.Functions.Health;

/// <summary>
/// Liveness probe for the Functions host — <c>GET /api/health</c>, anonymous.
///
/// <para>
/// <b>Why this exists.</b> Every other trigger in this app is a timer or a
/// queue trigger, so the host had no externally observable surface at all and
/// the deploy pipeline's smoke gate never asked whether it was alive. A
/// Functions host that cannot start is silent: as
/// <c>FunctionsHostCompositionTests</c> spells out, the isolated worker exits
/// before indexing a single function, so <c>ProcessOutboxTimer</c> never fires,
/// the outbox never drains and <b>no transactional email is ever sent</b> —
/// with valid credentials and nothing wrong on the Web hosts to point at.
/// </para>
///
/// <para>
/// <b>What a 200 here proves — and what it does NOT.</b> Be precise, because an
/// earlier version of this comment overclaimed and a green probe that means
/// less than you think is exactly the failure this project keeps hitting.
/// </para>
/// <list type="bullet">
///   <item><description><b>Proven:</b> the site is deployed and reachable, the
///     worker process is running (a crash-looping host answers 503, not 200),
///     the HTTP trigger was indexed, and every <c>ValidateOnStart</c> options
///     check passed — <c>AddMakablesAuth</c>, <c>AddMakablesBlobStorage</c> and
///     <c>AddMakablesClients</c> all call it, and unlike container validation it
///     runs in <b>every</b> environment. A missing Key Vault reference or a
///     malformed option therefore does turn this red.</description></item>
///   <item><description><b>NOT proven:</b> that the full DI graph resolves.
///     <c>ValidateOnBuild</c>/<c>ValidateScopes</c> are enabled only when
///     <c>IsDevelopment()</c>, and the deployed host runs as Production — so an
///     unresolvable handler dependency surfaces at first invocation, not at
///     startup. Nor that every OTHER function indexed cleanly: a trigger with an
///     unresolvable <c>%Setting%</c> binding is reported as "in error" while the
///     host keeps serving this endpoint. Nor that host storage is usable. Nor
///     that the build now running is the one just pushed.</description></item>
/// </list>
///
/// <para>
/// Those gaps are covered by the deploy gate's companion step, which reads
/// <c>/admin/host/status</c> and asserts <c>state == Running</c> with an empty
/// <c>errors</c> array. This endpoint is the cheap, unauthenticated half; that
/// one is the half that catches a function in error.
/// </para>
///
/// <para>
/// <b>Deliberately dependency-free</b>, matching the Web hosts' <c>/health</c>:
/// it injects nothing and touches neither the database nor the queues. A probe
/// that took a dependency would turn a transient outage into instance restarts
/// and would report a running host as unhealthy. Depth belongs in the gate's
/// DB-backed probe, not here.
/// </para>
///
/// <para>
/// <b>Anonymous on purpose.</b> The response carries no state, no configuration
/// and no identity — the same trade the four Web hosts already make for
/// <c>/health</c>. <c>AuthorizationLevel.Function</c> would force the smoke gate
/// to obtain a key before it could tell whether the host was alive, which
/// inverts the dependency this probe exists to test. This is a documented
/// exception to ADR 0020's "Function HTTP triggers require an
/// <c>x-functions-key</c>" rule — see that ADR's amendment.
/// </para>
/// </summary>
public sealed class HealthFunction
{
    public const string FunctionName = "Health";

    /// <summary>
    /// Informational version of the deployed assembly. Included so an operator
    /// reading the probe can tell WHICH build answered. The deploy gate does
    /// not assert it — nothing stamps the commit SHA at build time today — so
    /// this endpoint cannot prove deployment freshness, only report it.
    /// </summary>
    private static readonly string BuildVersion =
        typeof(HealthFunction).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(HealthFunction).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    /// <summary>
    /// Returns <see cref="HttpResponseData"/> rather than an
    /// <c>IActionResult</c>. That distinction is not cosmetic: this app does not
    /// reference <c>Worker.Extensions.Http.AspNetCore</c> and does not call
    /// <c>ConfigureFunctionsWebApplication()</c>, so returning an
    /// <c>OkObjectResult</c> compiles cleanly and then gets JSON-serialised as
    /// the MVC result object itself — the caller receives
    /// <c>{"Value":{...},"Formatters":[],...}</c> as <c>text/plain</c> instead
    /// of the payload, which is what an earlier version of this file did.
    /// </summary>
    [Function(FunctionName)]
    public static async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new HealthResponse("healthy", BuildVersion));
        return response;
    }

    public sealed record HealthResponse(string Status, string Version);
}
