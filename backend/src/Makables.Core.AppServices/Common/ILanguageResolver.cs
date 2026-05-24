using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Identity;

namespace Makables.Core.AppServices.Common;

/// <summary>
/// Resolves the language a user should receive UI / email content in. Per
/// T-0028 design directive: <c>User.PreferredLanguage → CountryConfiguration.DefaultLanguageCode → "cs-CZ"</c>.
///
/// The resolver runs at outbox-enqueue time so the language is locked in
/// before the email is dispatched (T-0029 consumer doesn't re-resolve).
/// </summary>
public interface ILanguageResolver
{
    /// <summary>
    /// Resolve the language for a fully-loaded <paramref name="user"/>.
    /// Returns a BCP-47 tag from <see cref="LanguageCode.Supported"/>.
    /// Falls back to <see cref="LanguageCode.DefaultFallback"/> if neither
    /// the user nor their country has a recognised language set.
    /// </summary>
    Task<string> ResolveForUserAsync(User user, CancellationToken cancellationToken);
}

/// <summary>
/// Read-only implementation. The country lookup is cached in-request by
/// <see cref="ICountryConfigurationRepository"/>, so resolving the
/// language for several users from the same country in one handler
/// doesn't roundtrip per user.
/// </summary>
public sealed class LanguageResolver(ICountryConfigurationRepository countries) : ILanguageResolver
{
    public async Task<string> ResolveForUserAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (LanguageCode.IsValid(user.PreferredLanguage))
            return user.PreferredLanguage!;

        var country = await countries.GetByCodeAsync(user.CountryCodePrimary, cancellationToken);
        if (country is not null && LanguageCode.IsValid(country.DefaultLanguageCode))
            return country.DefaultLanguageCode;

        return LanguageCode.DefaultFallback;
    }
}
