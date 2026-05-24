namespace Makables.Core.Domain.Identity;

/// <summary>
/// Lockout policy per ADR 0012 §Lockout. Bound from <c>Auth:Lockout</c>
/// configuration section.
/// </summary>
public sealed class LockoutOptions
{
    public const string SectionName = "Auth:Lockout";

    /// <summary>Failed-attempt count that triggers a lock. ADR default 5.</summary>
    public int Threshold { get; set; } = 5;

    /// <summary>Lockout window after the threshold is reached. ADR default 15 min.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(15);
}
