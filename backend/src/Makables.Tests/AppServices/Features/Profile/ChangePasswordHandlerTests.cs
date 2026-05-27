using FluentAssertions;
using Makables.Core.AppServices.Features.Profile;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Profile;

/// <summary>
/// T-0036 ChangePassword pins: Unauthorized when session has no user,
/// NotFound when user is missing, Unauthorized when current password
/// is wrong, Unauthorized when the user has no password (OAuth-only
/// account), happy-path re-hashes and stores via SetPasswordHash.
/// </summary>
public class ChangePasswordHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly ChangePassword.Handler _sut;

    public ChangePasswordHandlerTests()
    {
        _session.GetUserId().Returns("user-1");
        _sut = new ChangePassword.Handler(_users, _hasher, _session);
    }

    private static User ExistingUserWithPassword() =>
        User.Create(
            id: "user-1",
            email: "user@example.cz",
            role: UserRole.Customer,
            fullName: "Karel Novák",
            countryCodePrimary: "CZ",
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");

    private static User ExistingOAuthOnlyUser()
    {
        // Customer that signed up via Google has no PasswordHash.
        var user = User.Create(
            id: "user-1",
            email: "oauth@example.cz",
            role: UserRole.Customer,
            fullName: "Anna OAuth",
            countryCodePrimary: "CZ",
            passwordHash: null);
        return user;
    }

    [Fact]
    public async Task Returns_Unauthorized_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(
            new ChangePassword.Command("old-pass", "new-pass-123"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _users.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_NotFound_when_user_is_missing()
    {
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _sut.Handle(
            new ChangePassword.Command("old-pass", "new-pass-123"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Returns_AuthCurrentPasswordWrong_when_user_has_no_password_hash()
    {
        // OAuth-only accounts: no password to verify. Return the same
        // auth.currentPasswordWrong as a mismatch so the password-presence
        // is not enumerable.
        var user = ExistingOAuthOnlyUser();
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(user);

        var result = await _sut.Handle(
            new ChangePassword.Command("old-pass", "new-pass-123"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthCurrentPasswordWrong);
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        _hasher.DidNotReceive().Verify(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Returns_AuthCurrentPasswordWrong_when_current_password_does_not_verify()
    {
        var user = ExistingUserWithPassword();
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("wrong-pass", user.PasswordHash!).Returns(false);

        var result = await _sut.Handle(
            new ChangePassword.Command("wrong-pass", "new-pass-123"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthCurrentPasswordWrong);
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        _hasher.DidNotReceive().Hash(Arg.Any<string>());
    }

    [Fact]
    public async Task Happy_path_verifies_current_then_stores_new_hash()
    {
        var user = ExistingUserWithPassword();
        var originalHash = user.PasswordHash;
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("correct-old", originalHash!).Returns(true);
        _hasher.Hash("new-pass-123").Returns("argon2id$v=19$m=8192,t=1,p=1$CCCC$DDDD");

        var result = await _sut.Handle(
            new ChangePassword.Command("correct-old", "new-pass-123"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().Be("argon2id$v=19$m=8192,t=1,p=1$CCCC$DDDD");
        user.PasswordHash.Should().NotBe(originalHash);
    }

    [Fact]
    public void Command_has_no_userId_or_makerId_field()
    {
        // IDOR shield pin — target is resolved from session, NOT from
        // request input. Adding such a field is a regression.
        var properties = typeof(ChangePassword.Command).GetProperties();
        properties.Should().NotContain(p =>
            p.Name.Equals("UserId", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals("MakerId", StringComparison.OrdinalIgnoreCase));
    }
}
