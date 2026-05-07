using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SifenInvoicing.Domain.PlatformAuth;

namespace SifenInvoicing.Infrastructure.Persistence.Configurations;

public sealed class PlatformRoleConfiguration : IEntityTypeConfiguration<PlatformRole>
{
    public void Configure(EntityTypeBuilder<PlatformRole> builder)
    {
        builder.ToTable("PlatformRoles");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Name)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(entity => entity.Description)
            .IsRequired()
            .HasMaxLength(250);
        builder.Property(entity => entity.Permissions)
            .HasConversion(
                permissions => string.Join(';', permissions),
                value => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyCollection<string>>(
                (left, right) => (left ?? Array.Empty<string>()).SequenceEqual(right ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase),
                value => (value ?? Array.Empty<string>())
                    .Aggregate(0, (current, item) => HashCode.Combine(current, StringComparer.OrdinalIgnoreCase.GetHashCode(item))),
                value => (value ?? Array.Empty<string>()).ToArray()));
        builder.Property(entity => entity.Permissions)
            .IsRequired()
            .HasColumnType("nvarchar(max)");
        builder.HasIndex(entity => entity.Name)
            .IsUnique();
    }
}
