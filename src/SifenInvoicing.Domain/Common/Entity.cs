namespace SifenInvoicing.Domain.Common;

public abstract class Entity
{
    protected Entity()
    {
    }

    protected Entity(Guid id)
    {
        Id = id == Guid.Empty ? throw new ArgumentException("Entity id cannot be empty.", nameof(id)) : id;
    }

    public Guid Id { get; protected set; }
}
