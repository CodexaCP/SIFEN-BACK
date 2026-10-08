using SifenInvoicing.Domain.Documents;

namespace SifenInvoicing.Application.Invoices;

public sealed record FeTenantInvoiceListItem(
    Guid InvoiceId,
    Guid TenantId,
    string InvoiceNumber,
    string CustomerName,
    decimal TotalAmount,
    string Currency,
    string InternalStatus,
    string? CorrelationId,
    int RetryCount,
    bool IsRetryable,
    string? LastErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string Cdc,
    SifenTransmissionState TransmissionState,
    SifenFiscalState FiscalState);
