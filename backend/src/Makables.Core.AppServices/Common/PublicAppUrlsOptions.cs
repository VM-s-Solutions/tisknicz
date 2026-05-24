namespace Makables.Core.AppServices.Common;

/// <summary>
/// Public-facing URLs the backend needs to reference when building links
/// for transactional emails. Bound from <c>PublicAppUrls</c> in
/// configuration; environment override at deploy time per ADR 0016.
///
/// Path templates use <c>{token}</c> as the substitution placeholder so
/// callers can swap in the raw single-use token without string-builder
/// gymnastics. Same shape across environments — only <see cref="WebBaseUrl"/>
/// differs between local / staging / production.
///
/// Validated at startup by <see cref="PublicAppUrlsOptionsValidator"/>
/// (T-0028 sec reviewer B-2 / M-1): WebBaseUrl must be absolute https
/// (or http on loopback for dev); every path template must start with
/// <c>/</c> and contain the literal substring <c>{token}</c>. A misconfig
/// crashes the host at boot, not at first email send.
/// </summary>
public sealed class PublicAppUrlsOptions
{
    public const string SectionName = "PublicAppUrls";
    public const string TokenPlaceholder = "{token}";

    /// <summary>Web frontend base URL, e.g. <c>https://makables.cz</c>. No trailing slash.</summary>
    public string WebBaseUrl { get; set; } = "https://makables.cz";

    /// <summary>Path template for the magic-link consume page. MUST contain <see cref="TokenPlaceholder"/>.</summary>
    public string MagicLinkPath { get; set; } = "/auth/magic?token={token}";

    /// <summary>Path template for the email-confirmation page. MUST contain <see cref="TokenPlaceholder"/>.</summary>
    public string EmailConfirmationPath { get; set; } = "/auth/confirm?token={token}";

    /// <summary>Path template for the password-reset page. MUST contain <see cref="TokenPlaceholder"/>.</summary>
    public string PasswordResetPath { get; set; } = "/auth/reset?token={token}";
}

/// <summary>
/// Startup validator for <see cref="PublicAppUrlsOptions"/>. Per T-0028
/// security reviewer B-2: a hostile or fat-fingered <c>WebBaseUrl</c>
/// like <c>javascript:</c> or <c>https://attacker.example/?x=</c> would
/// produce phishing-grade clickable links in every transactional email.
/// </summary>
public static class PublicAppUrlsOptionsValidator
{
    public static (bool Ok, string? Error) Validate(PublicAppUrlsOptions options)
    {
        if (options is null) return (false, "PublicAppUrls section is missing.");

        if (!Uri.TryCreate(options.WebBaseUrl, UriKind.Absolute, out var uri))
            return (false, $"PublicAppUrls:WebBaseUrl '{options.WebBaseUrl}' is not a valid absolute URI.");
        var isHttps = uri.Scheme == Uri.UriSchemeHttps;
        var isLoopbackHttp = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        if (!isHttps && !isLoopbackHttp)
            return (false, $"PublicAppUrls:WebBaseUrl scheme must be https (or http on loopback for dev). Got: '{uri.Scheme}'.");

        foreach (var (name, path) in new[]
        {
            ("MagicLinkPath", options.MagicLinkPath),
            ("EmailConfirmationPath", options.EmailConfirmationPath),
            ("PasswordResetPath", options.PasswordResetPath),
        })
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
                return (false, $"PublicAppUrls:{name} must be a non-empty path starting with '/'. Got: '{path}'.");
            if (!path.Contains(PublicAppUrlsOptions.TokenPlaceholder, StringComparison.Ordinal))
                return (false, $"PublicAppUrls:{name} must contain the literal placeholder '{PublicAppUrlsOptions.TokenPlaceholder}'. Got: '{path}'.");
        }

        return (true, null);
    }
}
