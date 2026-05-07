using SifenInvoicing.Domain.Documents;

namespace SifenInvoicing.Application.Invoices;

public sealed record InvoiceRetryResult(
    Guid Id,
    string Cdc,
    SifenDocumentStatus Status,
    string? StatusCode,
    string? StatusMessage,
    DateTimeOffset AttemptedAt,
    int AttemptNumber);
