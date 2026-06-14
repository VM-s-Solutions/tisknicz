using Makables.Core.Domain.Orders;

namespace Makables.Core.Domain.Admin;

/// <summary>
/// Filter dimensions for the admin order list (T-0111 / US-admin-0009
/// AC-1): exactly state, country, maker, customer email — no speculative
/// additions (Q-E). All fields nullable; any subset applies. The query
/// matches <see cref="CustomerEmail"/> case-insensitively against the
/// order's snapshot contact email (the privileged admin view).
/// </summary>
public sealed record AdminOrderFilter(
    OrderState? State,
    string? CountryCode,
    string? MakerId,
    string? CustomerEmail);
