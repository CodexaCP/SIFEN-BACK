using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SifenInvoicing.Domain.Documents;

namespace SifenInvoicing.Infrastructure.Persistence.Configurations;

public sealed class FeTenantLogConfiguration : IEntityTypeConfiguration<FeTenantLog>
{
    public void Configure(EntityTypeBuilder<FeTenantLog> builder)
    {
        builder.ToTable("FeTenantLogs");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.CorrelationId)
            .HasMaxLength(80);

        builder.Property(entity => entity.Level)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(entity => entity.Source)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(entity => entity.Message)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(entity => entity.TechnicalDetail)
            .HasColumnType("nvarchar(max)");

        builder.Property(entity => entity.CreatedAt)
            .HasDefaultValueSql("sysutcdatetime()")
            .IsRequired();

        builder.HasIndex(entity => new { entity.TenantId, entity.CreatedAt });
        builder.HasIndex(entity => new { entity.TenantId, entity.InvoiceId, entity.CreatedAt });
    }
}
