namespace Makables.Core.Domain.Observability;

/// <summary>
/// Payment-provider instrumentation seam per ADR 0023 §4 (T-0165).
/// Records the outcome of every attempt to create a payment session, tagged
/// by provider so a Comgate outage is distinguishable from a Stripe one once
/// a second provider exists (ADR 0016 keeps the adapter swappable).
///
/// <para>
/// This is the money path's leading indicator: a spike in
/// <c>failed</c>/<c>transient</c> means customers cannot pay, minutes before
/// the order-volume graph would show it.
/// </para>
/// </summary>
public interface IPaymentMetrics
{
    /// <summary>
    /// Count one payment-session creation attempt. <paramref name="outcome"/>
    /// ∈ <see cref="PaymentSessionOutcome"/>.
    /// </summary>
    void RecordSessionCreated(string provider, string outcome);
}

/// <summary>Canonical <c>outcome</c> tag values for <see cref="IPaymentMetrics"/>.</summary>
public static class PaymentSessionOutcome
{
    public const string Created = "created";

    /// <summary>Provider refused (validation, rejected merchant config) — will not succeed on retry.</summary>
    public const string Permanent = "permanent";

    /// <summary>Provider unreachable / 5xx — the retry-worthy bucket.</summary>
    public const string Transient = "transient";
}
