using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Infrastructure.Persistence.Configurations;

public sealed class SifenDocumentConfiguration : IEntityTypeConfiguration<SifenDocument>
{
    public void Configure(EntityTypeBuilder<SifenDocument> builder)
    {
        builder.ToTable("Documents");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Environment)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(entity => entity.Kind)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(entity => entity.CorrelationId)
            .HasMaxLength(80);

        builder.Property(entity => entity.InternalStatus)
            .HasConversion<string>()
            .HasMaxLength(40)
            .HasDefaultValue(FeInvoiceInternalStatus.DRAFT)
            .IsRequired();

        builder.Property(entity => entity.RetryCount)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(entity => entity.IsRetryable)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(entity => entity.LastErrorCode)
            .HasMaxLength(80);

        builder.Property(entity => entity.LastErrorMessage)
            .HasMaxLength(500);

        builder.Property(entity => entity.Cdc)
            .HasMaxLength(44)
            .IsRequired();

        builder.Property(entity => entity.DocumentType)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(entity => entity.ExternalDocumentNumber)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(entity => entity.EstablishmentCode)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(entity => entity.ExpeditionPointCode)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(entity => entity.CurrencyCode)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(entity => entity.SaleCondition)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(entity => entity.Notes)
            .HasMaxLength(1000);

        builder.Property(entity => entity.ReceiverName)
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(entity => entity.ReceiverDocument)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(entity => entity.ReceiverAddress)
            .HasMaxLength(500);

        builder.Property(entity => entity.ReceiverEmail)
            .HasMaxLength(250);

        builder.Property(entity => entity.ReceiverPhone)
            .HasMaxLength(60);

        builder.Property(entity => entity.SubtotalAmount)
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(entity => entity.Vat5Amount)
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(entity => entity.Vat10Amount)
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(entity => entity.ExemptAmount)
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(entity => entity.TotalVatAmount)
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(entity => entity.TotalAmount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(entity => entity.TestCdc)
            .HasMaxLength(120);

        builder.Property(entity => entity.TestQrText)
            .HasMaxLength(500);

        builder.Property(entity => entity.IsFiscalPreviewValid)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(entity => entity.XmlPayload)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(entity => entity.SignedXmlPayload)
            .HasColumnType("nvarchar(max)");

        builder.Property(entity => entity.LastSubmissionEndpoint)
            .HasMaxLength(500);

        builder.Property(entity => entity.SifenTrackingId)
            .HasMaxLength(100);

        builder.Property(entity => entity.StatusCode)
            .HasMaxLength(50);

        builder.Property(entity => entity.StatusMessage)
            .HasMaxLength(500);

        builder.Property(entity => entity.RawSifenResponse)
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(entity => new { entity.TenantId, entity.Cdc })
            .IsUnique();

        builder.HasIndex(entity => new
            {
                entity.TenantId,
                entity.Environment,
                entity.Kind,
                entity.EstablishmentCode,
                entity.ExpeditionPointCode,
                entity.ExternalDocumentNumber
            })
            .IsUnique();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(entity => entity.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
