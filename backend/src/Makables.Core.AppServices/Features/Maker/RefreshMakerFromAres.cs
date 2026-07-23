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
///
/// <para>
/// <b>Authorization.</b> The handler does NOT verify the caller is an
/// admin. The host that wires this controller MUST gate the endpoint
/// with <c>[Authorize(Roles = "Admin")]</c>. T-0034 security reviewer M-1.
/// </para>
///
/// <para>
/// <b>Audit-log gap.</b> The audit pipeline snapshots the Maker target
/// only; the linked Address row's mutation is NOT captured in the
/// before/after JSONB (T-0034 sec reviewer m-2). For an action named
/// "refresh from ARES" the seat change is implicit, but a follow-up
/// ticket should either widen the audit snapshot or append a second
/// audit row keyed on the Address.
/// </para>
/// </summary>
public static class RefreshMakerFromAres
{
    public sealed record Command(string MakerId, string? Notes)
        : ICommand<RefreshMakerFromAresResponse>, IAdminAuditableCommand
    {
        public string ActionCode => "maker.refreshFromAres";
        public string TargetEntity => "maker";
        public string TargetId => MakerId;
    }

    // Globally-unique response name (NSwag convention — a schema named just
    // `Response` shadows the DOM Response type in the generated TS client).
    public sealed record RefreshMakerFromAresResponse(bool SnapshotIsStale);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.MakerId)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            // Cap Notes at the audit-log column width. T-0034 sec
            // reviewer m-3.
            When(c => c.Notes is not null, () =>
            {
                RuleFor(c => c.Notes!)
                    .MaximumLength(2000).WithErrorCode(BusinessErrorMessage.MaxLength);
            });
        }
    }

    public sealed class Handler(
        IMakerRepository makers,
        IAddressRepository addresses,
        ICompanyRegistryFactory companyRegistryFactory,
        IUserSessionProvider session,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BusinessResult<RefreshMakerFromAresResponse>>
    {
        public async Task<BusinessResult<RefreshMakerFromAresResponse>> Handle(Command command, CancellationToken cancellationToken)
        {
            // T-0034 Copilot review: fail-closed when there's no session
            // user. The host-level [Authorize(Roles="Admin")] gate should
            // make this unreachable, but attributing a privileged state
            // change to "system" via AdminAuditPipelineBehavior would
            // mask a misconfigured endpoint. Matches DeactivateMaker /
            // VerifyMaker shape.
            if (string.IsNullOrEmpty(session.GetUserId()))
            {
                return BusinessResult.Failure<RefreshMakerFromAresResponse>(Error.Unauthorized());
            }

            var maker = await makers.GetByIdAsync(command.MakerId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<RefreshMakerFromAresResponse>(Error.NotFound("maker"));
            }

            // Registry adapter selected by the maker's country
            // (CountryConfiguration.DefaultRegistry) via the keyed factory —
            // T-0124.
            var registryResolve = await companyRegistryFactory.ResolveAsync(
                maker.CountryCode, cancellationToken);
            if (!registryResolve.IsSuccess)
            {
                return BusinessResult.Failure<RefreshMakerFromAresResponse>(registryResolve.Error!);
            }

            var registryResult = await registryResolve.Value!.LookupByRegistrationNumberAsync(
                maker.RegistrationNumber, cancellationToken);
            if (!registryResult.IsSuccess)
            {
                return BusinessResult.Failure<RefreshMakerFromAresResponse>(registryResult.Error!);
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

            return BusinessResult.Success(new RefreshMakerFromAresResponse(SnapshotIsStale: company.IsStale));
        }
    }
}
