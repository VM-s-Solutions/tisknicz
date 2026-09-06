using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Makables.Functions.Health;

/// <summary>
/// Liveness probe for the Functions host — <c>GET /api/health</c>, anonymous.
///
/// <para>
/// <b>Why this exists.</b> Every other trigger in this app is a timer or a
/// queue trigger, so until now the host had no externally observable surface
/// at all. The deploy pipeline's smoke gate probed the four API hosts and the
/// frontend and never the Functions app, which meant the one failure this
/// project keeps hitting — ARM reports SUCCESS while the thing is dead — had
/// no detector here. A Functions host that cannot start is completely silent:
/// as <c>FunctionsHostCompositionTests</c> spells out, the isolated worker
/// exits before indexing a single function, so <c>ProcessOutboxTimer</c> never
/// fires, the outbox never drains and <b>no transactional email is ever
/// sent</b> — with valid credentials and nothing wrong on the Web hosts to
/// point at the cause.
/// </para>
///
/// <para>
/// <b>What a 200 here actually proves.</b> More than it looks. Reaching this
/// method means the worker process started, the DI graph validated, the host
/// authenticated to its storage account and indexed its functions. That last
/// part is the point: identity-based <c>AzureWebJobsStorage</c> means the host
/// needs its managed-identity role assignments to have propagated, and until
/// they do it 403s on host storage and never starts. This endpoint is what
/// turns that window into an observable, waited-on condition instead of a
/// green deploy over a dead host.
/// </para>
///
/// <para>
/// <b>Deliberately dependency-free</b>, matching the Web hosts' <c>/health</c>
/// (see each <c>Program.cs</c>): it injects nothing and touches neither the
/// database nor the queues. A probe that took a dependency would turn a
/// transient outage into restarts, and would report a host that IS running as
/// unhealthy. Depth belongs in the deploy gate's DB-backed probe, not here.
/// </para>
///
/// <para>
/// <b>Anonymous on purpose.</b> The response carries no state, no
/// configuration and no identity — the same trade the four Web hosts already
/// make. <c>AuthorizationLevel.Function</c> would require the smoke gate to
/// fetch a host key before it could tell whether the host was alive, which is
/// circular: fetching that key goes through the very host storage this probe
/// exists to prove is reachable.
/// </para>
/// </summary>
public sealed class HealthFunction
{
    public const string FunctionName = "Health";

    [Function(FunctionName)]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData _)
        => new OkObjectResult(new { status = "healthy" });
}
