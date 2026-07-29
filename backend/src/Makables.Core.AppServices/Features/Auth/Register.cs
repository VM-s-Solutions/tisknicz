using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Registry;
using Makables.Core.Domain.Registry.Validators;
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
///
/// <para>
/// T-0162 "Jsem firma": <see cref="Command.CompanyRegistrationNumber"/> is
/// the optional IČO of a company customer. When provided, the handler runs
/// the RegisterMaker-shaped company branch — mod-11 gate before any I/O
/// (ADR 0018 budget guard) → authoritative registry lookup via the keyed
/// factory → dissolved-entity reject
/// (<see cref="BusinessErrorMessage.CustomerCompanyDissolved"/>, Permanent)
/// → ARES snapshot (IČO + name + DIČ + fetched-at) attached to the
/// <see cref="User"/>. Ordering deviates from RegisterMaker in ONE spot:
/// the email-conflict pre-check runs BEFORE the registry lookup so an
/// already-taken email never burns an ARES call. A stale (≤7-day cache)
/// record registers silently — customers have no admin verification lane.
/// The client-side preview (T-0159 endpoint) is UX only; this lookup is
/// the gate.
/// </para>
/// </summary>
public static class Register
{
    public sealed record Command(
        string Email,
        string Password,
        string FullName,
        string CountryCodePrimary,
        UserRole Role,
        string? CompanyRegistrationNumber = null) : ICommand<Response>;

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

            // T-0162 "Jsem firma": IČO shape (length + digits) is validated
            // here only when the field is provided; the mod-11 checksum is
            // the handler's gate — same deliberate double-gate split as
            // RegisterMaker.Validator.
            When(c => c.CompanyRegistrationNumber is not null, () =>
            {
                RuleFor(c => c.CompanyRegistrationNumber!)
                    .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                    .Length(8).WithErrorCode(BusinessErrorMessage.IcoFormatInvalid)
                    .Matches("^[0-9]+$").WithErrorCode(BusinessErrorMessage.IcoFormatInvalid);
            });
        }
    }

    public sealed class Handler(
        IUserRepository users,
        ICompanyRegistryFactory companyRegistryFactory,
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

            // T-0162: mod-11 gate before ANY I/O — a bad checksum must not
            // consume the ARES rate-limit budget (ADR 0018). Shape (length +
            // digits) was already enforced by the Validator.
            if (command.CompanyRegistrationNumber is not null
                && !CzechIcoValidator.IsValid(command.CompanyRegistrationNumber))
            {
                return BusinessResult.Failure<Response>(
                    Error.Validation(nameof(command.CompanyRegistrationNumber), BusinessErrorMessage.IcoFormatInvalid));
            }

            var emailNormalized = User.NormalizeEmail(command.Email);
            if (await users.EmailExistsAsync(emailNormalized, cancellationToken))
            {
                return BusinessResult.Failure<Response>(
                    Error.Conflict("email", BusinessErrorMessage.AuthEmailAlreadyExists));
            }

            // T-0162 company branch — authoritative server-side lookup; the
            // FE preview is UX only. Runs AFTER the email conflict check so
            // an already-taken email never burns an ARES call (deliberate
            // ordering deviation from RegisterMaker, see class doc).
            CompanyRecord? company = null;
            if (command.CompanyRegistrationNumber is not null)
            {
                var registryResolve = await companyRegistryFactory.ResolveAsync(
                    command.CountryCodePrimary, cancellationToken);
                if (!registryResolve.IsSuccess)
                {
                    return BusinessResult.Failure<Response>(registryResolve.Error!);
                }

                var registryResult = await registryResolve.Value!.LookupByRegistrationNumberAsync(
                    command.CompanyRegistrationNumber, cancellationToken);
                if (!registryResult.IsSuccess)
                {
                    // Pre-classified by the adapter (NotFound / Transient /
                    // Permanent) — pass through.
                    return BusinessResult.Failure<Response>(registryResult.Error!);
                }

                company = registryResult.Value!;
                if (!company.IsActiveInRegistry)
                {
                    logger.LogInformation(
                        "Register rejected for {Ico}: registry reports company as no longer active.",
                        command.CompanyRegistrationNumber);
                    return BusinessResult.Failure<Response>(
                        Error.Permanent(BusinessErrorMessage.CustomerCompanyDissolved));
                }
            }

            var passwordHash = hasher.Hash(command.Password);
            var user = User.Create(
                id: ids.Next(),
                email: command.Email,
                role: command.Role,
                fullName: command.FullName,
                countryCodePrimary: command.CountryCodePrimary,
                passwordHash: passwordHash);

            if (company is not null)
            {
                user.AttachCompanySnapshot(
                    company.RegistrationNumber, company.CompanyName, company.VatId, company.FetchedAt);
            }

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
