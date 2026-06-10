using Makables.Core.Domain.Identity;
using Makables.Core.Domain.OrderMessages;
using Makables.Core.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

/// <summary>
/// EF mapping for the <see cref="OrderMessage"/> child table of
/// <see cref="Core.Domain.Orders.Order"/> (T-0079). Per locked decision
/// §C.3:
/// <list type="bullet">
///   <item><description><c>order_messages</c> table with PK + FK to
///     <c>orders.id</c> + FK to <c>users.id</c> for the author audit
///     trail.</description></item>
///   <item><description>Composite index <c>(order_id, created_at DESC)</c>
///     for the per-order paged thread read (AC-10 / AC-12).</description></item>
///   <item><description>Partial index <c>(order_id, author_role)</c>
///     filtered to <c>read_by_counterparty_at IS NULL</c> for the
///     MarkAsRead bulk-UPDATE sweep.</description></item>
/// </list>
///
/// <para>
/// <b>No HasMany relationship</b> declared from <see cref="Core.Domain.Orders.Order"/>
/// to messages — the aggregate stays lightweight; the messages thread is
/// read independently via <see cref="IOrderMessageQueries"/>. Mirrors
/// the <see cref="Core.Domain.Orders.Order"/> ↔ <c>Invoice</c> pattern
/// (Invoices are loaded by OrderId, not via a HasMany navigation). The
/// FKs are shadow relationships (<c>HasOne&lt;T&gt;().WithMany()</c>, no
/// navigation property): orders → CASCADE (a hard-deleted order takes
/// its thread with it, per the order_attachments precedent); users →
/// RESTRICT (a user row must never cascade-delete the audit trail of
/// messages it authored — GDPR erasure is an explicit T-0110 flow).
/// </para>
/// </summary>
internal sealed class OrderMessageEntityConfiguration : IEntityTypeConfiguration<OrderMessage>
{
    public void Configure(EntityTypeBuilder<OrderMessage> builder)
    {
        builder.ToTable("order_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").HasMaxLength(40).IsRequired();

        builder.Property(m => m.OrderId).HasColumnName("order_id").HasMaxLength(40).IsRequired();

        builder.Property(m => m.AuthorRole)
            .HasColumnName("author_role")
            // SMALLINT — matches the enum's `: short` backing per house
            // convention (OrderDeliverySource precedent).
            .HasConversion<short>()
            .IsRequired();

        builder.Property(m => m.AuthorUserId)
            .HasColumnName("author_user_id").HasMaxLength(40).IsRequired();

        builder.Property(m => m.Body)
            .HasColumnName("body")
            .HasMaxLength(OrderMessage.MaxBodyLength)
            .IsRequired();

        builder.Property(m => m.ReadByCounterpartyAt)
            .HasColumnName("read_by_counterparty_at");

        // Composite index for the per-order paged thread read. EF Core
        // emits a Postgres B-tree; the DESC on created_at backs the
        // ORDER BY in the read query without a sort step.
        builder.HasIndex(m => new { m.OrderId, m.CreatedAt })
            .HasDatabaseName("ix_order_messages_order_created")
            .IsDescending(false, true);

        // Partial index for the MarkAsRead bulk UPDATE — only unread rows
        // need to be scanned; on a healthy thread the unread set is
        // small. WHERE clause is Postgres-specific; mirrors the
        // ix_orders_payment_provider_ref convention.
        builder.HasIndex(m => new { m.OrderId, m.AuthorRole })
            .HasDatabaseName("ix_order_messages_order_author_unread")
            .HasFilter("read_by_counterparty_at IS NULL AND is_active");

        // Backs the users FK below (RESTRICT checks on user deletion +
        // future "messages by author" admin lookups). Explicit so the
        // name follows the house lowercase convention instead of the
        // EF FK-convention default.
        builder.HasIndex(m => m.AuthorUserId)
            .HasDatabaseName("ix_order_messages_author_user");

        // === FKs (review fold HIGH-1 / T-0079 AC-12 + §C.3) ===
        // Shadow relationships — no navigation properties; the Order
        // aggregate stays lightweight per ADR 0013.
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(m => m.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<OrderMessage> b)
    {
        b.Property(m => m.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        b.Property(m => m.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(m => m.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        b.Property(m => m.CreatedAt).HasColumnName("created_at");
        b.Property(m => m.UpdatedBy).HasColumnName("updated_by").HasMaxLength(200);
        b.Property(m => m.UpdatedAt).HasColumnName("updated_at");
        b.Property(m => m.DeactivatedBy).HasColumnName("deactivated_by").HasMaxLength(200);
        b.Property(m => m.DeactivatedAt).HasColumnName("deactivated_at");
    }
}
