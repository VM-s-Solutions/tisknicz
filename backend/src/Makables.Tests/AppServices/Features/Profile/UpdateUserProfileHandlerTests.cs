using FluentAssertions;
using Makables.Core.AppServices.Features.Profile;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Profile;

/// <summary>T-0036 — pins UpdateUserProfile (full name + phone).</summary>
public class UpdateUserProfileHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly UpdateUserProfile.Handler _sut;

    public UpdateUserProfileHandlerTests()
    {
        _session.GetUserId().Returns("user-1");
        _sut = new UpdateUserProfile.Handler(_users, _session);
    }

    private static User ExistingUser() =>
        User.Create(
            id: "user-1",
            email: "karel@example.cz",
            role: UserRole.Customer,
            fullName: "Karel Novák",
            countryCodePrimary: "CZ");

    [Fact]
    public async Task Returns_Unauthorized_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(
            new UpdateUserProfile.Command("Karel Novák", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _users.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_NotFound_when_user_is_missing()
    {
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _sut.Handle(
            new UpdateUserProfile.Command("New Name", "+420 123 456 789"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Happy_path_updates_full_name_and_phone()
    {
        var user = ExistingUser();
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(user);

        var result = await _sut.Handle(
            new UpdateUserProfile.Command("Karel Novák ml.", "+420 123 456 789"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.FullName.Should().Be("Karel Novák ml.");
        user.Phone.Should().Be("+420 123 456 789");
    }

    [Fact]
    public void Command_has_no_userId_or_makerId_field()
    {
        // IDOR shield — target resolved from session, not request input.
        var properties = typeof(UpdateUserProfile.Command).GetProperties();
        properties.Should().NotContain(p =>
            p.Name.Equals("UserId", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals("MakerId", StringComparison.OrdinalIgnoreCase));
    }
}
