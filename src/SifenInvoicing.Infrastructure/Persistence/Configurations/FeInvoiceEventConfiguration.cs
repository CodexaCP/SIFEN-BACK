using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SifenInvoicing.Domain.Documents;

namespace SifenInvoicing.Infrastructure.Persistence.Configurations;

public sealed class FeInvoiceEventConfiguration : IEntityTypeConfiguration<FeInvoiceEvent>
{
    public void Configure(EntityTypeBuilder<FeInvoiceEvent> builder)
    {
        builder.ToTable("FeInvoiceEvents");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.CorrelationId)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(entity => entity.PreviousStatus)
            .HasMaxLength(40);

        builder.Property(entity => entity.NewStatus)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(entity => entity.EventType)
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(entity => entity.Message)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(entity => entity.TechnicalDetail)
            .HasColumnType("nvarchar(max)");

        builder.Property(entity => entity.CreatedAt)
            .HasDefaultValueSql("sysutcdatetime()")
            .IsRequired();

        builder.HasIndex(entity => new { entity.TenantId, entity.InvoiceId, entity.CreatedAt });
    }
}
