using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Domain.Documents;

public sealed class SifenDocumentLog : TenantScopedEntity
{
    private SifenDocumentLog()
    {
        EventType = string.Empty;
        Message = string.Empty;
    }

    private SifenDocumentLog(
        Guid id,
        Guid tenantId,
        Guid documentId,
        DocumentLogLevel level,
        string eventType,
        string message,
        string? metadataJson)
        : base(id, tenantId)
    {
        DocumentId = documentId == Guid.Empty
            ? throw new DomainException("documentId is required.")
            : documentId;
        Level = level;
        EventType = RequireValue(eventType, nameof(eventType));
        Message = RequireValue(message, nameof(message));
        MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? null : metadataJson.Trim();
    }

    public Guid DocumentId { get; private set; }

    public DocumentLogLevel Level { get; private set; }

    public string EventType { get; private set; }

    public string Message { get; private set; }

    public string? MetadataJson { get; private set; }

    public static SifenDocumentLog Create(
        Guid tenantId,
        Guid documentId,
        DocumentLogLevel level,
        string eventType,
        string message,
        string? metadataJson = null)
    {
        return new SifenDocumentLog(Guid.NewGuid(), tenantId, documentId, level, eventType, message, metadataJson);
    }

    private static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{parameterName} is required.");
        }

        return value.Trim();
    }
}
