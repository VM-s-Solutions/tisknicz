using Makables.Core.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

/// <summary>
/// Append-only by application convention AND by DB trigger per ADR 0014.
/// The trigger that rejects UPDATE / DELETE lives in the migration's
/// raw-SQL Up step (it can't be expressed through EF Core fluent API).
/// </summary>
internal sealed class AdminAuditLogEntryConfiguration
    : IEntityTypeConfiguration<AdminAuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AdminAuditLogEntry> builder)
    {
        builder.ToTable("admin_audit_log");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasMaxLength(26).IsRequired();
        builder.Property(e => e.AdminUserId).HasColumnName("admin_user_id").HasMaxLength(64).IsRequired();
        builder.Property(e => e.ActionCode).HasColumnName("action_code").HasMaxLength(100).IsRequired();
        builder.Property(e => e.TargetEntity).HasColumnName("target_entity").HasMaxLength(50).IsRequired();
        builder.Property(e => e.TargetId).HasColumnName("target_id").HasMaxLength(64).IsRequired();
        builder.Property(e => e.BeforeJson).HasColumnName("before_json").HasColumnType("jsonb");
        builder.Property(e => e.AfterJson).HasColumnName("after_json").HasColumnType("jsonb");
        builder.Property(e => e.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(e => e.IpAddress).HasColumnName("ip_address").HasMaxLength(45);  // IPv6 max
        builder.Property(e => e.UserAgent).HasColumnName("user_agent").HasMaxLength(500);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(e => new { e.AdminUserId, e.CreatedAt })
            .HasDatabaseName("ix_admin_audit_log_admin_user_id_created_at");
        builder.HasIndex(e => new { e.TargetEntity, e.TargetId, e.CreatedAt })
            .HasDatabaseName("ix_admin_audit_log_target_created_at");
        builder.HasIndex(e => new { e.ActionCode, e.CreatedAt })
            .HasDatabaseName("ix_admin_audit_log_action_code_created_at");
    }
}
