using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Begin the Google OAuth flow. Per ADR 0012 §Google OAuth:
///   - Audience MUST be <c>customer</c> or <c>maker</c>. Admin
///     authentication via Google is rejected at this entry — admins
///     log in with password.
///   - The audience is HMAC-signed into the OAuth state so the
///     callback can verify it was not tampered with.
///   - The redirect URL Google sends the user to is returned in the
///     response; the controller (T-0027 / T-0035) is responsible for
///     issuing the actual HTTP 302.
/// </summary>
public static class StartGoogleOAuth
{
    public sealed record Command(string Audience, string RedirectUri) : ICommand<Response>;

    public sealed record Response(string AuthorizationUrl);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Audience)
                .Must(MakablesAudiences.IsValid)
                .WithErrorCode(BusinessErrorMessage.InvalidEnumValue);
            RuleFor(c => c.RedirectUri)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required);
        }
    }

    public sealed class Handler(
        IOAuthStateSigner stateSigner,
        IGoogleOAuthClient googleClient,
        IIdGenerator ids,
        IClock clock,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<Response>>
    {
        public Task<BusinessResult<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            if (command.Audience == MakablesAudiences.Admin)
            {
                logger.LogWarning("Attempted Google OAuth start with admin audience; rejected.");
                return Task.FromResult(BusinessResult.Failure<Response>(
                    Error.Forbidden(BusinessErrorMessage.AuthOAuthNotAllowedForAdmin)));
            }

            var state = stateSigner.Sign(new OAuthStatePayload(
                Audience: command.Audience,
                Nonce: ids.Next(),
                IssuedAt: clock.UtcNow));

            var url = googleClient.BuildAuthorizationUrl(state, command.RedirectUri);
            return Task.FromResult(BusinessResult.Success(new Response(url)));
        }
    }
}
