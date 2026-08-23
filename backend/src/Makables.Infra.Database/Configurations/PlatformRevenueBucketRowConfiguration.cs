using Makables.Infra.Database.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

/// <summary>
/// Registers the T-0192 revenue-series projection target. Keyless and
/// view-less: <c>ToView(null)</c> tells EF this type backs no table and no
/// view, so <c>dotnet ef migrations add</c> never emits DDL for it and the
/// model snapshot stays free of a phantom table. Its only use is as the
/// result shape of the raw <c>date_trunc</c> aggregate in
/// <see cref="Orders.OrderQueries.GetPlatformRevenueSeriesAsync"/>.
///
/// <para>
/// Column names are declared explicitly rather than left to the naming
/// convention, because the contract here is with hand-written SQL: the
/// aliases in that query and these names are the same string, and a
/// mismatch surfaces as a runtime "column not found" rather than a compile
/// error. Keeping them side by side in one file is the mitigation.
/// </para>
/// </summary>
internal sealed class PlatformRevenueBucketRowConfiguration
    : IEntityTypeConfiguration<PlatformRevenueBucketRow>
{
    public void Configure(EntityTypeBuilder<PlatformRevenueBucketRow> builder)
    {
        builder.HasNoKey();
        builder.ToView(null);

        builder.Property(r => r.BucketStart).HasColumnName("bucket_start");
        builder.Property(r => r.PaidOrderCount).HasColumnName("paid_order_count");
        builder.Property(r => r.GrossVolumeMinor).HasColumnName("gross_volume_minor");
        builder.Property(r => r.PlatformFeeMinor).HasColumnName("platform_fee_minor");
        builder.Property(r => r.MakerPayoutMinor).HasColumnName("maker_payout_minor");
        builder.Property(r => r.RefundedMinor).HasColumnName("refunded_minor");
    }
}
