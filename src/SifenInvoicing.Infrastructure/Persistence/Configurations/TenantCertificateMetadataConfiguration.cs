using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Infrastructure.Persistence.Configurations;

public sealed class TenantCertificateMetadataConfiguration : IEntityTypeConfiguration<TenantCertificateMetadata>
{
    public void Configure(EntityTypeBuilder<TenantCertificateMetadata> builder)
    {
        builder.ToTable("TenantCertificateMetadata");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Environment)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(entity => entity.Purpose)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(entity => entity.Alias)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(entity => entity.Subject)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(entity => entity.FingerprintSha256)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(entity => entity.SerialNumber)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(entity => entity.CertificateSecretReference)
            .HasColumnName("CertificateSecretReference")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(entity => entity.CertificatePasswordSecretReference)
            .HasColumnName("CertificatePasswordSecretReference")
            .HasMaxLength(500)
            .IsRequired();

        builder.HasIndex(entity => new { entity.TenantId, entity.Environment, entity.Purpose, entity.Alias })
            .IsUnique();

        builder.HasIndex(entity => new { entity.TenantId, entity.FingerprintSha256 });

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(entity => entity.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
