using Makables.Core.Domain.Makers;
using FluentAssertions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Auth;

public class RegisterHandlerTests
{
    private const string ValidIco = "27074358";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ICompanyRegistry _companyRegistry = Substitute.For<ICompanyRegistry>();
    private readonly ICompanyRegistryFactory _companyRegistryFactory =
        Substitute.For<ICompanyRegistryFactory>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly IOneTimeTokenIssuer _issuer = Substitute.For<IOneTimeTokenIssuer>();
    private readonly Register.Handler _handler;

    public RegisterHandlerTests()
    {
        _ids.Next().Returns("user-fresh-01");
        _hasher.Hash(Arg.Any<string>()).Returns("argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        // T-0162: the company branch resolves the registry per country via
        // the keyed factory (T-0124); tests stub the registry and route the
        // factory to it, same as RegisterMakerHandlerTests.
        _companyRegistryFactory
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(_companyRegistry));
        _handler = new Register.Handler(
            _users, _companyRegistryFactory, _hasher, _ids, _issuer,
            NullLogger<Register.Handler>.Instance);
    }

    private static CompanyRecord AresRecord(
        bool isActiveInRegistry = true, bool isStale = false, string? vatId = "CZ27074358") =>
        new(
            RegistrationNumber: ValidIco,
            VatId: vatId,
            CompanyName: "Avast Software s.r.o.",
            LegalForm: "Společnost s ručením omezeným",
            LegalType: MakerLegalType.LegalEntity,
            RegisteredAddress: Address.Create(
                id: $"ares-snapshot-{ValidIco}",
                street: "Pikrtova", houseNumber: "1737", city: "Praha", zip: "14000",
                countryCodeIso: "CZ", auditCountryCode: "CZ"),
            IncorporatedOn: new DateOnly(2006, 9, 4),
            IsActiveInRegistry: isActiveInRegistry,
            SourceRegistry: "ares",
            FetchedAt: new DateTimeOffset(2026, 7, 25, 10, 0, 0, TimeSpan.Zero),
            IsStale: isStale);

    [Fact]
    public async Task Returns_conflict_when_email_already_exists()
    {
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _handler.Handle(
            new Register.Command("anna@example.cz", "abcd1234567", "Anna", "CZ", UserRole.Customer),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthEmailAlreadyExists);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        await _issuer.DidNotReceive().IssueAsync(Arg.Any<IssueRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejects_admin_role_on_public_registration()
    {
        var result = await _handler.Handle(
            new Register.Command("ops@example.cz", "abcd1234567", "Ops", "CZ", UserRole.Admin),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthForbidden);
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        _users.DidNotReceive().Add(Arg.Any<User>());
        await _issuer.DidNotReceive().IssueAsync(Arg.Any<IssueRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Happy_path_creates_user_and_delegates_to_issuer_with_shared_rate_limit_budget()
    {
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _handler.Handle(
            new Register.Command("Anna.Nováková@example.cz", "abcd1234567", "Anna Nováková", "cz", UserRole.Customer),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.UserId.Should().Be("user-fresh-01");

        _hasher.Received(1).Hash("abcd1234567");
        _users.Received(1).Add(Arg.Is<User>(u =>
            u.Id == "user-fresh-01" &&
            u.Email == "Anna.Nováková@example.cz" &&
            u.EmailNormalized == User.NormalizeEmail("Anna.Nováková@example.cz") &&
            u.Role == UserRole.Customer &&
            u.PasswordHash == "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB"));

        // T-0024 security M-2 fix: Register flows through the SAME issuer
        // as SendEmailConfirmation, so register + 2 resends = 3 emails in
        // 10 min (not 4 — sharing the per-user rate-limit budget).
        await _issuer.Received(1).IssueAsync(Arg.Is<IssueRequest>(r =>
            r.Email == "Anna.Nováková@example.cz" &&
            r.Purpose == OneTimeTokenPurpose.EmailConfirmation &&
            r.TokenLifetime == SendEmailConfirmation.TokenLifetime &&
            r.OutboxEventType == OutboxEventTypes.AuthEmailConfirmationSend &&
            r.MaxRequestsPerWindow == SendEmailConfirmation.MaxRequestsPerWindow &&
            r.RateLimitWindow == SendEmailConfirmation.RateLimitWindow &&
            r.EligibilityFilter != null),
            Arg.Any<CancellationToken>());
    }

    // ---- T-0162 company branch ("Jsem firma") ----

    [Fact]
    public async Task Null_company_ico_skips_registry_and_persists_null_snapshot()
    {
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _handler.Handle(
            new Register.Command("anna@example.cz", "abcd1234567", "Anna", "CZ", UserRole.Customer),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _companyRegistryFactory.DidNotReceive()
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _users.Received(1).Add(Arg.Is<User>(u =>
            u.CompanyRegistrationNumber == null &&
            u.CompanyName == null &&
            u.CompanyVatId == null &&
            u.CompanySnapshotFetchedAt == null));
    }

    [Fact]
    public async Task Rejects_checksum_invalid_ico_without_calling_registry()
    {
        // "00000000" passes the shape rules (8 digits) but fails mod-11 —
        // the handler gate must reject BEFORE any registry spend (ADR 0018).
        var result = await _handler.Handle(
            new Register.Command("anna@example.cz", "abcd1234567", "Anna", "CZ", UserRole.Customer, "00000000"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.IcoFormatInvalid);
        result.Error.Type.Should().Be(ErrorType.Validation);
        await _companyRegistryFactory.DidNotReceive()
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _users.DidNotReceive().Add(Arg.Any<User>());
    }

    [Fact]
    public async Task Email_conflict_beats_registry_lookup()
    {
        // Budget guard: an already-taken email must not burn an ARES call.
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _handler.Handle(
            new Register.Command("anna@example.cz", "abcd1234567", "Anna", "CZ", UserRole.Customer, ValidIco),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthEmailAlreadyExists);
        await _companyRegistryFactory.DidNotReceive()
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Active_company_persists_snapshot_from_registry_record()
    {
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _companyRegistry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(AresRecord()));

        var result = await _handler.Handle(
            new Register.Command("anna@example.cz", "abcd1234567", "Anna", "CZ", UserRole.Customer, ValidIco),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.UserId.Should().Be("user-fresh-01");
        _users.Received(1).Add(Arg.Is<User>(u =>
            u.CompanyRegistrationNumber == ValidIco &&
            u.CompanyName == "Avast Software s.r.o." &&
            u.CompanyVatId == "CZ27074358" &&
            u.CompanySnapshotFetchedAt == new DateTimeOffset(2026, 7, 25, 10, 0, 0, TimeSpan.Zero)));
        await _issuer.Received(1).IssueAsync(Arg.Any<IssueRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Company_without_vat_id_persists_null_dic()
    {
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _companyRegistry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(AresRecord(vatId: null)));

        var result = await _handler.Handle(
            new Register.Command("anna@example.cz", "abcd1234567", "Anna", "CZ", UserRole.Customer, ValidIco),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _users.Received(1).Add(Arg.Is<User>(u =>
            u.CompanyRegistrationNumber == ValidIco &&
            u.CompanyVatId == null));
    }

    [Fact]
    public async Task Company_not_found_passes_through_and_creates_nothing()
    {
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _companyRegistry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<CompanyRecord>(
                new Error("registrationNumber", BusinessErrorMessage.CompanyNotFound, ErrorType.NotFound)));

        var result = await _handler.Handle(
            new Register.Command("anna@example.cz", "abcd1234567", "Anna", "CZ", UserRole.Customer, ValidIco),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CompanyNotFound);
        result.Error.Type.Should().Be(ErrorType.NotFound);
        _users.DidNotReceive().Add(Arg.Any<User>());
        await _issuer.DidNotReceive().IssueAsync(Arg.Any<IssueRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dissolved_company_is_rejected_with_customer_scoped_code()
    {
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _companyRegistry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(AresRecord(isActiveInRegistry: false)));

        var result = await _handler.Handle(
            new Register.Command("anna@example.cz", "abcd1234567", "Anna", "CZ", UserRole.Customer, ValidIco),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CustomerCompanyDissolved);
        result.Error.Type.Should().Be(ErrorType.Permanent);
        _users.DidNotReceive().Add(Arg.Any<User>());
    }

    [Fact]
    public async Task Registry_transient_failure_passes_through_and_creates_nothing()
    {
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _companyRegistry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<CompanyRecord>(
                Error.Transient(BusinessErrorMessage.CompanyRegistryTransient)));

        var result = await _handler.Handle(
            new Register.Command("anna@example.cz", "abcd1234567", "Anna", "CZ", UserRole.Customer, ValidIco),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CompanyRegistryTransient);
        result.Error.Type.Should().Be(ErrorType.Transient);
        _users.DidNotReceive().Add(Arg.Any<User>());
    }

    [Fact]
    public async Task Stale_cached_snapshot_still_registers()
    {
        // ADR 0018 7-day stale fallback: customers accept the stale record
        // silently (no admin verification lane, unlike makers).
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _companyRegistry.LookupByRegistrationNumberAsync(ValidIco, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(AresRecord(isStale: true)));

        var result = await _handler.Handle(
            new Register.Command("anna@example.cz", "abcd1234567", "Anna", "CZ", UserRole.Customer, ValidIco),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _users.Received(1).Add(Arg.Is<User>(u => u.CompanyRegistrationNumber == ValidIco));
    }
}
