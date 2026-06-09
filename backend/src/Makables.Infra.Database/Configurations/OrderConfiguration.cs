using Makables.Core.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

internal sealed class OrderEntityConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").HasMaxLength(40).IsRequired();

        // === Identity ===
        builder.Property(o => o.OrderNumber).HasColumnName("order_number").HasMaxLength(40).IsRequired();
        // Partial-unique mirrors Maker/Product policy (a soft-deleted
        // order frees its number for a future use). In practice the
        // OrderNumber generator never reuses a sequence, so this is a
        // belt-and-braces safety net rather than a design point.
        builder.HasIndex(o => o.OrderNumber)
            .IsUnique()
            .HasDatabaseName("ix_orders_order_number")
            .HasFilter("is_active");

        // === Parties ===
        builder.Property(o => o.CustomerUserId).HasColumnName("customer_user_id").HasMaxLength(40).IsRequired();
        builder.Property(o => o.MakerId).HasColumnName("maker_id").HasMaxLength(40).IsRequired();
        builder.Property(o => o.ProductId).HasColumnName("product_id").HasMaxLength(40);

        // === Contact snapshot ===
        builder.Property(o => o.ContactName)
            .HasColumnName("contact_name").HasMaxLength(Order.MaxContactNameLength).IsRequired();
        builder.Property(o => o.ContactEmail)
            .HasColumnName("contact_email").HasMaxLength(Order.MaxContactEmailLength).IsRequired();
        builder.Property(o => o.ContactPhone)
            .HasColumnName("contact_phone").HasMaxLength(Order.MaxContactPhoneLength).IsRequired();

        // === Pricing snapshot (minor units; ADR 0003) ===
        builder.Property(o => o.ProductPriceAmountMinor)
            .HasColumnName("product_price_amount_minor").IsRequired();
        builder.Property(o => o.ShippingPriceAmountMinor)
            .HasColumnName("shipping_price_amount_minor").IsRequired();
        builder.Property(o => o.PlatformFeeAmountMinor)
            .HasColumnName("platform_fee_amount_minor").IsRequired();
        builder.Property(o => o.MakerPayoutAmountMinor)
            .HasColumnName("maker_payout_amount_minor").IsRequired();
        builder.Property(o => o.TotalAmountMinor)
            .HasColumnName("total_amount_minor").IsRequired();
        builder.Property(o => o.Currency)
            .HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(o => o.VatRateBp)
            .HasColumnName("vat_rate_bp").IsRequired();

        // === Shipping choice ===
        builder.Property(o => o.ShippingMethod)
            .HasColumnName("shipping_method")
            // String-storage so dotnet ef database update reads cleanly
            // (same convention as Product.PriceType).
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(o => o.ZasilkovnaPickupPointId)
            .HasColumnName("zasilkovna_pickup_point_id").HasMaxLength(40);

        // === State + timestamps ===
        builder.Property(o => o.State)
            .HasColumnName("state")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(o => o.PaidAt).HasColumnName("paid_at");
        builder.Property(o => o.AcceptedAt).HasColumnName("accepted_at");
        builder.Property(o => o.ShippedAt).HasColumnName("shipped_at");
        builder.Property(o => o.DeliveredAt).HasColumnName("delivered_at");
        builder.Property(o => o.CompletedAt).HasColumnName("completed_at");
        builder.Property(o => o.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(o => o.RefundedAt).HasColumnName("refunded_at");
        builder.Property(o => o.DisputedAt).HasColumnName("disputed_at");

        // === Provider refs + auto-deliver ===
        builder.Property(o => o.PaymentProviderRef)
            .HasColumnName("payment_provider_ref").HasMaxLength(200);
        // Partial-unique on the payment ref backs webhook idempotency:
        // a duplicate Comgate notification trying to create a second
        // order against the same transaction loses the unique race.
        // The UniqueConstraintTranslator intentionally leaves this
        // constraint unmapped (T-0060 Copilot review M-1) so the 23505
        // rethrows — the next webhook delivery then hits the
        // GetByPaymentProviderRefAsync pre-check and returns 200
        // idempotently. Translating to Error.Conflict here would cause
        // Comgate to retry, which is the wrong resolution.
        builder.HasIndex(o => o.PaymentProviderRef)
            .IsUnique()
            .HasDatabaseName("ix_orders_payment_provider_ref")
            .HasFilter("payment_provider_ref IS NOT NULL AND is_active");

        // T-0065: cached Comgate redirect URL for the 24h retry window.
        // Nullable until the customer's first payment-session call. Length
        // 500 — generous headroom over Comgate's ~120-char URLs.
        builder.Property(o => o.PaymentRedirectUrl)
            .HasColumnName("payment_redirect_url").HasMaxLength(500);

        // T-0067: provider's payment-method label (e.g. CARD_CZ, BANK_CZ_RB).
        // Length 40 mirrors the shipping_method column precedent. No index —
        // admin queries by payment_method are out-of-MVP analytics.
        builder.Property(o => o.PaymentMethod)
            .HasColumnName("payment_method").HasMaxLength(40);

        builder.Property(o => o.ShippingCarrierRef)
            .HasColumnName("shipping_carrier_ref").HasMaxLength(200);

        // T-0070: pre-computed customer-facing tracking URL. Set by T-0072
        // ShipOrder.Handler; null for personal-pickup (T-0073) + any
        // pre-T-0072 orders. Length 500 matches Order.MaxShippingCarrierTrackingUrlLength
        // (Packeta URLs are ~50 chars; 500 gives generous headroom).
        builder.Property(o => o.ShippingCarrierTrackingUrl)
            .HasColumnName("shipping_carrier_tracking_url")
            .HasMaxLength(Order.MaxShippingCarrierTrackingUrlLength);

        builder.Property(o => o.AutoDeliverAt).HasColumnName("auto_deliver_at");

        // T-0076: delivery source (Customer/Auto/Carrier). SMALLINT NULL —
        // nullable so historical Delivered orders pre-T-0076 stay valid;
        // the enum's `: short` backing keeps the column at 2 bytes per row.
        // Per ADR 0013 — queryable from analytics + dispute trails
        // (Phase 5 T-0106 / T-0118) without joining outbox / audit logs.
        builder.Property(o => o.DeliverySource)
            .HasColumnName("delivery_source")
            .HasConversion<short?>();

        // T-0083: cancellation source (Customer/AutoExpiry/Admin). SMALLINT
        // NULL — nullable so non-cancelled orders carry null; matches the
        // DeliverySource pattern.
        builder.Property(o => o.CancellationSource)
            .HasColumnName("cancellation_source")
            .HasConversion<short?>();

        // T-0079: denormalized unread-message counters per locked decision
        // A.3. Default 0 so backfill is trivial. Drive O(1) reads on the
        // T-0080 / T-0081 list endpoints without a per-row subquery.
        builder.Property(o => o.CustomerUnreadMessageCount)
            .HasColumnName("customer_unread_message_count")
            .HasDefaultValue(0)
            .IsRequired();
        builder.Property(o => o.MakerUnreadMessageCount)
            .HasColumnName("maker_unread_message_count")
            .HasDefaultValue(0)
            .IsRequired();

        // T-0079: 5-min notification-debounce pointers per locked decision
        // A.2. Nullable so a fresh order has neither pointer set; null
        // means "next post fires immediately".
        builder.Property(o => o.CustomerPendingNotificationEmailAt)
            .HasColumnName("customer_pending_notification_email_at");
        builder.Property(o => o.MakerPendingNotificationEmailAt)
            .HasColumnName("maker_pending_notification_email_at");

        // === Customer notes ===
        builder.Property(o => o.CustomerNotes)
            .HasColumnName("customer_notes").HasMaxLength(Order.MaxCustomerNotesLength);

        // === Indexes ===
        // Customer order list (T-0080): newest-first per customer.
        builder.HasIndex(o => new { o.CustomerUserId, o.CreatedAt })
            .HasDatabaseName("ix_orders_customer_created")
            .IsDescending(false, true);

        // Maker dashboard (T-0081): per-maker, per-state, newest-first.
        builder.HasIndex(o => new { o.MakerId, o.State, o.CreatedAt })
            .HasDatabaseName("ix_orders_maker_state_created")
            .IsDescending(false, false, true);

        // Auto-deliver scan (T-0077) + pending-payment auto-cancel
        // scan (T-0083) both filter by state. Partial WHERE is_active
        // matches the house convention (e.g. ix_makers_catalog_sort);
        // soft-deleted orders are never the target of either scan, so
        // the partial index stays small. T-0060 Copilot review M-2.
        builder.HasIndex(o => o.State)
            .HasDatabaseName("ix_orders_state")
            .HasFilter("is_active");

        // === Attachments (T-0064) ===
        // One-to-many to OrderAttachment with cascade-delete at the FK
        // level. Soft-delete on the parent doesn't touch the FK; the
        // global query filter on Auditable.IsActive hides both rows from
        // every non-admin query. The cascade only fires on the GDPR
        // right-to-erasure hard-delete (T-0110).
        builder.HasMany(o => o.Attachments)
            .WithOne()
            .HasForeignKey(a => a.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Backing-field metadata for the private List<OrderAttachment>.
        // Lets EF materialise straight into the field rather than going
        // through the read-only IReadOnlyCollection property.
        // AutoInclude mirrors ProductConfiguration's Images navigation:
        // the attachment-count gate (Order.AddAttachment / handler / upload
        // controller) needs the collection populated to be correct. Loading
        // a partial aggregate would let an 11th row through. The shape is
        // bounded at 10 rows per order so the JOIN is cheap.
        builder.Navigation(o => o.Attachments)
            .HasField("_attachments")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<Order> b)
    {
        b.Property(o => o.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        b.Property(o => o.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(o => o.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        b.Property(o => o.CreatedAt).HasColumnName("created_at");
        b.Property(o => o.UpdatedBy).HasColumnName("updated_by").HasMaxLength(200);
        b.Property(o => o.UpdatedAt).HasColumnName("updated_at");
        b.Property(o => o.DeactivatedBy).HasColumnName("deactivated_by").HasMaxLength(200);
        b.Property(o => o.DeactivatedAt).HasColumnName("deactivated_at");
    }
}
