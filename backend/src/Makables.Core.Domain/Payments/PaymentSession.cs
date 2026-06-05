namespace Makables.Core.Domain.Payments;

/// <summary>
/// The output of <see cref="IPaymentProvider.CreatePaymentAsync"/>:
/// the provider's session id (e.g. Comgate <c>transId</c>) and the
/// redirect URL the customer's browser should navigate to. Per ADR 0016.
/// </summary>
public sealed record PaymentSession(string ProviderRef, string RedirectUrl);
