using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Registry;
using Makables.Core.Domain.Registry.Validators;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Maker;

/// <summary>
/// Anonymous IČO → company preview for the maker registration form
/// (T-0159, business decision Q4: "IČO → ARES předvyplní, maker potvrdí
/// správnost"). Read-only: resolves the country's registry adapter via
/// the T-0124 keyed factory and returns the display slice of the
/// <see cref="CompanyRecord"/> so the form can show WHO the IČO belongs
/// to before the user submits. Registration itself (T-0033) re-runs the
/// authoritative lookup server-side — this endpoint is UX, not a gate.
///
/// The checksum gate runs BEFORE the registry call (ADR 0018
/// §"Validation before lookup") so garbage input never consumes the
/// ARES rate-limit budget; the 24h registry cache (T-0032) makes the
/// happy path cheap for the registration that follows.
/// </summary>
public static class LookupCompanyPreview
{
    public sealed record Query(
        string RegistrationNumber,
        string CountryCode) : IQuery<LookupCompanyPreviewResponse>;

    /// <summary>Display slice only — no <c>FetchedAt</c>/source internals. Globally-unique
    /// name — a nested record named plain "Response" becomes a TS class that shadows the
    /// DOM Response type in the NSwag client (T-0076/T-0080 precedent).</summary>
    public sealed record LookupCompanyPreviewResponse(
        string RegistrationNumber,
        string CompanyName,
        string? LegalForm,
        string? VatId,
        string Street,
        string HouseNumber,
        string City,
        string Zip,
        bool IsActiveInRegistry,
        bool IsStale);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.RegistrationNumber)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .Length(8).WithErrorCode(BusinessErrorMessage.IcoFormatInvalid)
                .Matches("^[0-9]+$").WithErrorCode(BusinessErrorMessage.IcoFormatInvalid);

            RuleFor(q => q.CountryCode)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .Length(2).WithErrorCode(BusinessErrorMessage.Required);
        }
    }

    public sealed class Handler(
        ICompanyRegistryFactory companyRegistryFactory,
        ILogger<Handler> logger) : IRequestHandler<Query, BusinessResult<LookupCompanyPreviewResponse>>
    {
        public async Task<BusinessResult<LookupCompanyPreviewResponse>> Handle(Query query, CancellationToken cancellationToken)
        {
            // Mod-11 gate mirrors RegisterMaker step 1 — the validator only
            // enforces shape; the checksum lives in CzechIcoValidator.
            if (!CzechIcoValidator.IsValid(query.RegistrationNumber))
            {
                return BusinessResult.Failure<LookupCompanyPreviewResponse>(
                    Error.Validation(nameof(query.RegistrationNumber), BusinessErrorMessage.IcoFormatInvalid));
            }

            var registryResolve = await companyRegistryFactory.ResolveAsync(
                query.CountryCode, cancellationToken);
            if (!registryResolve.IsSuccess)
            {
                return BusinessResult.Failure<LookupCompanyPreviewResponse>(registryResolve.Error!);
            }

            var registryResult = await registryResolve.Value!.LookupByRegistrationNumberAsync(
                query.RegistrationNumber, cancellationToken);
            if (!registryResult.IsSuccess)
            {
                // Already classified (NotFound / Transient / Permanent) —
                // pass through; the form maps codes to copy.
                return BusinessResult.Failure<LookupCompanyPreviewResponse>(registryResult.Error!);
            }

            var company = registryResult.Value!;
            logger.LogInformation(
                "Company preview served for IČO {RegistrationNumber} (active={IsActive}, stale={IsStale}).",
                company.RegistrationNumber, company.IsActiveInRegistry, company.IsStale);

            return BusinessResult.Success(new LookupCompanyPreviewResponse(
                RegistrationNumber: company.RegistrationNumber,
                CompanyName: company.CompanyName,
                LegalForm: company.LegalForm,
                VatId: company.VatId,
                Street: company.RegisteredAddress.Street,
                HouseNumber: company.RegisteredAddress.HouseNumber,
                City: company.RegisteredAddress.City,
                Zip: company.RegisteredAddress.Zip,
                IsActiveInRegistry: company.IsActiveInRegistry,
                IsStale: company.IsStale));
        }
    }
}
