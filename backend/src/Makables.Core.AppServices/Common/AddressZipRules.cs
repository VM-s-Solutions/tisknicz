using FluentValidation;
using Makables.Core.Domain.Addresses.Validators;
using Makables.Core.Domain.Common;

namespace Makables.Core.AppServices.Common;

/// <summary>
/// FluentValidation extension that wraps <see cref="IAddressFormatValidator.IsValidZipAsync"/>
/// in a clean rule-builder chain. Use from feature validators that
/// carry both a country code AND a zip on the same command:
///
/// <code>
/// public sealed class Validator : AbstractValidator&lt;Command&gt;
/// {
///     public Validator(IAddressFormatValidator zipValidator)
///     {
///         RuleFor(c => c.Zip)
///             .MustBeValidZipForCountry(zipValidator, c => c.CountryCode)
///             .WithErrorCode(BusinessErrorMessage.InvalidZipFormat);
///     }
/// }
/// </code>
///
/// The instance member name <c>countryAccessor</c> takes the COMMAND
/// (the whole DTO), not the zip — so the country selector lives on the
/// same level as the rule, regardless of whether the address is the
/// command itself or a nested sub-record.
/// </summary>
public static class AddressZipRules
{
    public static IRuleBuilderOptions<T, string> MustBeValidZipForCountry<T>(
        this IRuleBuilder<T, string> builder,
        IAddressFormatValidator addressFormatValidator,
        Func<T, string> countryAccessor)
    {
        ArgumentNullException.ThrowIfNull(addressFormatValidator);
        ArgumentNullException.ThrowIfNull(countryAccessor);

        return builder
            .MustAsync(async (command, zip, ct) =>
            {
                var country = countryAccessor(command);
                if (string.IsNullOrWhiteSpace(country) || string.IsNullOrWhiteSpace(zip))
                    return false;
                return await addressFormatValidator.IsValidZipAsync(country, zip, ct);
            })
            .WithErrorCode(BusinessErrorMessage.InvalidZipFormat);
    }
}
