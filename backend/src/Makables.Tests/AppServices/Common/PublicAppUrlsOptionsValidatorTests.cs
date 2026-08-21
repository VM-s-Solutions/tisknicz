using FluentAssertions;
using Makables.Core.AppServices.Common;

namespace Makables.Tests.AppServices.Common;

/// <summary>
/// Pins T-0028 sec reviewer B-2 / M-1: a misconfigured
/// <see cref="PublicAppUrlsOptions"/> (javascript: URL, blank path,
/// missing {token} placeholder) MUST crash the host at boot rather than
/// silently ship phishing-grade links in every transactional email.
/// </summary>
public class PublicAppUrlsOptionsValidatorTests
{
    private static PublicAppUrlsOptions ValidDefaults() => new();

    [Fact]
    public void Defaults_are_valid()
    {
        var (ok, err) = PublicAppUrlsOptionsValidator.Validate(ValidDefaults());
        ok.Should().BeTrue(err);
    }

    /// <summary>
    /// T-0166 (audit AUTH-H1): the frontend `(auth)` route group adds NO URL
    /// segment, so the real pages are /verify, /magic and /reset. The old
    /// "/auth/*" defaults 404'd in every environment — only WebBaseUrl is
    /// overridden at deploy time, never the paths. Pinned exactly so a
    /// regression back to a prefixed (or renamed) path fails loudly here.
    /// </summary>
    [Fact]
    public void Default_paths_target_the_real_frontend_routes()
    {
        var opts = ValidDefaults();

        opts.MagicLinkPath.Should().Be("/magic?token={token}");
        opts.EmailConfirmationPath.Should().Be("/verify?token={token}");
        opts.PasswordResetPath.Should().Be("/reset?token={token}");
    }

    /// <summary>
    /// T-0166 AC-1: the full composed action URLs, using the same composition
    /// contract as EmailSendService.BuildActionUrl (base trimmed of trailing
    /// '/', path template's {token} substituted URL-escaped).
    /// </summary>
    [Theory]
    [InlineData("https://makables.cz")]
    [InlineData("https://makables.cz/")]
    public void Default_paths_compose_to_real_frontend_urls(string baseUrl)
    {
        var opts = ValidDefaults();
        opts.WebBaseUrl = baseUrl;

        string Compose(string template) =>
            opts.WebBaseUrl.TrimEnd('/') +
            template.Replace(PublicAppUrlsOptions.TokenPlaceholder, Uri.EscapeDataString("tok/1+2"));

        Compose(opts.EmailConfirmationPath).Should().Be("https://makables.cz/verify?token=tok%2F1%2B2");
        Compose(opts.MagicLinkPath).Should().Be("https://makables.cz/magic?token=tok%2F1%2B2");
        Compose(opts.PasswordResetPath).Should().Be("https://makables.cz/reset?token=tok%2F1%2B2");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,foo")]
    [InlineData("ftp://makables.cz")]
    [InlineData("http://makables.cz")]                   // non-loopback http
    [InlineData("not a url")]
    [InlineData("")]
    public void Rejects_non_https_or_non_loopback_WebBaseUrl(string baseUrl)
    {
        var opts = ValidDefaults();
        opts.WebBaseUrl = baseUrl;

        var (ok, err) = PublicAppUrlsOptionsValidator.Validate(opts);

        ok.Should().BeFalse();
        err.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("http://127.0.0.1:3000")]
    [InlineData("https://makables.cz")]
    [InlineData("https://staging.makables.cz")]
    public void Accepts_https_or_loopback_http(string baseUrl)
    {
        var opts = ValidDefaults();
        opts.WebBaseUrl = baseUrl;

        var (ok, _) = PublicAppUrlsOptionsValidator.Validate(opts);

        ok.Should().BeTrue();
    }

    [Fact]
    public void Rejects_path_template_missing_token_placeholder()
    {
        var opts = ValidDefaults();
        opts.MagicLinkPath = "/auth/magic";   // missing {token}

        var (ok, err) = PublicAppUrlsOptionsValidator.Validate(opts);

        ok.Should().BeFalse();
        err.Should().Contain("MagicLinkPath");
        err.Should().Contain("{token}");
    }

    [Fact]
    public void Rejects_path_template_not_starting_with_slash()
    {
        var opts = ValidDefaults();
        opts.EmailConfirmationPath = "auth/confirm?token={token}";

        var (ok, err) = PublicAppUrlsOptionsValidator.Validate(opts);

        ok.Should().BeFalse();
        err.Should().Contain("EmailConfirmationPath");
    }

    [Fact]
    public void Rejects_blank_path_template()
    {
        var opts = ValidDefaults();
        opts.PasswordResetPath = "";

        var (ok, err) = PublicAppUrlsOptionsValidator.Validate(opts);

        ok.Should().BeFalse();
        err.Should().Contain("PasswordResetPath");
    }
}
