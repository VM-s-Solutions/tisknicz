using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Auth;

public class RegisterHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IOneTimeTokenRepository _tokens = Substitute.For<IOneTimeTokenRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly Register.Handler _handler;

    public RegisterHandlerTests()
    {
        _ids.Next().Returns("user-fresh-01");
        _hasher.Hash(Arg.Any<string>()).Returns("argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        _handler = new Register.Handler(_users, _tokens, _outbox, _hasher, _ids, _clock, NullLogger<Register.Handler>.Instance);
    }

    [Fact]
    public async Task Returns_conflict_when_email_already_exists()
    {
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _handler.Handle(
            new Register.Command("anna@example.cz", "abcd1234567", "Anna", "CZ", UserRole.Customer),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthEmailAlreadyExists);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        _tokens.DidNotReceive().Add(Arg.Any<OneTimeToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Rejects_admin_role_on_public_registration()
    {
        var result = await _handler.Handle(
            new Register.Command("ops@example.cz", "abcd1234567", "Ops", "CZ", UserRole.Admin),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthForbidden);
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        _users.DidNotReceive().Add(Arg.Any<User>());
    }

    [Fact]
    public async Task Happy_path_creates_user_and_enqueues_email_confirmation()
    {
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _handler.Handle(
            new Register.Command("Anna.Nováková@example.cz", "abcd1234567", "Anna Nováková", "cz", UserRole.Customer),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.UserId.Should().Be("user-fresh-01");

        _hasher.Received(1).Hash("abcd1234567");
        _users.Received(1).Add(Arg.Is<User>(u =>
            u.Id == "user-fresh-01" &&
            u.Email == "Anna.Nováková@example.cz" &&
            u.EmailNormalized == User.NormalizeEmail("Anna.Nováková@example.cz") &&
            u.Role == UserRole.Customer &&
            u.PasswordHash == "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB"));

        // T-0024: registration auto-enqueues the FIRST confirmation token.
        _tokens.Received(1).Add(Arg.Is<OneTimeToken>(t =>
            t.UserId == "user-fresh-01" &&
            t.Purpose == OneTimeTokenPurpose.EmailConfirmation &&
            t.ExpiresAt == _clock.UtcNow + SendEmailConfirmation.TokenLifetime));
        _outbox.Received(1).Enqueue(
            "user-fresh-01",
            SendEmailConfirmation.OutboxEventType,
            Arg.Any<string>());
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; } = now; }
}
