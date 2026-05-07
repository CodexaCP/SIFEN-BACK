namespace SifenInvoicing.Domain.Common;

public abstract class TenantScopedEntity : AuditableEntity
{
    protected TenantScopedEntity()
    {
    }

    protected TenantScopedEntity(Guid id, Guid tenantId)
        : base(id)
    {
        TenantId = tenantId == Guid.Empty
            ? throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId))
            : tenantId;
    }

    public Guid TenantId { get; protected set; }
}
