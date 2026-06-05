namespace Makables.Core.Domain.Payments;

/// <summary>
/// The output of <see cref="IPaymentProvider.VerifyPaymentAsync"/>:
/// the normalised <see cref="PaymentState"/>, an optional provider
/// payment-method label (<c>card</c>, <c>bank-transfer</c>, etc.) and
/// the moment of capture when the state is <see cref="PaymentState.Paid"/>.
/// Per ADR 0016.
/// </summary>
public sealed record PaymentStatus(
    PaymentState State,
    string? PaymentMethod,
    DateTimeOffset? PaidAt);
