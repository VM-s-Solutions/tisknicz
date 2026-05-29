using FluentAssertions;
using Makables.Core.Domain.Makers;

namespace Makables.Tests.Domain.Makers;

public class MakerTests
{
    private static readonly DateTimeOffset SnapshotAt = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);

    private static Maker ValidDefaults() => Maker.Create(
        id: "maker-1",
        userId: "user-1",
        registrationNumber: "27074358",
        vatId: "CZ27074358",
        companyName: "Avast Software s.r.o.",
        legalForm: "Společnost s ručením omezeným",
        registeredAddressId: "addr-1",
        incorporatedOn: new DateOnly(2006, 9, 4),
        isActiveInRegistry: true,
        sourceRegistry: "ares",
        snapshotFetchedAt: SnapshotAt,
        snapshotIsStale: false,
        countryCode: "cz");

    [Fact]
    public void Create_normalises_strings_and_country_code()
    {
        var m = Maker.Create(
            id: "maker-1",
            userId: "user-1",
            registrationNumber: "  27074358  ",
            vatId: "  CZ27074358  ",
            companyName: "  Avast  ",
            legalForm: "  s.r.o.  ",
            registeredAddressId: "addr-1",
            incorporatedOn: null,
            isActiveInRegistry: true,
            sourceRegistry: "ARES",
            snapshotFetchedAt: SnapshotAt,
            snapshotIsStale: false,
            countryCode: "cz");

        m.RegistrationNumber.Should().Be("27074358");
        m.VatId.Should().Be("CZ27074358");
        m.CompanyName.Should().Be("Avast");
        m.LegalForm.Should().Be("s.r.o.");
        m.SourceRegistry.Should().Be("ares");
        m.CountryCode.Should().Be("CZ");
    }

    [Fact]
    public void Create_treats_blank_optional_fields_as_null()
    {
        var m = Maker.Create(
            id: "maker-1",
            userId: "user-1",
            registrationNumber: "27074358",
            vatId: "   ",
            companyName: "X",
            legalForm: "   ",
            registeredAddressId: "addr-1",
            incorporatedOn: null,
            isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: SnapshotAt,
            snapshotIsStale: false,
            countryCode: "CZ");

        m.VatId.Should().BeNull();
        m.LegalForm.Should().BeNull();
    }

    [Theory]
    [InlineData("", "user-1", "12345678", "X", "addr-1", "ares", "CZ")]
    [InlineData("maker-1", "", "12345678", "X", "addr-1", "ares", "CZ")]
    [InlineData("maker-1", "user-1", "", "X", "addr-1", "ares", "CZ")]
    [InlineData("maker-1", "user-1", "12345678", "", "addr-1", "ares", "CZ")]
    [InlineData("maker-1", "user-1", "12345678", "X", "", "ares", "CZ")]
    [InlineData("maker-1", "user-1", "12345678", "X", "addr-1", "", "CZ")]
    [InlineData("maker-1", "user-1", "12345678", "X", "addr-1", "ares", "")]
    [InlineData("maker-1", "user-1", "12345678", "X", "addr-1", "ares", "CZE")]
    public void Create_rejects_invalid_required_fields(
        string id, string userId, string ico, string name, string addr, string source, string country)
    {
        var act = () => Maker.Create(id, userId, ico, null, name, null, addr, null,
            true, source, SnapshotAt, false, country);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void New_maker_is_not_verified()
    {
        var m = ValidDefaults();
        m.IsVerified.Should().BeFalse();
    }

    [Fact]
    public void MarkVerified_flips_the_flag()
    {
        var m = ValidDefaults();

        m.MarkVerified();

        m.IsVerified.Should().BeTrue();
    }

    [Fact]
    public void MarkVerified_twice_throws()
    {
        var m = ValidDefaults();
        m.MarkVerified();

        var act = () => m.MarkVerified();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void UpdateSnapshot_refreshes_fields_without_touching_IsVerified()
    {
        var m = ValidDefaults();
        m.MarkVerified();

        var laterAt = SnapshotAt.AddDays(30);
        m.UpdateSnapshot(
            companyName: "Avast Software s.r.o. v likvidaci",
            vatId: null,
            legalForm: "Společnost s ručením omezeným",
            incorporatedOn: new DateOnly(2006, 9, 4),
            isActiveInRegistry: false,
            snapshotFetchedAt: laterAt,
            snapshotIsStale: false);

        m.CompanyName.Should().Be("Avast Software s.r.o. v likvidaci");
        m.VatId.Should().BeNull();
        m.IsActiveInRegistry.Should().BeFalse();
        m.SnapshotFetchedAt.Should().Be(laterAt);
        m.IsVerified.Should().BeTrue("admin verification survives a registry-snapshot refresh");
    }

    [Fact]
    public void UpdateSnapshot_rejects_blank_company_name()
    {
        var m = ValidDefaults();

        var act = () => m.UpdateSnapshot(
            companyName: "   ", vatId: null, legalForm: null,
            incorporatedOn: null, isActiveInRegistry: true,
            snapshotFetchedAt: SnapshotAt, snapshotIsStale: false);

        act.Should().Throw<ArgumentException>();
    }

    // === T-0034 maker-editable profile fields ===

    [Fact]
    public void Create_defaults_maker_editable_fields_to_null_or_false()
    {
        var m = ValidDefaults();

        m.Bio.Should().BeNull();
        m.BankAccount.Should().BeNull();
        m.PersonalPickupEnabled.Should().BeFalse();
        m.PickupNote.Should().BeNull();
    }

    [Fact]
    public void UpdateProfile_patches_only_supplied_fields()
    {
        var m = ValidDefaults();
        m.UpdateProfile(bio: "Initial bio", bankAccount: "123456789/0100", personalPickupEnabled: true, pickupNote: "Po dohodě");

        // Patch only Bio; rest must remain.
        m.UpdateProfile(bio: "Updated bio", bankAccount: null, personalPickupEnabled: null, pickupNote: null);

        m.Bio.Should().Be("Updated bio");
        m.BankAccount.Should().Be("123456789/0100");
        m.PersonalPickupEnabled.Should().BeTrue();
        m.PickupNote.Should().Be("Po dohodě");
    }

    [Fact]
    public void UpdateProfile_empty_string_clears_optional_value()
    {
        var m = ValidDefaults();
        m.UpdateProfile(bio: "Initial bio", bankAccount: "123456789/0100", personalPickupEnabled: null, pickupNote: "Note");

        m.UpdateProfile(bio: "", bankAccount: "", personalPickupEnabled: null, pickupNote: "");

        m.Bio.Should().BeNull();
        m.BankAccount.Should().BeNull();
        m.PickupNote.Should().BeNull();
    }

    [Fact]
    public void UpdateProfile_rejects_bio_over_500_chars()
    {
        var m = ValidDefaults();

        var act = () => m.UpdateProfile(
            bio: new string('x', 501),
            bankAccount: null, personalPickupEnabled: null, pickupNote: null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateProfile_does_not_touch_snapshot_or_admin_state()
    {
        var m = ValidDefaults();
        m.MarkVerified();
        var originalCompany = m.CompanyName;
        var originalIco = m.RegistrationNumber;

        m.UpdateProfile(bio: "Bio", bankAccount: "123456789/0100", personalPickupEnabled: true, pickupNote: "Note");

        m.CompanyName.Should().Be(originalCompany, "snapshot fields are read-only for makers");
        m.RegistrationNumber.Should().Be(originalIco);
        m.IsVerified.Should().BeTrue("admin verification is independent of profile edits");
    }

    // === T-0043 catalog fields ===

    [Fact]
    public void Create_derives_slug_from_company_name()
    {
        var m = ValidDefaults();
        m.Slug.Should().Be("avast-software-s-r-o");
    }

    [Fact]
    public void Create_accepts_explicit_slug_override()
    {
        var m = Maker.Create(
            id: "maker-1", userId: "user-1", registrationNumber: "27074358",
            vatId: null, companyName: "Avast Software s.r.o.", legalForm: null,
            registeredAddressId: "addr-1", incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: SnapshotAt, snapshotIsStale: false,
            countryCode: "CZ", slug: "avast-software-s-r-o-27074358");

        m.Slug.Should().Be("avast-software-s-r-o-27074358");
    }

    [Fact]
    public void Create_falls_back_to_ico_when_company_name_has_no_sluggable_chars()
    {
        var m = Maker.Create(
            id: "maker-1", userId: "user-1", registrationNumber: "27074358",
            vatId: null, companyName: "***", legalForm: null,
            registeredAddressId: "addr-1", incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: SnapshotAt, snapshotIsStale: false, countryCode: "CZ");

        m.Slug.Should().Be("27074358");
    }

    [Fact]
    public void Create_defaults_catalog_stats_to_zero()
    {
        var m = ValidDefaults();
        m.RatingAverageBp.Should().Be(0);
        m.RatingCount.Should().Be(0);
        m.TotalOrders.Should().Be(0);
    }

    [Fact]
    public void UpdateSnapshot_does_not_change_slug()
    {
        var m = ValidDefaults();
        var originalSlug = m.Slug;

        m.UpdateSnapshot(
            companyName: "Avast Software a.s.", vatId: null, legalForm: null,
            incorporatedOn: null, isActiveInRegistry: true,
            snapshotFetchedAt: SnapshotAt.AddDays(30), snapshotIsStale: false);

        m.Slug.Should().Be(originalSlug, "a public URL must survive a company-name refresh from ARES");
    }
}
