using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Domain.PlatformAuth;
using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Infrastructure.Persistence;

public class SifenDbContext : DbContext
{
    private readonly ISystemClock _clock;
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public SifenDbContext(
        DbContextOptions<SifenDbContext> options,
        ISystemClock clock,
        ITenantContextAccessor tenantContextAccessor)
        : base(options)
    {
        _clock = clock;
        _tenantContextAccessor = tenantContextAccessor;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TaxpayerProfile> TaxpayerProfiles => Set<TaxpayerProfile>();
    public DbSet<TaxpayerEconomicActivity> TaxpayerEconomicActivities => Set<TaxpayerEconomicActivity>();

    public DbSet<TenantSifenSettings> TenantSifenSettings => Set<TenantSifenSettings>();

    public DbSet<TenantCertificateMetadata> TenantCertificateMetadata => Set<TenantCertificateMetadata>();

    public DbSet<TenantKudeTemplateSettings> TenantKudeTemplateSettings => Set<TenantKudeTemplateSettings>();

    public DbSet<SifenDocument> Documents => Set<SifenDocument>();

    public DbSet<SifenDocumentLine> DocumentLines => Set<SifenDocumentLine>();

    public DbSet<SifenDocumentLog> DocumentLogs => Set<SifenDocumentLog>();

    public DbSet<SifenDocumentError> DocumentErrors => Set<SifenDocumentError>();

    public DbSet<FeInvoiceEvent> FeInvoiceEvents => Set<FeInvoiceEvent>();

    public DbSet<FeTenantLog> FeTenantLogs => Set<FeTenantLog>();

    public DbSet<FiscalStamp> FiscalStamps => Set<FiscalStamp>();

    public DbSet<NumberingSequence> NumberingSequences => Set<NumberingSequence>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<PlatformRole> PlatformRoles => Set<PlatformRole>();

    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();

    private Guid? CurrentTenantId => _tenantContextAccessor.Current.ResolvedTenantId;

    // Fail-closed: sin tenant resuelto (Guid.Empty) los filtros globales no devuelven filas.
    private Guid CurrentTenantGuid => CurrentTenantId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SifenDbContext).Assembly);
        modelBuilder.Entity<TaxpayerProfile>().HasQueryFilter(entity =>
            CurrentTenantGuid != Guid.Empty && entity.TenantId == CurrentTenantGuid);
        modelBuilder.Entity<TaxpayerEconomicActivity>().HasQueryFilter(entity =>
            CurrentTenantGuid != Guid.Empty && entity.TenantId == CurrentTenantGuid);
        modelBuilder.Entity<TenantSifenSettings>().HasQueryFilter(entity =>
            CurrentTenantGuid != Guid.Empty && entity.TenantId == CurrentTenantGuid);
        modelBuilder.Entity<TenantCertificateMetadata>().HasQueryFilter(entity =>
            CurrentTenantGuid != Guid.Empty && entity.TenantId == CurrentTenantGuid);
        modelBuilder.Entity<TenantKudeTemplateSettings>().HasQueryFilter(entity =>
            CurrentTenantGuid != Guid.Empty && entity.TenantId == CurrentTenantGuid);
        modelBuilder.Entity<FiscalStamp>().HasQueryFilter(entity =>
            CurrentTenantGuid != Guid.Empty && entity.TenantId == CurrentTenantGuid);
        modelBuilder.Entity<NumberingSequence>().HasQueryFilter(entity =>
            CurrentTenantGuid != Guid.Empty && entity.TenantId == CurrentTenantGuid);
        modelBuilder.Entity<IdempotencyRecord>().HasQueryFilter(entity =>
            CurrentTenantGuid != Guid.Empty && entity.TenantId == CurrentTenantGuid);
        modelBuilder.Entity<SifenDocument>().HasQueryFilter(entity =>
            CurrentTenantGuid != Guid.Empty && entity.TenantId == CurrentTenantGuid);
        modelBuilder.Entity<SifenDocumentLine>().HasQueryFilter(entity =>
            CurrentTenantGuid != Guid.Empty && entity.TenantId == CurrentTenantGuid);
        modelBuilder.Entity<SifenDocumentLog>().HasQueryFilter(entity =>
            CurrentTenantGuid != Guid.Empty && entity.TenantId == CurrentTenantGuid);
        modelBuilder.Entity<SifenDocumentError>().HasQueryFilter(entity =>
            CurrentTenantGuid != Guid.Empty && entity.TenantId == CurrentTenantGuid);
        modelBuilder.Entity<FeInvoiceEvent>().HasQueryFilter(entity =>
            CurrentTenantGuid != Guid.Empty && entity.TenantId == CurrentTenantGuid);
        modelBuilder.Entity<FeTenantLog>().HasQueryFilter(entity =>
            CurrentTenantGuid != Guid.Empty && entity.TenantId == CurrentTenantGuid);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditAndTenantGuards();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditAndTenantGuards();
        return base.SaveChanges();
    }

    private void ApplyAuditAndTenantGuards()
    {
        var utcNow = _clock.UtcNow;
        var currentTenantId = CurrentTenantId;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.MarkCreated(utcNow);
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Entity.MarkUpdated(utcNow);
            }
        }

        if (!currentTenantId.HasValue)
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries<TenantScopedEntity>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                entry.Entity.TenantId != currentTenantId.Value)
            {
                throw new InvalidOperationException(
                    $"Tenant isolation violation. Current tenant {currentTenantId.Value} cannot modify entity for tenant {entry.Entity.TenantId}.");
            }
        }
    }
}
