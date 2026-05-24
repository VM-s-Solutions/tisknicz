using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using MediatR;

namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// (Re-)send an email-confirmation link. Per ADR 0012 §Email confirmation.
/// Delegates the issue-an-opaque-token-by-email pipeline to
/// <see cref="IOneTimeTokenIssuer"/>; the extra "skip if already
/// confirmed" guard lives in the eligibility filter so the timing-
/// equalization invariant stays in one place.
/// </summary>
public static class SendEmailConfirmation
{
    /// <summary>TTL per ADR 0012 §Email confirmation.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);

    public const int MaxRequestsPerWindow = 3;
    public static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(10);

    public sealed record Command(string Email, string? IpAddress) : ICommand, IPersistOnFailureCommand;

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
                Purpose: OneTimeTokenPurpose.EmailConfirmation,
                TokenLifetime: TokenLifetime,
                OutboxEventType: OutboxEventTypes.AuthEmailConfirmationSend,
                MaxRequestsPerWindow: MaxRequestsPerWindow,
                RateLimitWindow: RateLimitWindow,
                IpAddress: command.IpAddress,
                // Already-confirmed account: silent no-op so a redundant
                // confirmation can't be mistaken for a phishing attempt.
                EligibilityFilter: u => u.EmailConfirmedAt is null),
                cancellationToken);

            return BusinessResult.Success();
        }
    }
}
