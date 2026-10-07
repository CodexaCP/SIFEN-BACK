using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Infrastructure.Persistence.Configurations;

public sealed class TaxpayerEconomicActivityConfiguration : IEntityTypeConfiguration<TaxpayerEconomicActivity>
{
    public void Configure(EntityTypeBuilder<TaxpayerEconomicActivity> builder)
    {
        builder.ToTable("TaxpayerEconomicActivities");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Code).HasMaxLength(8).IsRequired();
        builder.Property(entity => entity.Description).HasMaxLength(300).IsRequired();

        builder.HasIndex(entity => new { entity.TaxpayerProfileId, entity.Code }).IsUnique();

        builder.HasOne<TaxpayerProfile>()
            .WithMany()
            .HasForeignKey(entity => entity.TaxpayerProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(entity => entity.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
