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

        // Pipeline behaviors. MediatR runs behaviors in registration order:
        // first-registered is OUTERMOST. The chain unwinds as
        // Validation → UnitOfWork → AdminAudit → Handler → (return)
        // AdminAudit (after-snapshot + Add audit row) →
        // UnitOfWork.SaveChangesAsync (commits handler state + audit row
        // atomically) → Validation (after).
        //
        // The UoW MUST wrap AdminAudit (not the other way around) so the
        // audit row added by AdminAudit lands in the same SaveChanges call
        // as the handler's state changes. Reviewer T-0011 BLOCKER fix.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkPipelineBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AdminAuditPipelineBehavior<,>));

        // FluentValidation validators in Core.AppServices.
        services.AddValidatorsFromAssembly(appServicesAssembly);

        return services;
    }
}
