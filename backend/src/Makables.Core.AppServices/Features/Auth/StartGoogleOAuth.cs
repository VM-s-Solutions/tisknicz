using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Begin the Google OAuth flow. Per ADR 0012 §Google OAuth + reviewer
/// T-0026 BLOCKER B-2:
///   - Audience MUST be <c>customer</c> or <c>maker</c>. Admin
///     authentication via Google is rejected at this entry.
///   - The audience is HMAC-signed into the OAuth state with a HKDF-
///     derived sub-key (B-1) so a JWT can never be mistaken for state.
///   - The state ALSO binds the <c>RedirectUri</c> and a hash of an
///     anti-CSRF cookie value the handler mints fresh and returns to
///     the caller. The caller (controller in T-0035) sets the cookie
///     as <c>HttpOnly; Secure; SameSite=Lax; __Host-</c> prefix so the
///     browser ships it back on the callback; <see cref="CompleteGoogleOAuth"/>
///     verifies it. Without this an attacker who captures the URL state
///     could replay it into a victim's browser (login-CSRF).
/// </summary>
public static class StartGoogleOAuth
{
    public sealed record Command(string Audience, string RedirectUri) : ICommand<Response>;

    /// <summary>
    /// Result. The controller MUST set <see cref="CsrfCookieValue"/> as
    /// an HttpOnly + Secure + SameSite=Lax cookie before redirecting to
    /// <see cref="AuthorizationUrl"/>; the cookie name convention is
    /// <c>__Host-makables_oauth_csrf</c>.
    /// </summary>
    public sealed record Response(string AuthorizationUrl, string CsrfCookieValue);

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

            // Mint a fresh anti-CSRF cookie value. 32 bytes of CSPRNG so
            // an attacker can't predict it; URL-safe base64 so it can be
            // set on a cookie without escaping. We reuse the same shape
            // used by refresh tokens (OpaqueTokenFactory).
            var (csrfCookieValue, _) = OpaqueTokenFactory.GenerateUrlSafe32();

            var state = stateSigner.Sign(
                audience: command.Audience,
                redirectUri: command.RedirectUri,
                csrfCookieValue: csrfCookieValue,
                nonce: ids.Next(),
                issuedAt: clock.UtcNow);

            var url = googleClient.BuildAuthorizationUrl(state, command.RedirectUri);
            return Task.FromResult(BusinessResult.Success(new Response(url, csrfCookieValue)));
        }
    }
}
