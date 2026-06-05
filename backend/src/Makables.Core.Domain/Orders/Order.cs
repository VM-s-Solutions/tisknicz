using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Orders;

/// <summary>
/// A customer's purchase intent from a specific maker, with full
/// lifecycle tracking per role/order.md. Inherits <see cref="Auditable"/>
/// so country scope + soft-delete + audit columns come free.
///
/// <para>
/// <b>Snapshot semantics.</b> Contact (name / email / phone) and pricing
/// (product, shipping, platform fee, maker payout, total, currency, VAT
/// basis points) are captured at <see cref="Create"/> time and are
/// IMMUTABLE for the life of the row — if a product's price changes
/// later, existing orders are unaffected (US-maker-0004 AC-3). Stored as
/// inline columns rather than owned EF objects because the snapshot is
/// fixed forever; the owned-object indirection would buy nothing and
/// complicates admin SQL aggregations (same call as the ARES snapshot
/// fields on <see cref="Makers.Maker"/>).
/// </para>
///
/// <para>
/// <b>State machine.</b> Every transition method takes <see cref="IClock"/>
/// as its first argument — the entity never reads a static
/// <c>DateTimeOffset.UtcNow</c> so tests can pin time. Illegal
/// transitions return <see cref="BusinessResult.Failure"/> with
/// <c>Error.Conflict("state", OrderInvalidTransition)</c> rather than
/// throwing. <see cref="ArgumentException"/> is reserved for genuinely
/// impossible inputs at the boundary (null clock, negative window).
/// </para>
///
/// <para>
/// <b>Authorisation note.</b> The entity exposes the state-graph EDGES
/// (e.g. <see cref="Cancel"/> works from <see cref="OrderState.PendingPayment"/>,
/// <see cref="OrderState.Paid"/>, or <see cref="OrderState.Accepted"/>).
/// Who may take which edge from which audience host lives in the
/// command-layer validators (T-0083 customer auto-cancel, T-0105 admin
/// refund, T-0106 dispute, T-0107 admin manual change). Don't bake role
/// checks into <see cref="Order"/>.
/// </para>
/// </summary>
public sealed class Order : Auditable
{
    /// <summary>Wire-shape of the contact email column.</summary>
    public const int MaxContactEmailLength = 200;

    /// <summary>Wire-shape of the contact name column.</summary>
    public const int MaxContactNameLength = 200;

    /// <summary>Wire-shape of the contact phone column.</summary>
    public const int MaxContactPhoneLength = 40;

    /// <summary>Wire-shape of free-form customer notes.</summary>
    public const int MaxCustomerNotesLength = 2000;

    // === Identity ===

    /// <summary>
    /// Customer-facing order number, immutable after creation. The
    /// generator wire-up (per-country sequence) lives in T-0062; this
    /// ticket only stores the value the caller supplies.
    /// </summary>
    public string OrderNumber { get; private set; } = default!;

    // === Parties ===

    /// <summary>FK to the customer user. Immutable.</summary>
    public string CustomerUserId { get; private set; } = default!;

    /// <summary>FK to the maker. Immutable.</summary>
    public string MakerId { get; private set; } = default!;

    /// <summary>
    /// FK to the product, or null for a custom order (per role/order.md
    /// the product link is optional — bespoke jobs ship with a null
    /// <see cref="ProductId"/> and rely on pricing supplied by the
    /// maker's quote).
    /// </summary>
    public string? ProductId { get; private set; }

    // === Contact snapshot at order time (inline columns) ===

    /// <summary>Customer name AS PROVIDED at order time. Snapshot.</summary>
    public string ContactName { get; private set; } = default!;

    /// <summary>Customer email AS PROVIDED at order time. Snapshot.</summary>
    public string ContactEmail { get; private set; } = default!;

    /// <summary>Customer phone AS PROVIDED at order time. Snapshot.</summary>
    public string ContactPhone { get; private set; } = default!;

    // === Pricing snapshot at order time (inline columns) ===

    /// <summary>Product line, minor units. Snapshot.</summary>
    public long ProductPriceAmountMinor { get; private set; }

    /// <summary>Shipping line, minor units. Snapshot.</summary>
    public long ShippingPriceAmountMinor { get; private set; }

    /// <summary>Platform commission, minor units. Snapshot.</summary>
    public long PlatformFeeAmountMinor { get; private set; }

    /// <summary>Maker net payout, minor units. Snapshot.</summary>
    public long MakerPayoutAmountMinor { get; private set; }

    /// <summary>
    /// What the customer charged-card amount equals. Snapshot.
    /// Equal to <see cref="ProductPriceAmountMinor"/> +
    /// <see cref="ShippingPriceAmountMinor"/> by <see cref="Create"/>
    /// invariant.
    /// </summary>
    public long TotalAmountMinor { get; private set; }

    /// <summary>ISO 4217 currency code (CHAR(3)). Snapshot.</summary>
    public string Currency { get; private set; } = default!;

    /// <summary>
    /// VAT rate at order time, basis points (2100 = 21%). Snapshot.
    /// Per ADR 0003 every VAT-bearing snapshot stores the rate that
    /// applied so an invoice rendered months later still reconciles.
    /// </summary>
    public int VatRateBp { get; private set; }

    // === Shipping choice ===

    /// <summary>Customer's chosen shipping method.</summary>
    public ShippingMethod ShippingMethod { get; private set; }

    /// <summary>
    /// Packeta branch id when <see cref="ShippingMethod"/> is
    /// <see cref="Orders.ShippingMethod.ZasilkovnaPickupPoint"/>; null
    /// for <see cref="Orders.ShippingMethod.PersonalPickup"/>.
    /// </summary>
    public string? ZasilkovnaPickupPointId { get; private set; }

    // === State + per-transition timestamps ===

    public OrderState State { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? ShippedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }
    public DateTimeOffset? DisputedAt { get; private set; }

    // === Provider refs (set-once) ===

    /// <summary>
    /// Comgate transaction id, set on <see cref="MarkAsPaid"/> OR on
    /// <see cref="ReservePaymentSession"/> (T-0065). Set-once invariant on
    /// <see cref="MarkAsPaid"/>; <see cref="ReservePaymentSession"/> allows
    /// overwrite-after-rejection per user decision Q1 (the handler vetted
    /// the prior session was Cancelled/Failed first via
    /// <see cref="Payments.IPaymentProvider.VerifyPaymentAsync"/>). A second
    /// <see cref="MarkAsPaid"/> call (or two parallel webhooks racing the
    /// same row) still surfaces as
    /// <see cref="BusinessErrorMessage.OrderInvalidTransition"/>.
    /// </summary>
    public string? PaymentProviderRef { get; private set; }

    /// <summary>
    /// Cached Comgate redirect URL for the 24h retry window
    /// (US-customer-0010 AC-3). Set by <see cref="ReservePaymentSession"/>
    /// (T-0065); read back when the customer revisits an order in
    /// <see cref="OrderState.PendingPayment"/> with the provider session
    /// still <see cref="Payments.PaymentState.Pending"/> or
    /// <see cref="Payments.PaymentState.Authorized"/> — saves a Comgate
    /// roundtrip on every retry. Null until the customer triggers their
    /// first payment-session creation. Length-bounded to 500 chars
    /// (Comgate URLs are ~120 chars; 500 gives generous headroom).
    /// </summary>
    public string? PaymentRedirectUrl { get; private set; }

    /// <summary>
    /// Packeta shipment id, set on <see cref="Ship"/>. Null for
    /// personal-pickup orders. Set-once.
    /// </summary>
    public string? ShippingCarrierRef { get; private set; }

    /// <summary>
    /// When the auto-deliver job will flip <see cref="OrderState.Shipped"/>
    /// to <see cref="OrderState.Delivered"/> if no manual / carrier
    /// confirmation arrives. Set atomically with <see cref="ShippedAt"/>
    /// to <c>ShippedAt + window</c>; null until shipped.
    /// </summary>
    public DateTimeOffset? AutoDeliverAt { get; private set; }

    // === Customer notes ===

    /// <summary>Free-form note from the customer to the maker.</summary>
    public string? CustomerNotes { get; private set; }

    // === Attachments ===

    /// <summary>
    /// Per-order cap on customer-uploaded attachments. Mirrors
    /// <see cref="Products.Product.MaxImageCount"/> at a slightly higher
    /// number — orders can be more complex than a product listing (multi-
    /// page spec sheets, photo references), but ten is enough at MVP per
    /// US-customer-0010 AC-1. The cap is enforced at
    /// <see cref="AddAttachment"/> and double-checked at the upload
    /// controller as an optimistic fast-path; the handler re-checks under
    /// the unit-of-work transaction (race defence).
    /// </summary>
    public const int MaxAttachmentCount = 10;

    private readonly List<OrderAttachment> _attachments = new();

    /// <summary>
    /// Customer-uploaded reference files. Read-only collection; the only
    /// path to add an attachment is <see cref="AddAttachment"/> which
    /// enforces the state-gate + count-cap invariants. Removed at MVP is
    /// not supported (append-only) — a future <c>RemoveAttachment</c>
    /// would soft-delete via <see cref="Auditable.MarkDeactivated"/>.
    /// </summary>
    public IReadOnlyCollection<OrderAttachment> Attachments => _attachments;

    /// <summary>
    /// True when the customer may still attach more reference files —
    /// limited to the pre-shipment states. After <see cref="OrderState.Shipped"/>
    /// the order snapshot is frozen for the maker; in
    /// <see cref="OrderState.Delivered"/> / <see cref="OrderState.Completed"/>
    /// the lifecycle is finished; <see cref="OrderState.Cancelled"/> /
    /// <see cref="OrderState.Refunded"/> / <see cref="OrderState.Disputed"/>
    /// are dead. T-0064 user decision Q4.
    /// </summary>
    public bool AllowsAttachmentUpload() =>
        State is OrderState.PendingPayment or OrderState.Paid or OrderState.Accepted;

    /// <summary>
    /// Append <paramref name="attachment"/> to the order. Refuses with
    /// <see cref="BusinessErrorMessage.OrderStateForbidsAttachment"/> when
    /// the order has progressed past the attach-window, and with
    /// <see cref="BusinessErrorMessage.OrderAttachmentLimitReached"/> at
    /// the <see cref="MaxAttachmentCount"/> ceiling.
    ///
    /// <para>
    /// Both invariants are also pre-checked at the upload controller
    /// (optimistic fast-path). This method is the source of truth: the
    /// controller's check can lose a concurrent-upload race; this guard
    /// runs inside the request unit-of-work so the second uploader sees
    /// the cap and gets a typed failure rather than silently writing the
    /// 11th row.
    /// </para>
    /// </summary>
    public BusinessResult AddAttachment(OrderAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        if (!AllowsAttachmentUpload())
        {
            return BusinessResult.Failure(
                Error.Conflict("order", BusinessErrorMessage.OrderStateForbidsAttachment));
        }
        if (_attachments.Count >= MaxAttachmentCount)
        {
            return BusinessResult.Failure(
                Error.Conflict("attachments", BusinessErrorMessage.OrderAttachmentLimitReached));
        }

        _attachments.Add(attachment);
        return BusinessResult.Success();
    }

    // EF Core needs a parameterless ctor.
    private Order() { }

    /// <summary>
    /// Build a new order in <see cref="OrderState.PendingPayment"/>. The
    /// caller (T-0063 <c>CreateOrder.Handler</c>) is responsible for
    /// computing the pricing snapshot via <c>OrderPricing</c> (T-0061)
    /// and persisting the row inside the request unit-of-work; this
    /// factory just enforces the snapshot invariants.
    ///
    /// <para>
    /// Throws <see cref="ArgumentException"/> for impossible inputs
    /// (negative amounts, blank required strings, currency wrong length,
    /// pickup-point id missing when method requires one, total ≠
    /// product + shipping, maker payout + platform fee ≠ product +
    /// shipping). User-input validation (e.g. phone format) belongs in
    /// the command's <c>Validator</c>; <see cref="Create"/> catches the
    /// programmer-error tail.
    /// </para>
    /// </summary>
    public static Order Create(
        string id,
        string orderNumber,
        string customerUserId,
        string makerId,
        string? productId,
        string contactName,
        string contactEmail,
        string contactPhone,
        long productPriceAmountMinor,
        long shippingPriceAmountMinor,
        long platformFeeAmountMinor,
        long makerPayoutAmountMinor,
        long totalAmountMinor,
        string currency,
        int vatRateBp,
        ShippingMethod shippingMethod,
        string? zasilkovnaPickupPointId,
        string countryCode,
        string? customerNotes = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(orderNumber))
            throw new ArgumentException("OrderNumber is required.", nameof(orderNumber));
        if (string.IsNullOrWhiteSpace(customerUserId))
            throw new ArgumentException("CustomerUserId is required.", nameof(customerUserId));
        if (string.IsNullOrWhiteSpace(makerId))
            throw new ArgumentException("MakerId is required.", nameof(makerId));

        if (string.IsNullOrWhiteSpace(contactName))
            throw new ArgumentException("ContactName is required.", nameof(contactName));
        if (string.IsNullOrWhiteSpace(contactEmail))
            throw new ArgumentException("ContactEmail is required.", nameof(contactEmail));
        if (string.IsNullOrWhiteSpace(contactPhone))
            throw new ArgumentException("ContactPhone is required.", nameof(contactPhone));

        var trimmedName = contactName.Trim();
        if (trimmedName.Length > MaxContactNameLength)
            throw new ArgumentException($"ContactName must be at most {MaxContactNameLength} chars.", nameof(contactName));

        var trimmedEmail = contactEmail.Trim();
        if (trimmedEmail.Length > MaxContactEmailLength)
            throw new ArgumentException($"ContactEmail must be at most {MaxContactEmailLength} chars.", nameof(contactEmail));

        var trimmedPhone = contactPhone.Trim();
        if (trimmedPhone.Length > MaxContactPhoneLength)
            throw new ArgumentException($"ContactPhone must be at most {MaxContactPhoneLength} chars.", nameof(contactPhone));

        if (productPriceAmountMinor < 0)
            throw new ArgumentException("ProductPriceAmountMinor cannot be negative.", nameof(productPriceAmountMinor));
        if (shippingPriceAmountMinor < 0)
            throw new ArgumentException("ShippingPriceAmountMinor cannot be negative.", nameof(shippingPriceAmountMinor));
        if (platformFeeAmountMinor < 0)
            throw new ArgumentException("PlatformFeeAmountMinor cannot be negative.", nameof(platformFeeAmountMinor));
        if (makerPayoutAmountMinor < 0)
            throw new ArgumentException("MakerPayoutAmountMinor cannot be negative.", nameof(makerPayoutAmountMinor));
        if (totalAmountMinor < 0)
            throw new ArgumentException("TotalAmountMinor cannot be negative.", nameof(totalAmountMinor));

        // Pricing-math invariants. The split between platform fee +
        // maker payout (the "back" side) and the product + shipping
        // total (the "front" side, what the customer pays) must
        // reconcile to the same gross figure — otherwise an invoice
        // months later would show inconsistent line totals.
        if (productPriceAmountMinor + shippingPriceAmountMinor != totalAmountMinor)
            throw new ArgumentException(
                $"Total ({totalAmountMinor}) must equal Product ({productPriceAmountMinor}) + Shipping ({shippingPriceAmountMinor}).",
                nameof(totalAmountMinor));
        if (makerPayoutAmountMinor + platformFeeAmountMinor != productPriceAmountMinor + shippingPriceAmountMinor)
            throw new ArgumentException(
                $"MakerPayout ({makerPayoutAmountMinor}) + PlatformFee ({platformFeeAmountMinor}) must equal " +
                $"Product ({productPriceAmountMinor}) + Shipping ({shippingPriceAmountMinor}).",
                nameof(makerPayoutAmountMinor));

        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new ArgumentException("Currency must be a 3-char ISO 4217 code.", nameof(currency));

        if (vatRateBp < 0)
            throw new ArgumentException("VatRateBp cannot be negative.", nameof(vatRateBp));

        if (shippingMethod == ShippingMethod.ZasilkovnaPickupPoint
            && string.IsNullOrWhiteSpace(zasilkovnaPickupPointId))
        {
            throw new ArgumentException(
                "ZasilkovnaPickupPointId is required when ShippingMethod is ZasilkovnaPickupPoint.",
                nameof(zasilkovnaPickupPointId));
        }

        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            throw new ArgumentException("CountryCode must be 2 chars (ISO 3166-1 alpha-2).", nameof(countryCode));

        var trimmedNotes = string.IsNullOrWhiteSpace(customerNotes) ? null : customerNotes.Trim();
        if (trimmedNotes is not null && trimmedNotes.Length > MaxCustomerNotesLength)
            throw new ArgumentException($"CustomerNotes must be at most {MaxCustomerNotesLength} chars.", nameof(customerNotes));

        return new Order
        {
            Id = id,
            OrderNumber = orderNumber.Trim(),
            CustomerUserId = customerUserId,
            MakerId = makerId,
            ProductId = string.IsNullOrWhiteSpace(productId) ? null : productId,
            ContactName = trimmedName,
            ContactEmail = trimmedEmail,
            ContactPhone = trimmedPhone,
            ProductPriceAmountMinor = productPriceAmountMinor,
            ShippingPriceAmountMinor = shippingPriceAmountMinor,
            PlatformFeeAmountMinor = platformFeeAmountMinor,
            MakerPayoutAmountMinor = makerPayoutAmountMinor,
            TotalAmountMinor = totalAmountMinor,
            Currency = currency.ToUpperInvariant(),
            VatRateBp = vatRateBp,
            ShippingMethod = shippingMethod,
            // Normalise the empty/whitespace personal-pickup case to null so
            // downstream consumers can rely on `IS NULL` rather than guessing.
            ZasilkovnaPickupPointId = string.IsNullOrWhiteSpace(zasilkovnaPickupPointId)
                ? null
                : zasilkovnaPickupPointId.Trim(),
            CountryCode = countryCode.ToUpperInvariant(),
            CustomerNotes = trimmedNotes,
            State = OrderState.PendingPayment,
        };
    }

    // === State-machine transitions ===

    /// <summary>
    /// <see cref="OrderState.PendingPayment"/> → <see cref="OrderState.Paid"/>.
    /// Dispatched from the Comgate webhook handler (T-0066). Set-once
    /// on <see cref="PaymentProviderRef"/> — a second invocation, even
    /// from <see cref="OrderState.PendingPayment"/>, is refused.
    /// </summary>
    public BusinessResult MarkAsPaid(IClock clock, string paymentProviderRef)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (string.IsNullOrWhiteSpace(paymentProviderRef))
            throw new ArgumentException("PaymentProviderRef is required.", nameof(paymentProviderRef));

        if (State != OrderState.PendingPayment)
            return InvalidTransition();

        // Belt-and-braces set-once: the state guard above already blocks
        // a second call (because the second call's State is Paid, not
        // PendingPayment), but if a future state-graph change lets a
        // Paid order revisit PendingPayment we don't want a silent
        // overwrite of the original ref.
        if (PaymentProviderRef is not null)
            return BusinessResult.Failure(
                Error.Conflict("paymentProviderRef", BusinessErrorMessage.OrderInvalidTransition));

        var now = clock.UtcNow;
        State = OrderState.Paid;
        PaidAt = now;
        PaymentProviderRef = paymentProviderRef.Trim();
        return BusinessResult.Success();
    }

    /// <summary>
    /// <see cref="OrderState.Paid"/> → <see cref="OrderState.Accepted"/>.
    /// Maker-driven (T-0071 <c>AcceptOrder.Command</c>).
    /// </summary>
    public BusinessResult Accept(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (State != OrderState.Paid)
            return InvalidTransition();

        State = OrderState.Accepted;
        AcceptedAt = clock.UtcNow;
        return BusinessResult.Success();
    }

    /// <summary>
    /// <see cref="OrderState.Accepted"/> → <see cref="OrderState.Shipped"/>.
    /// Sets <see cref="ShippedAt"/>, <see cref="AutoDeliverAt"/> =
    /// <see cref="ShippedAt"/> + <paramref name="autoDeliverWindowDays"/>,
    /// and <see cref="ShippingCarrierRef"/> when supplied.
    ///
    /// <para>
    /// <paramref name="shippingCarrierRef"/> is nullable because the
    /// personal-pickup path (T-0073) has no carrier ref. When non-null
    /// the set-once invariant holds — re-shipping with a different ref
    /// is refused.
    /// </para>
    ///
    /// <para>
    /// <paramref name="autoDeliverWindowDays"/> must be positive. The
    /// caller (T-0072 <c>ShipOrder.Handler</c>) supplies the constant
    /// 7 per role/order.md / ADR 0017. The parameter exists so a future
    /// <c>CountryConfiguration</c>-driven window can be plumbed without
    /// re-shaping the entity.
    /// </para>
    /// </summary>
    public BusinessResult Ship(IClock clock, string? shippingCarrierRef, int autoDeliverWindowDays)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (autoDeliverWindowDays <= 0)
            throw new ArgumentException("AutoDeliverWindowDays must be positive.", nameof(autoDeliverWindowDays));

        if (State != OrderState.Accepted)
            return InvalidTransition();

        // Belt-and-braces set-once guard on ShippingCarrierRef. The state
        // guard above already blocks a second Ship call in the current
        // state graph (second call's State is Shipped, not Accepted), but
        // if a future state-graph change lets a Shipped order revisit
        // Accepted we don't want a silent overwrite of the carrier ref.
        // Field-only check: any prior non-null value is sticky, even if
        // the new call passes a null carrier ref. Mirrors MarkAsPaid's
        // layering. T-0060 Copilot review R2-2.
        if (ShippingCarrierRef is not null)
            return BusinessResult.Failure(
                Error.Conflict("shippingCarrierRef", BusinessErrorMessage.OrderInvalidTransition));

        var now = clock.UtcNow;
        State = OrderState.Shipped;
        ShippedAt = now;
        AutoDeliverAt = now.AddDays(autoDeliverWindowDays);
        if (!string.IsNullOrWhiteSpace(shippingCarrierRef))
            ShippingCarrierRef = shippingCarrierRef.Trim();
        return BusinessResult.Success();
    }

    /// <summary>
    /// <see cref="OrderState.Shipped"/> → <see cref="OrderState.Delivered"/>.
    /// Dispatched from the customer-confirm command (T-0076), the
    /// auto-deliver job (T-0077), or a carrier webhook.
    /// </summary>
    public BusinessResult MarkAsDelivered(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (State != OrderState.Shipped)
            return InvalidTransition();

        State = OrderState.Delivered;
        DeliveredAt = clock.UtcNow;
        return BusinessResult.Success();
    }

    /// <summary>
    /// <see cref="OrderState.Delivered"/> → <see cref="OrderState.Completed"/>.
    /// Dispatched from the payout-settled callback once the maker
    /// payout batch closes.
    /// </summary>
    public BusinessResult Complete(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (State != OrderState.Delivered)
            return InvalidTransition();

        State = OrderState.Completed;
        CompletedAt = clock.UtcNow;
        return BusinessResult.Success();
    }

    /// <summary>
    /// <see cref="OrderState.PendingPayment"/> /
    /// <see cref="OrderState.Paid"/> /
    /// <see cref="OrderState.Accepted"/> → <see cref="OrderState.Cancelled"/>.
    /// The entity exposes the edge; who may take it from which audience
    /// is enforced by the command layer (T-0083 auto-cancel,
    /// T-0107 admin manual change).
    /// </summary>
    public BusinessResult Cancel(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (State is not (OrderState.PendingPayment or OrderState.Paid or OrderState.Accepted))
            return InvalidTransition();

        State = OrderState.Cancelled;
        CancelledAt = clock.UtcNow;
        return BusinessResult.Success();
    }

    /// <summary>
    /// <see cref="OrderState.Paid"/> /
    /// <see cref="OrderState.Accepted"/> /
    /// <see cref="OrderState.Shipped"/> /
    /// <see cref="OrderState.Delivered"/> /
    /// <see cref="OrderState.Completed"/> → <see cref="OrderState.Refunded"/>.
    /// Admin-only authorisation lives in T-0105 <c>RefundOrder.Command</c>.
    /// </summary>
    public BusinessResult Refund(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (State is not (OrderState.Paid
            or OrderState.Accepted
            or OrderState.Shipped
            or OrderState.Delivered
            or OrderState.Completed))
        {
            return InvalidTransition();
        }

        State = OrderState.Refunded;
        RefundedAt = clock.UtcNow;
        return BusinessResult.Success();
    }

    /// <summary>
    /// <see cref="OrderState.Shipped"/> /
    /// <see cref="OrderState.Delivered"/> /
    /// <see cref="OrderState.Completed"/> → <see cref="OrderState.Disputed"/>.
    /// Customer-or-maker authorisation lives in T-0106
    /// <c>OpenDispute.Command</c>.
    /// </summary>
    public BusinessResult OpenDispute(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (State is not (OrderState.Shipped or OrderState.Delivered or OrderState.Completed))
            return InvalidTransition();

        State = OrderState.Disputed;
        DisputedAt = clock.UtcNow;
        return BusinessResult.Success();
    }

    /// <summary>
    /// T-0065. Record the provider's session reference and redirect URL on
    /// an order in <see cref="OrderState.PendingPayment"/>. Does NOT change
    /// <see cref="State"/> — that's the webhook's job
    /// (<see cref="MarkAsPaid"/> via T-0067).
    ///
    /// <para>
    /// <b>State gate.</b> Refuses with
    /// <see cref="BusinessErrorMessage.OrderInvalidTransition"/> for any
    /// state other than <see cref="OrderState.PendingPayment"/>. The
    /// handler also enforces a stricter
    /// <see cref="BusinessErrorMessage.OrderInvalidStateForPayment"/>
    /// at the command boundary (so already-paid orders surface a UX
    /// distinct message); this entity-level guard is defence-in-depth.
    /// </para>
    ///
    /// <para>
    /// <b>Idempotent on same ref.</b> When
    /// <see cref="PaymentProviderRef"/> already equals the incoming ref,
    /// only <see cref="PaymentRedirectUrl"/> is refreshed and the call
    /// succeeds. Comgate retries on the same <c>refId</c> return the same
    /// <c>transId</c>; the URL may legitimately rotate, so we accept the
    /// fresh value.
    /// </para>
    ///
    /// <para>
    /// <b>Overwrite-after-rejection.</b> When the existing ref differs
    /// from the incoming one, this method ALSO succeeds — but the caller
    /// (T-0065 <c>CreatePaymentSession.Handler</c>) is responsible for
    /// having verified the prior session was
    /// <see cref="Payments.PaymentState.Cancelled"/> or
    /// <see cref="Payments.PaymentState.Failed"/> first. The aggregate
    /// trusts the caller here because the verify-then-recreate sequencing
    /// lives at the application-service layer, not in the entity (an
    /// admin manual fix-up via T-0107 may also overwrite without the
    /// verify step). Per user decision Q1.
    /// </para>
    /// </summary>
    public BusinessResult ReservePaymentSession(string providerRef, string redirectUrl, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (string.IsNullOrWhiteSpace(providerRef))
            throw new ArgumentException("ProviderRef is required.", nameof(providerRef));
        if (string.IsNullOrWhiteSpace(redirectUrl))
            throw new ArgumentException("RedirectUrl is required.", nameof(redirectUrl));

        if (State != OrderState.PendingPayment)
            return InvalidTransition();

        var trimmedRef = providerRef.Trim();
        var trimmedUrl = redirectUrl.Trim();

        // Same ref (Comgate idempotency on refId) → only the URL refreshes.
        // Different ref (overwrite-after-rejection per Q1) → both update.
        PaymentProviderRef = trimmedRef;
        PaymentRedirectUrl = trimmedUrl;
        UpdatedAt = clock.UtcNow;
        return BusinessResult.Success();
    }

    private static BusinessResult InvalidTransition() =>
        BusinessResult.Failure(
            Error.Conflict("state", BusinessErrorMessage.OrderInvalidTransition));
}
