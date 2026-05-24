namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Configuration for the default country assigned to a brand-new user
/// when no per-request country signal exists — currently Google OAuth's
/// "Sign in with Google" flow where Google doesn't tell us the country.
/// Per reviewer T-0026 code-quality MAJOR M-1 (replace the hardcoded
/// "CZ" in <see cref="CompleteGoogleOAuth"/>).
///
/// Bound from <c>Auth:DefaultCountryCode</c>. Per CLAUDE.md the launch
/// market is Czechia, so the default is "CZ"; when DE/SK comes online
/// it's a config change, not a code change. The handler still falls
/// back to "CZ" if the config is missing so a deploy without the value
/// never crashes.
/// </summary>
public sealed class AuthDefaultCountryOptions
{
    public const string SectionName = "Auth:DefaultCountry";

    /// <summary>ISO 3166-1 alpha-2. Defaults to "CZ" per the launch market.</summary>
    public string CountryCodePrimary { get; set; } = "CZ";
}
