namespace Makables.Core.Domain.Common;

/// <summary>
/// Abstracts "now" for testability. Real implementation
/// (<c>SystemClock</c> in <c>Infra.Common</c>) returns
/// <see cref="DateTimeOffset.UtcNow"/>; tests inject a fake clock.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
