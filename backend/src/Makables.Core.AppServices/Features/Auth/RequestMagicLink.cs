using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using MediatR;

namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Issue a magic-link email for an existing account. Per ADR 0012 §Magic
/// link. Delegates the issue-an-opaque-token-by-email pipeline to
/// <see cref="IOneTimeTokenIssuer"/>; the timing-equalization B-1
/// invariant lives in the issuer so it can't drift between flows.
/// </summary>
public static class RequestMagicLink
{
    /// <summary>How long a magic-link token is valid. ADR 0012 §Magic link.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Per-email request budget — 3 requests per 10 minutes.</summary>
    public const int MaxRequestsPerWindow = 3;
    public static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(10);

    public sealed record Command(
        string Email,
        string? IpAddress) : ICommand, IPersistOnFailureCommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Email)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(320).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(IOneTimeTokenIssuer issuer) : IRequestHandler<Command, BusinessResult>
    {
        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            await issuer.IssueAsync(new IssueRequest(
                Email: command.Email,
                Purpose: OneTimeTokenPurpose.MagicLink,
                TokenLifetime: TokenLifetime,
                OutboxEventType: OutboxEventTypes.AuthMagicLinkSend,
                MaxRequestsPerWindow: MaxRequestsPerWindow,
                RateLimitWindow: RateLimitWindow,
                IpAddress: command.IpAddress),
                cancellationToken);

            return BusinessResult.Success();
        }
    }
}
