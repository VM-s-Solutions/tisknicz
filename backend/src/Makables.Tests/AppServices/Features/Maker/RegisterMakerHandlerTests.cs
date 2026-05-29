using FluentAssertions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.AppServices.Features.Maker;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Maker;

/// <summary>
/// Pins the T-0033 RegisterMaker contract: IČO format gate → ARES lookup
/// → dissolved-entity gate → email + IČO uniqueness pre-checks →
/// User + Address + Maker added → email-confirmation issued. The handler
/// does NOT call SaveChangesAsync; the pipeline behavior commits everything
/// atomically on success.
/// </summary>
public class RegisterMakerHandlerTests
{
    private const string ValidIco = "27074358";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IAddressRepository _addresses = Substitute.For<IAddressRepository>();
    private readonly ICompanyRegistry _companyRegistry = Substitute.For<ICompanyRegistry>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly IOneTimeTokenIssuer _issuer = Substitute.For<IOneTimeTokenIssuer>();
    private readonly RegisterMaker.Handler _sut;

    public RegisterMakerHandlerTests()
    {
        // Sequential ids so the test can spot-check User vs Address vs Maker.
        var idQueue = new Queue<string>(["user-1", "addr-1", "maker-1"]);
        _ids.Next().Returns(_ => idQueue.Count > 0 ? idQueue.Dequeue() : Guid.NewGuid().ToString());
        _hasher.Hash(Arg.Any<string>()).Returns("argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");

        _sut = new RegisterMaker.Handler(
            _users, _makers, _addresses, _companyRegistry, _hasher, _ids, _issuer,
            NullLogger<RegisterMaker.Handler>.Instance);
    }

    private static RegisterMaker.Command ValidCommand(string ico = ValidIco) => new(
        Email: "anna@example.cz",
        Password: "correct-horse-battery-staple",
        FullName: "Anna Nováková",
        CountryCodePrimary: "CZ",
        RegistrationNumber: ico);

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

    // ---- format gate ----

    [Theory]
    [InlineData("1234567")]      // wrong length (mod-11 doesn't run; the format check rejects first)
    [InlineData("00000000")]     // mod-11 fails
    public async Task Rejects_invalid_ico_format_without_calling_registry(string ico)
    {
        var result = await _sut.Handle(ValidCommand(ico), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.IcoFormatInvalid);
        result.Error.Type.Should().Be(ErrorType.Validation);
        await _companyRegistry.DidNotReceive().LookupByRegistrationNumberAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---- registry failure passthrough ----

    [Fact]
    public async Task Passes_through_registry_NotFound()
    {
        _companyRegistry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<CompanyRecord>(Error.NotFound("company")));

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        _users.DidNotReceive().Add(Arg.Any<User>());
        _makers.DidNotReceive().Add(Arg.Any<Makables.Core.Domain.Makers.Maker>());
    }

    [Fact]
    public async Task Passes_through_registry_Transient()
    {
        _companyRegistry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<CompanyRecord>(
                Error.Transient(BusinessErrorMessage.CompanyRegistryTransient)));

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CompanyRegistryTransient);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }

    // ---- dissolved-entity gate ----

    [Fact]
    public async Task Rejects_dissolved_company_with_MakerCompanyDissolved()
    {
        _companyRegistry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(AresRecord(isActiveInRegistry: false)));

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.MakerCompanyDissolved);
        result.Error.Type.Should().Be(ErrorType.Permanent);
        _users.DidNotReceive().Add(Arg.Any<User>());
        _makers.DidNotReceive().Add(Arg.Any<Makables.Core.Domain.Makers.Maker>());
        await _issuer.DidNotReceive().IssueAsync(Arg.Any<IssueRequest>(), Arg.Any<CancellationToken>());
    }

    // ---- conflict pre-checks ----

    [Fact]
    public async Task Rejects_when_email_already_taken_AND_does_not_call_uniqueness_for_ico()
    {
        // Email pre-check runs before ICO pre-check (in order they appear in the handler).
        _companyRegistry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(AresRecord()));
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthEmailAlreadyExists);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        await _makers.DidNotReceive().IcoExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _users.DidNotReceive().Add(Arg.Any<User>());
    }

    [Fact]
    public async Task Rejects_when_ico_already_registered_on_the_platform()
    {
        _companyRegistry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(AresRecord()));
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _makers.IcoExistsAsync(ValidIco, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.MakerIcoAlreadyRegistered);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        _users.DidNotReceive().Add(Arg.Any<User>());
        _makers.DidNotReceive().Add(Arg.Any<Makables.Core.Domain.Makers.Maker>());
    }

    // ---- happy path ----

    [Fact]
    public async Task Happy_path_adds_User_Address_Maker_and_enqueues_email_confirmation()
    {
        _companyRegistry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(AresRecord()));
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _makers.IcoExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.UserId.Should().Be("user-1");
        result.Value.MakerId.Should().Be("maker-1");
        result.Value.SnapshotIsStale.Should().BeFalse();

        _users.Received(1).Add(Arg.Is<User>(u =>
            u.Id == "user-1" && u.Role == UserRole.Maker && u.EmailConfirmedAt == null));
        _addresses.Received(1).Add(Arg.Is<Address>(a =>
            a.Id == "addr-1" && a.CountryCodeIso == "CZ" && a.City == "Praha"));
        _makers.Received(1).Add(Arg.Is<Makables.Core.Domain.Makers.Maker>(m =>
            m.Id == "maker-1"
            && m.UserId == "user-1"
            && m.RegistrationNumber == ValidIco
            && m.RegisteredAddressId == "addr-1"
            && m.IsActiveInRegistry
            && !m.IsVerified
            && !m.SnapshotIsStale));

        await _issuer.Received(1).IssueAsync(
            Arg.Is<IssueRequest>(r =>
                r.Email == "anna@example.cz"
                && r.Purpose == OneTimeTokenPurpose.EmailConfirmation
                && r.OutboxEventType == OutboxEventTypes.AuthEmailConfirmationSend),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stale_ARES_snapshot_proceeds_and_surfaces_on_response()
    {
        _companyRegistry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(AresRecord(isStale: true)));
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _makers.IcoExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SnapshotIsStale.Should().BeTrue("ADR 0018 — stale snapshot must not block onboarding");
        _makers.Received(1).Add(Arg.Is<Makables.Core.Domain.Makers.Maker>(m => m.SnapshotIsStale));
    }

    // ---- slug disambiguation ladder (T-0043 Copilot review) ----

    [Fact]
    public async Task Slug_collision_falls_back_to_base_dash_ico()
    {
        _companyRegistry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(AresRecord()));
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _makers.IcoExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        // Base slug "avast-software-s-r-o" already taken; the
        // {base}-{ico} fallback is free.
        _makers.SlugExistsAsync("avast-software-s-r-o", Arg.Any<CancellationToken>()).Returns(true);
        _makers.SlugExistsAsync($"avast-software-s-r-o-{ValidIco}", Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _makers.Received(1).Add(Arg.Is<Makables.Core.Domain.Makers.Maker>(m =>
            m.Slug == $"avast-software-s-r-o-{ValidIco}"));
    }

    [Fact]
    public async Task Slug_double_collision_falls_back_to_bare_ico()
    {
        _companyRegistry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(AresRecord()));
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _makers.IcoExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        // BOTH base and {base}-{ico} taken → fall back to bare IČO
        // (globally unique among active makers, IČO uniqueness was
        // pre-checked).
        _makers.SlugExistsAsync("avast-software-s-r-o", Arg.Any<CancellationToken>()).Returns(true);
        _makers.SlugExistsAsync($"avast-software-s-r-o-{ValidIco}", Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _makers.Received(1).Add(Arg.Is<Makables.Core.Domain.Makers.Maker>(m => m.Slug == ValidIco));
    }
}
