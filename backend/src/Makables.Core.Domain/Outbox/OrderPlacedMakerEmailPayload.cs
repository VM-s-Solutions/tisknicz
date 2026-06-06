namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for the maker-facing "new order arrived" notification
/// email, enqueued by <c>MarkOrderPaid.Handler</c> alongside the customer
/// confirmation email. T-0067 (US-maker-0006).
///
/// <para>
/// PascalCase JSON property names match the
/// <see cref="OneTimeTokenOutboxPayload"/> convention.
/// </para>
///
/// <para>
/// <see cref="MakerEmail"/> is resolved at enqueue time from the linked
/// <c>User.Email</c> (the <see cref="Makables.Core.Domain.Makers.Maker"/>
/// aggregate has no separate notification email field at MVP; the
/// maker's account email is the sole channel).
/// </para>
///
/// <para>
/// <see cref="ActionUrl"/> is pre-baked per Q4:
/// <c>{WebBaseUrl}/dashboard/maker/objednavky/{OrderId}</c>.
/// </para>
/// </summary>
public sealed record OrderPlacedMakerEmailPayload(
    string OrderId,
    string OrderNumber,
    string MakerId,
    string MakerEmail,
    long TotalAmountMinor,
    string Currency,
    string LanguageCode,
    string ActionUrl);
