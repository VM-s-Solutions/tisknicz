using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;

namespace Makables.Core.Domain.Shipping;

/// <summary>
/// Adapter for an external shipping carrier per ADR 0017. One implementation
/// per carrier (Packeta at MVP; future DPD / Česká pošta). Selected per
/// country via <see cref="IShippingCarrierFactory.ResolveAsync"/> reading
/// <c>CountryConfiguration.DefaultShippingCarrier</c>.
///
/// <para>
/// <b>Invariants</b> (mirror of <see cref="Payments.IPaymentProvider"/>):
/// <list type="bullet">
///   <item><description>The adapter never mutates the order. State transitions
///     happen via Mediator commands in the application layer (T-0072 / T-0073).</description></item>
///   <item><description>The adapter never writes to the database.</description></item>
///   <item><description><see cref="WidgetConfig"/> is sync because it's a pure
///     data lookup off the adapter's options — no I/O.</description></item>
/// </list>
/// </para>
///
/// <para>
/// All async methods return <see cref="BusinessResult{T}"/>. Exceptions from
/// the underlying HTTP client are translated to <see cref="ErrorType.Transient"/>
/// (network blip, timeout, 5xx) / <see cref="ErrorType.Permanent"/> (carrier
/// rejected the request with a business error like "address id not found")
/// / <see cref="ErrorType.Configuration"/> (bad API key — alert ops) per
/// the table in ADR 0017 §"Error classification" + ADR 0016 §A.14.
/// </para>
/// </summary>
public interface IShippingCarrier
{
    /// <summary>The carrier code, e.g. <c>"packeta"</c>. Matches
    /// <c>CountryConfiguration.DefaultShippingCarrier</c> for selection.</summary>
    string Code { get; }

    /// <summary>
    /// Configuration the customer-facing checkout widget needs to render
    /// the pickup-point picker. Sync — pure data lookup, no I/O. The
    /// public widget-config endpoint (T-0070) returns this verbatim.
    /// </summary>
    PickupPointWidgetConfig WidgetConfig(string locale, string countryCode);

    /// <summary>
    /// T-0072. Create a shipment at the carrier for the given
    /// <paramref name="order"/>; return the carrier's numeric reference
    /// + the pre-computed customer-facing tracking URL.
    /// </summary>
    Task<BusinessResult<Shipment>> CreateShipmentAsync(
        Order order,
        CancellationToken cancellationToken);

    /// <summary>
    /// T-0078. Read the current state of an existing shipment from the
    /// carrier. Used by the status-sync timer to transition Shipped →
    /// Delivered when the carrier reports a delivered signal ahead of
    /// <c>Order.AutoDeliverAt</c>.
    /// </summary>
    Task<BusinessResult<ShipmentStatus>> GetStatusAsync(
        string carrierRef,
        CancellationToken cancellationToken);

    /// <summary>
    /// T-0074 / T-0075. Fetch the carrier's shipping-label PDF for an
    /// existing shipment. The CALLER MUST DISPOSE the returned
    /// <see cref="Stream"/> after consuming it. T-0074 uploads the stream
    /// to blob storage; T-0075 streams it directly to the maker on a
    /// cache-miss fallback.
    /// </summary>
    Task<BusinessResult<Stream>> GetLabelPdfAsync(
        string carrierRef,
        CancellationToken cancellationToken);
}
