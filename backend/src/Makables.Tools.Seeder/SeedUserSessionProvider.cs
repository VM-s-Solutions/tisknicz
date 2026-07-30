using Makables.Core.Domain.Common;

namespace Makables.Tools.Seeder;

/// <summary>
/// Session identity for the seeder process. The audit interceptor stamps
/// rows the seed script did not stamp explicitly via
/// <c>MarkCreated</c>/<c>MarkUpdated</c> with this actor, matching the
/// <c>created_by = 'seed'</c> convention of the migration-based reference
/// seeds.
/// </summary>
public sealed class SeedUserSessionProvider : IUserSessionProvider
{
    public string? GetUserId() => "seed";

    public string? GetUserEmail() => null;

    public string? GetUserCountryCode() => "CZ";
}
