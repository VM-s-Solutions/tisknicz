namespace Makables.Core.Domain.Payments;

/// <summary>
/// Result of <see cref="IPaymentProvider.ParseAndVerifyWebhookAsync"/>:
/// the provider's session reference, the normalised <see cref="PaymentState"/>
/// the webhook announces, and (when present) the provider's payment-method
/// label. T-0066 consumes this from the Public-host webhook controller.
/// </summary>
public sealed record WebhookPayload(
    string ProviderRef,
    PaymentState State,
    string? PaymentMethod);
