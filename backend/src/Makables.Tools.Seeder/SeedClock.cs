using Makables.Core.Domain.Common;

namespace Makables.Tools.Seeder;

/// <summary>
/// Mutable <see cref="IClock"/> for the seeder. The seed script sets
/// <see cref="UtcNow"/> before each order state transition so the
/// per-transition timestamps (PaidAt, ShippedAt, …) land at realistic
/// historical moments instead of all at run time.
/// </summary>
public sealed class SeedClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}
