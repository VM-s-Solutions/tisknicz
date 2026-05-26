using FluentAssertions;
using Makables.Core.AppServices.Features.Maker;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Maker;

/// <summary>
/// T-0034 — pins UpdateMakerProfile self-service shape: user resolved
/// from session (NOT from the request), 404 if the caller has no maker
/// row, snapshot fields untouched.
/// </summary>
public class UpdateMakerProfileHandlerTests
{
    private static readonly DateTimeOffset SnapshotAt = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly UpdateMakerProfile.Handler _sut;

    public UpdateMakerProfileHandlerTests()
    {
        _sut = new UpdateMakerProfile.Handler(_makers, _session);
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

        var result = await _sut.Handle(new UpdateMakerProfile.Command("Bio", null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _makers.DidNotReceive().GetByUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_NotFound_when_caller_has_no_maker_row()
    {
        _session.GetUserId().Returns("user-1");
        _makers.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns((Makables.Core.Domain.Makers.Maker?)null);

        var result = await _sut.Handle(new UpdateMakerProfile.Command("Bio", null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Patches_only_supplied_fields_and_leaves_snapshot_untouched()
    {
        var maker = ExistingMaker();
        _session.GetUserId().Returns("user-1");
        _makers.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(maker);

        var result = await _sut.Handle(
            new UpdateMakerProfile.Command(
                Bio: "Vyrábím keramiku",
                BankAccount: "2000145399/0100",
                PersonalPickupEnabled: true,
                PickupNote: "Po dohodě"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        maker.Bio.Should().Be("Vyrábím keramiku");
        maker.BankAccount.Should().Be("2000145399/0100");
        maker.PersonalPickupEnabled.Should().BeTrue();
        maker.PickupNote.Should().Be("Po dohodě");
        maker.CompanyName.Should().Be("Avast Software s.r.o.", "snapshot fields are read-only via this command");
        maker.RegistrationNumber.Should().Be("27074358");
    }

    [Fact]
    public async Task Resolves_user_from_session_only_never_from_request_input()
    {
        // The Command has no UserId/MakerId field by design (IDOR shield).
        // This test pins that contract — adding such a field is a regression.
        var properties = typeof(UpdateMakerProfile.Command).GetProperties();
        properties.Should().NotContain(p =>
            p.Name.Equals("UserId", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals("MakerId", StringComparison.OrdinalIgnoreCase));
        await Task.CompletedTask;
    }
}
