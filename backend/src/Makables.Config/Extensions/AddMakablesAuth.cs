using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Infra.Common.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

namespace Makables.Config.Extensions;

/// <summary>
/// Per-audience JWT validation per ADR 0012 §JWT structure /
/// patterns §A.17 (custom authentication). Each host calls this with
/// its own audience string ("customer", "maker", "admin", or "public").
///
/// Audience binding (T-0027): every host accepts its own audience PLUS
/// "admin" — admins can call any audience host. So a customer JWT
/// (aud=customer) CANNOT be presented to <c>Web.Maker</c>; a maker JWT
/// CANNOT be presented to <c>Web.Customer</c>. An admin JWT can be
/// presented to any.
///
/// Validation parameters wired here:
///   - Signature: HMAC-SHA256 against <see cref="JwtOptions.SigningKeyBase64"/>.
///   - Issuer: <see cref="JwtOptions.Issuer"/>.
///   - Audience: see <see cref="AcceptedAudiencesFor"/>.
///   - Lifetime: enforced (15-min access token per ADR 0012).
///   - Clock skew: 30 s; tighter than the .NET 5-min default so a
///     near-expiry token doesn't slip through.
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

        // Bind JwtOptions through the options system so the binder
        // picks up configuration sources added AFTER this method runs
        // — notably WebApplicationFactory.ConfigureAppConfiguration in
        // integration tests, which prepends sources at host build but
        // doesn't influence eager reads inside this method.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName));

        var acceptedAudiences = AcceptedAudiencesFor(audience);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Post-configure the JWT bearer options at runtime so the
        // signing key + issuer + lifetime params are read from the
        // FINAL bound JwtOptions (after every configuration source has
        // contributed). Failures here surface on the first protected
        // request, not at host build.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwtAccessor) =>
            {
                var jwt = jwtAccessor.Value;
                if (string.IsNullOrWhiteSpace(jwt.SigningKeyBase64))
                    throw new InvalidOperationException(
                        "Jwt:SigningKeyBase64 is not configured. Set it in appsettings or environment.");
                if (string.IsNullOrWhiteSpace(jwt.Issuer))
                    throw new InvalidOperationException("Jwt:Issuer is not configured.");

                var keyBytes = Convert.FromBase64String(jwt.SigningKeyBase64);
                if (keyBytes.Length < 32)
                    throw new InvalidOperationException(
                        "Jwt:SigningKeyBase64 must decode to at least 32 bytes.");
                var signingKey = new SymmetricSecurityKey(keyBytes) { KeyId = jwt.KeyId };

                options.RequireHttpsMetadata = false; // dev convenience; prod enforces HTTPS at the proxy
                options.SaveToken = false;            // we never need to re-emit the inbound token
                options.MapInboundClaims = false;     // keep `sub` / `email` / `role` as wire-formatted

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudiences = acceptedAudiences,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimTypes.NameIdentifier,
                    RoleClaimType = "role",
                };
            });

        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Audience policy per ADR 0012 §JWT. Customer/Maker/Public hosts
    /// accept their own audience plus admin (admins can call any host).
    /// The Public host accepts every audience because some catalog
    /// endpoints are open + some accept any authenticated caller.
    /// </summary>
    internal static string[] AcceptedAudiencesFor(string hostAudience) =>
        hostAudience switch
        {
            MakablesAudiences.Customer => [MakablesAudiences.Customer, MakablesAudiences.Admin],
            MakablesAudiences.Maker    => [MakablesAudiences.Maker,    MakablesAudiences.Admin],
            MakablesAudiences.Admin    => [MakablesAudiences.Admin],
            // Public host: any authenticated caller is welcome on the
            // protected subset of endpoints (T-0035+). The actual
            // [Authorize] is per-endpoint.
            "public" => [MakablesAudiences.Customer, MakablesAudiences.Maker, MakablesAudiences.Admin],
            _ => throw new ArgumentException($"Unknown host audience '{hostAudience}'.", nameof(hostAudience)),
        };
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
