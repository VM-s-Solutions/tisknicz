using FluentAssertions;
using Makables.Config.Extensions;
using Makables.Core.AppServices;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// Pins the T-0063 MediatR handler-registration contract: the host's
/// <c>AddMakablesMediator</c> equivalent (assembly scan of
/// <see cref="AssemblyReference"/>) must wire
/// <see cref="CreateOrder.Handler"/> as
/// <see cref="IRequestHandler{TRequest,TResponse}"/>. Failing this
/// test means the integration test will produce a runtime
/// "No service for type IRequestHandler" — easier to debug at the DI
/// boundary than via an HTTP round-trip.
///
/// <para>
/// <b>Regression net for T-0063 root-cause.</b>
/// The original <c>AddMakablesMediator</c> read
/// <c>typeof(AssemblyReference).Assembly</c> with the unqualified name,
/// which C# resolves to the current assembly first when an identically
/// named type lives in BOTH <c>Makables.Core.AppServices</c> AND
/// <c>Makables.Config</c>. The wrapper silently pointed the scanner at
/// the wrong DLL and registered zero handlers — only surfacing at the
/// first <see cref="Mediator.Send"/> call. The
/// <see cref="AddMakablesMediator_registers_CreateOrder_Handler"/> test
/// pins the fix: the marker is fully qualified inside Config.
/// </para>
/// </summary>
public class CreateOrderRegistrationTests
{
    [Fact]
    public void Assembly_scan_registers_CreateOrder_Handler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(AssemblyReference).Assembly));

        // The handler depends on every IRepo / IService — to construct
        // it we'd have to wire everything. Instead we just check the
        // ServiceDescriptor list directly: registration is a build-time
        // signal that the assembly scan picked the handler up.
        var handlerInterface = typeof(IRequestHandler<CreateOrder.Command, BusinessResult<CreateOrder.Response>>);
        services.Should().Contain(d => d.ServiceType == handlerInterface);
    }

    [Fact]
    public void AddMakablesMediator_registers_CreateOrder_Handler()
    {
        // Regression net for the T-0063 root-cause. Calls the SAME wrapper
        // the production hosts call (Makables.Config's AddMakablesMediator)
        // — not a bespoke AddMediatR(cfg => ...) inline. If the wrapper
        // points its assembly scan at the wrong DLL (e.g. by reading the
        // unqualified `typeof(AssemblyReference).Assembly` which resolves
        // to Makables.Config's own marker before Core.AppServices'), this
        // test fails before the integration test's HTTP round-trip would.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMakablesMediator();

        var handlerInterface = typeof(IRequestHandler<CreateOrder.Command, BusinessResult<CreateOrder.Response>>);
        services.Should().Contain(d => d.ServiceType == handlerInterface);
    }

    [Fact]
    public void Handler_type_implements_IRequestHandler_with_expected_generic_args()
    {
        // Belt-and-braces sanity check. If a future refactor renames the
        // nested types, breaks the inheritance, or changes the response
        // shape, the auto-scan registration silently won't pick it up
        // and the runtime "No service for type IRequestHandler" error
        // surfaces only at the first HTTP request.
        var expected = typeof(IRequestHandler<CreateOrder.Command, BusinessResult<CreateOrder.Response>>);
        var handler = typeof(CreateOrder.Handler);
        handler.GetInterfaces().Should().Contain(i => i == expected);
    }
}
