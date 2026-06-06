namespace Makables.Core.Domain.Payments;

/// <summary>
/// Result of <see cref="IPaymentProvider.ParseAndVerifyWebhookAsync"/>:
/// the provider's session reference, the normalised <see cref="PaymentState"/>
/// the webhook announces, the provider's payment-method label (when
/// present) and the provider's authoritative capture timestamp (when the
/// state is <see cref="PaymentState.Paid"/>). T-0066 introduced the record;
/// T-0067 widened it with <see cref="PaidAt"/> so the customer-facing
/// invoice records Comgate's PAID timestamp instead of our webhook
/// receive moment.
/// </summary>
public sealed record WebhookPayload(
    string ProviderRef,
    PaymentState State,
    string? PaymentMethod,
    DateTimeOffset? PaidAt);
