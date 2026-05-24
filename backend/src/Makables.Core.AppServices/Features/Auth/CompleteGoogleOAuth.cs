using System.Text.Json;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Complete the Google OAuth flow. Per ADR 0012 §Google OAuth + reviewer
/// T-0026 BLOCKERs B-1 / B-2 and code-quality MAJORs.
///
/// Steps:
///   1. Verify the signed <c>state</c> — covers signature (HKDF-derived
///      sub-key, B-1), redirect-URI binding, anti-CSRF cookie hash
///      binding (B-2), and stale-window. Any failure →
///      <see cref="BusinessErrorMessage.AuthOAuthInvalidState"/>.
///   2. Admin audience is rejected here too (defense-in-depth).
///   3. Exchange the code via <see cref="IGoogleOAuthClient"/>. We
///      narrowly catch <c>HttpRequestException</c> / <c>JsonException</c>
///      and the Google client's own <c>GoogleOAuthException</c>; we
///      re-throw <c>OperationCanceledException</c> on caller cancel.
///   4. Refuse profiles where Google has not verified the email.
///   5. Resolve or create the user via <see cref="ResolveOrCreateUserAsync"/>.
///      Brand-new accounts get the role from the signed audience and
///      the country code from <see cref="AuthDefaultCountryOptions"/>
///      (closes code-quality MAJOR M-1 "hardcoded CZ").
///   6. Mint the session via the same refresh-token pattern as
///      <see cref="Login"/>.
///
/// <see cref="IPersistOnFailureCommand"/> is set ONLY because a user
/// who reaches the "link Google to existing password account" branch
/// may then fail the subsequent audience check: the link mutation
/// must persist so a later legitimate attempt from the correct
/// audience succeeds without re-linking. Other failure paths leave the
/// DbContext untouched.
/// </summary>
public static class CompleteGoogleOAuth
{
    private static readonly TimeSpan RefreshTokenLifetime = RefreshToken.DefaultLifetime;

    public sealed record Command(
        string Code,
        string State,
        string RedirectUri,
        string CsrfCookieValue,
        string? UserAgent,
        string? IpAddress) : ICommand<SessionResult>, IPersistOnFailureCommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Code).NotEmpty().WithErrorCode(BusinessErrorMessage.Required);
            RuleFor(c => c.State).NotEmpty().WithErrorCode(BusinessErrorMessage.Required);
            RuleFor(c => c.RedirectUri).NotEmpty().WithErrorCode(BusinessErrorMessage.Required);
            RuleFor(c => c.CsrfCookieValue).NotEmpty().WithErrorCode(BusinessErrorMessage.Required);
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
        IOptions<AuthDefaultCountryOptions> defaultCountryOptions,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<SessionResult>>
    {
        public async Task<BusinessResult<SessionResult>> Handle(Command command, CancellationToken cancellationToken)
        {
            var now = clock.UtcNow;

            // 1. Verify state (signature + redirectUri + csrf cookie + stale window).
            var state = stateSigner.TryVerify(command.State, command.RedirectUri, command.CsrfCookieValue, now);
            if (state is null)
            {
                logger.LogWarning("Google OAuth callback with invalid / stale / unbound state; rejected.");
                return InvalidState();
            }

            // 2. Defense-in-depth: admin audience disallowed here too.
            if (state.Audience == MakablesAudiences.Admin)
            {
                return BusinessResult.Failure<SessionResult>(
                    Error.Forbidden(BusinessErrorMessage.AuthOAuthNotAllowedForAdmin));
            }

            // 3. Exchange the code. Narrowed catch — re-throw caller
            //    cancellation so the framework can wind it up correctly.
            GoogleProfile profile;
            try
            {
                profile = await googleClient.ExchangeCodeAsync(command.Code, command.RedirectUri, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       || ex.GetType().Name == "GoogleOAuthException")
            {
                logger.LogWarning(ex, "Google OAuth exchange failed.");
                return BusinessResult.Failure<SessionResult>(
                    Error.Validation("code", BusinessErrorMessage.AuthOAuthExchangeFailed));
            }

            // 4. Email-verified gate.
            if (!profile.EmailVerified)
            {
                return BusinessResult.Failure<SessionResult>(
                    Error.Validation("email", BusinessErrorMessage.AuthOAuthEmailNotVerified));
            }

            // 5. Resolve / create user.
            var resolution = await ResolveOrCreateUserAsync(profile, state, now, cancellationToken);
            if (resolution.User is null)
            {
                // Soft-deleted user with same email. Wire-indistinguishable
                // from "bad code" so attackers can't enumerate.
                return BusinessResult.Failure<SessionResult>(
                    Error.Validation("email", BusinessErrorMessage.AuthOAuthExchangeFailed));
            }
            var user = resolution.User;

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

        /// <summary>
        /// Resolves the user behind the verified Google profile:
        ///   - existing GoogleSub match → return as-is;
        ///   - existing active password account with same email → link
        ///     <c>GoogleSub</c> + confirm email;
        ///   - no match → create new with role from signed audience and
        ///     country code from configuration.
        /// Returns <c>(null)</c> when the email matches a soft-deleted
        /// account; the caller surfaces a generic exchange-failed.
        /// </summary>
        private async Task<UserResolution> ResolveOrCreateUserAsync(
            GoogleProfile profile,
            OAuthStatePayload state,
            DateTimeOffset now,
            CancellationToken ct)
        {
            var existingBySub = await users.GetByGoogleSubAsync(profile.Sub, ct);
            if (existingBySub is not null)
            {
                return existingBySub.IsActive
                    ? new UserResolution(existingBySub)
                    : new UserResolution(null);
            }

            var emailNormalized = User.NormalizeEmail(profile.Email);
            var existingByEmail = await users.GetByEmailNormalizedAsync(emailNormalized, ct);
            if (existingByEmail is not null)
            {
                if (!existingByEmail.IsActive) return new UserResolution(null);
                existingByEmail.LinkGoogleSub(profile.Sub);
                existingByEmail.ConfirmEmail(now);
                return new UserResolution(existingByEmail);
            }

            var role = state.Audience switch
            {
                MakablesAudiences.Customer => UserRole.Customer,
                MakablesAudiences.Maker => UserRole.Maker,
                _ => UserRole.Customer, // Admin already rejected upstream.
            };

            var newUser = User.Create(
                id: ids.Next(),
                email: profile.Email,
                role: role,
                fullName: profile.Name ?? profile.Email,
                countryCodePrimary: defaultCountryOptions.Value.CountryCodePrimary,
                passwordHash: null,
                googleSub: profile.Sub,
                emailAlreadyConfirmed: true,
                confirmedAt: now);
            users.Add(newUser);
            return new UserResolution(newUser);
        }

        private readonly record struct UserResolution(User? User);

        private static BusinessResult<SessionResult> InvalidState() =>
            BusinessResult.Failure<SessionResult>(
                Error.Validation("state", BusinessErrorMessage.AuthOAuthInvalidState));
    }
}
