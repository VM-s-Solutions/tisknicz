using Makables.Core.Domain.OrderMessages;
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
/// (Invoices are loaded by OrderId, not via a HasMany navigation).
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
