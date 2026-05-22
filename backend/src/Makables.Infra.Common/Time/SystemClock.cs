using Makables.Core.Domain.Common;

namespace Makables.Infra.Common.Time;

/// <summary>
/// Production implementation of <see cref="IClock"/> backed by
/// <see cref="DateTimeOffset.UtcNow"/>. Registered as a singleton.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
