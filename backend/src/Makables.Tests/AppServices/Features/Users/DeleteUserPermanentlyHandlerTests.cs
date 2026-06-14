using FluentAssertions;
using Makables.Core.AppServices.Features.Users;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Privacy;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Users;

/// <summary>
/// Unit tests for the T-0110 <see cref="DeleteUserPermanently"/> handler.
/// The erasure matrix itself is owned by the seam (asserted in the
/// integration tests); these pin the handler's gates + the no-Silent-Success
/// rule.
/// </summary>
public sealed class DeleteUserPermanentlyHandlerTests
{
    private const string UserId = "user-1";
    private const string UserEmail = "anna@example.cz";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUserDataDeletionService _deletion = Substitute.For<IUserDataDeletionService>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly DeleteUserPermanently.Handler _sut;

    public DeleteUserPermanentlyHandlerTests()
    {
        _session.GetUserId().Returns("admin-1");
        _deletion.EraseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success());
        _sut = new DeleteUserPermanently.Handler(_users, _makers, _orders, _deletion, _session);
    }

    private static User BuildUser() =>
        User.Create(UserId, UserEmail, UserRole.Customer, "Anna", "CZ",
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");

    private DeleteUserPermanently.Command Command(string? confirmedEmail = null) =>
        new(UserId, confirmedEmail ?? UserEmail, "GDPR-2026-014");

    [Fact]
    public async Task Happy_path_invokes_erasure_seam_once_and_returns_erased_id()
    {
        _users.GetByIdIgnoringFiltersAsync(UserId, Arg.Any<CancellationToken>()).Returns(BuildUser());
        _orders.HasInFlightOrderForUserAsync(UserId, Arg.Any<string?>(), Arg.Any<IReadOnlyCollection<OrderState>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.Handle(Command(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ErasedUserId.Should().Be(UserId);
        await _deletion.Received(1).EraseAsync(UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_session_user_returns_Unauthorized_and_seam_not_called()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(Command(), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _deletion.DidNotReceive().EraseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task User_not_found_returns_UserNotFound_and_seam_not_called()
    {
        _users.GetByIdIgnoringFiltersAsync(UserId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _sut.Handle(Command(), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.UserNotFound);
        await _deletion.DidNotReceive().EraseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Email_mismatch_returns_deleteConfirmationMismatch_and_seam_not_called()
    {
        _users.GetByIdIgnoringFiltersAsync(UserId, Arg.Any<CancellationToken>()).Returns(BuildUser());

        var result = await _sut.Handle(Command(confirmedEmail: "wrong@example.cz"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.UserDeleteConfirmationMismatch);
        await _deletion.DidNotReceive().EraseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Email_match_is_case_and_whitespace_insensitive()
    {
        _users.GetByIdIgnoringFiltersAsync(UserId, Arg.Any<CancellationToken>()).Returns(BuildUser());
        _orders.HasInFlightOrderForUserAsync(UserId, Arg.Any<string?>(), Arg.Any<IReadOnlyCollection<OrderState>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.Handle(Command(confirmedEmail: "  ANNA@EXAMPLE.CZ  "), default);

        result.IsSuccess.Should().BeTrue();
        await _deletion.Received(1).EraseAsync(UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task In_flight_order_blocks_erasure_cannotDeleteWithInFlightOrders()
    {
        _users.GetByIdIgnoringFiltersAsync(UserId, Arg.Any<CancellationToken>()).Returns(BuildUser());
        _orders.HasInFlightOrderForUserAsync(UserId, Arg.Any<string?>(), Arg.Any<IReadOnlyCollection<OrderState>>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.Handle(Command(), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.UserCannotDeleteWithInFlightOrders);
        await _deletion.DidNotReceive().EraseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_in_flight_orders_proceeds_to_the_seam()
    {
        _users.GetByIdIgnoringFiltersAsync(UserId, Arg.Any<CancellationToken>()).Returns(BuildUser());
        _orders.HasInFlightOrderForUserAsync(UserId, Arg.Any<string?>(), Arg.Any<IReadOnlyCollection<OrderState>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.Handle(Command(), default);

        result.IsSuccess.Should().BeTrue();
        await _deletion.Received(1).EraseAsync(UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Re_call_after_erasure_returns_UserNotFound_not_silent_success()
    {
        // The first erase removed the row; a second call finds nothing.
        _users.GetByIdIgnoringFiltersAsync(UserId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _sut.Handle(Command(), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.UserNotFound);
    }

    [Fact]
    public void Validator_rejects_empty_ConfirmedEmail_and_empty_Reason_and_long_Reason()
    {
        var validator = new DeleteUserPermanently.Validator();

        validator.Validate(new DeleteUserPermanently.Command(UserId, "", "r")).IsValid.Should().BeFalse();
        validator.Validate(new DeleteUserPermanently.Command(UserId, UserEmail, "")).IsValid.Should().BeFalse();
        validator.Validate(new DeleteUserPermanently.Command(UserId, UserEmail, new string('x', 2001)))
            .IsValid.Should().BeFalse();
        validator.Validate(new DeleteUserPermanently.Command(UserId, UserEmail, "GDPR-2026-014"))
            .IsValid.Should().BeTrue();
    }
}
