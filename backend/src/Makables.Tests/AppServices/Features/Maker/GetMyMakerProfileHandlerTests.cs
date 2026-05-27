using FluentAssertions;
using Makables.Core.AppServices.Features.Maker;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Maker;

/// <summary>T-0036 — pins GetMyMakerProfile query shape and session resolution.</summary>
public class GetMyMakerProfileHandlerTests
{
    private static readonly DateTimeOffset SnapshotAt = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly GetMyMakerProfile.Handler _sut;

    public GetMyMakerProfileHandlerTests()
    {
        _session.GetUserId().Returns("user-1");
        _sut = new GetMyMakerProfile.Handler(_makers, _session);
    }

    private static Makables.Core.Domain.Makers.Maker ExistingMaker() =>
        Makables.Core.Domain.Makers.Maker.Create(
            id: "maker-1",
            userId: "user-1",
            registrationNumber: "27074358",
            vatId: "CZ27074358",
            companyName: "Avast Software s.r.o.",
            legalForm: "s.r.o.",
            registeredAddressId: "addr-1",
            incorporatedOn: new DateOnly(2006, 9, 4),
            isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: SnapshotAt,
            snapshotIsStale: false,
            countryCode: "CZ");

    [Fact]
    public async Task Returns_Unauthorized_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(new GetMyMakerProfile.Query(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _makers.DidNotReceive().GetByUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_NotFound_when_caller_has_no_maker_row()
    {
        _makers.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns((Makables.Core.Domain.Makers.Maker?)null);

        var result = await _sut.Handle(new GetMyMakerProfile.Query(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Happy_path_returns_full_maker_shape_in_one_roundtrip()
    {
        var maker = ExistingMaker();
        maker.UpdateProfile(
            bio: "Vyrábím keramiku",
            bankAccount: "2000145399/0100",
            personalPickupEnabled: true,
            pickupNote: "Po dohodě");
        _makers.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(maker);

        var result = await _sut.Handle(new GetMyMakerProfile.Query(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MakerId.Should().Be("maker-1");
        result.Value.RegistrationNumber.Should().Be("27074358");
        result.Value.CompanyName.Should().Be("Avast Software s.r.o.");
        result.Value.Bio.Should().Be("Vyrábím keramiku");
        result.Value.BankAccount.Should().Be("2000145399/0100");
        result.Value.PersonalPickupEnabled.Should().BeTrue();
        result.Value.PickupNote.Should().Be("Po dohodě");
        result.Value.SnapshotIsStale.Should().BeFalse();
    }
}
