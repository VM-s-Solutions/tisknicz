namespace Makables.Core.Domain.Observability;

/// <summary>
/// Webhook-ingest instrumentation seam per ADR 0023 §4 (T-0165).
///
/// <para>
/// Deliberately counted at the transport edge, before any business
/// decision: the operational question is "is the provider still calling us,
/// and are we accepting the calls" — a webhook that stops arriving produces
/// no error anywhere else in the system, and silence is indistinguishable
/// from a quiet night without this counter.
/// </para>
/// </summary>
public interface IWebhookMetrics
{
    /// <summary>
    /// Count one inbound webhook delivery. <paramref name="outcome"/> ∈
    /// <see cref="WebhookOutcome"/>.
    /// </summary>
    void RecordReceived(string provider, string outcome);
}

/// <summary>Canonical <c>outcome</c> tag values for <see cref="IWebhookMetrics"/>.</summary>
public static class WebhookOutcome
{
    /// <summary>Verified and applied — the state transition happened.</summary>
    public const string Accepted = "accepted";

    /// <summary>Verified, but already in the target state — the idempotent re-delivery path.</summary>
    public const string Duplicate = "duplicate";

    /// <summary>Rejected before any DB access: bad signature, unknown ref, disallowed IP.</summary>
    public const string Rejected = "rejected";

    /// <summary>Malformed body — could not be parsed at all.</summary>
    public const string Malformed = "malformed";

    /// <summary>
    /// We failed on our side (provider not registered, verify re-fetch
    /// unavailable, handler blew up) — the provider should retry. Kept apart
    /// from <see cref="Rejected"/> because that one means "working as
    /// intended, request refused" and this one means "page someone".
    /// </summary>
    public const string Error = "error";
}
