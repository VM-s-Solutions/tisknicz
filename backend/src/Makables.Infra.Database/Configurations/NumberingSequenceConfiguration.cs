using Makables.Core.Domain.Numbering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

internal sealed class NumberingSequenceConfiguration : IEntityTypeConfiguration<NumberingSequence>
{
    public void Configure(EntityTypeBuilder<NumberingSequence> builder)
    {
        builder.ToTable("numbering_sequence");

        builder.HasKey(s => new { s.CountryCode, s.Scope, s.Year });

        builder.Property(s => s.CountryCode)
            .HasColumnName("country_code")
            .HasMaxLength(2)
            .IsRequired();

        builder.Property(s => s.Scope)
            .HasColumnName("scope")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(s => s.Year)
            .HasColumnName("year")
            .IsRequired();

        builder.Property(s => s.LastUsedValue)
            .HasColumnName("last_used_value")
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
    }
}
