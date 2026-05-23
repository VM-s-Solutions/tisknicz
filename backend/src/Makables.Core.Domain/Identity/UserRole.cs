namespace Makables.Core.Domain.Identity;

/// <summary>
/// Role assigned to a <see cref="User"/>. Per ADR 0012 §JWT.
///
/// Each role aligns 1:1 with a JWT audience and a Web host; the audience
/// claim is set from the role at token issue time. T-0021 / T-0027 enforce
/// audience binding so a customer JWT cannot be replayed against the maker
/// or admin host.
/// </summary>
public enum UserRole
{
    Customer = 0,
    Maker = 1,
    Admin = 2,
}
