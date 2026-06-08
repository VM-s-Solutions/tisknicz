namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for the <c>shipping.generate.label</c> outbox event,
/// enqueued by Zásilkovna <c>ShipOrder.Handler</c> (T-0072) atomically
/// with the customer shipped-email event. Consumer is the
/// <c>GenerateLabelFunction</c> (T-0074) — it deserialises this payload
/// and dispatches <c>FetchAndStoreShippingLabel.Command</c> via Mediator.
///
/// <para>
/// Intentionally minimal — T-0074's handler queries the Order via
/// <c>IOrderRepository.GetByIdUnscopedAsync</c> and reads
/// <see cref="Orders.Order.ShippingCarrierRef"/> + <see cref="Orders.Order.CountryCode"/>
/// fresh. Rationale: (a) if the state graph ever lets <c>Ship()</c>
/// restamp the carrier ref (it won't under current rules), the payload
/// stays in sync; (b) mirrors T-0069's <c>IssueInvoice.Command(OrderId)</c>
/// shape — the queue trigger Function is a thin dispatcher.
/// </para>
///
/// <para>
/// PascalCase JSON property name matches the
/// <see cref="OrderPaidCustomerEmailPayload"/> convention so a single
/// <see cref="System.Text.Json.JsonSerializer"/> with default options
/// round-trips every outbox payload shape.
/// </para>
/// </summary>
public sealed record GenerateLabelOutboxPayload(string OrderId);
