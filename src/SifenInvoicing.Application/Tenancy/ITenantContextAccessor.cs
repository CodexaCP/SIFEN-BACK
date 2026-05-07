namespace SifenInvoicing.Application.Tenancy;

public interface ITenantContextAccessor
{
    TenantContext Current { get; }

    void SetCurrent(TenantContext context);

    void Clear();
}
