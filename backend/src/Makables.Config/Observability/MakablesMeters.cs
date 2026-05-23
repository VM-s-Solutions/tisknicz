namespace Makables.Config.Observability;

/// <summary>
/// Meter names registered with OpenTelemetry per ADR 0023 §4 Metrics.
///
/// The actual instruments (counters, gauges) are created in the modules
/// that own the signal — e.g. <c>OutboxLagMeter</c> in
/// <c>Makables.Infra.Database.Outbox</c>, <c>PaymentMeter</c> in
/// <c>Makables.Infra.Clients.Comgate</c>. Centralizing the *names* here
/// keeps the <c>.AddMeter(...)</c> registration in <c>AddMakablesObservability</c>
/// honest: when an instrument is added to one of these meters it is
/// automatically exported.
/// </summary>
public static class MakablesMeters
{
    /// <summary>Outbox lag / stalled-count instruments (ADR 0023 §4).</summary>
    public const string Outbox = "Makables.Outbox";

    /// <summary>Payment provider success/failure counters (ADR 0023 §4).</summary>
    public const string Payments = "Makables.Payments";

    /// <summary>Webhook ingest counters labeled by provider+outcome (ADR 0023 §4).</summary>
    public const string Webhooks = "Makables.Webhooks";

    /// <summary>Order lifecycle gauges (auto-deliver count etc., ADR 0023 §4).</summary>
    public const string Orders = "Makables.Orders";

    /// <summary>Payout-batch total gauges (ADR 0023 §4).</summary>
    public const string Payouts = "Makables.Payouts";

    /// <summary>All registered meter names; consumed by <c>AddMeter(...)</c> in observability wiring.</summary>
    public static readonly string[] All =
    [
        Outbox,
        Payments,
        Webhooks,
        Orders,
        Payouts,
    ];
}
