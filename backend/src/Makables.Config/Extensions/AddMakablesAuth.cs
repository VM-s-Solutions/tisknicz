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
/// its own host identifier (<see cref="MakablesHosts"/>).
///
/// Audience binding: every host accepts its own audience PLUS
/// <c>admin</c> (admins can call any audience host). So a customer JWT
/// (aud=customer) CANNOT be presented to <c>Web.Maker</c>; a maker JWT
/// CANNOT be presented to <c>Web.Customer</c>. An admin JWT can be
/// presented to any.
///
/// <c>Web.Public</c> is the exception: it accepts ANY of the three
/// audiences. Public is the OAuth/login/anonymous surface plus a
/// catch-all for "any-authenticated-caller-welcome" endpoints (e.g.
/// posting a review while logged in). This is INTENTIONAL — a maker
/// must be able to read a public catalog page. If a future Public
/// endpoint needs role restriction, it MUST mount a named
/// authorization policy (<c>[Authorize(Policy="...")]</c>) that
/// checks the <c>role</c> claim or the <c>aud</c> claim explicitly —
/// bare <c>[Authorize]</c> on Public is not enough. Reviewer T-0027
/// sec B-1 contract.
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
        //
        // ValidateOnStart fires JwtOptionsValidator at host build, so
        // a missing/typo'd Jwt:SigningKeyBase64 or Jwt:Issuer crashes
        // the host instead of surviving deploy and only erroring on
        // the first protected request (reviewer T-0027 sec M-2).
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(JwtOptionsValidator.IsValid,
                "Jwt configuration invalid — see Issuer / SigningKeyBase64 / AccessTokenLifetime.")
            .ValidateOnStart();

        var acceptedAudiences = AcceptedAudiencesFor(audience);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Post-configure the JWT bearer options at runtime so the
        // signing key + issuer + lifetime params are read from the
        // FINAL bound JwtOptions (after every configuration source has
        // contributed).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwtAccessor) =>
            {
                var jwt = jwtAccessor.Value;
                var keyBytes = Convert.FromBase64String(jwt.SigningKeyBase64);
                var signingKey = new SymmetricSecurityKey(keyBytes) { KeyId = jwt.KeyId };

                // HS256-only with a local symmetric key — no metadata
                // endpoint is fetched, so RequireHttpsMetadata is moot
                // here. Inbound-request HTTPS is enforced by the
                // ingress proxy in prod, not by this flag.
                options.RequireHttpsMetadata = false;
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
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
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
    /// Audience policy per ADR 0012 §JWT — the runtime source of truth
    /// for the per-host audience table. ADR 0012 narrative + the
    /// T-0027 ticket point here; if they disagree, this method wins.
    /// </summary>
    internal static string[] AcceptedAudiencesFor(string hostAudience) =>
        hostAudience switch
        {
            MakablesHosts.Customer => [MakablesAudiences.Customer, MakablesAudiences.Admin],
            MakablesHosts.Maker    => [MakablesAudiences.Maker,    MakablesAudiences.Admin],
            MakablesHosts.Admin    => [MakablesAudiences.Admin],
            MakablesHosts.Public   => [MakablesAudiences.Customer, MakablesAudiences.Maker, MakablesAudiences.Admin],
            _ => throw new ArgumentException($"Unknown host audience '{hostAudience}'.", nameof(hostAudience)),
        };
}

/// <summary>
/// Validates <see cref="JwtOptions"/> at startup. The same shape checks
/// <see cref="Makables.Infra.Common.Auth.JwtIssuer"/> performs in its
/// constructor — duplicated here so a host that wires auth but never
/// issues tokens (e.g. <c>Web.Public</c>) still fails fast at boot.
/// </summary>
internal static class JwtOptionsValidator
{
    public static bool IsValid(JwtOptions options)
    {
        if (options is null) return false;
        if (string.IsNullOrWhiteSpace(options.Issuer)) return false;
        if (string.IsNullOrWhiteSpace(options.SigningKeyBase64)) return false;
        if (options.AccessTokenLifetime <= TimeSpan.Zero) return false;
        try
        {
            var bytes = Convert.FromBase64String(options.SigningKeyBase64);
            return bytes.Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// Reads JWT claims off the inbound HTTP request and exposes them as
/// <see cref="IUserSessionProvider"/>. Per ADR 0012 and role file
/// <c>docs/architecture/roles/user-session-provider.md</c>.
///
/// Fail-closed contract (T-0027 sec reviewer M-4): if the principal IS
/// authenticated, <see cref="GetUserId"/> throws when the required
/// <c>sub</c> / <c>NameIdentifier</c> claim is missing. The token is
/// trusted on the wire (signature validated by the JWT middleware) but
/// the claim shape is not — a future issuer bug or external IdP that
/// skips <c>sub</c> should fail at the boundary, not give downstream
/// handlers a <c>null</c> that resolves to a NRE on first use.
/// Unauthenticated requests return <c>null</c>; the pipeline already
/// gates protected endpoints with <c>[Authorize]</c>.
/// </summary>
internal sealed class HttpContextUserSessionProvider(IHttpContextAccessor accessor)
    : IUserSessionProvider
{
    public string? GetUserId()
    {
        var user = accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return null;
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(sub))
            throw new InvalidOperationException(
                "Authenticated principal is missing the 'sub' / NameIdentifier claim. " +
                "This indicates a token-issuer bug — Makables JWTs always include sub.");
        return sub;
    }

    public string? GetUserEmail() =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email);

    public string? GetUserCountryCode() =>
        accessor.HttpContext?.User.FindFirstValue(MakablesClaimTypes.CountryCode);
}
