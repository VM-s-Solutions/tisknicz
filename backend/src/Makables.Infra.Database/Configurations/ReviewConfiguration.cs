using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Reviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

/// <summary>
/// EF mapping for the <see cref="Review"/> table (T-0100 §C.12).
/// <list type="bullet">
///   <item><description><c>reviews</c> table with PK + FK to
///     <c>orders.id</c> (the per-order anchor) + FK to <c>makers.id</c>
///     (denormalized) + FK to <c>users.id</c> (the reviewer audit
///     trail).</description></item>
///   <item><description><b>Partial unique index</b>
///     <c>ux_reviews_order_active UNIQUE (order_id) WHERE is_active</c> —
///     one active review per order (Q1); soft-delete frees the slot
///     (self-healing).</description></item>
///   <item><description>Index <c>(maker_id, created_at DESC)</c> for the
///     maker-received-reviews list + the recompute aggregate scan.</description></item>
/// </list>
///
/// <para>
/// FKs are shadow relationships (<c>HasOne&lt;T&gt;().WithMany()</c>, no
/// navigation property) so the Order / Maker aggregates stay lightweight
/// (ADR 0013): orders + makers → CASCADE (a GDPR hard delete takes the
/// review with it, per the order_messages / disputes precedent); users →
/// RESTRICT (a user row must never cascade-delete the audit trail).
/// </para>
/// </summary>
internal sealed class ReviewEntityConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasMaxLength(40).IsRequired();

        builder.Property(r => r.OrderId).HasColumnName("order_id").HasMaxLength(40).IsRequired();
        builder.Property(r => r.MakerId).HasColumnName("maker_id").HasMaxLength(40).IsRequired();
        builder.Property(r => r.CustomerUserId).HasColumnName("customer_user_id").HasMaxLength(40).IsRequired();

        builder.Property(r => r.Rating)
            .HasColumnName("rating")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(r => r.Body)
            .HasColumnName("body")
            .HasMaxLength(Review.MaxBodyLength);

        builder.Property(r => r.MakerReply)
            .HasColumnName("maker_reply")
            .HasMaxLength(Review.MaxReplyLength);

        builder.Property(r => r.MakerReplyAt)
            .HasColumnName("maker_reply_at");

        // Q1 per-order grain enforcement + soft-delete self-healing: at
        // most ONE active review per order. The partial filter mirrors the
        // ux_disputes_order_open precedent (Postgres-specific WHERE clause).
        builder.HasIndex(r => r.OrderId)
            .IsUnique()
            .HasDatabaseName("ux_reviews_order_active")
            .HasFilter("is_active");

        // Backs the maker-received-reviews list ORDER BY created_at DESC
        // and the recompute AVG(rating) scan over a maker's reviews.
        builder.HasIndex(r => new { r.MakerId, r.CreatedAt })
            .HasDatabaseName("ix_reviews_maker_created")
            .IsDescending(false, true);

        // Backs the customer-side reads (reviewable-orders anti-join +
        // submitted-reviews list).
        builder.HasIndex(r => r.CustomerUserId)
            .HasDatabaseName("ix_reviews_customer_user");

        // === FKs (shadow relationships — no navigation properties) ===
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Maker>()
            .WithMany()
            .HasForeignKey(r => r.MakerId)
            .OnDelete(DeleteBehavior.Cascade);

        // NO enforced FK to users on CustomerUserId (T-0110): the GDPR
        // erasure de-identifies the author by overwriting this column with
        // the "Anonymized" sentinel (which is not a real user id) and
        // hard-deletes the user row while the review CONTENT is retained
        // (it's about the maker). An enforced FK would make either step a
        // 23503/Restrict violation. This mirrors Order.CustomerUserId, which
        // likewise keeps a denormalized author id with no enforced FK. The
        // ix_reviews_customer_user index above still backs the customer reads.

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<Review> b)
    {
        b.Property(r => r.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        b.Property(r => r.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(r => r.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        b.Property(r => r.CreatedAt).HasColumnName("created_at");
        b.Property(r => r.UpdatedBy).HasColumnName("updated_by").HasMaxLength(200);
        b.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        b.Property(r => r.DeactivatedBy).HasColumnName("deactivated_by").HasMaxLength(200);
        b.Property(r => r.DeactivatedAt).HasColumnName("deactivated_at");
    }
}
