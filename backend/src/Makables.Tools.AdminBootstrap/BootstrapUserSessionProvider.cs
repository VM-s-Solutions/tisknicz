using Makables.Core.Domain.Common;

namespace Makables.Tools.AdminBootstrap;

/// <summary>
/// Session identity for the bootstrap process. The audit interceptor stamps
/// <c>created_by</c> with this actor, so the first admin row is attributable to
/// the tool rather than to a user id that does not exist yet.
/// </summary>
internal sealed class BootstrapUserSessionProvider : IUserSessionProvider
{
    public string? GetUserId() => "admin-bootstrap";

    public string? GetUserEmail() => null;

    public string? GetUserCountryCode() => "CZ";
}
