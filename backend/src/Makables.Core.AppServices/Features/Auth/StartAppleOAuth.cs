using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Begin the Apple OAuth flow. Structurally identical to
/// <see cref="StartGoogleOAuth"/> — same audience-rejection rule, same
/// <see cref="IOAuthStateSigner"/> usage, same anti-CSRF cookie
/// minting. Per ADR 0026 / T-0139:
///   - Audience MUST be <c>customer</c> or <c>maker</c>. Admin
///     authentication via Apple is rejected at this entry (mirrors
///     Google — ADR 0012's admin-must-use-password rule is
///     provider-agnostic).
///   - <see cref="IAppleOAuthClient.BuildAuthorizationUrl"/> requests
///     <c>response_mode=form_post</c>, so the corresponding controller
///     action for the callback MUST be a POST reading form fields — the
///     one real HTTP-contract delta from Google's GET callback.
/// </summary>
public static class StartAppleOAuth
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
        IAppleOAuthClient appleClient,
        IIdGenerator ids,
        IClock clock,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<Response>>
    {
        public Task<BusinessResult<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            if (command.Audience == MakablesAudiences.Admin)
            {
                logger.LogWarning("Attempted Apple OAuth start with admin audience; rejected.");
                return Task.FromResult(BusinessResult.Failure<Response>(
                    Error.Forbidden(BusinessErrorMessage.AuthOAuthNotAllowedForAdmin)));
            }

            // Mint a fresh anti-CSRF cookie value — same shape as
            // StartGoogleOAuth (32 bytes CSPRNG, URL-safe base64).
            var (csrfCookieValue, _) = OpaqueTokenFactory.GenerateUrlSafe32();

            var state = stateSigner.Sign(
                audience: command.Audience,
                redirectUri: command.RedirectUri,
                csrfCookieValue: csrfCookieValue,
                nonce: ids.Next(),
                issuedAt: clock.UtcNow);

            var url = appleClient.BuildAuthorizationUrl(state, command.RedirectUri);
            return Task.FromResult(BusinessResult.Success(new Response(url, csrfCookieValue)));
        }
    }
}
