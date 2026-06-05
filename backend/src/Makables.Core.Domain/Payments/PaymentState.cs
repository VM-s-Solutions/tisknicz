namespace Makables.Core.Domain.Payments;

/// <summary>
/// Normalised provider-agnostic payment state. Every <see cref="IPaymentProvider"/>
/// implementation maps the provider-specific status vocabulary into one of these
/// values via <see cref="IPaymentProvider.VerifyPaymentAsync"/> and
/// <see cref="IPaymentProvider.ParseAndVerifyWebhookAsync"/>.
/// </summary>
public enum PaymentState
{
    /// <summary>Session created at the provider but no money has moved yet.</summary>
    Pending,

    /// <summary>Funds reserved (e.g. card pre-authorised) but not yet captured.</summary>
    Authorized,

    /// <summary>Funds captured. Terminal happy-path state on the provider side.</summary>
    Paid,

    /// <summary>Customer (or provider) abandoned the session before capture.</summary>
    Cancelled,

    /// <summary>Captured payment was reversed via <see cref="IPaymentProvider.RefundAsync"/>.</summary>
    Refunded,

    /// <summary>Capture failed at the provider (declined, fraud, etc.). Terminal.</summary>
    Failed,
}
