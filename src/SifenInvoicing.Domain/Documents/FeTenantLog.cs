using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Domain.Documents;

public sealed class FeTenantLog : TenantScopedEntity
{
    private FeTenantLog()
    {
        Source = string.Empty;
        Message = string.Empty;
    }

    private FeTenantLog(
        Guid id,
        Guid tenantId,
        Guid? invoiceId,
        string? correlationId,
        FeTenantLogLevel level,
        string source,
        string message,
        string? technicalDetail,
        DateTimeOffset createdAt)
        : base(id, tenantId)
    {
        InvoiceId = invoiceId;
        CorrelationId = NormalizeOptional(correlationId, 80);
        Level = level;
        Source = RequireValue(source, nameof(source), 80);
        Message = RequireValue(message, nameof(message), 500);
        TechnicalDetail = NormalizeOptional(technicalDetail);
        CreatedAt = createdAt;
    }

    public Guid? InvoiceId { get; private set; }

    public string? CorrelationId { get; private set; }

    public FeTenantLogLevel Level { get; private set; }

    public string Source { get; private set; }

    public string Message { get; private set; }

    public string? TechnicalDetail { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static FeTenantLog Create(
        Guid tenantId,
        Guid? invoiceId,
        string? correlationId,
        FeTenantLogLevel level,
        string source,
        string message,
        string? technicalDetail,
        DateTimeOffset createdAt)
    {
        return new FeTenantLog(
            Guid.NewGuid(),
            tenantId,
            invoiceId,
            correlationId,
            level,
            source,
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
