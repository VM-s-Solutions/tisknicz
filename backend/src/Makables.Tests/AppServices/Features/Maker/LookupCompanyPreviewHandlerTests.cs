using FluentAssertions;
using Makables.Core.AppServices.Features.Maker;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Maker;

/// <summary>
/// Pins the T-0159 IČO → company-preview contract (business decision Q4):
/// mod-11 gate before any registry spend, per-country registry via the
/// T-0124 factory, display-slice mapping, and error passthrough (the
/// registry adapter already classified NotFound/Transient/Permanent).
/// </summary>
public class LookupCompanyPreviewHandlerTests
{
    private const string ValidIco = "27074358";

    private readonly ICompanyRegistry _registry = Substitute.For<ICompanyRegistry>();
    private readonly ICompanyRegistryFactory _factory = Substitute.For<ICompanyRegistryFactory>();
    private readonly LookupCompanyPreview.Handler _sut;

    public LookupCompanyPreviewHandlerTests()
    {
        _factory.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(_registry));
        _sut = new LookupCompanyPreview.Handler(
            _factory, NullLogger<LookupCompanyPreview.Handler>.Instance);
    }

    private static CompanyRecord AresRecord(bool isActiveInRegistry = true, bool isStale = false) =>
        new(
            RegistrationNumber: ValidIco,
            VatId: "CZ27074358",
            CompanyName: "Avast Software s.r.o.",
            LegalForm: "Společnost s ručením omezeným",
            RegisteredAddress: Address.Create(
                id: $"ares-snapshot-{ValidIco}",
                street: "Pikrtova", houseNumber: "1737", city: "Praha", zip: "14000",
                countryCodeIso: "CZ", auditCountryCode: "CZ"),
            IncorporatedOn: new DateOnly(2006, 9, 4),
            IsActiveInRegistry: isActiveInRegistry,
            SourceRegistry: "ares",
            FetchedAt: new DateTimeOffset(2026, 5, 25, 10, 0, 0, TimeSpan.Zero),
            IsStale: isStale);

    [Fact]
    public async Task Maps_the_display_slice_for_a_found_company()
    {
        _registry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(AresRecord()));

        var result = await _sut.Handle(
            new LookupCompanyPreview.Query(ValidIco, "CZ"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var preview = result.Value!;
        preview.CompanyName.Should().Be("Avast Software s.r.o.");
        preview.VatId.Should().Be("CZ27074358");
        preview.Street.Should().Be("Pikrtova");
        preview.HouseNumber.Should().Be("1737");
        preview.City.Should().Be("Praha");
        preview.Zip.Should().Be("14000");
        preview.IsActiveInRegistry.Should().BeTrue();
        preview.IsStale.Should().BeFalse();
    }

    [Theory]
    [InlineData("00000000")] // mod-11 checksum fails
    [InlineData("12345678")] // mod-11 checksum fails
    public async Task Rejects_checksum_invalid_ico_without_registry_spend(string ico)
    {
        var result = await _sut.Handle(
            new LookupCompanyPreview.Query(ico, "CZ"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.IcoFormatInvalid);
        await _registry.DidNotReceiveWithAnyArgs()
            .LookupByRegistrationNumberAsync(default!, default);
    }

    [Fact]
    public async Task Passes_registry_failures_through_unchanged()
    {
        _registry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<CompanyRecord>(
                Error.NotFound("registrationNumber", BusinessErrorMessage.CompanyNotFound)));

        var result = await _sut.Handle(
            new LookupCompanyPreview.Query(ValidIco, "CZ"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CompanyNotFound);
    }

    [Fact]
    public async Task Passes_factory_failures_through_unchanged()
    {
        _factory.ResolveAsync("ZZ", Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<ICompanyRegistry>(
                Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigurationNotFound)));

        var result = await _sut.Handle(
            new LookupCompanyPreview.Query(ValidIco, "ZZ"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CountryConfigurationNotFound);
    }

    [Fact]
    public async Task Reports_dissolved_and_stale_flags_verbatim()
    {
        _registry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(AresRecord(isActiveInRegistry: false, isStale: true)));

        var result = await _sut.Handle(
            new LookupCompanyPreview.Query(ValidIco, "CZ"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsActiveInRegistry.Should().BeFalse();
        result.Value.IsStale.Should().BeTrue();
    }
}
