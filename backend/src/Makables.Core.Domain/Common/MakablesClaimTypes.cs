namespace Makables.Core.Domain.Common;

/// <summary>
/// Names of Makables-specific JWT claims that are not part of
/// <see cref="System.Security.Claims.ClaimTypes"/>. Per ADR 0012 (auth) and
/// ADR 0023 §4 (observability needs <c>country_code</c> on every log entry).
/// </summary>
public static class MakablesClaimTypes
{
    /// <summary>ISO-3166-1 alpha-2 of the user's home country (issued by T-0021's JwtIssuer).</summary>
    public const string CountryCode = "country_code";
}
