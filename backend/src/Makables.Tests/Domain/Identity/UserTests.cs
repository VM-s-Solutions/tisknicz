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
        // U+00E1 (precomposed á) vs. U+0061 U+0301 (a + combining acute).
        var precomposed = "anna@domáin.cz";
        var decomposed = "anna@domáin.cz";

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
    public void UpdateProfile_trims_and_blanks_phone()
    {
        var u = CreateValidUser();

        u.UpdateProfile("  Petr Novák  ", "  ");

        u.FullName.Should().Be("Petr Novák");
        u.Phone.Should().BeNull("whitespace-only phone is normalized to null");
    }
}
