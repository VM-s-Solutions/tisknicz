using FluentAssertions;
using Makables.Core.AppServices.Features.Maker;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Maker;

/// <summary>T-0034 — pins RefreshMakerFromAres admin command (US-admin-0005).</summary>
public class RefreshMakerFromAresHandlerTests
{
    private static readonly DateTimeOffset RegisteredAt = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RefreshedAt = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
    private const string Ico = "27074358";

    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IAddressRepository _addresses = Substitute.For<IAddressRepository>();
    private readonly ICompanyRegistry _registry = Substitute.For<ICompanyRegistry>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly RefreshMakerFromAres.Handler _sut;

    public RefreshMakerFromAresHandlerTests()
    {
        _session.GetUserId().Returns("admin-1");
        _sut = new RefreshMakerFromAres.Handler(
            _makers, _addresses, _registry, _session, NullLogger<RefreshMakerFromAres.Handler>.Instance);
    }

    private static Makables.Core.Domain.Makers.Maker ExistingMaker() =>
        Makables.Core.Domain.Makers.Maker.Create(
            id: "maker-1",
            userId: "user-1",
            registrationNumber: Ico,
            vatId: "CZ27074358",
            companyName: "Avast Software s.r.o.",
            legalForm: "s.r.o.",
            registeredAddressId: "addr-1",
            incorporatedOn: new DateOnly(2006, 9, 4),
            isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: RegisteredAt,
            snapshotIsStale: false,
            countryCode: "CZ");

    private static Address ExistingAddress() => Address.Create(
        id: "addr-1",
        street: "Pikrtova", houseNumber: "1737", city: "Praha", zip: "14000",
        countryCodeIso: "CZ", auditCountryCode: "CZ");

    private static CompanyRecord RefreshedRecord(bool isActiveInRegistry = true, bool isStale = false) => new(
        RegistrationNumber: Ico,
        VatId: "CZ27074358",
        CompanyName: "Avast Software s.r.o. v likvidaci",
        LegalForm: "s.r.o.",
        RegisteredAddress: Address.Create(
            id: $"ares-snapshot-{Ico}",
            street: "Nová 1", houseNumber: "10", city: "Brno", zip: "60200",
            countryCodeIso: "CZ", auditCountryCode: "CZ"),
        IncorporatedOn: new DateOnly(2006, 9, 4),
        IsActiveInRegistry: isActiveInRegistry,
        SourceRegistry: "ares",
        FetchedAt: RefreshedAt,
        IsStale: isStale);

    [Fact]
    public async Task Returns_NotFound_when_maker_is_missing()
    {
        _makers.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((Makables.Core.Domain.Makers.Maker?)null);

        var result = await _sut.Handle(new RefreshMakerFromAres.Command("missing", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        await _registry.DidNotReceive().LookupByRegistrationNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Passes_through_registry_Transient_failure()
    {
        var maker = ExistingMaker();
        _makers.GetByIdAsync("maker-1", Arg.Any<CancellationToken>()).Returns(maker);
        _registry.LookupByRegistrationNumberAsync(Ico, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<CompanyRecord>(
                Error.Transient(BusinessErrorMessage.CompanyRegistryTransient)));

        var result = await _sut.Handle(new RefreshMakerFromAres.Command("maker-1", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Transient);
        maker.CompanyName.Should().Be("Avast Software s.r.o.", "snapshot must not change when registry fails");
    }

    [Fact]
    public async Task Happy_path_updates_snapshot_and_address_but_preserves_verification()
    {
        var maker = ExistingMaker();
        maker.MarkVerified();
        var addr = ExistingAddress();
        _makers.GetByIdAsync("maker-1", Arg.Any<CancellationToken>()).Returns(maker);
        _addresses.GetByIdAsync("addr-1", Arg.Any<CancellationToken>()).Returns(addr);
        _registry.LookupByRegistrationNumberAsync(Ico, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(RefreshedRecord()));

        var result = await _sut.Handle(new RefreshMakerFromAres.Command("maker-1", "Customer reported name change"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SnapshotIsStale.Should().BeFalse();
        maker.CompanyName.Should().Be("Avast Software s.r.o. v likvidaci");
        maker.SnapshotFetchedAt.Should().Be(RefreshedAt);
        maker.IsVerified.Should().BeTrue("admin verification is independent of registry refresh");
        addr.City.Should().Be("Brno");
        addr.Zip.Should().Be("60200");
        addr.Latitude.Should().BeNull("address moved — coordinates are cleared for the geocoder sweep");
    }

    [Fact]
    public async Task Surfaces_stale_snapshot_flag_on_response()
    {
        var maker = ExistingMaker();
        var addr = ExistingAddress();
        _makers.GetByIdAsync("maker-1", Arg.Any<CancellationToken>()).Returns(maker);
        _addresses.GetByIdAsync("addr-1", Arg.Any<CancellationToken>()).Returns(addr);
        _registry.LookupByRegistrationNumberAsync(Ico, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(RefreshedRecord(isStale: true)));

        var result = await _sut.Handle(new RefreshMakerFromAres.Command("maker-1", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SnapshotIsStale.Should().BeTrue();
        maker.SnapshotIsStale.Should().BeTrue();
    }

    [Fact]
    public async Task Updates_snapshot_even_when_address_is_missing()
    {
        // The maker's RegisteredAddressId might dangle if an admin manually
        // tampered with the address row. Refreshing the snapshot must still
        // succeed; the warning log is the operator's signal to investigate.
        var maker = ExistingMaker();
        _makers.GetByIdAsync("maker-1", Arg.Any<CancellationToken>()).Returns(maker);
        _addresses.GetByIdAsync("addr-1", Arg.Any<CancellationToken>()).Returns((Address?)null);
        _registry.LookupByRegistrationNumberAsync(Ico, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(RefreshedRecord()));

        var result = await _sut.Handle(new RefreshMakerFromAres.Command("maker-1", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        maker.CompanyName.Should().Be("Avast Software s.r.o. v likvidaci");
    }

    [Fact]
    public async Task Returns_Unauthorized_when_session_has_no_user()
    {
        // Fail-closed shape — host-level [Authorize] should make this
        // unreachable, but attributing the refresh to "system" via the
        // audit pipeline would mask a misconfigured endpoint. The
        // registry MUST NOT be hit on the unauthorized branch.
        // T-0034 Copilot review.
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(new RefreshMakerFromAres.Command("maker-1", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _makers.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _registry.DidNotReceive().LookupByRegistrationNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Command_carries_admin_audit_metadata()
    {
        var cmd = new RefreshMakerFromAres.Command("maker-1", "notes");
        cmd.ActionCode.Should().Be("maker.refreshFromAres");
        cmd.TargetEntity.Should().Be("maker");
        cmd.TargetId.Should().Be("maker-1");
    }
}
