using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Registry;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Maker;

/// <summary>
/// Admin re-fetches the Maker's ARES snapshot (US-admin-0005). Updates
/// <c>CompanyName</c>, <c>DIČ</c>, <c>LegalForm</c>,
/// <c>IsActiveInRegistry</c>, and the linked <c>Address</c> row's
/// fields (legal seat). Does NOT touch <c>IsVerified</c> per ADR 0018
/// §"Snapshot semantics".
///
/// <para>
/// Audited via <c>AdminAuditPipelineBehavior</c>. ARES transient
/// failures pass through with <see cref="ErrorType.Transient"/> so the
/// admin can retry (US-admin-0005 AC-2).
/// </para>
///
/// <para>
/// AC-3 invariant: past invoices reference their OWN snapshot of the
/// Maker fields (captured at order time via a future invoice
/// projection). Mutating the Maker row here only affects future
/// invoices. This handler does not need to do anything special — the
/// invoice-time snapshot lives outside the Maker entity.
/// </para>
/// </summary>
public static class RefreshMakerFromAres
{
    public sealed record Command(string MakerId, string? Notes)
        : ICommand<Response>, IAdminAuditableCommand
    {
        public string ActionCode => "maker.refreshFromAres";
        public string TargetEntity => "maker";
        public string TargetId => MakerId;
    }

    public sealed record Response(bool SnapshotIsStale);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.MakerId)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IMakerRepository makers,
        IAddressRepository addresses,
        ICompanyRegistry companyRegistry,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            var maker = await makers.GetByIdAsync(command.MakerId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<Response>(Error.NotFound("maker"));
            }

            var registryResult = await companyRegistry.LookupByRegistrationNumberAsync(
                maker.RegistrationNumber, cancellationToken);
            if (!registryResult.IsSuccess)
            {
                return BusinessResult.Failure<Response>(registryResult.Error!);
            }

            var company = registryResult.Value!;

            maker.UpdateSnapshot(
                companyName: company.CompanyName,
                vatId: company.VatId,
                legalForm: company.LegalForm,
                incorporatedOn: company.IncorporatedOn,
                isActiveInRegistry: company.IsActiveInRegistry,
                snapshotFetchedAt: company.FetchedAt,
                snapshotIsStale: company.IsStale);

            // Refresh the linked Address row in-place — invoice projections
            // capture the address snapshot at order time, so mutating the
            // legal-seat row here doesn't break invoice history.
            var address = await addresses.GetByIdAsync(maker.RegisteredAddressId, cancellationToken);
            if (address is not null)
            {
                var seat = company.RegisteredAddress;
                address.Update(
                    street: seat.Street,
                    houseNumber: seat.HouseNumber,
                    city: seat.City,
                    zip: seat.Zip,
                    countryCodeIso: seat.CountryCodeIso,
                    state: seat.State);
            }
            else
            {
                logger.LogWarning(
                    "RefreshMakerFromAres: maker {MakerId} references missing address {AddressId} — snapshot updated but legal seat could not be refreshed.",
                    maker.Id, maker.RegisteredAddressId);
            }

            logger.LogInformation(
                "RefreshMakerFromAres succeeded for maker {MakerId} (IČO {Ico}, stale snapshot={Stale}).",
                maker.Id, maker.RegistrationNumber, company.IsStale);

            return BusinessResult.Success(new Response(SnapshotIsStale: company.IsStale));
        }
    }
}
