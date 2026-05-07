using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SifenInvoicing.Domain.Documents;

namespace SifenInvoicing.Infrastructure.Persistence.Configurations;

public sealed class SifenDocumentLogConfiguration : IEntityTypeConfiguration<SifenDocumentLog>
{
    public void Configure(EntityTypeBuilder<SifenDocumentLog> builder)
    {
        builder.ToTable("Logs");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Level)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(entity => entity.EventType)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(entity => entity.Message)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(entity => entity.MetadataJson)
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(entity => new { entity.TenantId, entity.DocumentId, entity.CreatedAt });

        builder.HasOne<SifenDocument>()
            .WithMany()
            .HasForeignKey(entity => entity.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
