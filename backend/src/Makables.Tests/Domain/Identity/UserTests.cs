using FluentAssertions;
using Makables.Core.Domain.Identity;

namespace Makables.Tests.Domain.Identity;

public class UserTests
{
    private static User CreateValidUser(string? id = null, string? email = null) =>
        User.Create(
            id: id ?? "user-01",
            email: email ?? "Anna.Nováková@example.cz",
            role: UserRole.Customer,
            fullName: "Anna Nováková",
            countryCodePrimary: "cz");

    [Fact]
    public void Create_normalizes_email_to_lowercase_NFC()
    {
        var u = CreateValidUser(email: "Anna.NOVÁKOVÁ@Example.cz");

        u.EmailNormalized.Should().Be("anna.nováková@example.cz");
        u.Email.Should().Be("Anna.NOVÁKOVÁ@Example.cz"); // raw email preserved for display
    }

    [Fact]
    public void Create_uppercases_country_code_and_mirrors_to_audit_field()
    {
        var u = CreateValidUser();

        u.CountryCodePrimary.Should().Be("CZ");
        u.CountryCode.Should().Be("CZ");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Create_rejects_blank_email(string? email)
    {
        Action act = () => User.Create("u-1", email!, UserRole.Customer, "Anna", "CZ");
        act.Should().Throw<ArgumentException>().WithParameterName("email");
    }

    [Theory]
    [InlineData("C")]
    [InlineData("CZE")]
    [InlineData("")]
    public void Create_rejects_country_code_not_exactly_two_chars(string country)
    {
        Action act = () => User.Create("u-1", "a@b.cz", UserRole.Customer, "Anna", country);
        act.Should().Throw<ArgumentException>().WithParameterName("countryCodePrimary");
    }

    [Fact]
    public void NormalizeEmail_is_stable_across_unicode_forms()
    {
        // Use \u escapes so the editor cannot silently normalize both
        // literals to NFC and turn the test into a tautology (reviewer
        // T-0020 MINOR fix).
        //   U+00E1            = precomposed á
        //   U+0061 + U+0301   = a + combining acute (decomposed form)
        var precomposed = "anna@domáin.cz";
        var decomposed = "anna@domáin.cz";

        precomposed.Should().NotBe(decomposed,
            "the two literals must differ before normalization or the test is a tautology");

        User.NormalizeEmail(precomposed).Should().Be(User.NormalizeEmail(decomposed));
    }

    [Fact]
    public void SetPasswordHash_clears_lockout_state()
    {
        var u = CreateValidUser();
        var now = DateTimeOffset.UtcNow;
        u.RegisterFailedLogin(now, lockoutThreshold: 5, lockoutWindow: TimeSpan.FromMinutes(15));
        u.RegisterFailedLogin(now, lockoutThreshold: 5, lockoutWindow: TimeSpan.FromMinutes(15));

        u.SetPasswordHash("argon2id$v=19$m=65536,t=3,p=1$abc$def");

        u.FailedLoginCount.Should().Be(0);
        u.LockedUntil.Should().BeNull();
        u.PasswordHash.Should().NotBeNull();
    }

    [Fact]
    public void ConfirmEmail_is_idempotent()
    {
        var u = CreateValidUser();
        var t1 = DateTimeOffset.UtcNow;
        var t2 = t1.AddMinutes(5);

        u.ConfirmEmail(t1);
        u.ConfirmEmail(t2);

        u.EmailConfirmedAt.Should().Be(t1, "first confirmation wins; later calls are no-ops");
    }

    [Fact]
    public void LinkGoogleSub_rejects_relinking_to_different_sub()
    {
        var u = CreateValidUser();
        u.LinkGoogleSub("google-sub-1");

        Action act = () => u.LinkGoogleSub("google-sub-2");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void LinkGoogleSub_is_idempotent_for_the_same_sub()
    {
        var u = CreateValidUser();
        u.LinkGoogleSub("google-sub-1");
        u.LinkGoogleSub("google-sub-1");

        u.GoogleSub.Should().Be("google-sub-1");
    }

    [Fact]
    public void RegisterFailedLogin_locks_account_at_threshold()
    {
        var u = CreateValidUser();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 4; i++)
        {
            u.RegisterFailedLogin(now, lockoutThreshold: 5, lockoutWindow: TimeSpan.FromMinutes(15));
            u.IsLocked(now).Should().BeFalse($"only {i + 1} of 5 failures so far");
        }

        u.RegisterFailedLogin(now, lockoutThreshold: 5, lockoutWindow: TimeSpan.FromMinutes(15));

        u.FailedLoginCount.Should().Be(5);
        u.IsLocked(now).Should().BeTrue();
        u.LockedUntil.Should().Be(now + TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void RegisterSuccessfulLogin_resets_lockout_state()
    {
        var u = CreateValidUser();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
            u.RegisterFailedLogin(now, 5, TimeSpan.FromMinutes(15));

        u.RegisterSuccessfulLogin();

        u.FailedLoginCount.Should().Be(0);
        u.LockedUntil.Should().BeNull();
        u.IsLocked(now).Should().BeFalse();
    }

    [Fact]
    public void IsLocked_returns_false_when_lockout_window_has_passed()
    {
        var u = CreateValidUser();
        var lockedAt = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
            u.RegisterFailedLogin(lockedAt, 5, TimeSpan.FromMinutes(15));

        var afterWindow = lockedAt + TimeSpan.FromMinutes(16);
        u.IsLocked(afterWindow).Should().BeFalse();
    }

    [Fact]
    public void RegisterFailedLogin_is_a_no_op_while_already_locked()
    {
        // Reviewer T-0020 MAJOR M-1: a determined attacker must not be
        // able to keep an account locked indefinitely by retrying inside
        // the lockout window.
        var u = CreateValidUser();
        var lockedAt = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
            u.RegisterFailedLogin(lockedAt, 5, TimeSpan.FromMinutes(15));

        var countAtLock = u.FailedLoginCount;
        var lockedUntilAtLock = u.LockedUntil;

        // Try to "extend" the lock from inside the window.
        var midWindow = lockedAt + TimeSpan.FromMinutes(5);
        u.RegisterFailedLogin(midWindow, 5, TimeSpan.FromMinutes(15));

        u.FailedLoginCount.Should().Be(countAtLock, "counter must not increment while locked");
        u.LockedUntil.Should().Be(lockedUntilAtLock, "lockout window must NOT be extended by attempts inside it");
    }

    [Fact]
    public void UpdateProfile_trims_and_blanks_phone()
    {
        var u = CreateValidUser();

        u.UpdateProfile("  Petr Novák  ", "  ");

        u.FullName.Should().Be("Petr Novák");
        u.Phone.Should().BeNull("whitespace-only phone is normalized to null");
    }

    [Fact]
    public void New_user_has_no_preferred_language_so_resolution_falls_back_to_country_default()
    {
        var u = CreateValidUser();

        u.PreferredLanguage.Should().BeNull();
    }

    [Theory]
    [InlineData("cs-CZ")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void SetPreferredLanguage_accepts_well_formed_BCP_47_tags(string tag)
    {
        var u = CreateValidUser();

        u.SetPreferredLanguage(tag);

        u.PreferredLanguage.Should().Be(tag);
    }

    [Fact]
    public void SetPreferredLanguage_null_clears_the_preference()
    {
        var u = CreateValidUser();
        u.SetPreferredLanguage("en-US");

        u.SetPreferredLanguage(null);

        u.PreferredLanguage.Should().BeNull();
    }

    [Theory]
    [InlineData("cs")]
    [InlineData("CS-cz")]
    [InlineData("cs_CZ")]
    public void SetPreferredLanguage_rejects_malformed_tags(string tag)
    {
        var u = CreateValidUser();

        var act = () => u.SetPreferredLanguage(tag);

        act.Should().Throw<ArgumentException>();
    }
}
