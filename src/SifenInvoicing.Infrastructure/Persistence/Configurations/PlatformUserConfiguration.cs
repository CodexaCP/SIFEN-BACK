using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SifenInvoicing.Domain.PlatformAuth;

namespace SifenInvoicing.Infrastructure.Persistence.Configurations;

public sealed class PlatformUserConfiguration : IEntityTypeConfiguration<PlatformUser>
{
    public void Configure(EntityTypeBuilder<PlatformUser> builder)
    {
        builder.ToTable("PlatformUsers");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.FullName)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(entity => entity.Email)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(entity => entity.PasswordHash)
            .IsRequired()
            .HasMaxLength(4000);
        builder.Property(entity => entity.IsActive)
            .IsRequired();
        builder.HasIndex(entity => entity.Email)
            .IsUnique();
        builder.Property(entity => entity.TenantId)
            .HasColumnType("uniqueidentifier");
        builder.HasOne(entity => entity.Role)
            .WithMany()
            .HasForeignKey(entity => entity.RoleId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        builder.HasOne(entity => entity.Tenant)
            .WithMany()
            .HasForeignKey(entity => entity.TenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
