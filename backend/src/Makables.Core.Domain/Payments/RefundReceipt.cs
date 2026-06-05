namespace Makables.Core.Domain.Payments;

/// <summary>
/// Result of <see cref="IPaymentProvider.RefundAsync"/>: the provider's
/// refund reference (distinct from the original capture's
/// <see cref="PaymentSession.ProviderRef"/>), the amount in minor units,
/// the ISO 4217 currency, and the moment the refund settled at the
/// provider. T-0105 will consume this from the admin refund command.
/// </summary>
public sealed record RefundReceipt(
    string RefundProviderRef,
    long AmountMinor,
    string Currency,
    DateTimeOffset RefundedAt);
