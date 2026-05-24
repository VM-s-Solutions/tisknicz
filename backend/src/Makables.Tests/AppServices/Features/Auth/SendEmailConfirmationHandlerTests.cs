using FluentAssertions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Auth;

public class SendEmailConfirmationHandlerTests
{
    private readonly IOneTimeTokenIssuer _issuer = Substitute.For<IOneTimeTokenIssuer>();
    private readonly SendEmailConfirmation.Handler _handler;

    public SendEmailConfirmationHandlerTests()
    {
        _handler = new SendEmailConfirmation.Handler(_issuer);
    }

    [Fact]
    public async Task Delegates_to_issuer_with_EmailConfirmation_purpose_and_skip_if_confirmed_filter()
    {
        var result = await _handler.Handle(
            new SendEmailConfirmation.Command("anna@example.cz", "1.2.3.4"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _issuer.Received(1).IssueAsync(Arg.Is<IssueRequest>(r =>
            r.Email == "anna@example.cz" &&
            r.IpAddress == "1.2.3.4" &&
            r.Purpose == OneTimeTokenPurpose.EmailConfirmation &&
            r.TokenLifetime == SendEmailConfirmation.TokenLifetime &&
            r.OutboxEventType == OutboxEventTypes.AuthEmailConfirmationSend &&
            r.MaxRequestsPerWindow == SendEmailConfirmation.MaxRequestsPerWindow &&
            r.RateLimitWindow == SendEmailConfirmation.RateLimitWindow &&
            r.EligibilityFilter != null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Eligibility_filter_skips_already_confirmed_users()
    {
        IssueRequest? captured = null;
        _issuer.IssueAsync(Arg.Do<IssueRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new IssueOutcome(false, null));

        await _handler.Handle(new SendEmailConfirmation.Command("anna@example.cz", null), CancellationToken.None);

        captured.Should().NotBeNull();
        var unconfirmedUser = User.Create("u-1", "anna@example.cz", UserRole.Customer, "Anna", "CZ");
        var confirmedUser = User.Create("u-2", "anna2@example.cz", UserRole.Customer, "Anna2", "CZ");
        confirmedUser.ConfirmEmail(DateTimeOffset.UtcNow);

        captured!.EligibilityFilter!(unconfirmedUser).Should().BeTrue("unconfirmed accounts get the email");
        captured.EligibilityFilter(confirmedUser).Should().BeFalse("already-confirmed accounts silently no-op");
    }
}
