using Makables.Core.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

internal sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("outbox_event");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasMaxLength(26).IsRequired();
        builder.Property(e => e.AggregateId).HasColumnName("aggregate_id").HasMaxLength(64).IsRequired();
        builder.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        builder.Property(e => e.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at");
        builder.Property(e => e.RetryCount).HasColumnName("retry_count").IsRequired();
        builder.Property(e => e.NextRetryAt).HasColumnName("next_retry_at");
        builder.Property(e => e.LastErrorKind)
            .HasColumnName("last_error_kind")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(e => e.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(100);
        builder.Property(e => e.AcknowledgedAt).HasColumnName("acknowledged_at");
        builder.Property(e => e.AcknowledgedBy).HasColumnName("acknowledged_by").HasMaxLength(64);

        // Hot-path index: the outbox processor sweeps for unprocessed events
        // whose next_retry_at has come due.
        builder.HasIndex(e => new { e.NextRetryAt, e.ProcessedAt })
            .HasFilter("processed_at IS NULL")
            .HasDatabaseName("ix_outbox_event_due");

        builder.HasIndex(e => e.EventType)
            .HasDatabaseName("ix_outbox_event_event_type");

        builder.HasIndex(e => e.AggregateId)
            .HasDatabaseName("ix_outbox_event_aggregate_id");
    }
}
