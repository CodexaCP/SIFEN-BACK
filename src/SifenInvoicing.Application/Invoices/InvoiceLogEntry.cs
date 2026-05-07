using SifenInvoicing.Domain.Documents;

namespace SifenInvoicing.Application.Invoices;

public sealed record InvoiceLogEntry(
    DateTimeOffset OccurredAt,
    DocumentLogLevel Level,
    string EventType,
    string Message,
    string? MetadataJson);
