using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Infrastructure.Persistence.Configurations;

public sealed class TenantSifenSettingsConfiguration : IEntityTypeConfiguration<TenantSifenSettings>
{
    public void Configure(EntityTypeBuilder<TenantSifenSettings> builder)
    {
        builder.ToTable("TenantSifenSettings");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Environment)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(entity => entity.CscIdentifier)
            .HasMaxLength(20);

        builder.Property(entity => entity.CscSecretReference)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(entity => entity.EstablishmentCode)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(entity => entity.ExpeditionPointCode)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(entity => entity.CurrentDocumentNumber)
            .HasMaxLength(7)
            .IsRequired();

        builder.Property(entity => entity.StampingNumber)
            .HasMaxLength(30);

        builder.Property(entity => entity.CertificateSecretReference)
            .HasMaxLength(500);

        builder.Property(entity => entity.CertificatePasswordSecretReference)
            .HasMaxLength(500);

        builder.Property(entity => entity.CertificateAlias)
            .HasMaxLength(120);

        builder.Property(entity => entity.XmlSchemaRootPath)
            .HasMaxLength(1000);

        builder.Property(entity => entity.EndpointUrl)
            .HasMaxLength(1000);

        builder.Property(entity => entity.TransportMode)
            .HasMaxLength(40);

        builder.HasIndex(entity => new { entity.TenantId, entity.Environment })
            .IsUnique();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(entity => entity.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
