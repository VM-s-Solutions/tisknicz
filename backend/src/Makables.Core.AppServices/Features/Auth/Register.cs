using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
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
///     <c>EmailConfirmedAt = null</c>; T-0024 owns the confirmation flow,
///     and order placement is gated on confirmation per ADR 0012 §Email
///     confirmation. No session is minted yet — the user logs in after
///     confirming.
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
            // requirements (NIST 800-63B). The top-100 breached-passwords
            // check happens in the handler so we can return the
            // domain-specific AuthPasswordTooCommon code.
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
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            if (command.Role == UserRole.Admin)
            {
                // Public registration must not mint an admin. Admins are
                // provisioned out-of-band.
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
            return BusinessResult.Success(new Response(user.Id));
        }
    }
}
