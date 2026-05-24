using System.Security.Claims;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Makables.Infra.Common.Auth;

/// <summary>
/// HS256 JWT issuer per ADR 0012 §JWT structure.
///
/// Audience binding is strict: the caller passes the audience the token
/// will be used against. The audience claim is what
/// <c>Web.&lt;audience&gt;</c>'s authentication middleware enforces — a
/// customer token won't be accepted by the maker host (the host policies
/// allow only their own audience, plus <c>admin</c>).
/// </summary>
public sealed class JwtIssuer : IJwtIssuer
{
    private static readonly string[] AllowedAudiences = ["customer", "maker", "admin"];

    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;

    public JwtIssuer(IOptions<JwtOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.SigningKeyBase64))
            throw new InvalidOperationException("Jwt:SigningKeyBase64 is not configured.");
        var keyBytes = Convert.FromBase64String(_options.SigningKeyBase64);
        if (keyBytes.Length < 32)
            throw new InvalidOperationException("Jwt signing key must be at least 32 bytes after base64 decoding.");
        if (string.IsNullOrWhiteSpace(_options.Issuer))
            throw new InvalidOperationException("Jwt:Issuer is not configured.");
        if (_options.AccessTokenLifetime <= TimeSpan.Zero)
            throw new InvalidOperationException("Jwt:AccessTokenLifetime must be positive.");

        var key = new SymmetricSecurityKey(keyBytes) { KeyId = _options.KeyId };
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public AccessToken Issue(User user, string audience, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!Array.Exists(AllowedAudiences, a => a == audience))
            throw new ArgumentException($"Audience '{audience}' is not allowed. Expected one of: {string.Join(", ", AllowedAudiences)}.", nameof(audience));

        var jti = Ulid.NewUlid().ToString();
        var expiresAt = now + _options.AccessTokenLifetime;

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.Id,
            [JwtRegisteredClaimNames.Email] = user.Email,
            [JwtRegisteredClaimNames.Jti] = jti,
            ["role"] = user.Role.ToString().ToLowerInvariant(),
            [MakablesClaimTypes.CountryCode] = user.CountryCodePrimary,
            // NameIdentifier mirrors `sub` so the standard ASP.NET claim
            // helpers (e.g. ClaimTypes.NameIdentifier in middleware) work
            // without a custom mapping.
            [ClaimTypes.NameIdentifier] = user.Id,
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = _signingCredentials,
        };

        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(descriptor);
        return new AccessToken(token, expiresAt, jti);
    }
}
