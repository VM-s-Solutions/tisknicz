using FluentAssertions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;

namespace Makables.Tests.AppServices.Features.Auth;

/// <summary>
/// Pins the T-0162 IČO shape rules on <see cref="Register.Validator"/>:
/// the optional <c>CompanyRegistrationNumber</c> is validated only when
/// provided (null = private person, no company rules run). Shape only —
/// mod-11 checksum is the handler's gate, mirroring RegisterMaker's
/// deliberate double-gate split.
/// </summary>
public class RegisterValidatorTests
{
    private readonly Register.Validator _validator = new();

    private static Register.Command Command(string? ico) => new(
        Email: "anna@example.cz",
        Password: "abcd1234567",
        FullName: "Anna Nováková",
        CountryCodePrimary: "CZ",
        Role: UserRole.Customer,
        CompanyRegistrationNumber: ico);

    [Fact]
    public void Null_company_ico_is_valid()
    {
        var result = _validator.Validate(Command(null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Shape_valid_ico_passes()
    {
        // Checksum is NOT the validator's job — "00000000" fails mod-11 but
        // passes shape; the handler gate owns the checksum reject.
        var result = _validator.Validate(Command("00000000"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_provided_ico_fails_required()
    {
        var result = _validator.Validate(Command(string.Empty));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(Register.Command.CompanyRegistrationNumber) &&
            e.ErrorCode == BusinessErrorMessage.Required);
    }

    [Theory]
    [InlineData("1234567")]     // too short
    [InlineData("123456789")]   // too long
    [InlineData("1234567a")]    // non-digit
    [InlineData("123 4567")]    // embedded space
    public void Malformed_ico_fails_with_ico_format_code(string ico)
    {
        var result = _validator.Validate(Command(ico));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(Register.Command.CompanyRegistrationNumber) &&
            e.ErrorCode == BusinessErrorMessage.IcoFormatInvalid);
    }

    [Fact]
    public void Existing_rules_unaffected_by_company_field()
    {
        var result = _validator.Validate(new Register.Command(
            Email: "not-an-email",
            Password: "short",
            FullName: "",
            CountryCodePrimary: "CZE",
            Role: UserRole.Customer));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotContain(e =>
            e.PropertyName == nameof(Register.Command.CompanyRegistrationNumber));
    }
}
