using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Domain.Documents;

/// <summary>
/// Registro de Idempotency-Key por tenant. Se inserta en la misma transaccion que el documento:
/// si la creacion falla, no queda registro ni numero consumido.
/// </summary>
public sealed class IdempotencyRecord : TenantScopedEntity
{
    private IdempotencyRecord()
    {
        Key = string.Empty;
        RequestHash = string.Empty;
    }

    private IdempotencyRecord(Guid id, Guid tenantId, string key, string requestHash, Guid documentId)
        : base(id, tenantId)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
        {
            throw new DomainException("Idempotency key is required (max 128 characters).");
        }

        Key = key;
        RequestHash = requestHash;
        DocumentId = documentId;
    }

    public string Key { get; private set; }

    public string RequestHash { get; private set; }

    public Guid DocumentId { get; private set; }

    public static IdempotencyRecord Create(Guid tenantId, string key, string requestHash, Guid documentId) =>
        new(Guid.NewGuid(), tenantId, key, requestHash, documentId);
}
