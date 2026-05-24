using Makables.Core.Domain.Common;

namespace Makables.TestUtilities;

/// <summary>
/// Frozen <see cref="IClock"/> for handler tests. Replaces the 10
/// nested copies that accumulated across the auth feature tests
/// (T-0025 code-quality review Mi-1).
///
/// <see cref="DefaultNow"/> matches the canonical date used by the
/// existing handler tests so the move is a no-op for assertions that
/// compare absolute timestamps.
/// </summary>
public sealed class FakeClock(DateTimeOffset now) : IClock
{
    /// <summary>
    /// Canonical now used by handler tests: 2026-05-24 12:00:00 UTC.
    /// </summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 5, 24, 12, 0, 0, TimeSpan.Zero);

    public FakeClock() : this(DefaultNow) { }

    public DateTimeOffset UtcNow { get; } = now;
}
