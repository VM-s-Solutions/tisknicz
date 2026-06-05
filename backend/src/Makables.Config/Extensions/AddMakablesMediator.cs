using FluentValidation;
using MediatR;
using Makables.Core.AppServices.Behaviors;
using Makables.Core.AppServices.Features.Auth;
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
        // T-0063: the marker MUST be the fully-qualified
        // Makables.Core.AppServices.AssemblyReference. The simple name
        // `AssemblyReference` also exists in Makables.Config and in
        // Makables.Core.Domain; C# overload resolution prefers the type
        // declared in the CURRENT assembly (this file's host), which means
        // `typeof(AssemblyReference)` resolves to
        // Makables.Config.AssemblyReference here — pointing the scanner at
        // the wrong DLL and silently registering zero handlers. The
        // integration test "No service for type IRequestHandler" failure
        // for CreateOrder surfaced this; the fix is to disambiguate.
        var appServicesAssembly = typeof(Makables.Core.AppServices.AssemblyReference).Assembly;
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

        // Shared "issue an opaque single-use token by email" pipeline
        // used by RequestMagicLink, SendEmailConfirmation, Register, and
        // RequestPasswordReset. Owns the timing-equalization invariant
        // (T-0023 B-1) in one place so it can't drift between flows.
        services.AddScoped<IOneTimeTokenIssuer, OneTimeTokenIssuer>();

        return services;
    }
}
