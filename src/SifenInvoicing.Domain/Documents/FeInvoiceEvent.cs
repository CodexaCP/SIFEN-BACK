using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Domain.Documents;

public sealed class FeInvoiceEvent : TenantScopedEntity
{
    private FeInvoiceEvent()
    {
        CorrelationId = string.Empty;
        NewStatus = string.Empty;
        EventType = string.Empty;
        Message = string.Empty;
    }

    private FeInvoiceEvent(
        Guid id,
        Guid tenantId,
        Guid invoiceId,
        string correlationId,
        string? previousStatus,
        string newStatus,
        string eventType,
        string message,
        string? technicalDetail,
        DateTimeOffset createdAt)
        : base(id, tenantId)
    {
        InvoiceId = invoiceId == Guid.Empty
            ? throw new DomainException("invoiceId is required.")
            : invoiceId;
        CorrelationId = RequireValue(correlationId, nameof(correlationId), 80);
        PreviousStatus = NormalizeOptional(previousStatus, 40);
        NewStatus = RequireValue(newStatus, nameof(newStatus), 40);
        EventType = RequireValue(eventType, nameof(eventType), 60);
        Message = RequireValue(message, nameof(message), 500);
        TechnicalDetail = NormalizeOptional(technicalDetail);
        CreatedAt = createdAt;
    }

    public Guid InvoiceId { get; private set; }

    public string CorrelationId { get; private set; }

    public string? PreviousStatus { get; private set; }

    public string NewStatus { get; private set; }

    public string EventType { get; private set; }

    public string Message { get; private set; }

    public string? TechnicalDetail { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static FeInvoiceEvent Create(
        Guid tenantId,
        Guid invoiceId,
        string correlationId,
        string? previousStatus,
        string newStatus,
        string eventType,
        string message,
        string? technicalDetail,
        DateTimeOffset createdAt)
    {
        return new FeInvoiceEvent(
            Guid.NewGuid(),
            tenantId,
            invoiceId,
            correlationId,
            previousStatus,
            newStatus,
            eventType,
            message,
            technicalDetail,
            createdAt);
    }

    private static string RequireValue(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{parameterName} is required.");
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new DomainException($"{parameterName} cannot exceed {maxLength} characters.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int? maxLength = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (maxLength.HasValue && normalized.Length > maxLength.Value)
        {
            throw new DomainException($"Optional value cannot exceed {maxLength.Value} characters.");
        }

        return normalized;
    }
}
