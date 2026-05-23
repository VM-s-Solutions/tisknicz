using FluentValidation;
using MediatR;
using Makables.Core.AppServices;
using Makables.Core.AppServices.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.Config.Extensions;

/// <summary>
/// Registers MediatR (handler scanning across Core.AppServices), the two
/// pipeline behaviors in the correct order, and every FluentValidation
/// validator in the same assembly. Per ADR 0002 / patterns §A.5.
/// </summary>
public static class MakablesMediatorExtensions
{
    public static IServiceCollection AddMakablesMediator(this IServiceCollection services)
    {
        var appServicesAssembly = typeof(AssemblyReference).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(appServicesAssembly);
        });

        // Pipeline behaviors run in registration order:
        //   ValidationPipelineBehavior — runs first, short-circuits on
        //     invalid input before the handler.
        //   UnitOfWorkPipelineBehavior — runs second, commits the UoW
        //     after the handler returns successfully (commands only).
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkPipelineBehavior<,>));

        // FluentValidation validators in Core.AppServices.
        services.AddValidatorsFromAssembly(appServicesAssembly);

        return services;
    }
}
