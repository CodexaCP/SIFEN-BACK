using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Infrastructure.Persistence.Configurations;

public sealed class TaxpayerProfileConfiguration : IEntityTypeConfiguration<TaxpayerProfile>
{
    public void Configure(EntityTypeBuilder<TaxpayerProfile> builder)
    {
        builder.ToTable("TaxpayerProfiles");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.RucNumber)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(entity => entity.RucCheckDigit)
            .HasMaxLength(2)
            .IsRequired();

        builder.Property(entity => entity.LegalName)
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(entity => entity.TradeName)
            .HasMaxLength(250);

        builder.Property(entity => entity.Address).HasMaxLength(255);
        builder.Property(entity => entity.HouseNumber).HasMaxLength(20);
        builder.Property(entity => entity.DepartmentCode).HasMaxLength(10);
        builder.Property(entity => entity.DepartmentDescription).HasMaxLength(60);
        builder.Property(entity => entity.DistrictCode).HasMaxLength(10);
        builder.Property(entity => entity.DistrictDescription).HasMaxLength(60);
        builder.Property(entity => entity.CityCode).HasMaxLength(10);
        builder.Property(entity => entity.CityDescription).HasMaxLength(60);
        builder.Property(entity => entity.Phone).HasMaxLength(20);
        builder.Property(entity => entity.Email).HasMaxLength(80);

        builder.HasIndex(entity => new { entity.TenantId, entity.RucNumber, entity.RucCheckDigit })
            .IsUnique();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(entity => entity.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
