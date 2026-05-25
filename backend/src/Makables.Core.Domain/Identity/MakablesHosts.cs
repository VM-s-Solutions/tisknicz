namespace Makables.Core.Domain.Identity;

/// <summary>
/// Per-audience Web host identifiers. These are NOT JWT audiences — see
/// <see cref="MakablesAudiences"/> for those. A host identifier names a
/// deployment surface (<c>Web.Customer</c>, <c>Web.Maker</c>,
/// <c>Web.Admin</c>, <c>Web.Public</c>); a JWT audience names a role
/// the token authorizes (<c>customer</c>, <c>maker</c>, <c>admin</c>).
///
/// The two overlap for Customer/Maker/Admin — each runs on a host
/// named the same as its audience — but Public is a host with no
/// matching audience (it accepts any authenticated caller, by
/// design — see <c>MakablesAuthExtensions.AcceptedAudiencesFor</c>).
/// Naming them separately prevents callers from typing
/// <c>"public"</c> as a JWT audience and getting silent failures.
/// </summary>
public static class MakablesHosts
{
    public const string Customer = "customer";
    public const string Maker = "maker";
    public const string Admin = "admin";
    public const string Public = "public";

    /// <summary>All host identifiers.</summary>
    public static readonly string[] All = [Customer, Maker, Admin, Public];
}
