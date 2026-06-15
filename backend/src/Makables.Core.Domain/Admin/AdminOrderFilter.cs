using Makables.Core.Domain.Orders;

namespace Makables.Core.Domain.Admin;

/// <summary>
/// Filter dimensions for the admin order list (T-0111 / US-admin-0009
/// AC-1): state, country, maker, customer email — plus the T-0127
/// per-user in-flight signal seam (Q-0024): <see cref="CustomerUserId"/>
/// matches the order's FK exactly. All fields nullable; any subset
/// applies. The query matches <see cref="CustomerEmail"/>
/// case-insensitively against the order's snapshot contact email (the
/// privileged admin view).
///
/// <para>
/// <b>The delete-user pre-disable signal (T-0127).</b> The delete-user
/// panel filters <c>customerUserId=X</c> + an in-flight <c>state</c>; a
/// non-empty <see cref="PagedData{T}.TotalCount"/> proactively disables
/// the destructive button. The backend T-0110 erase gate stays
/// authoritative — this is the UX layer.
/// </para>
/// </summary>
public sealed record AdminOrderFilter(
    OrderState? State,
    string? CountryCode,
    string? MakerId,
    string? CustomerEmail,
    string? CustomerUserId = null);
