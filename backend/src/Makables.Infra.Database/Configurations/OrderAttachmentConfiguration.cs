using Makables.Core.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

/// <summary>
/// EF mapping for the <see cref="OrderAttachment"/> child of
/// <see cref="Order"/>. Per T-0064 user decision Q3 the storage shape is
/// a child table (one row per attachment) with FK to <c>orders.id</c> +
/// <c>ON DELETE CASCADE</c>. The FK relationship itself is declared in
/// <see cref="OrderEntityConfiguration"/> via
/// <c>HasMany(o =&gt; o.Attachments).WithOne()</c> — this configuration
/// only owns the per-entity column shapes + the support index.
///
/// <para>
/// <b>Cascade rationale.</b> The only hard-delete path on <c>orders</c>
/// is the GDPR right-to-erasure flow (T-0110), which intends to wipe
/// attachments too. Soft-delete on <see cref="Order"/> doesn't touch the
/// FK; the global soft-delete query filter hides both rows.
/// </para>
/// </summary>
internal sealed class OrderAttachmentEntityConfiguration : IEntityTypeConfiguration<OrderAttachment>
{
    public void Configure(EntityTypeBuilder<OrderAttachment> builder)
    {
        builder.ToTable("order_attachments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasMaxLength(40).IsRequired();

        builder.Property(a => a.OrderId).HasColumnName("order_id").HasMaxLength(40).IsRequired();

        builder.Property(a => a.BlobPath)
            .HasColumnName("blob_path")
            .HasMaxLength(OrderAttachment.MaxBlobPathLength)
            .IsRequired();

        builder.Property(a => a.OriginalFilename)
            .HasColumnName("original_filename")
            .HasMaxLength(OrderAttachment.MaxOriginalFilenameLength)
            .IsRequired();

        builder.Property(a => a.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(OrderAttachment.MaxContentTypeLength)
            .IsRequired();

        builder.Property(a => a.SizeBytes).HasColumnName("size_bytes").IsRequired();

        builder.Property(a => a.UploadedByUserId)
            .HasColumnName("uploaded_by_user_id")
            .HasMaxLength(40)
            .IsRequired();

        // Per-order lookup index — supports the maker dashboard "show
        // attachments for this order" path (T-0081 detail view) and the
        // download endpoint's join from order to attachment. Partial WHERE
        // is_active matches the house convention (e.g. ix_orders_state);
        // soft-deleted attachments aren't part of the live query.
        builder.HasIndex(a => a.OrderId)
            .HasDatabaseName("ix_order_attachments_order_id")
            .HasFilter("is_active");

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<OrderAttachment> b)
    {
        b.Property(a => a.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        b.Property(a => a.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(a => a.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        b.Property(a => a.CreatedAt).HasColumnName("created_at");
        b.Property(a => a.UpdatedBy).HasColumnName("updated_by").HasMaxLength(200);
        b.Property(a => a.UpdatedAt).HasColumnName("updated_at");
        b.Property(a => a.DeactivatedBy).HasColumnName("deactivated_by").HasMaxLength(200);
        b.Property(a => a.DeactivatedAt).HasColumnName("deactivated_at");
    }
}
