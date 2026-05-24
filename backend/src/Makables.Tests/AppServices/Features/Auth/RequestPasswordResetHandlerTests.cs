using FluentAssertions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Auth;

public class RequestPasswordResetHandlerTests
{
    private readonly IOneTimeTokenIssuer _issuer = Substitute.For<IOneTimeTokenIssuer>();
    private readonly IOneTimeTokenRepository _tokens = Substitute.For<IOneTimeTokenRepository>();
    private readonly RequestPasswordReset.Handler _handler;

    public RequestPasswordResetHandlerTests()
    {
        _handler = new RequestPasswordReset.Handler(_issuer, _tokens);
    }

    [Fact]
    public async Task Delegates_to_issuer_with_PasswordReset_purpose_and_invalidate_pre_issue_hook()
    {
        var result = await _handler.Handle(
            new RequestPasswordReset.Command("anna@example.cz", "1.2.3.4"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _issuer.Received(1).IssueAsync(Arg.Is<IssueRequest>(r =>
            r.Email == "anna@example.cz" &&
            r.IpAddress == "1.2.3.4" &&
            r.Purpose == OneTimeTokenPurpose.PasswordReset &&
            r.TokenLifetime == RequestPasswordReset.TokenLifetime &&
            r.OutboxEventType == OutboxEventTypes.AuthPasswordResetSend &&
            r.MaxRequestsPerWindow == RequestPasswordReset.MaxRequestsPerWindow &&
            r.RateLimitWindow == RequestPasswordReset.RateLimitWindow &&
            r.PreIssueHook != null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pre_issue_hook_invalidates_prior_reset_tokens_for_the_user()
    {
        IssueRequest? captured = null;
        _issuer.IssueAsync(Arg.Do<IssueRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new IssueOutcome(true, "user-1"));

        await _handler.Handle(new RequestPasswordReset.Command("anna@example.cz", null), CancellationToken.None);

        captured!.PreIssueHook.Should().NotBeNull();
        var user = User.Create("user-1", "anna@example.cz", UserRole.Customer, "Anna", "CZ");
        var now = new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero);
        await captured.PreIssueHook!(user, now, CancellationToken.None);

        await _tokens.Received(1).InvalidateRedeemableAsync(
            "user-1", OneTimeTokenPurpose.PasswordReset, now, Arg.Any<CancellationToken>());
    }
}
