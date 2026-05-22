namespace Makables.Core.AppServices.Abstractions;

/// <summary>
/// Read-side abstraction over the current authenticated user, sourced from
/// the JWT claims of the inbound HTTP request (in Web hosts) or the
/// configured "system" identity (in Functions / cron). Per ADR 0012
/// (authentication) and patterns §A.17.
///
/// Returns null for unauthenticated callers; consumers that require a user
/// (e.g. handlers behind <c>[Authorize]</c>) can safely null-bang.
/// </summary>
public interface IUserSessionProvider
{
    /// <summary>
    /// The authenticated user id (User.Id ULID), or null for anonymous.
    /// In Functions/cron context, this is the literal string "system".
    /// </summary>
    string? GetUserId();

    /// <summary>
    /// The authenticated user's email, or null for anonymous.
    /// </summary>
    string? GetUserEmail();

    /// <summary>
    /// The authenticated user's primary country code (ISO 3166-1 alpha-2),
    /// or null for anonymous. Drives country-scoped queries when the request
    /// itself doesn't carry an explicit country.
    /// </summary>
    string? GetUserCountryCode();
}
