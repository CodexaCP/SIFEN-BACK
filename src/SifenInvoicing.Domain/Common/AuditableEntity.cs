namespace SifenInvoicing.Domain.Common;

public abstract class AuditableEntity : Entity
{
    protected AuditableEntity()
    {
    }

    protected AuditableEntity(Guid id)
        : base(id)
    {
    }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public void MarkCreated(DateTimeOffset utcNow)
    {
        CreatedAt = utcNow;
    }

    public void MarkUpdated(DateTimeOffset utcNow)
    {
        UpdatedAt = utcNow;
    }
}
