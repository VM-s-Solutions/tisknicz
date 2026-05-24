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
/// </summary>
public sealed class PublicAppUrlsOptions
{
    public const string SectionName = "PublicAppUrls";

    /// <summary>Web frontend base URL, e.g. <c>https://makables.cz</c>. No trailing slash.</summary>
    public string WebBaseUrl { get; set; } = "https://makables.cz";

    /// <summary>Path template for the magic-link consume page. Default: <c>/auth/magic?token={token}</c>.</summary>
    public string MagicLinkPath { get; set; } = "/auth/magic?token={token}";

    /// <summary>Path template for the email-confirmation page. Default: <c>/auth/confirm?token={token}</c>.</summary>
    public string EmailConfirmationPath { get; set; } = "/auth/confirm?token={token}";

    /// <summary>Path template for the password-reset page. Default: <c>/auth/reset?token={token}</c>.</summary>
    public string PasswordResetPath { get; set; } = "/auth/reset?token={token}";
}
