using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Functions.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.Functions.Identity;

/// <summary>
/// Pins T-0114 <see cref="DataRetentionCleanupFunction"/> as a thin wrapper
/// over <see cref="IAuthRetentionStore.PurgeExpiredAsync"/>: the cutoff is
/// <c>UtcNow - ExpiredArtifactRetentionDays</c>, and a misconfigured
/// zero/negative window is clamped so the job can never delete an artifact the
/// moment it expires (the T-0113 clamp precedent).
/// </summary>
public sealed class DataRetentionCleanupFunctionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-16T03:00:00Z");
    private static readonly TimerInfo Timer = new();

    private readonly IAuthRetentionStore _store = Substitute.For<IAuthRetentionStore>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<DataRetentionCleanupFunction> _logger =
        Substitute.For<ILogger<DataRetentionCleanupFunction>>();

    public DataRetentionCleanupFunctionTests()
    {
        _clock.UtcNow.Returns(Now);
        _store.PurgeExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(AuthRetentionPurgeResult.Empty);
    }

    private DataRetentionCleanupFunction Build(int retentionDays) =>
        new(_store,
            Options.Create(new AuthRetentionOptions { ExpiredArtifactRetentionDays = retentionDays }),
            _clock,
            _logger);

    [Fact]
    public async Task Purges_artifacts_expired_before_now_minus_the_retention_window()
    {
        _store.PurgeExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new AuthRetentionPurgeResult(4, 2, 1));

        await Build(retentionDays: 30).RunAsync(Timer, CancellationToken.None);

        await _store.Received(1).PurgeExpiredAsync(Now.AddDays(-30), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-7)]
    public async Task Clamps_a_non_positive_retention_window_to_one_day(int configuredDays)
    {
        await Build(configuredDays).RunAsync(Timer, CancellationToken.None);

        await _store.Received(1).PurgeExpiredAsync(
            Now.AddDays(-DataRetentionCleanupFunction.MinimumRetentionDays),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Honours_a_configured_window_longer_than_the_default()
    {
        await Build(retentionDays: 90).RunAsync(Timer, CancellationToken.None);

        await _store.Received(1).PurgeExpiredAsync(Now.AddDays(-90), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Default_retention_window_is_thirty_days()
    {
        new AuthRetentionOptions().ExpiredArtifactRetentionDays.Should().Be(30);
    }

    [Fact]
    public async Task Passes_the_host_cancellation_token_through()
    {
        using var cts = new CancellationTokenSource();

        await Build(retentionDays: 30).RunAsync(Timer, cts.Token);

        await _store.Received(1).PurgeExpiredAsync(Arg.Any<DateTimeOffset>(), cts.Token);
    }
}
