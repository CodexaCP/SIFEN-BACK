using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SifenInvoicing.Domain.Documents;

namespace SifenInvoicing.Infrastructure.Persistence.Configurations;

public sealed class SifenDocumentLineConfiguration : IEntityTypeConfiguration<SifenDocumentLine>
{
    public void Configure(EntityTypeBuilder<SifenDocumentLine> builder)
    {
        builder.ToTable("DocumentLines");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Description)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(entity => entity.Quantity)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(entity => entity.UnitPrice)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(entity => entity.VatRate)
            .IsRequired();

        builder.Property(entity => entity.VatAmount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(entity => entity.ExemptAmount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(entity => entity.SubtotalAmount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(entity => entity.TotalAmount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(entity => entity.ProductCode).HasMaxLength(50);
        builder.Property(entity => entity.UnitDescription).HasMaxLength(10);

        builder.HasIndex(entity => new { entity.DocumentId, entity.LineNumber })
            .IsUnique();

        builder.HasOne<SifenDocument>()
            .WithMany()
            .HasForeignKey(entity => entity.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
