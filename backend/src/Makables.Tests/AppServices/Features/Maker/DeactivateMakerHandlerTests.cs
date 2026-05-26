using FluentAssertions;
using Makables.Core.AppServices.Features.Maker;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Maker;

/// <summary>T-0034 — pins the DeactivateMaker admin command (US-admin-0004).</summary>
public class DeactivateMakerHandlerTests
{
    private static readonly DateTimeOffset SnapshotAt = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DeactivatedAt = new(2026, 5, 26, 9, 0, 0, TimeSpan.Zero);

    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly DeactivateMaker.Handler _sut;

    public DeactivateMakerHandlerTests()
    {
        _clock.UtcNow.Returns(DeactivatedAt);
        _session.GetUserId().Returns("admin-1");
        _sut = new DeactivateMaker.Handler(_makers, _session, _clock);
    }

    private static Makables.Core.Domain.Makers.Maker ExistingMaker() =>
        Makables.Core.Domain.Makers.Maker.Create(
            id: "maker-1",
            userId: "user-1",
            registrationNumber: "27074358",
            vatId: null,
            companyName: "Avast s.r.o.",
            legalForm: null,
            registeredAddressId: "addr-1",
            incorporatedOn: null,
            isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: SnapshotAt,
            snapshotIsStale: false,
            countryCode: "CZ");

    [Fact]
    public async Task Returns_NotFound_when_maker_is_missing()
    {
        _makers.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((Makables.Core.Domain.Makers.Maker?)null);

        var result = await _sut.Handle(new DeactivateMaker.Command("missing", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Soft_deletes_an_active_maker_and_stamps_audit_fields()
    {
        var maker = ExistingMaker();
        _makers.GetByIdAsync("maker-1", Arg.Any<CancellationToken>()).Returns(maker);

        var result = await _sut.Handle(new DeactivateMaker.Command("maker-1", "policy violation"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        maker.IsActive.Should().BeFalse();
        maker.DeactivatedBy.Should().Be("admin-1");
        maker.DeactivatedAt.Should().Be(DeactivatedAt);
    }

    [Fact]
    public async Task Already_deactivated_surfaces_as_NotFound_via_soft_delete_query_filter()
    {
        // GetByIdAsync respects the global soft-delete filter, so an
        // already-deactivated maker is invisible to the admin command.
        // T-0034 sec reviewer n-1: the previous "MakerNotActive" branch
        // in the handler was unreachable.
        _makers.GetByIdAsync("maker-1", Arg.Any<CancellationToken>()).Returns((Makables.Core.Domain.Makers.Maker?)null);

        var result = await _sut.Handle(new DeactivateMaker.Command("maker-1", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Returns_Unauthorized_when_session_has_no_user()
    {
        // Fail-closed if the [Authorize] gate misfires — destructive
        // actions must never be attributed to a "system" pseudo-user.
        // T-0034 sec reviewer m-1.
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(new DeactivateMaker.Command("maker-1", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _makers.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Command_carries_admin_audit_metadata()
    {
        var cmd = new DeactivateMaker.Command("maker-1", "policy violation");
        cmd.ActionCode.Should().Be("maker.deactivate");
        cmd.TargetEntity.Should().Be("maker");
        cmd.TargetId.Should().Be("maker-1");
    }
}
