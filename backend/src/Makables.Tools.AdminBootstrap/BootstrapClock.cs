using Makables.Core.Domain.Common;

namespace Makables.Tools.AdminBootstrap;

/// <summary>
/// Wall-clock <see cref="IClock"/> for the bootstrap process. Unlike the
/// seeder's mutable clock there is nothing to backdate here — the account is
/// created now, and its audit entry should say so.
/// </summary>
internal sealed class BootstrapClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
