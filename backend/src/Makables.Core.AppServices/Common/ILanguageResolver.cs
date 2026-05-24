using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Identity;

namespace Makables.Core.AppServices.Common;

/// <summary>
/// Resolves the language a user should receive UI / email content in. Per
/// T-0028 design directive: <c>preferredLang → CountryConfiguration.DefaultLanguageCode → "cs-CZ"</c>.
///
/// The resolver runs at outbox-enqueue time so the language is locked in
/// before the email is dispatched (T-0029 consumer doesn't re-resolve).
/// </summary>
public interface ILanguageResolver
{
    /// <summary>
    /// Resolve the language given an explicit preferred-language string and
    /// a country code. Use this overload from places that don't (yet) have
    /// a <see cref="User"/> aggregate — e.g. the unknown-user branch of
    /// <c>OneTimeTokenIssuer</c> which must pay the same DB-roundtrip
    /// cost as the known-user branch (timing-equalization invariant
    /// T-0023 B-1). <paramref name="preferredLanguage"/> may be null /
    /// blank / malformed; it's accepted only when it passes
    /// <see cref="LanguageCode.IsValid"/>.
    /// </summary>
    Task<string> ResolveAsync(
        string? preferredLanguage,
        string countryCode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Convenience overload that reads the inputs off <paramref name="user"/>.
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
    public async Task<string> ResolveAsync(
        string? preferredLanguage,
        string countryCode,
        CancellationToken cancellationToken)
    {
        if (LanguageCode.IsValid(preferredLanguage))
            return preferredLanguage!;

        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            var country = await countries.GetByCodeAsync(countryCode, cancellationToken);
            if (country is not null && LanguageCode.IsValid(country.DefaultLanguageCode))
                return country.DefaultLanguageCode;
        }

        return LanguageCode.DefaultFallback;
    }

    public Task<string> ResolveForUserAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return ResolveAsync(user.PreferredLanguage, user.CountryCodePrimary, cancellationToken);
    }
}
