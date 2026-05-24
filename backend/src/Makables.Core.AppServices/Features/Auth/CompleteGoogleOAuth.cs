using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Complete the Google OAuth flow. Per ADR 0012 §Google OAuth.
///
/// Steps:
///   1. Verify the signed <c>state</c> returned by Google. Tampered or
///      stale state → <see cref="BusinessErrorMessage.AuthOAuthInvalidState"/>.
///   2. Admin audience is rejected here too (defense-in-depth).
///   3. Exchange the authorization code with Google. Network failure /
///      bad code → <see cref="BusinessErrorMessage.AuthOAuthExchangeFailed"/>.
///   4. Refuse profiles where Google has not verified the email.
///   5. Match by GoogleSub first (covers users who already linked).
///      Then by EmailNormalized (covers password users who Sign-in-
///      with-Google for the first time — we link). Otherwise create a
///      new user with EmailConfirmedAt = now (Google did the
///      verification).
///   6. Mint the session via the same refresh-token pattern as
///      <see cref="Login"/>.
///
/// <see cref="IPersistOnFailureCommand"/> so any token-rotation /
/// linking side effects on the soft-deleted / locked branches persist.
/// </summary>
public static class CompleteGoogleOAuth
{
    private static readonly TimeSpan RefreshTokenLifetime = RefreshToken.DefaultLifetime;

    public sealed record Command(
        string Code,
        string State,
        string RedirectUri,
        string? UserAgent,
        string? IpAddress) : ICommand<SessionResult>, IPersistOnFailureCommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Code).NotEmpty().WithErrorCode(BusinessErrorMessage.Required);
            RuleFor(c => c.State).NotEmpty().WithErrorCode(BusinessErrorMessage.Required);
            RuleFor(c => c.RedirectUri).NotEmpty().WithErrorCode(BusinessErrorMessage.Required);
        }
    }

    public sealed class Handler(
        IOAuthStateSigner stateSigner,
        IGoogleOAuthClient googleClient,
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IJwtIssuer jwt,
        IIdGenerator ids,
        IClock clock,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<SessionResult>>
    {
        public async Task<BusinessResult<SessionResult>> Handle(Command command, CancellationToken cancellationToken)
        {
            var now = clock.UtcNow;

            // 1. Verify state.
            var state = stateSigner.TryVerify(command.State, now);
            if (state is null)
            {
                logger.LogWarning("Google OAuth callback with invalid / stale state; rejected.");
                return InvalidState();
            }

            // 2. Defense-in-depth: admin audience disallowed here too.
            if (state.Audience == MakablesAudiences.Admin)
            {
                return BusinessResult.Failure<SessionResult>(
                    Error.Forbidden(BusinessErrorMessage.AuthOAuthNotAllowedForAdmin));
            }

            // 3. Exchange the code. The GoogleOAuthClient impl throws on
            //    any failure (bad code, network error, ID-token validation
            //    failure); we treat all of them the same on the wire.
            GoogleProfile profile;
            try
            {
                profile = await googleClient.ExchangeCodeAsync(command.Code, command.RedirectUri, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Google OAuth exchange failed.");
                return BusinessResult.Failure<SessionResult>(
                    Error.Validation("code", BusinessErrorMessage.AuthOAuthExchangeFailed));
            }

            // 4. Email-verified gate. Google guarantees this only when
            //    payload.email_verified is true.
            if (!profile.EmailVerified)
            {
                return BusinessResult.Failure<SessionResult>(
                    Error.Validation("email", BusinessErrorMessage.AuthOAuthEmailNotVerified));
            }

            // 5. Resolve / create user.
            var user = await users.GetByGoogleSubAsync(profile.Sub, cancellationToken);
            if (user is null)
            {
                // No prior GoogleSub link. Check if a password account
                // with the same email exists — if so we link.
                var emailNormalized = User.NormalizeEmail(profile.Email);
                user = await users.GetByEmailNormalizedAsync(emailNormalized, cancellationToken);
                if (user is not null && user.IsActive)
                {
                    user.LinkGoogleSub(profile.Sub);
                    user.ConfirmEmail(now);
                }
                else if (user is null)
                {
                    // Brand-new account. Role from the signed audience
                    // state — that's why audience tampering matters.
                    var role = state.Audience switch
                    {
                        MakablesAudiences.Customer => UserRole.Customer,
                        MakablesAudiences.Maker => UserRole.Maker,
                        // Admin already rejected above; this is unreachable.
                        _ => UserRole.Customer,
                    };

                    user = User.Create(
                        id: ids.Next(),
                        email: profile.Email,
                        role: role,
                        fullName: profile.Name ?? profile.Email,
                        countryCodePrimary: "CZ",
                        passwordHash: null,
                        googleSub: profile.Sub,
                        emailAlreadyConfirmed: true,
                        confirmedAt: now);
                    users.Add(user);
                }
                else
                {
                    // Soft-deleted account. Refuse.
                    return BusinessResult.Failure<SessionResult>(
                        Error.Validation("email", BusinessErrorMessage.AuthOAuthExchangeFailed));
                }
            }
            else if (!user.IsActive)
            {
                return BusinessResult.Failure<SessionResult>(
                    Error.Validation("email", BusinessErrorMessage.AuthOAuthExchangeFailed));
            }

            // Audience check — the signed state's audience MUST match
            // the user's role (or user must be admin, which we already
            // rejected). Non-admins cannot cross audiences.
            if (!user.MatchesAudience(state.Audience))
            {
                return BusinessResult.Failure<SessionResult>(
                    Error.Forbidden(BusinessErrorMessage.AuthForbidden));
            }

            user.RegisterSuccessfulLogin();

            // 6. Mint session.
            var access = jwt.Issue(user, state.Audience, now);
            var (rawRefresh, refreshHash) = OpaqueTokenFactory.GenerateUrlSafe32();
            var refreshExpiresAt = now + RefreshTokenLifetime;

            refreshTokens.Add(RefreshToken.IssueNew(
                id: ids.Next(),
                userId: user.Id,
                tokenHash: refreshHash,
                familyId: ids.Next(),
                expiresAt: refreshExpiresAt,
                countryCode: user.CountryCodePrimary,
                userAgent: command.UserAgent,
                ipAddress: command.IpAddress));

            logger.LogInformation("Google OAuth completed for {UserId} (audience {Audience}).", user.Id, state.Audience);

            return BusinessResult.Success(new SessionResult(
                UserId: user.Id,
                AccessToken: access.Token,
                AccessTokenExpiresAt: access.ExpiresAt,
                RefreshToken: rawRefresh,
                RefreshTokenExpiresAt: refreshExpiresAt));
        }

        private static BusinessResult<SessionResult> InvalidState() =>
            BusinessResult.Failure<SessionResult>(
                Error.Validation("state", BusinessErrorMessage.AuthOAuthInvalidState));
    }
}
