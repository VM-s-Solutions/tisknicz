using Makables.Core.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.Config.Extensions;

/// <summary>
/// Registers the non-HTTP <see cref="IUserSessionProvider"/> used by hosts
/// that have no inbound authenticated request — the Functions host (queue
/// and timer triggers) per ADR 0020.
///
/// Web hosts get <c>HttpContextUserSessionProvider</c> from
/// <c>AddMakablesAuth</c>; the Functions host deliberately does not
/// register auth (nothing arrives over an authenticated HTTP request), so
/// without this the container fails <c>ValidateOnBuild</c> for every
/// MediatR handler that takes <see cref="IUserSessionProvider"/> and the
/// isolated worker dies at startup before a single function is indexed.
///
/// The identity matches the <c>"system"</c> actor
/// <c>AuditableSaveChangesInterceptor</c> already falls back to, and the
/// <see cref="IUserSessionProvider"/> contract's documented
/// "configured 'system' identity (in Functions / cron)".
/// </summary>
public static class MakablesSystemSessionExtensions
{
    public static IServiceCollection AddMakablesSystemSession(this IServiceCollection services)
    {
        services.AddScoped<IUserSessionProvider, SystemUserSessionProvider>();
        return services;
    }
}

/// <summary>
/// Session identity for background hosts. Background work acts as the
/// platform itself, not as a user: audit stamps read <c>system</c>, and
/// country-scoped reads carry no user country — a queue/timer trigger
/// resolves country from the aggregate it loaded, never from the caller.
/// </summary>
public sealed class SystemUserSessionProvider : IUserSessionProvider
{
    /// <summary>The audit actor for background work; matches the interceptor's fallback.</summary>
    public const string SystemActor = "system";

    public string? GetUserId() => SystemActor;

    public string? GetUserEmail() => null;

    public string? GetUserCountryCode() => null;
}
