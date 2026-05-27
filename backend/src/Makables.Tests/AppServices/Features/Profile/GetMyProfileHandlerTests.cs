using FluentAssertions;
using Makables.Core.AppServices.Features.Profile;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Profile;

/// <summary>T-0036 — pins GetMyProfile query shape and session resolution.</summary>
public class GetMyProfileHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly GetMyProfile.Handler _sut;

    public GetMyProfileHandlerTests()
    {
        _session.GetUserId().Returns("user-1");
        _sut = new GetMyProfile.Handler(_users, _session);
    }

    [Fact]
    public async Task Returns_Unauthorized_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(new GetMyProfile.Query(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _users.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_NotFound_when_user_is_missing()
    {
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _sut.Handle(new GetMyProfile.Query(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Happy_path_returns_user_shape()
    {
        var user = User.Create(
            id: "user-1",
            email: "karel@example.cz",
            role: UserRole.Customer,
            fullName: "Karel Novák",
            countryCodePrimary: "CZ",
            emailAlreadyConfirmed: true,
            confirmedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(user);

        var result = await _sut.Handle(new GetMyProfile.Query(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.UserId.Should().Be("user-1");
        result.Value.Email.Should().Be("karel@example.cz");
        result.Value.FullName.Should().Be("Karel Novák");
        result.Value.Role.Should().Be(UserRole.Customer);
        result.Value.EmailConfirmed.Should().BeTrue();
    }
}
