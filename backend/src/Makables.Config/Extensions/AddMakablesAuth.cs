using Makables.Core.Domain.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Makables.Config.Extensions;

/// <summary>
/// Per-audience JWT validation per ADR 0012 / patterns §A.17 (custom
/// authentication). Each host calls this with its own audience string
/// ("customer", "maker", "admin", or "public" — though Public skips auth
/// entirely for catalog reads).
///
/// Phase 1 ships the minimal skeleton: JWT bearer with audience binding,
/// a host-side <c>HttpContextUserSessionProvider</c>, and the
/// <c>IUserSessionProvider</c> registration. The signing key, refresh
/// tokens, and AuthService land in T-0020+ (Phase 2 — Identity).
/// </summary>
public static class MakablesAuthExtensions
{
    public static IServiceCollection AddMakablesAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        string audience)
    {
        if (string.IsNullOrWhiteSpace(audience))
        {
            throw new ArgumentException("Audience is required.", nameof(audience));
        }

        services.AddHttpContextAccessor();
        services.AddScoped<IUserSessionProvider, HttpContextUserSessionProvider>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Real validation parameters (signing key, issuer, lifetime)
                // are bound in T-0020 (Identity / AuthService). For now we
                // configure the audience binding only; Phase 1 hosts have
                // no protected endpoints so this is forward-wiring.
                options.Audience = audience;

                // TokenValidationParameters set in T-0020.
            });

        services.AddAuthorization();

        return services;
    }
}

/// <summary>
/// Reads JWT claims off the inbound HTTP request and exposes them as
/// <see cref="IUserSessionProvider"/>. Per ADR 0012 and role file
/// <c>docs/architecture/roles/user-session-provider.md</c>.
/// </summary>
internal sealed class HttpContextUserSessionProvider(IHttpContextAccessor accessor)
    : IUserSessionProvider
{
    public string? GetUserId() =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? GetUserEmail() =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email);

    public string? GetUserCountryCode() =>
        accessor.HttpContext?.User.FindFirstValue(MakablesClaimTypes.CountryCode);
}
