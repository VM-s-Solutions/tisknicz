using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using MediatR;

namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Issue a password-reset email. Per ADR 0012 §Password reset.
/// Delegates the issue-an-opaque-token-by-email pipeline to
/// <see cref="IOneTimeTokenIssuer"/>; the extra "invalidate prior reset
/// tokens" step lives in the pre-issue hook so it runs ONLY on the
/// happy path (rate-limited or unknown branches do NOT invalidate, so
/// the rate-limit can't accidentally kill a legitimate in-flight reset).
/// </summary>
public static class RequestPasswordReset
{
    /// <summary>TTL per ADR 0012 §Password reset.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

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

    public sealed class Handler(
        IOneTimeTokenIssuer issuer,
        IOneTimeTokenRepository tokens) : IRequestHandler<Command, BusinessResult>
    {
        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            await issuer.IssueAsync(new IssueRequest(
                Email: command.Email,
                Purpose: OneTimeTokenPurpose.PasswordReset,
                TokenLifetime: TokenLifetime,
                OutboxEventType: OutboxEventTypes.AuthPasswordResetSend,
                MaxRequestsPerWindow: MaxRequestsPerWindow,
                RateLimitWindow: RateLimitWindow,
                IpAddress: command.IpAddress,
                PreIssueHook: (user, now, ct) =>
                    tokens.InvalidateRedeemableAsync(user.Id, OneTimeTokenPurpose.PasswordReset, now, ct)),
                cancellationToken);

            return BusinessResult.Success();
        }
    }
}
