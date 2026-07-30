using FluentAssertions;
using Makables.Core.AppServices.Features.Profile;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using NSubstitute;
using DomainMaker = Makables.Core.Domain.Makers.Maker;

namespace Makables.Tests.AppServices.Features.Profile;

/// <summary>
/// Unit tests for the self-service <see cref="DeleteMyAccount"/> handler:
/// the retype gate, the in-flight interlock, the soft-delete of both
/// aggregates, and the logout-all token revocation.
/// </summary>
public sealed class DeleteMyAccountHandlerTests
{
    private const string UserId = "user-1";
    private const string UserEmail = "anna@example.cz";
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly DeleteMyAccount.Handler _sut;

    public DeleteMyAccountHandlerTests()
    {
        _session.GetUserId().Returns(UserId);
        _clock.UtcNow.Returns(Now);
        _refreshTokens.GetActiveByUserAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([]);
        _sut = new DeleteMyAccount.Handler(_users, _makers, _orders, _refreshTokens, _session, _clock);
    }

    private static User BuildUser() =>
        User.Create(UserId, UserEmail, UserRole.Customer, "Anna", "CZ",
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");

    private static DomainMaker BuildMaker() =>
        DomainMaker.Create("maker-1", UserId, "12345678", null, "Anna s.r.o.", "s.r.o.",
            "addr-1", null, isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: Now, snapshotIsStale: false, countryCode: "CZ");

    private static DeleteMyAccount.Command Command(string? confirmedEmail = null) =>
        new(confirmedEmail ?? UserEmail);

    [Fact]
    public async Task Happy_path_soft_deletes_the_user()
    {
        var user = BuildUser();
        _users.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _orders.HasInFlightOrderForUserAsync(UserId, Arg.Any<string?>(), Arg.Any<IReadOnlyCollection<OrderState>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.Handle(Command(), default);

        result.IsSuccess.Should().BeTrue();
        user.IsActive.Should().BeFalse();
        user.DeactivatedBy.Should().Be(UserId);
        user.DeactivatedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Maker_profile_is_soft_deleted_together_with_the_user()
    {
        var user = BuildUser();
        var maker = BuildMaker();
        _users.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _makers.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(maker);
        _orders.HasInFlightOrderForUserAsync(UserId, maker.Id, Arg.Any<IReadOnlyCollection<OrderState>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.Handle(Command(), default);

        result.IsSuccess.Should().BeTrue();
        user.IsActive.Should().BeFalse();
        maker.IsActive.Should().BeFalse();
        maker.DeactivatedBy.Should().Be(UserId);
    }

    [Fact]
    public async Task All_active_refresh_tokens_are_revoked()
    {
        var user = BuildUser();
        var tokenA = RefreshToken.IssueNew("rt-1", UserId, new string('a', 64), "fam-1",
            Now.AddDays(30), "CZ", null, null);
        var tokenB = RefreshToken.IssueNew("rt-2", UserId, new string('b', 64), "fam-2",
            Now.AddDays(30), "CZ", null, null);
        _users.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _orders.HasInFlightOrderForUserAsync(UserId, Arg.Any<string?>(), Arg.Any<IReadOnlyCollection<OrderState>>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _refreshTokens.GetActiveByUserAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([tokenA, tokenB]);

        var result = await _sut.Handle(Command(), default);

        result.IsSuccess.Should().BeTrue();
        tokenA.IsActiveAt(Now.AddSeconds(1)).Should().BeFalse();
        tokenB.IsActiveAt(Now.AddSeconds(1)).Should().BeFalse();
    }

    [Fact]
    public async Task Missing_session_user_returns_Unauthorized()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(Command(), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Already_deactivated_user_surfaces_as_UserNotFound()
    {
        // The filtered load hides a soft-deleted row — a stale JWT cannot
        // re-run the deletion.
        _users.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _sut.Handle(Command(), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.UserNotFound);
    }

    [Fact]
    public async Task Email_mismatch_returns_deleteConfirmationMismatch_and_nothing_is_deactivated()
    {
        var user = BuildUser();
        _users.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _sut.Handle(Command(confirmedEmail: "wrong@example.cz"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.UserDeleteConfirmationMismatch);
        user.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Email_match_is_case_and_whitespace_insensitive()
    {
        var user = BuildUser();
        _users.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _orders.HasInFlightOrderForUserAsync(UserId, Arg.Any<string?>(), Arg.Any<IReadOnlyCollection<OrderState>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.Handle(Command(confirmedEmail: "  ANNA@EXAMPLE.CZ  "), default);

        result.IsSuccess.Should().BeTrue();
        user.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task In_flight_order_blocks_deletion_and_nothing_is_deactivated()
    {
        var user = BuildUser();
        var maker = BuildMaker();
        _users.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _makers.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(maker);
        _orders.HasInFlightOrderForUserAsync(UserId, maker.Id, Arg.Any<IReadOnlyCollection<OrderState>>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.Handle(Command(), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.UserCannotDeleteWithInFlightOrders);
        user.IsActive.Should().BeTrue();
        maker.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Validator_rejects_empty_and_overlong_ConfirmedEmail()
    {
        var validator = new DeleteMyAccount.Validator();

        validator.Validate(new DeleteMyAccount.Command("")).IsValid.Should().BeFalse();
        validator.Validate(new DeleteMyAccount.Command(new string('x', 201))).IsValid.Should().BeFalse();
        validator.Validate(new DeleteMyAccount.Command(UserEmail)).IsValid.Should().BeTrue();
    }
}
