namespace Makables.Core.Domain.Common;

/// <summary>
/// Read-side abstraction over the current authenticated user, sourced from
/// the JWT claims of the inbound HTTP request (in Web hosts) or the
/// configured "system" identity (in Functions / cron). Per ADR 0012
/// (authentication) and patterns §A.17.
///
/// Returns null for unauthenticated callers; consumers that require a user
/// (e.g. handlers behind <c>[Authorize]</c>) can safely null-bang.
///
/// Lives in <c>Core.Domain</c> rather than <c>Core.AppServices</c> because
/// <c>Infra.Database</c>'s audit interceptor consumes it directly, and
/// <c>Infra.*</c> does not reference <c>Core.AppServices</c> per ADR 0001.
/// </summary>
public interface IUserSessionProvider
{
    /// <summary>
    /// The authenticated user id, or null for anonymous. In Functions/cron
    /// context, the interceptor falls back to the literal string "system"
    /// for audit stamps.
    /// </summary>
    string? GetUserId();

    /// <summary>The authenticated user's email, or null for anonymous.</summary>
    string? GetUserEmail();

    /// <summary>
    /// The authenticated user's primary country code (ISO 3166-1 alpha-2),
    /// or null for anonymous. Drives country-scoped queries when the request
    /// itself doesn't carry an explicit country.
    /// </summary>
    string? GetUserCountryCode();
}
