namespace Makables.Core.Domain.Identity;

/// <summary>
/// Allowed JWT audience values per ADR 0012 §JWT structure. Single source of
/// truth: the issuer (<c>Makables.Infra.Common.Auth.JwtIssuer</c>) validates
/// against this list when minting, and T-0027's per-host authentication
/// middleware references the same constants when accepting.
/// </summary>
public static class MakablesAudiences
{
    public const string Customer = "customer";
    public const string Maker = "maker";
    public const string Admin = "admin";

    /// <summary>All allowed audience values in mint order.</summary>
    public static readonly string[] All = [Customer, Maker, Admin];

    /// <summary>
    /// True if <paramref name="audience"/> is one of the allowed values.
    /// Case-sensitive — audiences are lowercase ASCII tokens by spec.
    /// </summary>
    public static bool IsValid(string? audience) =>
        audience is not null && Array.Exists(All, a => a == audience);
}
