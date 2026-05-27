using FluentAssertions;
using FluentValidation;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Addresses.Validators;
using Makables.Core.Domain.Common;
using NSubstitute;

namespace Makables.Tests.AppServices.Common;

/// <summary>
/// Pins the T-0030 FluentValidation mixin contract: feature validators
/// can chain <c>RuleFor(x =&gt; x.Zip).MustBeValidZipForCountry(...)</c>
/// without re-implementing the per-country lookup. The rule reports the
/// canonical <see cref="BusinessErrorMessage.InvalidZipFormat"/> code.
/// </summary>
public class AddressZipRulesTests
{
    private sealed record Cmd(string Zip, string CountryCode);

    private sealed class CmdValidator : AbstractValidator<Cmd>
    {
        public CmdValidator(IAddressFormatValidator zipValidator)
        {
            RuleFor(c => c.Zip).MustBeValidZipForCountry(zipValidator, c => c.CountryCode);
        }
    }

    private readonly IAddressFormatValidator _zipValidator = Substitute.For<IAddressFormatValidator>();
    private readonly CmdValidator _sut;

    public AddressZipRulesTests()
    {
        _sut = new CmdValidator(_zipValidator);
    }

    [Fact]
    public async Task Passes_when_validator_accepts_the_zip()
    {
        _zipValidator.IsValidZipAsync("CZ", "123 45", Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.ValidateAsync(new Cmd("123 45", "CZ"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Fails_with_invalidZip_code_when_validator_rejects()
    {
        _zipValidator.IsValidZipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.ValidateAsync(new Cmd("not-a-zip", "CZ"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.ErrorCode.Should().Be(BusinessErrorMessage.InvalidZipFormat);
    }

    [Theory]
    [InlineData("", "CZ")]
    [InlineData("12345", "")]
    public async Task Fails_short_circuit_for_blank_zip_or_country_without_hitting_validator(
        string zip, string country)
    {
        var result = await _sut.ValidateAsync(new Cmd(zip, country));

        result.IsValid.Should().BeFalse();
        await _zipValidator.DidNotReceive().IsValidZipAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Passes_country_from_accessor_through_to_validator_call()
    {
        _zipValidator.IsValidZipAsync("SK", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        await _sut.ValidateAsync(new Cmd("83100", "SK"));

        await _zipValidator.Received(1).IsValidZipAsync("SK", "83100", Arg.Any<CancellationToken>());
    }
}
