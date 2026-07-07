using Makables.Core.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

internal sealed class DisputeEntityConfiguration : IEntityTypeConfiguration<Dispute>
{
    public void Configure(EntityTypeBuilder<Dispute> builder)
    {
        builder.ToTable("disputes");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").HasMaxLength(40).IsRequired();

        builder.Property(d => d.OrderId).HasColumnName("order_id").HasMaxLength(40).IsRequired();

        // Enum → SMALLINT conversions per the DeliverySource / CancellationSource
        // precedent (`: short` backing keeps the column at 2 bytes per row).
        builder.Property(d => d.Category)
            .HasColumnName("category")
            .HasConversion<short>()
            .IsRequired();
        builder.Property(d => d.Source)
            .HasColumnName("source")
            .HasConversion<short>()
            .IsRequired();
        builder.Property(d => d.ResolutionOutcome)
            .HasColumnName("resolution_outcome")
            .HasConversion<short?>();

        builder.Property(d => d.Description)
            .HasColumnName("description")
            .HasMaxLength(Dispute.MaxDescriptionLength)
            .IsRequired();
        builder.Property(d => d.ResolutionNotes)
            .HasColumnName("resolution_notes")
            .HasMaxLength(Dispute.MaxResolutionNotesLength);
        builder.Property(d => d.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(d => d.AutoEscalatedAt).HasColumnName("auto_escalated_at");

        // FK declared as a shadow relationship (no navigation on Order —
        // the aggregate stays lightweight per ADR 0013): CASCADE per the
        // order_attachments / order_messages precedent. Soft delete never
        // touches the FK; the cascade fires only on the GDPR hard delete
        // (T-0110).
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(d => d.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // T-0106 §C.4: at most ONE open dispute per order — the partial
        // unique index makes the concurrent-open race safe (the loser
        // hits 23505) and backs the Silent-Success re-open contract.
        // Resolved disputes accumulate as history.
        builder.HasIndex(d => d.OrderId)
            .IsUnique()
            .HasDatabaseName("ux_disputes_order_open")
            .HasFilter("resolved_at IS NULL");

        // T-0145 sweep predicate: ResolvedAt IS NULL AND Source = Customer
        // AND AutoEscalatedAt IS NULL AND CreatedAt < cutoff. Partial index
        // keeps the daily scan to the (small) open-customer-sourced subset.
        builder.HasIndex(d => d.CreatedAt)
            .HasDatabaseName("ix_disputes_auto_escalation_candidates")
            .HasFilter("resolved_at IS NULL AND source = 0 AND auto_escalated_at IS NULL");

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<Dispute> b)
    {
        b.Property(d => d.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        b.Property(d => d.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(d => d.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        b.Property(d => d.CreatedAt).HasColumnName("created_at");
        b.Property(d => d.UpdatedBy).HasColumnName("updated_by").HasMaxLength(200);
        b.Property(d => d.UpdatedAt).HasColumnName("updated_at");
        b.Property(d => d.DeactivatedBy).HasColumnName("deactivated_by").HasMaxLength(200);
        b.Property(d => d.DeactivatedAt).HasColumnName("deactivated_at");
    }
}
