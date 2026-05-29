using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Registry;
using Makables.Core.Domain.Registry.Validators;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Maker;

/// <summary>
/// Register a new maker. Per ADR 0010 (Address), ADR 0012 (Auth), ADR
/// 0018 (Company Registry). The flow:
///
/// <list type="number">
///   <item><description><b>IČO format gate</b> via <see cref="CzechIcoValidator"/>
///     before any I/O. Invalid input returns
///     <see cref="BusinessErrorMessage.IcoFormatInvalid"/> immediately —
///     does not consume the ARES rate-limit budget.</description></item>
///   <item><description><b>ARES lookup</b> via <see cref="ICompanyRegistry"/>.
///     A NotFound surfaces as <see cref="BusinessErrorMessage.CompanyNotFound"/>.
///     A Transient failure with no usable stale-cache fallback surfaces
///     as <see cref="BusinessErrorMessage.CompanyRegistryTransient"/> —
///     the user can retry later. Permanent / Configuration failures
///     are mapped to <see cref="BusinessErrorMessage.CompanyRegistryPermanent"/>.</description></item>
///   <item><description><b>Dissolved-entity gate.</b> If ARES says
///     <see cref="CompanyRecord.IsActiveInRegistry"/> is false, reject
///     with <see cref="BusinessErrorMessage.MakerCompanyDissolved"/>
///     (Permanent). T-0033 design decision: dissolved entities can't
///     onboard as platform makers.</description></item>
///   <item><description><b>Conflict pre-checks.</b> Email already taken
///     → <see cref="BusinessErrorMessage.AuthEmailAlreadyExists"/>;
///     IČO already on Makables →
///     <see cref="BusinessErrorMessage.MakerIcoAlreadyRegistered"/>.
///     Both run before any aggregate is added to the change tracker so
///     a rolled-back transaction doesn't leave half-built state.</description></item>
///   <item><description><b>Build + add</b> User + Address (ARES legal seat) +
///     Maker (snapshot of ARES fields). All three sit in the same
///     change tracker; <c>UnitOfWorkPipelineBehavior</c> commits them
///     atomically on success.</description></item>
///   <item><description><b>Email-confirmation token</b> via the shared
///     <see cref="IOneTimeTokenIssuer"/> (same pipeline as the customer
///     <see cref="Register"/> — keeps the per-user rate-limit budget
///     unified, T-0024 sec M-2).</description></item>
/// </list>
///
/// Geocoding is deliberately OUT of the handler: ADR 0010 §"Geocoding
/// policy" says non-blocking. <see cref="Address.Latitude"/> /
/// <see cref="Address.Longitude"/> are left null; the partial index
/// <c>ix_addresses_pending_geocode</c> (from T-0030) lets a future
/// background sweep fill them.
///
/// <see cref="Response.SnapshotIsStale"/> propagates the ADR 0018
/// stale-cache fallback flag — T-0035 frontend can render a
/// "registry data may be outdated, admin will refresh" notice.
/// </summary>
public static class RegisterMaker
{
    public sealed record Command(
        string Email,
        string Password,
        string FullName,
        string CountryCodePrimary,
        string RegistrationNumber)
        : ICommand<Response>;

    public sealed record Response(
        string UserId,
        string MakerId,
        bool SnapshotIsStale);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Email)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .EmailAddress().WithErrorCode(BusinessErrorMessage.InvalidEmailFormat)
                .MaximumLength(320).WithErrorCode(BusinessErrorMessage.MaxLength);

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

            // IČO shape is validated by FluentValidation here (length +
            // digits) AND by CzechIcoValidator inside the handler (mod-11
            // checksum). The double-gate is deliberate: the validator
            // catches simple bad input; the handler catches the
            // checksum-fail case AND short-circuits ARES.
            RuleFor(c => c.RegistrationNumber)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .Length(8).WithErrorCode(BusinessErrorMessage.IcoFormatInvalid)
                .Matches("^[0-9]+$").WithErrorCode(BusinessErrorMessage.IcoFormatInvalid);
        }
    }

    public sealed class Handler(
        IUserRepository users,
        IMakerRepository makers,
        IAddressRepository addresses,
        ICompanyRegistry companyRegistry,
        IPasswordHasher hasher,
        IIdGenerator ids,
        IOneTimeTokenIssuer issuer,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            // 1. Format gate. Re-checked here even though the validator
            // ran — the validator only enforces length + digits; mod-11
            // is in CzechIcoValidator.
            if (!CzechIcoValidator.IsValid(command.RegistrationNumber))
            {
                return BusinessResult.Failure<Response>(
                    Error.Validation(nameof(command.RegistrationNumber), BusinessErrorMessage.IcoFormatInvalid));
            }

            // 2. ARES lookup (cache or fresh fetch).
            var registryResult = await companyRegistry.LookupByRegistrationNumberAsync(
                command.RegistrationNumber, cancellationToken);
            if (!registryResult.IsSuccess)
            {
                // The registry adapter already classified the failure
                // (NotFound / Transient / Permanent). Pass through.
                return BusinessResult.Failure<Response>(registryResult.Error!);
            }

            var company = registryResult.Value!;

            // 3. Dissolved-entity gate.
            if (!company.IsActiveInRegistry)
            {
                logger.LogInformation(
                    "RegisterMaker rejected for {Ico}: ARES reports company as no longer active.",
                    command.RegistrationNumber);
                return BusinessResult.Failure<Response>(
                    Error.Permanent(BusinessErrorMessage.MakerCompanyDissolved));
            }

            // 4. Conflict pre-checks — run BEFORE any aggregate is added.
            var emailNormalized = User.NormalizeEmail(command.Email);
            if (await users.EmailExistsAsync(emailNormalized, cancellationToken))
            {
                return BusinessResult.Failure<Response>(
                    Error.Conflict("email", BusinessErrorMessage.AuthEmailAlreadyExists));
            }
            if (await makers.IcoExistsAsync(command.RegistrationNumber, cancellationToken))
            {
                return BusinessResult.Failure<Response>(
                    Error.Conflict("registrationNumber", BusinessErrorMessage.MakerIcoAlreadyRegistered));
            }

            // 5. Build + add aggregates. UnitOfWorkPipelineBehavior commits
            //    User + Address + Maker atomically on success.
            //    ARES address.Id is "ares-snapshot-{ico}" from T-0032 mapper;
            //    we MUST re-key for persistence (one Address row per registration).
            var passwordHash = hasher.Hash(command.Password);
            var user = User.Create(
                id: ids.Next(),
                email: command.Email,
                role: UserRole.Maker,
                fullName: command.FullName,
                countryCodePrimary: command.CountryCodePrimary,
                passwordHash: passwordHash);
            users.Add(user);

            // Clone the ARES-returned address into a real row using the
            // address aggregate's factory so the row has a server-issued
            // id + correct audit tenancy.
            var aresAddress = company.RegisteredAddress;
            var addressId = ids.Next();
            var legalSeat = Address.Create(
                id: addressId,
                street: aresAddress.Street,
                houseNumber: aresAddress.HouseNumber,
                city: aresAddress.City,
                zip: aresAddress.Zip,
                countryCodeIso: aresAddress.CountryCodeIso,
                auditCountryCode: command.CountryCodePrimary,
                state: aresAddress.State);
            addresses.Add(legalSeat);

            // Derive the public-profile slug from the ARES company name.
            // On a collision with an existing active maker, append the
            // IČO so the URL stays unique (T-0043). The IČO is itself
            // unique across active makers (pre-checked above), so the
            // fallback is collision-free.
            var baseSlug = SlugGenerator.Slugify(company.CompanyName);
            if (baseSlug.Length == 0)
            {
                baseSlug = company.RegistrationNumber;
            }
            var slug = baseSlug;
            if (await makers.SlugExistsAsync(slug, cancellationToken))
            {
                slug = $"{baseSlug}-{company.RegistrationNumber}";
            }

            var maker = Makables.Core.Domain.Makers.Maker.Create(
                id: ids.Next(),
                userId: user.Id,
                registrationNumber: company.RegistrationNumber,
                vatId: company.VatId,
                companyName: company.CompanyName,
                legalForm: company.LegalForm,
                registeredAddressId: addressId,
                incorporatedOn: company.IncorporatedOn,
                isActiveInRegistry: company.IsActiveInRegistry,
                sourceRegistry: company.SourceRegistry,
                snapshotFetchedAt: company.FetchedAt,
                snapshotIsStale: company.IsStale,
                countryCode: command.CountryCodePrimary,
                slug: slug);
            makers.Add(maker);

            // 6. Email-confirmation token (same pipeline as customer Register).
            await issuer.IssueAsync(new IssueRequest(
                Email: command.Email,
                Purpose: OneTimeTokenPurpose.EmailConfirmation,
                TokenLifetime: SendEmailConfirmation.TokenLifetime,
                OutboxEventType: OutboxEventTypes.AuthEmailConfirmationSend,
                MaxRequestsPerWindow: SendEmailConfirmation.MaxRequestsPerWindow,
                RateLimitWindow: SendEmailConfirmation.RateLimitWindow,
                EligibilityFilter: u => u.EmailConfirmedAt is null),
                cancellationToken);

            logger.LogInformation(
                "RegisterMaker succeeded for {Ico} (user {UserId}, maker {MakerId}, stale snapshot={Stale}).",
                command.RegistrationNumber, user.Id, maker.Id, company.IsStale);

            return BusinessResult.Success(new Response(
                UserId: user.Id,
                MakerId: maker.Id,
                SnapshotIsStale: company.IsStale));
        }
    }
}
