using FluentAssertions;
using Makables.Functions.Health;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Makables.Tests.Functions.Health;

/// <summary>
/// Guards the contract the production deploy gate depends on. The smoke job in
/// both deploy workflows probes <c>GET /api/health</c> on the Functions app and
/// fails the deploy if it never returns 200, so the route, the verb and the
/// authorization level here are not cosmetic — changing any of them silently
/// turns that gate into a false alarm (or, worse, into a step that can never
/// pass and blocks every deploy).
/// </summary>
public class HealthFunctionTests
{
    private static HttpTriggerAttribute Trigger =>
        typeof(HealthFunction)
            .GetMethod(nameof(HealthFunction.Run))!
            .GetParameters()[0]
            .GetCustomAttributes(typeof(HttpTriggerAttribute), inherit: false)
            .Cast<HttpTriggerAttribute>()
            .Single();

    [Fact]
    public void Returns_200_With_A_Healthy_Status()
    {
        var result = new HealthFunction().Run(null!);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new { status = "healthy" });
    }

    /// <summary>
    /// The probe must not take a dependency. A health endpoint that touched the
    /// database or the queues would report a running host as unhealthy during a
    /// transient outage — and on App Service that turns an outage into an
    /// instance restart loop. Depth belongs in the gate's DB-backed probe.
    /// </summary>
    [Fact]
    public void Is_Dependency_Free_So_A_Transient_Outage_Cannot_Fail_It()
    {
        typeof(HealthFunction).GetConstructors().Should().ContainSingle()
            .Which.GetParameters().Should().BeEmpty();
    }

    /// <summary>
    /// Anonymous is required, not incidental. With
    /// <see cref="AuthorizationLevel.Function"/> the smoke gate would have to
    /// fetch a host key before it could tell whether the host was alive — and
    /// fetching that key goes through the very host storage this probe exists
    /// to prove is reachable.
    /// </summary>
    [Fact]
    public void Is_Anonymous_So_The_Deploy_Gate_Needs_No_Host_Key()
    {
        Trigger.AuthLevel.Should().Be(AuthorizationLevel.Anonymous);
    }

    [Fact]
    public void Is_Served_At_The_Route_And_Verb_The_Deploy_Gate_Probes()
    {
        // The workflows curl https://<app>.azurewebsites.net/api/health.
        // "api" is the host.json default routePrefix, which this repo does not
        // override — so the route here is the second half of that URL.
        Trigger.Route.Should().Be("health");
        Trigger.Methods.Should().ContainSingle().Which.Should().BeEquivalentTo("get");
    }
}
