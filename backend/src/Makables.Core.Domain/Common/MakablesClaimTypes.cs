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

    /// <summary>
    /// Unix seconds (UTC) when the user confirmed their email, or absent
    /// if not yet confirmed. T-0063 adds this so the customer-host
    /// <c>RequireEmailConfirmedMiddleware</c> can gate every
    /// authenticated endpoint without a per-request DB hit. The claim
    /// follows the existing pattern (role / country_code in the token)
    /// rather than the alternative of caching <c>IUserRepository</c>
    /// per request: cheaper, and the 15-minute access-token lifetime
    /// caps how long a freshly-confirmed user has to wait for refresh.
    /// Absent for unconfirmed users so a forged claim of <c>0</c> on a
    /// future evil token still fails closed.
    /// </summary>
    public const string EmailConfirmedAt = "email_confirmed_at";
}
