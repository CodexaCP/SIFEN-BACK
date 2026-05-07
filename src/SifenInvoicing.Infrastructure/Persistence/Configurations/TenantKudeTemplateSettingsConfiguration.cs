using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Infrastructure.Persistence.Configurations;

public sealed class TenantKudeTemplateSettingsConfiguration : IEntityTypeConfiguration<TenantKudeTemplateSettings>
{
    public void Configure(EntityTypeBuilder<TenantKudeTemplateSettings> builder)
    {
        builder.ToTable("TenantKudeTemplateSettings");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.TemplateCode)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(entity => entity.LogoUrl)
            .HasMaxLength(1000);

        builder.Property(entity => entity.PrimaryColor)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(entity => entity.SecondaryColor)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(entity => entity.FooterText)
            .HasMaxLength(500)
            .IsRequired();

        builder.HasIndex(entity => entity.TenantId)
            .IsUnique();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(entity => entity.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
