using SifenInvoicing.Application.Tenancy;

namespace SifenInvoicing.Infrastructure.Tenancy;

public sealed class AsyncLocalTenantContextAccessor : ITenantContextAccessor
{
    private static readonly AsyncLocal<TenantContext?> CurrentContext = new();

    public TenantContext Current => CurrentContext.Value ?? TenantContext.Empty;

    public void SetCurrent(TenantContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        CurrentContext.Value = context;
    }

    public void Clear()
    {
        CurrentContext.Value = null;
    }
}
