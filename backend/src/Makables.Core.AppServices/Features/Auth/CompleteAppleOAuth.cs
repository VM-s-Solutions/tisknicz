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
/// Complete the Apple OAuth flow. Mirrors <see cref="CompleteGoogleOAuth"/>
/// exactly in shape and semantics per ADR 0026 / T-0139; the two
/// provider-specific deltas (ES256 client secret, one-time <c>user</c>
/// field) are entirely inside <see cref="IAppleOAuthClient"/> — this
/// handler's steps are otherwise identical:
///   1. Verify the signed <c>state</c> (same <see cref="IOAuthStateSigner"/>
///      instance/config as Google — see ADR 0026 Defense section on why
///      no per-provider discriminator claim is required).
///   2. Admin audience rejected here too (defense-in-depth).
///   3. Exchange the code via <see cref="IAppleOAuthClient"/>. Narrow
///      catch — <c>HttpRequestException</c> / <c>TaskCanceledException</c>
///      / <c>JsonException</c> / <c>AppleOAuthException</c>; re-throw
///      <c>OperationCanceledException</c> on caller cancel.
///   4. Refuse profiles where Apple has not verified the email
///      (<see cref="AppleProfile.EmailVerified"/> already normalizes the
///      string-vs-bool wire quirk inside the client).
///   5. Resolve or create the user via <see cref="ResolveOrCreateUserAsync"/>:
///      AppleSub-match / link-by-email / create-new. The one-time
///      <c>user</c>-field name (<see cref="AppleProfile.Name"/>) is used
///      ONLY at account-creation time — link-by-email and match-by-sub
///      branches never overwrite an existing <c>FullName</c> (ADR 0026).
///   6. Mint the session via the same refresh-token pattern as Google/
///      <see cref="Login"/>.
/// </summary>
public static class CompleteAppleOAuth
{
    private static readonly TimeSpan RefreshTokenLifetime = RefreshToken.DefaultLifetime;

    public sealed record Command(
        string Code,
        string State,
        string RedirectUri,
        string CsrfCookieValue,
        string? UserFieldJson,
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
        IAppleOAuthClient appleClient,
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
                logger.LogWarning("Apple OAuth callback with invalid / stale / unbound state; rejected.");
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
            AppleProfile profile;
            try
            {
                profile = await appleClient.ExchangeCodeAsync(
                    command.Code, command.RedirectUri, command.UserFieldJson, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       || ex.GetType().Name == "AppleOAuthException")
            {
                logger.LogWarning(ex, "Apple OAuth exchange failed.");
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

            logger.LogInformation("Apple OAuth completed for {UserId} (audience {Audience}).", user.Id, state.Audience);

            return BusinessResult.Success(new SessionResult(
                UserId: user.Id,
                AccessToken: access.Token,
                AccessTokenExpiresAt: access.ExpiresAt,
                RefreshToken: rawRefresh,
                RefreshTokenExpiresAt: refreshExpiresAt));
        }

        /// <summary>
        /// Resolves the user behind the verified Apple profile:
        ///   - existing AppleSub match → return as-is;
        ///   - existing active password/Google account with same email →
        ///     link <c>AppleSub</c> + confirm email (name NOT overwritten);
        ///   - no match → create new with role from signed audience and
        ///     country code from configuration; <c>FullName</c> sourced
        ///     from the one-time <c>user</c> field when present, else a
        ///     placeholder derived from the email local-part.
        /// Returns <c>(null)</c> when the email matches a soft-deleted
        /// account; the caller surfaces a generic exchange-failed.
        /// </summary>
        private async Task<UserResolution> ResolveOrCreateUserAsync(
            AppleProfile profile,
            OAuthStatePayload state,
            DateTimeOffset now,
            CancellationToken ct)
        {
            var existingBySub = await users.GetByAppleSubAsync(profile.Sub, ct);
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
                existingByEmail.LinkAppleSub(profile.Sub);
                existingByEmail.ConfirmEmail(now);
                return new UserResolution(existingByEmail);
            }

            var role = state.Audience switch
            {
                MakablesAudiences.Customer => UserRole.Customer,
                MakablesAudiences.Maker => UserRole.Maker,
                _ => UserRole.Customer, // Admin already rejected upstream.
            };

            // AC-6: name comes from the one-time `user` field on first
            // authorization only; falls back to the email local-part
            // when Apple sent no name (e.g. the user declined to share it).
            var fullName = profile.Name ?? profile.Email;

            var newUser = User.Create(
                id: ids.Next(),
                email: profile.Email,
                role: role,
                fullName: fullName,
                countryCodePrimary: defaultCountryOptions.Value.CountryCodePrimary,
                passwordHash: null,
                googleSub: null,
                emailAlreadyConfirmed: true,
                confirmedAt: now);
            newUser.LinkAppleSub(profile.Sub);
            users.Add(newUser);
            return new UserResolution(newUser);
        }

        private readonly record struct UserResolution(User? User);

        private static BusinessResult<SessionResult> InvalidState() =>
            BusinessResult.Failure<SessionResult>(
                Error.Validation("state", BusinessErrorMessage.AuthOAuthInvalidState));
    }
}
