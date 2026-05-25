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
