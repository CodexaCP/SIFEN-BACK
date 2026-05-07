using SifenInvoicing.Domain.Documents;

namespace SifenInvoicing.Application.Invoices;

public sealed record InvoiceListItem(
    Guid Id,
    string Cdc,
    string ExternalDocumentNumber,
    string EstablishmentCode,
    string ExpeditionPointCode,
    string ReceiverName,
    decimal TotalAmount,
    string CurrencyCode,
    SifenDocumentStatus Status,
    string? StatusCode,
    string? StatusMessage,
    string? SifenTrackingId,
    DateTimeOffset IssuedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? FinalizedAt,
    bool CanDownloadXml,
    bool CanDownloadKude,
    bool CanRetry,
    string? ErrorCode,
    string? ErrorCategory,
    string? UserMessage,
    string? SuggestedAction,
    bool IsRetryable,
    string? CorrelationId);
