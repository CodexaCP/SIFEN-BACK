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

    [Fact]
    public async Task Queries_ShouldBeFailClosed_WhenNoTenantIsResolved()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var accessor = new AsyncLocalTenantContextAccessor();
        var databaseName = Guid.NewGuid().ToString();

        foreach (var tenantId in new[] { tenantA, tenantB })
        {
            accessor.SetCurrent(new TenantContext { TenantId = tenantId.ToString(), ResolvedTenantId = tenantId, IsResolved = true });
            await using var seed = CreateDbContext(accessor, databaseName);
            seed.TaxpayerProfiles.Add(TaxpayerProfile.Create(tenantId, "80000000", "0", $"Taxpayer {tenantId}"));
            await seed.SaveChangesAsync();
        }

        accessor.Clear();
        await using var withoutTenant = CreateDbContext(accessor, databaseName);
        Assert.Empty(await withoutTenant.TaxpayerProfiles.ToListAsync());

        accessor.SetCurrent(new TenantContext { TenantId = tenantA.ToString(), ResolvedTenantId = tenantA, IsResolved = true });
        await using var asA = CreateDbContext(accessor, databaseName);
        var visible = await asA.TaxpayerProfiles.ToListAsync();
        Assert.Single(visible);
        Assert.Equal(tenantA, visible[0].TenantId);
    }

    private static SifenDbContext CreateDbContext(ITenantContextAccessor tenantAccessor, string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<SifenDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;

        return new SifenDbContext(options, new SystemClock(), tenantAccessor);
    }
}
