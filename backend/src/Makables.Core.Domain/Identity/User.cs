using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Identity;

/// <summary>
/// Authenticated principal. Email is the primary identity key; the three
/// sign-in methods (password, magic link, Google OAuth) converge on the
/// same <see cref="User"/> row, looked up by <see cref="EmailNormalized"/>.
/// Per ADR 0012.
///
/// Mutation is constrained to a handful of explicit domain methods so the
/// command handlers in <c>Core.AppServices.Features.Auth.*</c> never write
/// to the entity directly; the audit interceptor (T-0002) stamps
/// <see cref="Auditable.UpdatedBy"/> / <see cref="Auditable.UpdatedAt"/>
/// for every accepted mutation.
/// </summary>
public sealed class User : Auditable
{
    public string Email { get; private set; } = default!;
    public string EmailNormalized { get; private set; } = default!;
    public string? PasswordHash { get; private set; }
    public DateTimeOffset? EmailConfirmedAt { get; private set; }
    public UserRole Role { get; private set; } = UserRole.Customer;
    public string FullName { get; private set; } = default!;
    public string? Phone { get; private set; }
    public string CountryCodePrimary { get; private set; } = default!;
    public string? GoogleSub { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>
    /// User's preferred UI / email language as a BCP-47 tag (e.g. <c>"cs-CZ"</c>,
    /// <c>"en-US"</c>). Null means "no preference set" — language resolution
    /// (T-0028 <c>ILanguageResolver</c>) falls back to the country's
    /// <see cref="Configuration.CountryConfiguration.DefaultLanguageCode"/> and
    /// finally to <c>"cs-CZ"</c>. Settable via <see cref="SetPreferredLanguage"/>.
    /// </summary>
    public string? PreferredLanguage { get; private set; }

    private User() { }

    /// <summary>
    /// Factory for the standard registration paths (email/password + magic
    /// link + OAuth). Does not stamp audit fields — the interceptor handles
    /// that on save. Caller is responsible for assigning <see cref="Auditable.Id"/>
    /// via <see cref="IIdGenerator"/>.
    /// </summary>
    public static User Create(
        string id,
        string email,
        UserRole role,
        string fullName,
        string countryCodePrimary,
        string? passwordHash = null,
        string? googleSub = null,
        bool emailAlreadyConfirmed = false,
        DateTimeOffset? confirmedAt = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("FullName is required.", nameof(fullName));
        if (string.IsNullOrWhiteSpace(countryCodePrimary) || countryCodePrimary.Length != 2)
            throw new ArgumentException("CountryCodePrimary must be 2 chars (ISO 3166-1 alpha-2).", nameof(countryCodePrimary));

        return new User
        {
            Id = id,
            Email = email.Trim(),
            EmailNormalized = NormalizeEmail(email),
            PasswordHash = passwordHash,
            Role = role,
            FullName = fullName.Trim(),
            CountryCodePrimary = countryCodePrimary.ToUpperInvariant(),
            CountryCode = countryCodePrimary.ToUpperInvariant(),
            GoogleSub = googleSub,
            EmailConfirmedAt = emailAlreadyConfirmed ? confirmedAt ?? DateTimeOffset.UtcNow : null,
        };
    }

    /// <summary>
    /// Lowercase + NFC normalize. Email comparison MUST go through this
    /// helper so an attacker can't register `Admin@x.com` + `admin@x.com`
    /// as two accounts.
    /// </summary>
    public static string NormalizeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return email.Trim().Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant();
    }

    // === Mutation methods ===

    public User SetPasswordHash(string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        PasswordHash = hash;
        // Successful password set after a lockout window resets the counter.
        FailedLoginCount = 0;
        LockedUntil = null;
        return this;
    }

    public User ConfirmEmail(DateTimeOffset at)
    {
        if (EmailConfirmedAt is not null) return this;
        EmailConfirmedAt = at;
        return this;
    }

    public User LinkGoogleSub(string googleSub)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(googleSub);
        if (GoogleSub is not null && GoogleSub != googleSub)
            throw new InvalidOperationException("User already has a different GoogleSub linked.");
        GoogleSub = googleSub;
        return this;
    }

    public User UpdateProfile(string fullName, string? phone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        FullName = fullName.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        return this;
    }

    /// <summary>
    /// Set or clear the user's preferred language. <paramref name="bcp47"/> must
    /// match <c>{lang}-{REGION}</c> (e.g. <c>"cs-CZ"</c>); pass <c>null</c> to
    /// clear (silent fallback to country default).
    /// </summary>
    public User SetPreferredLanguage(string? bcp47)
    {
        if (bcp47 is null)
        {
            PreferredLanguage = null;
            return this;
        }
        if (!LanguageCode.IsValid(bcp47))
            throw new ArgumentException(
                $"Preferred language '{bcp47}' is not a recognised BCP-47 tag. Expected e.g. 'cs-CZ' or 'en-US'.",
                nameof(bcp47));
        PreferredLanguage = bcp47;
        return this;
    }

    /// <summary>
    /// Increments <see cref="FailedLoginCount"/> by one. When it reaches
    /// <paramref name="lockoutThreshold"/> the account is locked for
    /// <paramref name="lockoutWindow"/> per ADR 0012 §Lockout.
    ///
    /// If the account is already locked at <paramref name="now"/> the call
    /// is a no-op — the lockout window does NOT extend with further
    /// attempts (reviewer T-0020 MAJOR M-1). The service layer should
    /// short-circuit on <see cref="IsLocked"/> before calling this, but
    /// the entity also guards defensively so a bypass can't sustain an
    /// indefinite lockout on a victim.
    /// </summary>
    public User RegisterFailedLogin(DateTimeOffset now, int lockoutThreshold, TimeSpan lockoutWindow)
    {
        if (IsLocked(now)) return this;

        FailedLoginCount += 1;
        if (FailedLoginCount >= lockoutThreshold)
        {
            LockedUntil = now + lockoutWindow;
        }
        return this;
    }

    public User RegisterSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockedUntil = null;
        return this;
    }

    /// <summary>True when the account is currently locked (i.e. <see cref="LockedUntil"/> is in the future).</summary>
    public bool IsLocked(DateTimeOffset now) => LockedUntil is not null && LockedUntil > now;

    /// <summary>
    /// True when the user can present a JWT with audience
    /// <paramref name="audience"/>. Non-admins are bound to the audience
    /// matching their role; admins can present any audience. The audience
    /// claim is lowercase per ADR 0012 §JWT, so the comparison is too.
    /// Reviewer T-0022 NIT — keeps the rule in one place across Login + Refresh.
    /// </summary>
    public bool MatchesAudience(string audience)
    {
        if (Role == UserRole.Admin) return MakablesAudiences.IsValid(audience);
        return audience == Role.ToString().ToLowerInvariant();
    }
}
