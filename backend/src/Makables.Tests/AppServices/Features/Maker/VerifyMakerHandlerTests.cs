using FluentAssertions;
using Makables.Core.AppServices.Features.Maker;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Maker;

/// <summary>T-0034 — pins the VerifyMaker admin command (US-admin-0003).</summary>
public class VerifyMakerHandlerTests
{
    private static readonly DateTimeOffset SnapshotAt = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly VerifyMaker.Handler _sut;

    public VerifyMakerHandlerTests()
    {
        _sut = new VerifyMaker.Handler(_makers);
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

        var result = await _sut.Handle(new VerifyMaker.Command("missing", Notes: null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Verifies_an_unverified_maker()
    {
        var maker = ExistingMaker();
        _makers.GetByIdAsync("maker-1", Arg.Any<CancellationToken>()).Returns(maker);

        var result = await _sut.Handle(new VerifyMaker.Command("maker-1", "reviewed photos"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        maker.IsVerified.Should().BeTrue();
    }

    [Fact]
    public async Task Rejects_a_double_verify_with_MakerAlreadyVerified()
    {
        var maker = ExistingMaker();
        maker.MarkVerified();
        _makers.GetByIdAsync("maker-1", Arg.Any<CancellationToken>()).Returns(maker);

        var result = await _sut.Handle(new VerifyMaker.Command("maker-1", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.MakerAlreadyVerified);
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void Command_carries_admin_audit_metadata()
    {
        var cmd = new VerifyMaker.Command("maker-1", "notes");
        cmd.ActionCode.Should().Be("maker.verify");
        cmd.TargetEntity.Should().Be("maker");
        cmd.TargetId.Should().Be("maker-1");
        cmd.Notes.Should().Be("notes");
    }
}
