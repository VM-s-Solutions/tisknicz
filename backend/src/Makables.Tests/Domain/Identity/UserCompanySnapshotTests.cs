using FluentAssertions;
using Makables.Core.Domain.Identity;

namespace Makables.Tests.Domain.Identity;

/// <summary>
/// Pins the T-0162 <see cref="User.AttachCompanySnapshot"/> contract: the
/// snapshot is attach-once-at-registration data mirroring the ARES
/// <c>CompanyRecord</c> slice (IČO + name + DIČ + fetched-at); blank DIČ
/// normalizes to null (neplátce DPH), inputs are trimmed, and blank
/// IČO / name are programmer errors.
/// </summary>
public class UserCompanySnapshotTests
{
    private static User NewUser() => User.Create(
        id: "user-1",
        email: "anna@example.cz",
        role: UserRole.Customer,
        fullName: "Anna Nováková",
        countryCodePrimary: "CZ");

    [Fact]
    public void New_user_has_no_company_snapshot()
    {
        var user = NewUser();

        user.CompanyRegistrationNumber.Should().BeNull();
        user.CompanyName.Should().BeNull();
        user.CompanyVatId.Should().BeNull();
        user.CompanySnapshotFetchedAt.Should().BeNull();
    }

    [Fact]
    public void Attach_sets_all_fields_trimmed()
    {
        var fetchedAt = new DateTimeOffset(2026, 7, 25, 10, 0, 0, TimeSpan.Zero);

        var user = NewUser().AttachCompanySnapshot(
            " 27074358 ", " Avast Software s.r.o. ", " CZ27074358 ", fetchedAt);

        user.CompanyRegistrationNumber.Should().Be("27074358");
        user.CompanyName.Should().Be("Avast Software s.r.o.");
        user.CompanyVatId.Should().Be("CZ27074358");
        user.CompanySnapshotFetchedAt.Should().Be(fetchedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_vat_id_normalizes_to_null(string? vatId)
    {
        var user = NewUser().AttachCompanySnapshot(
            "27074358", "Avast Software s.r.o.", vatId, DateTimeOffset.UtcNow);

        user.CompanyVatId.Should().BeNull();
    }

    [Theory]
    [InlineData("", "Avast Software s.r.o.")]
    [InlineData("   ", "Avast Software s.r.o.")]
    [InlineData("27074358", "")]
    [InlineData("27074358", "   ")]
    public void Blank_ico_or_name_throws(string ico, string name)
    {
        var act = () => NewUser().AttachCompanySnapshot(ico, name, null, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>();
    }
}
