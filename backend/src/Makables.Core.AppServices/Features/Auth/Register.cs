using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Register a new account with email + password. Per ADR 0012 §1 and
/// §Password policy.
///
/// Outcome:
///   - Validation: missing email / weak password / wrong country → ValidationFailure
///   - Email already exists (active or soft-deleted) → AuthEmailAlreadyExists
///   - Success → returns the new user id. The account is created with
///     <c>EmailConfirmedAt = null</c>; the handler auto-fires the first
///     email-confirmation token through <see cref="IOneTimeTokenIssuer"/>
///     (same pipeline as the user-driven <see cref="SendEmailConfirmation"/>
///     flow, so the per-email rate-limit budget is shared — the user
///     who registers and then resends gets 3 total emails per 10 min,
///     not 4. Closes T-0024 reviewer security M-2).
///
/// Audience is the role being registered (customer / maker / admin), but
/// admins are not self-registerable in the MVP. The handler rejects
/// <see cref="UserRole.Admin"/> with AuthForbidden so callers can't
/// elevate via the public endpoint.
/// </summary>
public static class Register
{
    public sealed record Command(
        string Email,
        string Password,
        string FullName,
        string CountryCodePrimary,
        UserRole Role) : ICommand<Response>;

    public sealed record Response(string UserId);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Email)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .EmailAddress().WithErrorCode(BusinessErrorMessage.InvalidEmailFormat)
                .MaximumLength(320).WithErrorCode(BusinessErrorMessage.MaxLength);

            // ADR 0012: minimum 10 characters, no other complexity
            // requirements (NIST 800-63B).
            RuleFor(c => c.Password)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MinimumLength(10).WithErrorCode(BusinessErrorMessage.MinLength)
                .MaximumLength(200).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.FullName)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(200).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.CountryCodePrimary)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .Length(2).WithErrorCode(BusinessErrorMessage.InvalidEnumValue);

            RuleFor(c => c.Role)
                .IsInEnum().WithErrorCode(BusinessErrorMessage.InvalidEnumValue);
        }
    }

    public sealed class Handler(
        IUserRepository users,
        IPasswordHasher hasher,
        IIdGenerator ids,
        IOneTimeTokenIssuer issuer,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            if (command.Role == UserRole.Admin)
            {
                logger.LogWarning("Attempted public registration with Admin role for {Email}; rejected.", command.Email);
                return BusinessResult.Failure<Response>(Error.Forbidden(BusinessErrorMessage.AuthForbidden));
            }

            var emailNormalized = User.NormalizeEmail(command.Email);
            if (await users.EmailExistsAsync(emailNormalized, cancellationToken))
            {
                return BusinessResult.Failure<Response>(
                    Error.Conflict("email", BusinessErrorMessage.AuthEmailAlreadyExists));
            }

            var passwordHash = hasher.Hash(command.Password);
            var user = User.Create(
                id: ids.Next(),
                email: command.Email,
                role: command.Role,
                fullName: command.FullName,
                countryCodePrimary: command.CountryCodePrimary,
                passwordHash: passwordHash);

            users.Add(user);

            // Auto-fire the first email-confirmation token through the
            // same pipeline the user-driven resend uses. Sharing the
            // issuer means the per-user rate-limit budget is shared
            // (closes T-0024 security M-2).
            await issuer.IssueAsync(new IssueRequest(
                Email: command.Email,
                Purpose: OneTimeTokenPurpose.EmailConfirmation,
                TokenLifetime: SendEmailConfirmation.TokenLifetime,
                OutboxEventType: OutboxEventTypes.AuthEmailConfirmationSend,
                MaxRequestsPerWindow: SendEmailConfirmation.MaxRequestsPerWindow,
                RateLimitWindow: SendEmailConfirmation.RateLimitWindow,
                EligibilityFilter: u => u.EmailConfirmedAt is null),
                cancellationToken);

            return BusinessResult.Success(new Response(user.Id));
        }
    }
}
