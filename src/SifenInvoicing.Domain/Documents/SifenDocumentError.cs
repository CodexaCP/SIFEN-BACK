using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Domain.Documents;

public sealed class SifenDocumentError : TenantScopedEntity
{
    private SifenDocumentError()
    {
        ErrorCode = string.Empty;
        TechnicalMessage = string.Empty;
        UserMessage = string.Empty;
        SuggestedAction = string.Empty;
        CorrelationId = string.Empty;
    }

    private SifenDocumentError(
        Guid id,
        Guid tenantId,
        Guid? invoiceId,
        string? cdc,
        string errorCode,
        SifenErrorCategory errorCategory,
        string technicalMessage,
        string userMessage,
        string suggestedAction,
        bool isRetryable,
        string correlationId,
        string? rawResponse,
        DateTimeOffset createdAt)
        : base(id, tenantId)
    {
        InvoiceId = invoiceId;
        Cdc = NormalizeOptional(cdc);
        ErrorCode = RequireValue(errorCode, nameof(errorCode));
        ErrorCategory = errorCategory;
        TechnicalMessage = RequireValue(technicalMessage, nameof(technicalMessage));
        UserMessage = RequireValue(userMessage, nameof(userMessage));
        SuggestedAction = RequireValue(suggestedAction, nameof(suggestedAction));
        IsRetryable = isRetryable;
        CorrelationId = RequireValue(correlationId, nameof(correlationId));
        RawResponse = NormalizeOptional(rawResponse);
        CreatedAt = createdAt;
    }

    public Guid? InvoiceId { get; private set; }

    public string? Cdc { get; private set; }

    public string ErrorCode { get; private set; }

    public SifenErrorCategory ErrorCategory { get; private set; }

    public string TechnicalMessage { get; private set; }

    public string UserMessage { get; private set; }

    public string SuggestedAction { get; private set; }

    public bool IsRetryable { get; private set; }

    public string CorrelationId { get; private set; }

    public string? RawResponse { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static SifenDocumentError Create(
        Guid tenantId,
        Guid? invoiceId,
        string? cdc,
        string errorCode,
        SifenErrorCategory errorCategory,
        string technicalMessage,
        string userMessage,
        string suggestedAction,
        bool isRetryable,
        string correlationId,
        string? rawResponse,
        DateTimeOffset createdAt)
    {
        return new SifenDocumentError(
            Guid.NewGuid(),
            tenantId,
            invoiceId,
            cdc,
            errorCode,
            errorCategory,
            technicalMessage,
            userMessage,
            suggestedAction,
            isRetryable,
            correlationId,
            rawResponse,
            createdAt);
    }

    private static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{parameterName} is required.");
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
