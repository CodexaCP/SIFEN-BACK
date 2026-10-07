using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Infrastructure.Persistence.Configurations;

public sealed class FiscalStampConfiguration : IEntityTypeConfiguration<FiscalStamp>
{
    public void Configure(EntityTypeBuilder<FiscalStamp> builder)
    {
        builder.ToTable("FiscalStamps");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Environment).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.StampingNumber).HasMaxLength(8).IsRequired();
        builder.HasIndex(entity => new { entity.TenantId, entity.Environment, entity.StampingNumber }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(entity => entity.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class NumberingSequenceConfiguration : IEntityTypeConfiguration<NumberingSequence>
{
    public void Configure(EntityTypeBuilder<NumberingSequence> builder)
    {
        builder.ToTable("NumberingSequences");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Environment).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.DocumentTypeCode).HasMaxLength(2).IsRequired();
        builder.Property(entity => entity.EstablishmentCode).HasMaxLength(3).IsRequired();
        builder.Property(entity => entity.ExpeditionPointCode).HasMaxLength(3).IsRequired();
        builder.Property(entity => entity.Series).HasMaxLength(10).IsRequired().HasDefaultValue(string.Empty);
        builder.Property(entity => entity.Version).IsConcurrencyToken();
        builder.HasIndex(entity => new
        {
            entity.TenantId,
            entity.Environment,
            entity.FiscalStampId,
            entity.DocumentTypeCode,
            entity.EstablishmentCode,
            entity.ExpeditionPointCode,
            entity.Series
        })
        .IsUnique();
        builder.HasOne<FiscalStamp>().WithMany().HasForeignKey(entity => entity.FiscalStampId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(entity => entity.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Key).HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.RequestHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(entity => new { entity.TenantId, entity.Key }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(entity => entity.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
