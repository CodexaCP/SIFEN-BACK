using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SifenInvoicing.Domain.Documents;

namespace SifenInvoicing.Infrastructure.Persistence.Configurations;

public sealed class SifenDocumentErrorConfiguration : IEntityTypeConfiguration<SifenDocumentError>
{
    public void Configure(EntityTypeBuilder<SifenDocumentError> builder)
    {
        builder.ToTable("DocumentErrors");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Cdc)
            .HasMaxLength(44);

        builder.Property(entity => entity.ErrorCode)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(entity => entity.ErrorCategory)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(entity => entity.TechnicalMessage)
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(entity => entity.UserMessage)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(entity => entity.SuggestedAction)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(entity => entity.CorrelationId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(entity => entity.RawResponse)
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(entity => new { entity.TenantId, entity.InvoiceId, entity.CreatedAt });
    }
}
