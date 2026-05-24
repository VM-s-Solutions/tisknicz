using FluentAssertions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Auth;

/// <summary>
/// Thin tests after the T-0025 consolidation: the handler delegates the
/// entire issue-an-opaque-token-by-email pipeline to
/// <see cref="IOneTimeTokenIssuer"/>. The pipeline itself is tested by
/// <c>OneTimeTokenIssuerTests</c> (timing equalization, rate-limit math,
/// eligibility filter, pre-issue hook). What this file pins is the
/// handler's CONTRACT with the issuer: correct purpose, lifetime, outbox
/// event type, rate-limit constants, ip address forwarded.
/// </summary>
public class RequestMagicLinkHandlerTests
{
    private readonly IOneTimeTokenIssuer _issuer = Substitute.For<IOneTimeTokenIssuer>();
    private readonly RequestMagicLink.Handler _handler;

    public RequestMagicLinkHandlerTests()
    {
        _handler = new RequestMagicLink.Handler(_issuer);
    }

    [Fact]
    public async Task Always_returns_success_and_delegates_to_the_issuer_with_MagicLink_purpose()
    {
        var result = await _handler.Handle(
            new RequestMagicLink.Command("anna@example.cz", "1.2.3.4"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _issuer.Received(1).IssueAsync(Arg.Is<IssueRequest>(r =>
            r.Email == "anna@example.cz" &&
            r.IpAddress == "1.2.3.4" &&
            r.Purpose == OneTimeTokenPurpose.MagicLink &&
            r.TokenLifetime == RequestMagicLink.TokenLifetime &&
            r.OutboxEventType == OutboxEventTypes.AuthMagicLinkSend &&
            r.MaxRequestsPerWindow == RequestMagicLink.MaxRequestsPerWindow &&
            r.RateLimitWindow == RequestMagicLink.RateLimitWindow &&
            r.EligibilityFilter == null &&
            r.PreIssueHook == null),
            Arg.Any<CancellationToken>());
    }
}
