using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using MediatR;

namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Log out the session associated with a refresh token. Revokes the
/// presented token explicitly (so the cookie is dead immediately even if
/// the access JWT still has lifetime left). Logout-all is a separate
/// command for the dashboards (T-0035+).
///
/// Idempotent — calling Logout with an unknown / already-revoked token
/// returns Success so the UI doesn't flap.
/// </summary>
public static class Logout
{
    public sealed record Command(string RawRefreshToken) : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.RawRefreshToken)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(200).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IRefreshTokenRepository refreshTokens,
        IClock clock) : IRequestHandler<Command, BusinessResult>
    {
        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            var hash = OpaqueTokenFactory.Sha256Hex(command.RawRefreshToken);
            var token = await refreshTokens.GetByTokenHashAsync(hash, cancellationToken);
            token?.Revoke(clock.UtcNow);
            return BusinessResult.Success();
        }
    }
}
