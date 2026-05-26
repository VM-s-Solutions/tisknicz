using Makables.Core.Domain.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

internal sealed class CompanyRegistryCacheEntryEntityConfiguration
    : IEntityTypeConfiguration<CompanyRegistryCacheEntry>
{
    public void Configure(EntityTypeBuilder<CompanyRegistryCacheEntry> builder)
    {
        builder.ToTable("company_registry_cache");

        // Composite primary key per ADR 0018 §"Caching policy" — one row
        // per (registry, ICO) pair.
        builder.HasKey(e => new { e.RegistryCode, e.RegistrationNumber });

        builder.Property(e => e.RegistryCode)
            .HasColumnName("registry_code").HasMaxLength(20).IsRequired();
        builder.Property(e => e.RegistrationNumber)
            .HasColumnName("registration_number").HasMaxLength(32).IsRequired();

        // jsonb on Postgres; "text" on SQLite (integration tests) — Npgsql's
        // type mapping picks the right column type per the active provider.
        builder.Property(e => e.PayloadJson)
            .HasColumnName("payload").HasColumnType("jsonb").IsRequired();

        builder.Property(e => e.FetchedAt).HasColumnName("fetched_at").IsRequired();
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at").IsRequired();

        // Supports the daily EvictExpiredRegistryCache sweep + the stale-fallback
        // window query (T-0032).
        builder.HasIndex(e => e.ExpiresAt).HasDatabaseName("ix_company_registry_cache_expires_at");
    }
}
