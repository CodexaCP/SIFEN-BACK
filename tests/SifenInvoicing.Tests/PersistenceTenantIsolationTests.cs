using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Tenancy;

namespace SifenInvoicing.Tests;

public sealed class PersistenceTenantIsolationTests
{
    [Fact]
    public async Task SaveChangesAsync_ShouldRejectTenantScopedEntitiesForDifferentTenant()
    {
        var currentTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        tenantAccessor.SetCurrent(new TenantContext
        {
            TenantId = currentTenantId.ToString(),
            ResolvedTenantId = currentTenantId,
            IsResolved = true
        });
        await using var dbContext = CreateDbContext(tenantAccessor);

        dbContext.TaxpayerProfiles.Add(TaxpayerProfile.Create(
            otherTenantId,
            "80000000",
            "0",
            "Other Taxpayer"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldAllowTenantScopedEntitiesForCurrentTenant()
    {
        var currentTenantId = Guid.NewGuid();
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        tenantAccessor.SetCurrent(new TenantContext
        {
            TenantId = currentTenantId.ToString(),
            ResolvedTenantId = currentTenantId,
            IsResolved = true
        });
        await using var dbContext = CreateDbContext(tenantAccessor);

        dbContext.TaxpayerProfiles.Add(TaxpayerProfile.Create(
            currentTenantId,
            "80000000",
            "0",
            "Current Taxpayer"));

        await dbContext.SaveChangesAsync();

        Assert.Single(await dbContext.TaxpayerProfiles.ToListAsync());
    }

    private static SifenDbContext CreateDbContext(ITenantContextAccessor tenantAccessor)
    {
        var options = new DbContextOptionsBuilder<SifenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SifenDbContext(options, new SystemClock(), tenantAccessor);
    }
}
