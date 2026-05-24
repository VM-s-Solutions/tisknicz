using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Outbox;

namespace Makables.Tests.AppServices.Common;

/// <summary>
/// Pins the T-0029 backoff curve. The schedule (1m, 5m, 15m, 1h, 6h,
/// 24h, then stall) is the single source of truth that
/// <c>SendEmailHandler</c> and <c>OutboxDispatcher</c> both read from.
/// </summary>
public class OutboxRetryPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    [InlineData(4, 60)]
    [InlineData(5, 360)]
    [InlineData(6, 1440)]
    public void Transient_failures_schedule_per_attempt_curve(int retryCount, int expectedMinutes)
    {
        var next = OutboxRetryPolicy.NextAttempt(OutboxErrorKind.Transient, retryCount, Now);

        next.Should().NotBeNull();
        next!.Value.Should().Be(Now + TimeSpan.FromMinutes(expectedMinutes));
    }

    [Fact]
    public void Transient_stalls_after_max_attempts()
    {
        var next = OutboxRetryPolicy.NextAttempt(
            OutboxErrorKind.Transient,
            OutboxRetryPolicy.MaxTransientAttempts + 1,
            Now);

        next.Should().BeNull();
    }

    [Theory]
    [InlineData(OutboxErrorKind.Permanent)]
    [InlineData(OutboxErrorKind.Configuration)]
    [InlineData(OutboxErrorKind.Unknown)]
    public void Non_transient_kinds_stall_on_the_first_failed_attempt(OutboxErrorKind kind)
    {
        var next = OutboxRetryPolicy.NextAttempt(kind, newRetryCount: 1, Now);
        next.Should().BeNull();
    }

    [Fact]
    public void None_is_not_a_failure_classification()
    {
        var act = () => OutboxRetryPolicy.NextAttempt(OutboxErrorKind.None, 1, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_retryCount_stalls(int retryCount)
    {
        var next = OutboxRetryPolicy.NextAttempt(OutboxErrorKind.Transient, retryCount, Now);
        next.Should().BeNull();
    }
}
