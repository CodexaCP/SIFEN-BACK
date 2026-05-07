using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Slug)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(entity => entity.DisplayName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(entity => entity.PlanName)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(entity => entity.MaxInvoicesPerMonth)
            .HasColumnType("int");

        builder.Property(entity => entity.MaxUsers)
            .HasColumnType("int");

        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(entity => entity.IsolationMode)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.HasIndex(entity => entity.Slug)
            .IsUnique();
    }
}
