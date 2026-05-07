using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Infrastructure.Tenancy;

namespace SifenInvoicing.Tests;

public sealed class TenantContextAccessorTests
{
    [Fact]
    public void Current_ShouldReturnEmptyContext_WhenNoTenantWasResolved()
    {
        var accessor = new AsyncLocalTenantContextAccessor();

        accessor.Clear();

        Assert.False(accessor.Current.IsResolved);
        Assert.Null(accessor.Current.TenantId);
        Assert.Null(accessor.Current.ClientId);
        Assert.Null(accessor.Current.TaxpayerRuc);
    }

    [Fact]
    public void Current_ShouldReturnResolvedTenantContext()
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        var context = new TenantContext
        {
            TenantId = "tenant-001",
            ClientId = "client-001",
            TaxpayerRuc = "80000000-0",
            IsResolved = true
        };

        accessor.SetCurrent(context);

        Assert.True(accessor.Current.IsResolved);
        Assert.Equal("tenant-001", accessor.Current.TenantId);
        Assert.Equal("client-001", accessor.Current.ClientId);
        Assert.Equal("80000000-0", accessor.Current.TaxpayerRuc);

        accessor.Clear();
    }
}
